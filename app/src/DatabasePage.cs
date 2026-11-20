using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace CompInfo
{
    // «База устройств»: the local copy of the records (view-comp-info.ps1) and the CSV export.
    public class DatabasePage : Page
    {
        readonly TextBox search = Ui.Input(new TextBox());
        // Filters of the web device list: ESET, inventory mark and repeated values.
        readonly ComboBox esetFilter = Ui.Combo();
        readonly ComboBox inventoryFilter = Ui.Combo();
        readonly ComboBox repeatsFilter = Ui.Combo();
        // Words of the search, for highlighting them in the table.
        List<string> words = new List<string>();
        readonly Label count = new Label();
        readonly StatusLine notes = new StatusLine();
        readonly DataGridView grid;
        readonly Label footer;
        // The records, repeats, filters and order: DeviceList.cs, shared with the WPF page.
        readonly DeviceList list = new DeviceList();
        List<RecordFile> all { get { return list.All; } }
        CloudState cloud { get { return list.Cloud; } }
        List<RecordFile> shown = new List<RecordFile>();
        string lastSync = "";
        CurrentComputer me;
        int sortColumn = 0;
        bool descending;

        static string[] Columns { get { return DeviceList.Columns; } }
        const int CommentColumn = DeviceList.CommentColumn;
        // Columns whose text is searched and highlighted (the pills and the date are not).
        static readonly int[] TextColumns = { 0, 1, 2, 3, CommentColumn };

        public DatabasePage()
        {
            Key = "database";
            Title = L.T("База устройств");
            Description = L.T("Записи, сохранённые в этой копии программы. Двойной щелчок или Enter открывает карточку компьютера.");
            Icon = Glyph.Database;
            int em = Font.Height;

            search.Width = em * 16;
            search.Margin = new Padding(0, em / 6, em / 2, em / 3);
            Ui.SetCue(search, L.T("Поиск по всем полям, кириллицей или латиницей"));
            search.TextChanged += delegate { Fill(); };
            esetFilter.Items.AddRange(DeviceList.EsetFilters);
            inventoryFilter.Items.AddRange(DeviceList.InventoryFilters);
            repeatsFilter.Items.AddRange(new object[] { "", "", "", "" });
            RepeatTexts();
            foreach (ComboBox c in new[] { esetFilter, inventoryFilter, repeatsFilter })
            {
                c.SelectedIndex = 0;
                // As wide as the longest item (counts of repeats: room for three digits).
                int widest = 0;
                foreach (object item in c.Items) widest = Math.Max(widest, TextRenderer.MeasureText(Convert.ToString(item) + "000", Font).Width);
                c.Width = widest + em * 2;
                c.Margin = new Padding(0, em / 6, em / 2, em / 3);
                c.SelectedIndexChanged += delegate { Fill(); };
            }
            Button reload = Ui.Secondary(L.T("Обновить"), Glyph.Refresh, Font);
            reload.Click += delegate { Reload(); };
            Button export = Ui.Secondary(L.T("Экспорт в CSV"), Glyph.Export, Font);
            export.Click += delegate { Export(); };
            Ui.Keys(reload, "F5");
            Ui.Keys(export, "Ctrl+E");
            count.AutoSize = true;
            count.ForeColor = Theme.Muted;
            count.Dock = DockStyle.Bottom;
            count.Padding = new Padding(0, em / 3, 0, 0);
            FlowLayoutPanel toolbar = Ui.Row();
            // Search, three filters and two buttons do not fit a narrow window in one row.
            toolbar.WrapContents = true;
            toolbar.Controls.Add(search);
            toolbar.Controls.Add(esetFilter);
            toolbar.Controls.Add(inventoryFilter);
            toolbar.Controls.Add(repeatsFilter);
            toolbar.Controls.Add(reload);
            toolbar.Controls.Add(export);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);

            notes.Dock = DockStyle.Top;

            grid = Ui.Grid(Font);
            grid.Dock = DockStyle.Fill;
            for (int i = 0; i < Columns.Length; i++)
            {
                int index = grid.Columns.Add("c" + i, Columns[i]);
                // Values are short and fully visible; the comment takes the rest of the width.
                grid.Columns[index].MinimumWidth = em * 2;
                grid.Columns[index].SortMode = DataGridViewColumnSortMode.Programmatic;
            }
            // Web table: host name semibold, technical values monospaced, comment muted; ESET and cloud as pills.
            grid.Columns[0].DefaultCellStyle.Font = new Font("Segoe UI Semibold", Font.SizeInPoints);
            grid.Columns[1].DefaultCellStyle.Font = Ui.MonoFont;
            grid.Columns[2].DefaultCellStyle.Font = Ui.MonoFont;
            grid.Columns[CommentColumn].DefaultCellStyle.ForeColor = Theme.Muted;
            Badges.Enable(grid, 5, 6, 7);
            grid.CellPainting += PaintMatches;
            // Widths are fitted to the window (FitColumns): no horizontal scroll bar.
            grid.ScrollBars = ScrollBars.Vertical;
            grid.ColumnHeaderMouseClick += delegate (object o, DataGridViewCellMouseEventArgs e)
            {
                if (!DeviceList.Sortable(e.ColumnIndex)) return;   // ESET and comment are not sortable, as in the console
                if (sortColumn == e.ColumnIndex) descending = !descending; else { sortColumn = e.ColumnIndex; descending = false; }
                Fill();
            };
            grid.CellDoubleClick += delegate (object o, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenCard(e.RowIndex); };
            grid.KeyDown += delegate (object o, KeyEventArgs k)
            {
                if (k.KeyCode == Keys.Enter && grid.CurrentRow != null) { k.SuppressKeyPress = true; OpenCard(grid.CurrentRow.Index); }
            };
            grid.Resize += delegate { FitColumns(); };

            footer = Ui.Paragraph(L.T("Подсвечен этот компьютер. Красным — повторы имени, IP или серийного номера, жёлтым — имя не по правилам, зелёным — собрано за 7 дней."), Font, Theme.Muted);
            footer.Dock = DockStyle.Bottom;

            Controls.Add(grid);
            Controls.Add(notes);
            Controls.Add(toolbar);
            Controls.Add(count);
            Controls.Add(footer);
        }

        public override void Opened()
        {
            Reload();
            search.Focus();
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { Reload(); return true; }
            if (keys == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keys == (Keys.Control | Keys.E)) { Export(); return true; }
            // From the search box, arrows move through the table without leaving the keyboard.
            if (search.Focused && (keys == Keys.Down || keys == Keys.Up || keys == Keys.Enter) && grid.Rows.Count > 0)
            {
                int i = grid.CurrentRow == null ? 0 : grid.CurrentRow.Index;
                if (keys == Keys.Enter) { OpenCard(i); return true; }
                i = Math.Max(0, Math.Min(grid.Rows.Count - 1, i + (keys == Keys.Down ? 1 : -1)));
                Select(i);
                return true;
            }
            return false;
        }

        void Reload()
        {
            if (me == null) me = CurrentComputer.Read();
            string selected = grid.CurrentRow != null && grid.CurrentRow.Index < shown.Count ? shown[grid.CurrentRow.Index].Path : null;
            list.Load(AppPaths.Root);
            List<string> warnings = list.Warnings;
            if (warnings.Count > 0)
                notes.Set(Glyph.Warning, L.T("Пропущены файлы, которые не удалось прочитать (") + warnings.Count + "): " + string.Join("; ", warnings), Theme.Warn);
            else notes.Clear();
            lastSync = list.LastSync;
            RepeatTexts();
            Fill();
            if (selected != null)
            {
                int index = shown.FindIndex(delegate (RecordFile r) { return r.Path == selected; });
                if (index >= 0) Select(index);
            }
        }

        // Repeats filter: how many values repeat per field, refreshed with the base.
        void RepeatTexts()
        {
            string[] texts = list.RepeatFilters;
            for (int i = 0; i < texts.Length; i++)
                if ((string)repeatsFilter.Items[i] != texts[i]) repeatsFilter.Items[i] = texts[i];
        }

        static string HostIssue(string name) { return DeviceList.HostIssue(name); }
        static bool Recent(string value) { return DeviceList.Recent(value); }
        static bool Inventoried(JsonObject d) { return DeviceList.Inventoried(d); }

        // Search words in the text cells: the matched parts on a yellow background, as <mark> on the website.
        void PaintMatches(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (words.Count == 0 || e.RowIndex < 0 || Array.IndexOf(TextColumns, e.ColumnIndex) < 0) return;
            string text = Convert.ToString(e.FormattedValue);
            List<int[]> ranges = LooseSearch.MatchRanges(text, words);
            if (ranges.Count == 0) return;
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, selected);
            Font font = e.CellStyle.Font ?? grid.Font;
            Color fore = selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
            Color back = selected ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
            // Yellow of the brand letters; the colour-blind palette has a single colour, then the warning tone.
            Color mark = Theme.Mix(back, Theme.Rainbow.Length > 1 ? Theme.Rainbow[1] : Theme.Warn, 0.38);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            Rectangle cell = e.CellBounds;
            int x = cell.X + 2 + e.CellStyle.Padding.Left;
            int right = cell.Right - 2;
            Region clip = e.Graphics.Clip;
            e.Graphics.SetClip(cell);
            int at = 0;
            List<int[]> parts = new List<int[]>();
            foreach (int[] r in ranges)
            {
                if (r[0] > at) parts.Add(new[] { at, r[0], 0 });
                parts.Add(new[] { r[0], r[1], 1 });
                at = r[1];
            }
            if (at < text.Length) parts.Add(new[] { at, text.Length, 0 });
            foreach (int[] p in parts)
            {
                if (x >= right) break;
                string piece = text.Substring(p[0], p[1] - p[0]);
                int width = TextRenderer.MeasureText(e.Graphics, piece, font, new Size(int.MaxValue, cell.Height), flags).Width;
                Rectangle box = new Rectangle(x, cell.Y, Math.Min(width, right - x), cell.Height);
                if (p[2] == 1)
                    using (SolidBrush b = new SolidBrush(mark))
                        e.Graphics.FillRectangle(b, new Rectangle(box.X, cell.Y + cell.Height / 6, box.Width, cell.Height - cell.Height / 3));
                TextRenderer.DrawText(e.Graphics, piece, font, box, fore, flags | TextFormatFlags.EndEllipsis);
                x += width;
            }
            e.Graphics.Clip = clip;
            e.Handled = true;
        }

        void Fill()
        {
            words = LooseSearch.Words(search.Text);
            shown = list.Shown(words, esetFilter.SelectedIndex, inventoryFilter.SelectedIndex, repeatsFilter.SelectedIndex, sortColumn, descending);
            bool filtered = words.Count > 0 || esetFilter.SelectedIndex > 0 || inventoryFilter.SelectedIndex > 0 || repeatsFilter.SelectedIndex > 0;

            grid.SuspendLayout();
            grid.Rows.Clear();
            Font bold = new Font(Font, FontStyle.Bold);
            foreach (RecordFile r in shown)
            {
                JsonObject d = r.Data;
                string ip = (d.Text("ip") ?? "").Trim();
                string collected = Fmt.Date(d.Text("collectedAt"));
                object eset = d["esetInstalled"];
                int index = grid.Rows.Add(d.Text("hostname"), ip, d.Text("serial"), d.Text("username"), collected.Length > 10 ? collected.Substring(0, 10) : collected,
                    L.T(r.CloudStatus), ComputerSnapshot.EsetText(eset), Inventoried(d) ? L.T("да") : L.T("нет"), d.Text("comment"));
                DataGridViewRow row = grid.Rows[index];
                row.Cells[7].Tag = Inventoried(d) ? BadgeKind.Ok : BadgeKind.Muted;
                row.Cells[7].ToolTipText = L.T("Отметку ставят на сайте");
                row.Cells[4].ToolTipText = collected;
                // Pills, as the badges of the web interface.
                row.Cells[5].Tag = r.CloudStatus == CloudState.Synced ? BadgeKind.Ok : r.CloudStatus == CloudState.Changed ? BadgeKind.Warn : BadgeKind.Muted;
                row.Cells[6].Tag = eset == null ? BadgeKind.Muted : (eset is bool && (bool)eset) ? BadgeKind.Ok : BadgeKind.Warn;
                // Hostname: not by the rules — yellow, repeated — red (wins), as in the web table.
                List<string> notes = new List<string>();
                string issue = HostIssue(d.Text("hostname"));
                if (issue != null) { row.Cells[0].Style.ForeColor = Theme.Warn; notes.Add(issue); }
                if (list.RepeatedHost(d)) { row.Cells[0].Style.ForeColor = Theme.Bad; notes.Insert(0, L.T("Это имя есть у нескольких устройств")); }
                if (me != null && me.Matches(d))
                {
                    row.DefaultCellStyle.BackColor = Theme.GoodLight;
                    notes.Insert(0, L.T("Этот компьютер"));
                }
                row.Cells[0].ToolTipText = string.Join("\n", notes);
                if (list.RepeatedIp(d))
                {
                    row.Cells[1].Style.ForeColor = Theme.Bad;
                    row.Cells[1].ToolTipText = L.T("Этот IP есть у нескольких устройств");
                }
                if (list.RepeatedSerial(d))
                {
                    row.Cells[2].Style.ForeColor = Theme.Bad;
                    row.Cells[2].ToolTipText = L.T("Этот серийный номер есть у нескольких устройств");
                }
                if (Recent(d.Text("collectedAt")))
                {
                    row.Cells[4].Style.ForeColor = Theme.Good;
                    row.Cells[4].Style.Font = bold;
                    row.Cells[4].ToolTipText = collected + " — " + L.T("собрано за последние 7 дней");
                }
            }            for (int i = 0; i < grid.Columns.Count; i++)
                grid.Columns[i].HeaderCell.SortGlyphDirection = i == sortColumn ? (descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            grid.ResumeLayout();
            FitColumns();
            count.Text = (filtered ? L.T("Найдено: ") + shown.Count + L.T(" из ") + all.Count : L.T("Записей: ") + all.Count) + L.T("   ·   синхронизация: ") + lastSync;
        }

        bool fitting;

        // Short values are shown in full and the comment takes the rest; in a narrow window
        // every column shrinks in proportion instead of a horizontal scroll bar.
        void FitColumns()
        {
            if (fitting || grid.Columns.Count == 0) return;
            fitting = true;
            try
            {
                int em = Font.Height;
                int last = grid.Columns.Count - 1;
                int[] wanted = new int[grid.Columns.Count];
                int sum = 0;
                for (int i = 0; i < last; i++)
                {
                    wanted[i] = grid.Columns[i].GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true) + em / 2 + (i == 5 || i == 6 || i == 7 ? em : 0);
                    sum += wanted[i];
                }
                int available = grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
                // Not enough room: serial number, then user, then the computer name give way;
                // IP, date and statuses always stay whole.
                int deficit = sum + em * 5 - available;
                foreach (int[] shrink in new[] { new[] { 2, em * 5 }, new[] { 3, em * 4 }, new[] { 6, em * 5 }, new[] { 5, em * 5 }, new[] { 0, em * 6 }, new[] { 1, em * 6 } })
                {
                    if (deficit <= 0) break;
                    int cut = Math.Min(deficit, Math.Max(0, wanted[shrink[0]] - shrink[1]));
                    wanted[shrink[0]] -= cut;
                    deficit -= cut;
                }
                for (int i = 0; i < last; i++)
                {
                    grid.Columns[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    grid.Columns[i].Width = wanted[i];
                }
                grid.Columns[last].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                grid.Columns[last].MinimumWidth = em * 4;
            }
            finally { fitting = false; }
        }

        void Select(int index)
        {
            if (index < 0 || index >= grid.Rows.Count) return;
            grid.ClearSelection();
            grid.Rows[index].Selected = true;
            grid.CurrentCell = grid.Rows[index].Cells[0];
        }

        // Development aid for the screenshot mode: the card of the first record, drawn off screen.
        public void SaveCardImage(string file)
        {
            if (shown.Count == 0) return;
            using (RecordDialog dialog = new RecordDialog(new List<RecordFile>(shown), 0, cloud))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-20000, -20000);
                dialog.Show();
                Application.DoEvents();
                using (Bitmap b = new Bitmap(dialog.Width, dialog.Height))
                {
                    dialog.DrawToBitmap(b, new Rectangle(0, 0, dialog.Width, dialog.Height));
                    b.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                }
                dialog.Close();
            }
        }

        void OpenCard(int index)
        {
            if (index < 0 || index >= shown.Count) return;
            // The dialog works on its own copy: a reload replaces the list.
            List<RecordFile> list = new List<RecordFile>(shown);
            using (RecordDialog dialog = new RecordDialog(list, index, cloud))
            {
                dialog.ShowDialog(FindForm());
                string path = list[dialog.Index].Path;
                if (dialog.Changed) Reload();
                Select(shown.FindIndex(delegate (RecordFile r) { return r.Path == path; }));
            }
        }

        void Export()
        {
            string path = CsvExport.DefaultPath();
            try
            {
                int n = CsvExport.Write(AppPaths.Data, path, CloudState.ForDisplay(AppPaths.Root), ';');
                if (Ui.Ask(FindForm(), L.T("Экспорт в CSV"), L.T("Экспортировано устройств: ") + n + "\n\n" + path, Glyph.Check, Theme.Good, L.T("Открыть папку"), L.T("Закрыть")) == 0)
                    Ui.ShowInExplorer(path);
            }
            catch (Exception e) { Ui.Error(FindForm(), L.T("Экспорт не выполнен: ") + e.Message); }
        }
    }

    // Card of one computer: all fields, cloud status and the editable comment.
    public class RecordDialog : Form
    {
        readonly List<RecordFile> records;
        readonly CloudState cloud;
        readonly TableLayoutPanel table = new TableLayoutPanel();
        readonly TextBox comment = Ui.Input(new TextBox());
        readonly Button saveComment;
        readonly Button previous, next;
        readonly StatusLine status = new StatusLine();
        public int Index;
        public bool Changed;

        static string[][] Labels { get { return DeviceList.CardFields; } }

        public RecordDialog(List<RecordFile> records, int index, CloudState cloud)
        {
            this.records = records;
            this.cloud = cloud;
            Index = index;
            Ui.Dialog(this);
            int em = Font.Height;
            Text = L.T("Карточка компьютера");
            FormBorderStyle = FormBorderStyle.Sizable;
            KeyPreview = true;
            ClientSize = new Size(em * 38, em * 34);
            MinimumSize = new Size(em * 30, em * 24);
            Padding = new Padding(em);

            table.Dock = DockStyle.Fill;
            table.ColumnCount = 2;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.AutoScroll = true;

            comment.Multiline = true;
            comment.Height = em * 3;
            comment.Dock = DockStyle.Fill;
            comment.ScrollBars = ScrollBars.Vertical;
            comment.MaxLength = 500;
            comment.TextChanged += delegate { saveComment.Enabled = comment.Text != (Current.Data.Text("comment") ?? ""); };

            saveComment = Ui.Primary(L.T("Сохранить комментарий"), Glyph.Save, Font);
            saveComment.Click += delegate { SaveComment(); };
            previous = Ui.Secondary(L.T("Предыдущая"), Glyph.Back, Font);
            previous.Click += delegate { Step(-1); };
            next = Ui.Secondary(L.T("Следующая"), Glyph.Forward, Font);
            next.Click += delegate { Step(1); };
            Button close = Ui.Secondary(L.T("Закрыть"), null, Font);
            close.Click += delegate { Close(); };
            CancelButton = close;

            FlowLayoutPanel nav = Ui.Row();
            nav.Controls.Add(previous);
            nav.Controls.Add(next);
            nav.Controls.Add(close);
            nav.Dock = DockStyle.Bottom;
            nav.Padding = new Padding(0, em / 2, 0, 0);
            FlowLayoutPanel commentRow = Ui.Row();
            commentRow.Controls.Add(saveComment);
            commentRow.Dock = DockStyle.Bottom;
            commentRow.Padding = new Padding(0, em / 3, 0, 0);
            Label commentLabel = Ui.Text(L.T("Комментарий"), Font, Theme.Muted);
            commentLabel.Dock = DockStyle.Bottom;
            commentLabel.Padding = new Padding(0, em / 2, 0, em / 4);
            Panel commentBox = new Panel();
            commentBox.Dock = DockStyle.Bottom;
            commentBox.Height = em * 3 + 4;
            commentBox.Controls.Add(comment);
            status.Dock = DockStyle.Bottom;

            Controls.Add(table);
            Controls.Add(commentLabel);
            Controls.Add(commentBox);
            Controls.Add(commentRow);
            Controls.Add(status);
            Controls.Add(nav);
            Display(index);
        }

        RecordFile Current { get { return records[Index]; } }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!comment.Focused && (e.KeyCode == Keys.Left || e.KeyCode == Keys.PageUp)) { Step(-1); e.Handled = true; }
            else if (!comment.Focused && (e.KeyCode == Keys.Right || e.KeyCode == Keys.PageDown)) { Step(1); e.Handled = true; }
            base.OnKeyDown(e);
        }

        void Step(int step)
        {
            int target = Index + step;
            if (target < 0 || target >= records.Count) return;
            if (saveComment.Enabled && Ui.Ask(this, L.T("Карточка компьютера"), L.T("Комментарий изменён, но не сохранён. Перейти без сохранения?"), Glyph.Warning, Theme.Warn,
                L.T("Перейти"), L.T("Остаться")) != 0) return;
            Display(target);
        }

        void Display(int index)
        {
            Index = index;
            RecordFile r = Current;
            JsonObject d = r.Data;
            int em = Font.Height;
            Text = L.T("Карточка компьютера — ") + (d.Text("hostname") ?? r.FileName) + " (" + (index + 1) + L.T(" из ") + records.Count + ")";
            table.SuspendLayout();
            table.Controls.Clear();
            table.RowStyles.Clear();
            table.RowCount = 0;
            List<string> known = new List<string>();
            foreach (string[] l in Labels)
            {
                known.Add(l[0]);
                AddField(l[1], Value(d, l[0]));
            }
            known.Add("comment");
            // Fields this version does not know are shown too, so nothing is hidden.
            foreach (string name in d.Names)
                if (!known.Contains(name)) AddField(name, d.Text(name) ?? "");
            AddField(L.T("Облако"), L.T(r.CloudStatus) + L.T("   (последняя синхронизация копии: ") + (string.IsNullOrEmpty(cloud.LastSyncAt) ? L.T("не было") : Fmt.Date(cloud.LastSyncAt)) + ")");
            AddField(L.T("Файл"), r.FileName);
            table.ResumeLayout();
            comment.Text = d.Text("comment") ?? "";
            comment.Select(comment.Text.Length, 0);
            saveComment.Enabled = false;
            previous.Enabled = index > 0;
            next.Enabled = index < records.Count - 1;
            status.Clear();
        }

        static string Value(JsonObject d, string name) { return DeviceList.CardValue(d, name); }

        void AddField(string label, string value)
        {
            int em = Font.Height;
            Label l = Ui.Text(label, Font, Theme.Muted);
            l.Margin = new Padding(0, em / 6, em, em / 6);
            // Read-only text box: the value can be selected and copied.
            TextBox v = new TextBox();
            v.ReadOnly = true;
            v.BorderStyle = BorderStyle.None;
            v.BackColor = Theme.Back;
            v.Text = value.Length == 0 ? "—" : value;
            v.ForeColor = value.Length == 0 ? Theme.Muted : Theme.Text;
            v.Dock = DockStyle.Fill;
            v.Margin = new Padding(0, em / 6, 0, em / 6);
            v.TabStop = false;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(l, 0, table.RowCount);
            table.Controls.Add(v, 1, table.RowCount);
            table.RowCount++;
        }

        void SaveComment()
        {
            RecordFile r = Current;
            try
            {
                JsonObject data = Files.ReadRecord(r.Path);
                data["comment"] = comment.Text;
                Files.WriteJson(r.Path, data);
                RecordFile updated = Records.Load(r.Path, cloud);
                records[Index] = updated;
                Changed = true;
                Display(Index);
                status.Set(Glyph.Check, L.T("Комментарий сохранён. При следующей синхронизации он попадёт в облако."), Theme.Good);
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось сохранить комментарий: ") + e.Message, Theme.Bad); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (saveComment.Enabled && Ui.Ask(this, L.T("Карточка компьютера"), L.T("Комментарий изменён, но не сохранён. Закрыть без сохранения?"), Glyph.Warning, Theme.Warn,
                L.T("Закрыть"), L.T("Остаться")) != 0) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
