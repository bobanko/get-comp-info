using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace CompInfo
{
    // «База устройств»: the local copy of the records (view-comp-info.ps1) and the CSV export.
    public class DatabasePage : Page
    {
        readonly TextBox search = new TextBox();
        readonly Label count = new Label();
        readonly StatusLine notes = new StatusLine();
        readonly DataGridView grid = new DataGridView();
        readonly Label footer = new Label();
        List<RecordFile> all = new List<RecordFile>();
        List<RecordFile> shown = new List<RecordFile>();
        Dictionary<string, int> ipCounts = new Dictionary<string, int>();
        CloudState cloud = new CloudState();
        string lastSync = "";
        CurrentComputer me;
        int sortColumn = 0;
        bool descending;

        static readonly string[] Columns = { "Имя компьютера", "IP-адрес", "Серийный номер", "Пользователь", "Собрано", "Облако", "ESET", "Комментарий" };

        public DatabasePage()
        {
            Key = "database";
            Title = "База устройств";
            Description = "Записи, сохранённые в этой копии программы. Двойной щелчок или Enter открывает карточку компьютера.";
            Icon = Glyph.Database;
            int em = Font.Height;

            search.Width = em * 22;
            search.Margin = new Padding(0, em / 6, em / 2, 0);
            Ui.SetCue(search, "Поиск: имя, серийный номер, пользователь…");
            search.TextChanged += delegate { Fill(); };
            Button reload = Ui.Secondary("Обновить", Glyph.Refresh, Font);
            reload.Click += delegate { Reload(); };
            Button export = Ui.Secondary("Экспорт в CSV", Glyph.Export, Font);
            export.Click += delegate { Export(); };
            count.AutoSize = true;
            count.ForeColor = Theme.Muted;
            count.Dock = DockStyle.Bottom;
            count.Padding = new Padding(0, em / 3, 0, 0);
            FlowLayoutPanel toolbar = Ui.Row();
            toolbar.Controls.Add(search);
            toolbar.Controls.Add(reload);
            toolbar.Controls.Add(export);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);

            notes.Dock = DockStyle.Top;

            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Theme.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Sidebar;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Sidebar;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersHeight = em * 2;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowTemplate.Height = em + em / 2;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0xCC, 0xE4, 0xF7);
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            for (int i = 0; i < Columns.Length; i++)
            {
                int index = grid.Columns.Add("c" + i, Columns[i]);
                // Values are short and fully visible; the comment takes the rest of the width.
                grid.Columns[index].MinimumWidth = em * 2;
                grid.Columns[index].SortMode = DataGridViewColumnSortMode.Programmatic;
            }
            grid.ColumnHeaderMouseClick += delegate (object o, DataGridViewCellMouseEventArgs e)
            {
                if (e.ColumnIndex == 6 || e.ColumnIndex == 7) return;   // ESET and comment are not sortable, as in the console
                if (sortColumn == e.ColumnIndex) descending = !descending; else { sortColumn = e.ColumnIndex; descending = false; }
                Fill();
            };
            grid.CellDoubleClick += delegate (object o, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenCard(e.RowIndex); };
            grid.KeyDown += delegate (object o, KeyEventArgs k)
            {
                if (k.KeyCode == Keys.Enter && grid.CurrentRow != null) { k.SuppressKeyPress = true; OpenCard(grid.CurrentRow.Index); }
            };
            Ui.DoubleBuffer(grid);
            grid.Resize += delegate { FitColumns(); };

            footer.Dock = DockStyle.Bottom;
            footer.AutoSize = false;
            footer.Height = em + em / 2;
            footer.ForeColor = Theme.Muted;
            footer.TextAlign = ContentAlignment.TopLeft;
            footer.Text = "Зелёный фон — этот компьютер. Красный IP-адрес — такой же адрес есть у другой записи.";

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

        void Reload()
        {
            if (me == null) me = CurrentComputer.Read();
            string selected = grid.CurrentRow != null && grid.CurrentRow.Index < shown.Count ? shown[grid.CurrentRow.Index].Path : null;
            List<string> warnings = new List<string>();
            cloud = CloudState.ForDisplay(AppPaths.Root);
            all = Records.ReadAll(AppPaths.Data, cloud, warnings);
            // Compare full addresses across the whole base, before the search.
            ipCounts = new Dictionary<string, int>();
            foreach (RecordFile r in all)
            {
                string ip = (r.Data.Text("ip") ?? "").Trim();
                if (ip.Length == 0) continue;
                int n;
                ipCounts[ip] = ipCounts.TryGetValue(ip, out n) ? n + 1 : 1;
            }
            if (warnings.Count > 0)
                notes.Set(Glyph.Warning, "Пропущены файлы, которые не удалось прочитать (" + warnings.Count + "): " + string.Join("; ", warnings), Theme.Warn);
            else notes.Clear();
            lastSync = string.IsNullOrEmpty(cloud.LastSyncAt) ? "не было" : Ui.Date(cloud.LastSyncAt);
            Fill();
            if (selected != null)
            {
                int index = shown.FindIndex(delegate (RecordFile r) { return r.Path == selected; });
                if (index >= 0) Select(index);
            }
        }

        static long IpOrder(string value)
        {
            string[] octets = (value ?? "").Trim().Split('.');
            if (octets.Length != 4) return long.MaxValue;
            long n = 0;
            foreach (string o in octets)
            {
                int v;
                if (o.Length == 0 || o.Length > 3 || !int.TryParse(o, NumberStyles.None, CultureInfo.InvariantCulture, out v) || v > 255) return long.MaxValue;
                n = n * 256 + v;
            }
            return n;
        }

        static string EsetShort(object value)
        {
            if (value == null) return "—";
            return value is bool && (bool)value ? "да" : "нет";
        }

        static int CloudOrder(string status)
        {
            return status == CloudState.Synced ? 0 : status == CloudState.Changed ? 1 : 2;
        }

        static DateTime DateOf(string value)
        {
            DateTime d;
            return DateTime.TryParse(value ?? "", out d) ? d : DateTime.MinValue;
        }

        int Compare(RecordFile a, RecordFile b)
        {
            StringComparer text = StringComparer.Create(CultureInfo.CurrentCulture, true);
            string ha = a.Data.Text("hostname") ?? "", hb = b.Data.Text("hostname") ?? "";
            int c;
            switch (sortColumn)
            {
                case 1: c = IpOrder(a.Data.Text("ip")).CompareTo(IpOrder(b.Data.Text("ip"))); break;
                case 2: c = text.Compare(a.Data.Text("serial") ?? "", b.Data.Text("serial") ?? ""); break;
                case 3:
                    string ua = a.Data.Text("username") ?? "", ub = b.Data.Text("username") ?? "";
                    // Records without a user go last.
                    c = (ua.Trim().Length == 0).CompareTo(ub.Trim().Length == 0);
                    if (c == 0) c = text.Compare(ua, ub);
                    break;
                case 4: c = DateOf(a.Data.Text("collectedAt")).CompareTo(DateOf(b.Data.Text("collectedAt"))); break;
                case 5: c = CloudOrder(a.CloudStatus).CompareTo(CloudOrder(b.CloudStatus)); break;
                default: c = 0; break;
            }
            if (c == 0) c = text.Compare(ha, hb);
            return descending ? -c : c;
        }

        void Fill()
        {
            string needle = search.Text.Trim();
            shown = new List<RecordFile>();
            foreach (RecordFile r in all)
            {
                if (needle.Length > 0)
                {
                    string haystack = string.Join(" \n", new string[] { r.Data.Text("hostname"), r.Data.Text("serial"), r.Data.Text("username"),
                        r.Data.Text("manufacturer"), r.Data.Text("model"), r.Data.Text("comment") });
                    if (haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                shown.Add(r);
            }
            // Stable sort: equal rows keep the file order.
            List<KeyValuePair<int, RecordFile>> indexed = new List<KeyValuePair<int, RecordFile>>();
            for (int i = 0; i < shown.Count; i++) indexed.Add(new KeyValuePair<int, RecordFile>(i, shown[i]));
            indexed.Sort(delegate (KeyValuePair<int, RecordFile> a, KeyValuePair<int, RecordFile> b)
            {
                int c = Compare(a.Value, b.Value);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            shown.Clear();
            foreach (KeyValuePair<int, RecordFile> p in indexed) shown.Add(p.Value);

            grid.SuspendLayout();
            grid.Rows.Clear();
            foreach (RecordFile r in shown)
            {
                JsonObject d = r.Data;
                string ip = (d.Text("ip") ?? "").Trim();
                int index = grid.Rows.Add(d.Text("hostname"), ip, d.Text("serial"), d.Text("username"), Ui.Date(d.Text("collectedAt")),
                    r.CloudStatus, EsetShort(d["esetInstalled"]), d.Text("comment"));
                DataGridViewRow row = grid.Rows[index];
                if (me != null && me.Matches(d))
                {
                    row.DefaultCellStyle.BackColor = Theme.GoodLight;
                    row.Cells[0].Style.Font = new Font(Font, FontStyle.Bold);
                    row.Cells[0].ToolTipText = "Этот компьютер";
                }
                int same;
                if (ip.Length > 0 && ipCounts.TryGetValue(ip, out same) && same > 1)
                {
                    row.Cells[1].Style.ForeColor = Theme.Bad;
                    row.Cells[1].ToolTipText = "Этот IP-адрес есть ещё у " + (same - 1) + " записи(ей)";
                }
                row.Cells[5].Style.ForeColor = r.CloudStatus == CloudState.Synced ? Theme.Good : r.CloudStatus == CloudState.Changed ? Theme.Warn : Theme.Muted;
                object eset = d["esetInstalled"];
                row.Cells[6].Style.ForeColor = eset == null ? Theme.Muted : (eset is bool && (bool)eset) ? Theme.Good : Theme.Warn;
                row.Cells[6].ToolTipText = ComputerSnapshot.EsetText(eset);
            }
            for (int i = 0; i < grid.Columns.Count; i++)
                grid.Columns[i].HeaderCell.SortGlyphDirection = i == sortColumn ? (descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            grid.ResumeLayout();
            FitColumns();
            count.Text = (needle.Length > 0 ? "Найдено: " + shown.Count + " из " + all.Count : "Записей: " + all.Count) + "   ·   синхронизация: " + lastSync;
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
                    wanted[i] = grid.Columns[i].GetPreferredWidth(DataGridViewAutoSizeColumnMode.AllCells, true) + em / 2;
                    sum += wanted[i];
                }
                int available = grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2;
                bool roomy = sum + em * 8 <= available;
                for (int i = 0; i <= last; i++)
                {
                    DataGridViewColumn c = grid.Columns[i];
                    c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    // Fill weights: real widths when everything fits (the comment gets the rest), proportions otherwise.
                    c.FillWeight = i == last ? (roomy ? Math.Max(1, available - sum) : em * 8) : wanted[i];
                }
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
                if (MessageBox.Show(FindForm(), "Экспортировано устройств: " + n + "\n\n" + path + "\n\nОткрыть папку с файлом?", "Экспорт в CSV",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Ui.ShowInExplorer(path);
            }
            catch (Exception e) { Ui.Error(FindForm(), "Экспорт не выполнен: " + e.Message); }
        }
    }

    // Card of one computer: all fields, cloud status and the editable comment.
    public class RecordDialog : Form
    {
        readonly List<RecordFile> records;
        readonly CloudState cloud;
        readonly TableLayoutPanel table = new TableLayoutPanel();
        readonly TextBox comment = new TextBox();
        readonly Button saveComment;
        readonly Button previous, next;
        readonly StatusLine status = new StatusLine();
        public int Index;
        public bool Changed;

        static readonly string[][] Labels =
        {
            new[] { "hostname", "Имя компьютера" }, new[] { "computerId", "ID компьютера" }, new[] { "computerIdSource", "Источник ID" },
            new[] { "serial", "Серийный номер" }, new[] { "smbiosUuid", "UUID" }, new[] { "windowsMachineGuid", "ID установки Windows" },
            new[] { "manufacturer", "Производитель" }, new[] { "model", "Модель" }, new[] { "windows", "Windows" },
            new[] { "partOfDomain", "Член домена" }, new[] { "domain", "Домен / группа" }, new[] { "ip", "IP-адрес" }, new[] { "mac", "MAC-адрес" },
            new[] { "username", "Пользователь" }, new[] { "esetInstalled", "ESET" }, new[] { "createdAt", "Создана" }, new[] { "collectedAt", "Собрана" },
        };

        public RecordDialog(List<RecordFile> records, int index, CloudState cloud)
        {
            this.records = records;
            this.cloud = cloud;
            Index = index;
            Font = new Font("Segoe UI", 10f);
            int em = Font.Height;
            Text = "Карточка компьютера";
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            ClientSize = new Size(em * 40, em * 36);
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

            saveComment = Ui.Primary("Сохранить комментарий", Glyph.Save, Font);
            saveComment.Click += delegate { SaveComment(); };
            previous = Ui.Secondary("Предыдущая", Glyph.Back, Font);
            previous.Click += delegate { Step(-1); };
            next = Ui.Secondary("Следующая", Glyph.Forward, Font);
            next.Click += delegate { Step(1); };
            Button close = Ui.Secondary("Закрыть", null, Font);
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
            Label commentLabel = Ui.Text("Комментарий", Font, Theme.Muted);
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
            if (saveComment.Enabled && MessageBox.Show(this, "Комментарий изменён, но не сохранён. Перейти без сохранения?", "Карточка компьютера",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Display(target);
        }

        void Display(int index)
        {
            Index = index;
            RecordFile r = Current;
            JsonObject d = r.Data;
            int em = Font.Height;
            Text = "Карточка компьютера — " + (d.Text("hostname") ?? r.FileName) + " (" + (index + 1) + " из " + records.Count + ")";
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
            AddField("Облако", r.CloudStatus + "   (последняя синхронизация копии: " + (string.IsNullOrEmpty(cloud.LastSyncAt) ? "не было" : Ui.Date(cloud.LastSyncAt)) + ")");
            AddField("Файл", r.FileName);
            table.ResumeLayout();
            comment.Text = d.Text("comment") ?? "";
            comment.Select(comment.Text.Length, 0);
            saveComment.Enabled = false;
            previous.Enabled = index > 0;
            next.Enabled = index < records.Count - 1;
            status.Clear();
        }

        static string Value(JsonObject d, string name)
        {
            if (name == "esetInstalled") return ComputerSnapshot.EsetText(d[name]);
            if (name == "createdAt" || name == "collectedAt") return Ui.Date(d.Text(name));
            if (name == "partOfDomain") { object v = d[name]; return v == null ? "" : (v is bool && (bool)v ? "да" : "нет"); }
            return d.Text(name) ?? "";
        }

        void AddField(string label, string value)
        {
            int em = Font.Height;
            Label l = Ui.Text(label, Font, Theme.Muted);
            l.Margin = new Padding(0, em / 6, em, em / 6);
            // Read-only text box: the value can be selected and copied.
            TextBox v = new TextBox();
            v.ReadOnly = true;
            v.BorderStyle = BorderStyle.None;
            v.BackColor = Color.White;
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
                status.Set(Glyph.Check, "Комментарий сохранён. При следующей синхронизации он попадёт в облако.", Theme.Good);
            }
            catch (Exception e) { status.Set(Glyph.Warning, "Не удалось сохранить комментарий: " + e.Message, Theme.Bad); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (saveComment.Enabled && MessageBox.Show(this, "Комментарий изменён, но не сохранён. Закрыть без сохранения?", "Карточка компьютера",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
