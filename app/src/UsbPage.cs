using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // Used by comp-info.exe and by the standalone youSBee.exe (YouSBee.cs).
    // «USB-накопители»: serial numbers of connected flash drives (get-usb-serial.ps1).
    public class UsbPage : Page
    {
        // Columns: flash drive icon, disk, name, size, serial number, «Копировать».
        const int NameColumn = 2, SerialColumn = 4, CopyColumn = 5;
        readonly DataGridView grid;
        readonly FlowLayoutPanel toolbar;
        readonly StatusLine status = new StatusLine();
        readonly Bitmap driveIcon;
        bool loading, again;

        public UsbPage()
        {
            Key = "usb";
            Title = L.T("USB-накопители");
            Description = L.T("Серийные номера подключённых флешек и USB-дисков. Список обновляется сам, когда накопитель подключают или отключают.");
            Icon = Glyph.Usb;
            int em = Font.Height;
            driveIcon = Glyph.Image(Glyph.Usb, em, Theme.Flash);

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
            copy.DefaultCellStyle.BackColor = Theme.Accent;
            copy.DefaultCellStyle.ForeColor = Theme.OnAccent;
            copy.DefaultCellStyle.SelectionBackColor = Theme.AccentHover;
            copy.DefaultCellStyle.SelectionForeColor = Theme.OnAccent;
            copy.DefaultCellStyle.Padding = new Padding(em / 4, 3, em / 4, 3);
            grid.Columns.Add(copy);
            grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
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

            status.Dock = DockStyle.Bottom;
            status.Padding = new Padding(0, em / 2, 0, 0);

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

        public override void Opened() { RefreshList(); grid.Focus(); }

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
            t.Tick += delegate { t.Dispose(); RefreshList(); };
            t.Start();
        }

        public async void RefreshList()
        {
            if (loading) { again = true; return; }
            loading = true;
            try
            {
                List<UsbDisk> disks = await Task.Run(delegate { return UsbDisk.List(); });
                grid.Rows.Clear();
                foreach (UsbDisk d in disks)
                {
                    bool has = d.Serial.Length > 0;
                    string diskName = L.T("Диск ") + d.Number + (d.DriveLetters.Length > 0 ? " (" + d.DriveLetters + ")" : "");
                    string name = d.VolumeName.Length > 0 ? d.VolumeName : d.Name;
                    int i = grid.Rows.Add(driveIcon, diskName, name, string.Format(L.T("{0:N1} ГБ"), d.Size / 1073741824.0),
                        has ? d.Serial : L.T("устройство не сообщает номер"), has ? L.T("Копировать") : "");
                    DataGridViewRow row = grid.Rows[i];
                    row.Cells[NameColumn].ToolTipText = d.VolumeName.Length > 0 && d.Name.Length > 0 ? d.VolumeName + " — " + d.Name : name;
                    row.Tag = d;
                    if (!has)
                    {
                        row.Cells[SerialColumn].Style.ForeColor = Theme.Warn;
                        row.Cells[SerialColumn].Style.Font = Font;
                        // No serial: the button cell looks like an empty cell.
                        row.Cells[CopyColumn].Style.BackColor = row.Cells[CopyColumn].Style.SelectionBackColor = Theme.Surface;
                    }
                }
                if (disks.Count == 0) status.Set(Glyph.Info, L.T("USB-накопители не найдены. Подключите флешку."), Theme.Warn);
                else status.Set(Glyph.Info, L.T("Кнопка «Копировать», двойной щелчок или Ctrl+C копирует серийный номер."), Theme.Muted);
            }
            catch (Exception e)
            {
                status.Set(Glyph.Warning, L.T("Не удалось получить список USB-накопителей: ") + e.Message, Theme.Bad);
            }
            finally
            {
                loading = false;
                if (again) { again = false; RefreshList(); }
            }
        }

        void Copy(int index)
        {
            UsbDisk d = grid.Rows[index].Tag as UsbDisk;
            if (d == null || d.Serial.Length == 0) return;
            try
            {
                Clipboard.SetText(d.Serial);
                status.Set(Glyph.Check, L.T("Серийный номер скопирован: ") + d.Serial, Theme.Good);
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось скопировать: ") + e.Message, Theme.Bad); }
        }
    }
}
