using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // Used by comp-info.exe and by the standalone youSBee.exe (YouSBee.cs).
    // «USB-накопители»: serial numbers of connected flash drives (get-usb-serial.ps1).
    // Drives removed since the last «Обновить» stay in the list, grey with a crossed icon, so several drives
    // can be plugged in one after another and their serial numbers compared. A newly connected drive lights up.
    public class UsbPage : Page
    {
        // Columns: flash drive icon, drive letter, name, size, serial number, «Копировать».
        const int IconColumn = 0, DiskColumn = 1, NameColumn = 2, SerialColumn = 4, CopyColumn = 5;

        // A drive seen since the last «Обновить»: the key is its serial number (or disk number and name without one).
        class Entry
        {
            public string Key;
            public UsbDisk Disk;
            public bool Connected;
            public DateTime HighlightUntil, CopiedUntil;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly DataGridView grid;
        readonly FlowLayoutPanel toolbar;
        readonly StatusLine status = new StatusLine();
        readonly Bitmap driveIcon, removedIcon, emptyIcon;
        readonly Timer fade = new Timer();
        bool loading, again, againManual, loaded, compact;
        int hoverRow = -1;

        public UsbPage()
        {
            Key = "usb";
            Title = L.T("USB-накопители");
            Description = L.T("Серийные номера подключённых флешек и USB-дисков. Список обновляется сам, когда накопитель подключают или отключают.");
            Icon = Glyph.Usb;
            int em = Font.Height;
            driveIcon = Glyph.Image(Glyph.Usb, em, Theme.Flash);
            removedIcon = RemovedIcon(em);
            emptyIcon = Glyph.Image(Glyph.Usb, em * 3, Theme.Border);

            Button refresh = Ui.Secondary(L.T("Обновить"), Glyph.Refresh, Font);
            refresh.Click += delegate { RefreshList(); };
            Ui.Keys(refresh, "F5");
            toolbar = Ui.Row();
            toolbar.Controls.Add(refresh);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);

            grid = Ui.Grid(Font);
            grid.Dock = DockStyle.Fill;
            DataGridViewImageColumn icon = new DataGridViewImageColumn();
            icon.HeaderText = "";
            icon.Width = em * 2;
            icon.DefaultCellStyle.NullValue = null;
            grid.Columns.Add(icon);
            grid.Columns.Add("disk", L.T("Диск"));
            grid.Columns.Add("name", L.T("Название"));
            grid.Columns.Add("size", L.T("Объём"));
            grid.Columns.Add("serial", L.T("Серийный номер"));
            DataGridViewButtonColumn copy = new DataGridViewButtonColumn();
            copy.HeaderText = "";
            copy.FlatStyle = FlatStyle.Flat;
            // The button itself is drawn by PaintCopyButton; the cell keeps the row colours.
            grid.Columns.Add(copy);
            grid.Columns[DiskColumn].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            // Volume labels are short (FAT32 up to 11, exFAT up to 15 characters); a longer device name is cut
            // with an ellipsis and shown in full in the tooltip. The serial column takes the free width.
            grid.Columns[NameColumn].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.Columns[NameColumn].Width = em * 6;
            grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            grid.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[SerialColumn].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[SerialColumn].MinimumWidth = em * 7;
            grid.Columns[SerialColumn].DefaultCellStyle.Font = new Font(Ui.MonoFont, FontStyle.Bold);
            grid.Columns[CopyColumn].Width = em * 6;
            grid.RowTemplate.Height = em * 2;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.CellContentClick += delegate (object o, DataGridViewCellEventArgs e) { if (e.ColumnIndex == CopyColumn && e.RowIndex >= 0) Copy(e.RowIndex); };
            grid.CellDoubleClick += delegate (object o, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex != CopyColumn) Copy(e.RowIndex); };
            grid.KeyDown += delegate (object o, KeyEventArgs k) { if (k.Control && k.KeyCode == Keys.C && grid.CurrentRow != null) { k.Handled = true; Copy(grid.CurrentRow.Index); } };
            grid.Paint += PaintEmpty;
            grid.CellPainting += PaintCopyButton;
            grid.CellMouseEnter += delegate (object o, DataGridViewCellEventArgs e) { Hover(e.ColumnIndex == CopyColumn ? e.RowIndex : -1); };
            grid.MouseLeave += delegate { Hover(-1); };
            // In a narrow window «Копировать» becomes a copy icon.
            grid.Resize += delegate
            {
                bool narrow = grid.Width < em * 28;
                if (narrow == compact) return;
                compact = narrow;
                grid.Columns[CopyColumn].Width = compact ? em * 2 + em / 2 : em * 6;
                grid.Invalidate();
            };

            status.Dock = DockStyle.Bottom;
            status.Padding = new Padding(0, em / 2, 0, 0);

            // Ends the highlight of new drives and the «Скопировано ✓» of the buttons.
            fade.Interval = 300;
            fade.Tick += delegate
            {
                bool any = false;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    Entry e = row.Tag as Entry;
                    if (e == null) continue;
                    if (e.HighlightUntil != DateTime.MinValue || e.CopiedUntil != DateTime.MinValue) { any = true; Style(row, e); }
                }
                if (!any) fade.Stop();
            };

            Controls.Add(grid);
            Controls.Add(toolbar);
            Controls.Add(status);
        }

        // youSBee shows its own «Обновить» in the window header, so the page row with it can be hidden.
        public bool ShowToolbar
        {
            get { return toolbar.Visible; }
            set { toolbar.Visible = value; }
        }

        public override void Opened() { Reload(false); grid.Focus(); }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { RefreshList(); return true; }
            return false;
        }

        public override void DevicesChanged()
        {
            // Windows needs a moment before the new disk appears in WMI.
            Timer t = new Timer();
            t.Interval = 1500;
            t.Tick += delegate { t.Dispose(); Reload(false); };
            t.Start();
        }

        // «Обновить»: the list of drives connected now; the removed ones go away.
        public void RefreshList() { Reload(true); }

        async void Reload(bool manual)
        {
            if (loading) { again = true; againManual |= manual; return; }
            loading = true;
            try
            {
                List<UsbDisk> disks = await Task.Run(delegate { return UsbDisk.List(); });
                Merge(disks, manual);
                Render();
                if (entries.Count == 0) status.Clear();
                else if (entries.Exists(delegate (Entry e) { return !e.Connected; }))
                    status.Set(Glyph.Info, L.T("Серые строки — отключённые флешки. «Обновить» убирает их из списка."), Theme.Muted);
                else status.Set(Glyph.Info, L.T("Кнопка «Копировать», двойной щелчок или Ctrl+C копирует серийный номер."), Theme.Muted);
            }
            catch (Exception e)
            {
                status.Set(Glyph.Warning, L.T("Не удалось получить список USB-накопителей: ") + e.Message, Theme.Bad);
            }
            finally
            {
                loading = false;
                if (again) { bool m = againManual; again = againManual = false; Reload(m); }
            }
        }

        void Merge(List<UsbDisk> disks, bool manual)
        {
            if (manual) entries.Clear();
            // Drives connected before this update; the others that show up now have just arrived.
            HashSet<string> before = new HashSet<string>();
            foreach (Entry e in entries) { if (e.Connected) before.Add(e.Key); e.Connected = false; }
            foreach (UsbDisk d in disks)
            {
                string key = d.Serial.Length > 0 ? "serial:" + d.Serial : "disk:" + d.Number + "|" + d.Name;
                Entry entry = entries.Find(delegate (Entry e) { return e.Key == key; });
                bool arrived = !before.Contains(key);
                if (entry == null) { entry = new Entry(); entry.Key = key; entries.Add(entry); }
                entry.Disk = d;
                entry.Connected = true;
                // Light up drives that appear while the program is open (not the ones already there at start).
                if (arrived && loaded && !manual) entry.HighlightUntil = DateTime.Now.AddSeconds(3);
            }
            loaded = true;
        }

        void Render()
        {
            Entry selected = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Entry;
            grid.Rows.Clear();
            foreach (Entry e in entries)
            {
                UsbDisk d = e.Disk;
                bool has = d.Serial.Length > 0;
                string name = d.VolumeName.Length > 0 ? d.VolumeName : d.Name;
                string letter = d.DriveLetters.Length > 0 ? d.DriveLetters : "—";
                int i = grid.Rows.Add(e.Connected ? driveIcon : removedIcon, e.Connected ? letter : L.T("отключена"), name,
                    string.Format(L.T("{0:N1} ГБ"), d.Size / 1073741824.0),
                    has ? d.Serial : L.T("устройство не сообщает номер"), has ? L.T("Копировать") : "");
                DataGridViewRow row = grid.Rows[i];
                row.Tag = e;
                row.Cells[IconColumn].ToolTipText = e.Connected ? L.T("Подключена") : L.T("Отключена — «Обновить» уберёт её из списка");
                row.Cells[DiskColumn].ToolTipText = L.T("Диск ") + d.Number + (e.Connected ? "" : " (" + letter + ")");
                row.Cells[NameColumn].ToolTipText = d.VolumeName.Length > 0 && d.Name.Length > 0 ? d.VolumeName + " — " + d.Name : name;
                Style(row, e);
                if (e == selected) grid.CurrentCell = row.Cells[DiskColumn];
            }
            if (selected == null) grid.ClearSelection();
            if (fadeNeeded()) fade.Start();
            grid.Invalidate();
        }

        bool fadeNeeded()
        {
            return entries.Exists(delegate (Entry e) { return e.HighlightUntil > DateTime.Now || e.CopiedUntil > DateTime.Now; });
        }

        // Colours of a row: grey when the drive is removed, lit up for a few seconds when it has just arrived,
        // and «Скопировано ✓» on the button right after copying.
        void Style(DataGridViewRow row, Entry e)
        {
            DateTime now = DateTime.Now;
            if (e.HighlightUntil != DateTime.MinValue && e.HighlightUntil <= now) e.HighlightUntil = DateTime.MinValue;
            if (e.CopiedUntil != DateTime.MinValue && e.CopiedUntil <= now) e.CopiedUntil = DateTime.MinValue;
            bool has = e.Disk.Serial.Length > 0;
            Color text = e.Connected ? Theme.Text : Theme.Muted;
            // Removed: a soft red row, «отключена» in red instead of the letter; just arrived: a soft green row.
            Color back = !e.Connected ? Theme.BadLight : e.HighlightUntil != DateTime.MinValue ? Theme.GoodLight : Color.Empty;
            for (int c = 0; c <= CopyColumn; c++)
            {
                row.Cells[c].Style.ForeColor = c == SerialColumn && !has ? Theme.Warn : c == DiskColumn && !e.Connected ? Theme.Bad : text;
                row.Cells[c].Style.BackColor = back;
                row.Cells[c].Style.SelectionBackColor = back.IsEmpty ? Color.Empty : Theme.Mix(back, Theme.Selection, 0.5);
            }
            if (!has) row.Cells[SerialColumn].Style.Font = Font;
            if (!has) return;
            row.Cells[CopyColumn].Value = e.CopiedUntil != DateTime.MinValue ? L.T("Скопировано ✓") : L.T("Копировать");
            grid.InvalidateCell(row.Cells[CopyColumn]);
        }

        void Hover(int row)
        {
            if (row == hoverRow) return;
            int old = hoverRow;
            hoverRow = row;
            if (old >= 0 && old < grid.Rows.Count) grid.InvalidateCell(CopyColumn, old);
            if (row >= 0 && row < grid.Rows.Count) grid.InvalidateCell(CopyColumn, row);
            grid.Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
        }

        void Copy(int index)
        {
            Entry e = grid.Rows[index].Tag as Entry;
            if (e == null || e.Disk.Serial.Length == 0) return;
            try
            {
                Clipboard.SetText(e.Disk.Serial);
                e.CopiedUntil = DateTime.Now.AddSeconds(2);
                Style(grid.Rows[index], e);
                fade.Start();
                status.Set(Glyph.Check, L.T("Серийный номер скопирован: ") + e.Disk.Serial, Theme.Good);
            }
            catch (Exception ex) { status.Set(Glyph.Warning, L.T("Не удалось скопировать: ") + ex.Message, Theme.Bad); }
        }

        // A grey flash drive with a red cross: removed since the last «Обновить».
        static Bitmap RemovedIcon(int em)
        {
            Bitmap icon = Glyph.Image(Glyph.Usb, em, Theme.Muted);
            using (Graphics g = Graphics.FromImage(icon))
            using (Bitmap cross = Glyph.Image(Glyph.Cross, em * 3 / 5, Theme.Bad))
                g.DrawImage(cross, icon.Width - cross.Width, icon.Height - cross.Height);
            return icon;
        }

        // An empty table says what to do.
        void PaintEmpty(object sender, PaintEventArgs e)
        {
            if (grid.Rows.Count > 0 || !loaded) return;
            int em = Font.Height;
            Rectangle area = grid.DisplayRectangle;
            area.Y += grid.ColumnHeadersHeight;
            area.Height -= grid.ColumnHeadersHeight;
            string text = L.T("Подключите флешку");
            Size size = TextRenderer.MeasureText(text, Font);
            int top = area.Y + Math.Max(0, (area.Height - emptyIcon.Height - em / 2 - size.Height) / 2);
            e.Graphics.DrawImage(emptyIcon, area.X + (area.Width - emptyIcon.Width) / 2, top);
            TextRenderer.DrawText(e.Graphics, text, Font, new Point(area.X + (area.Width - size.Width) / 2, top + emptyIcon.Height + em / 2), Theme.Muted);
        }

        // «Копировать» as a rounded button with a margin inside the cell: blue, green right after copying,
        // plain for a removed drive; in a narrow window a copy icon (a check after copying) instead of text.
        void PaintCopyButton(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != CopyColumn) return;
            e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
            e.Handled = true;
            Entry entry = grid.Rows[e.RowIndex].Tag as Entry;
            if (entry == null || entry.Disk.Serial.Length == 0) return;
            int em = Font.Height;
            Rectangle box = new Rectangle(e.CellBounds.X + em / 4, e.CellBounds.Y + em / 4, e.CellBounds.Width - em / 2 - 1, e.CellBounds.Height - em / 2 - 1);
            bool copied = entry.CopiedUntil != DateTime.MinValue, hover = e.RowIndex == hoverRow;
            Color fill = copied ? Theme.Good : !entry.Connected ? Theme.Surface : hover ? Theme.AccentHover : Theme.Accent;
            Color ink = copied || entry.Connected ? Theme.OnAccent : Theme.Text;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Drawing2D.GraphicsPath shape = Shapes.Round(box, em / 3))
            {
                using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, shape);
                if (!copied && !entry.Connected)
                    using (Pen border = new Pen(hover ? Theme.Accent : Theme.Border)) e.Graphics.DrawPath(border, shape);
            }
            if (compact) Glyph.Draw(e.Graphics, copied ? Glyph.Check : Glyph.Copy, box, ink, em * 0.8f);
            else TextRenderer.DrawText(e.Graphics, Convert.ToString(e.Value), Font, box, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
