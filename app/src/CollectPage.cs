using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // «Сбор информации»: reads this computer and saves its record to data\ by itself: right after the program
    // starts, whenever the data differ from the record, and after the comment is edited. The page shows the
    // computer, where its record is (the database next to the program, the cloud) and what this collection changed.
    public class CollectPage : Page
    {
        // Off for screenshots of the interface: they must not write to the database.
        public static bool AutoSave = true;

        // Order of the values; the last ones are technical identifiers, shown in grey.
        static readonly string[] ListedFields = { "hostname", "ip", "mac", "username", "partOfDomain", "domain", "windows", "computer", "serial", "computerId", "windowsMachineGuid" };
        static readonly string[] TechnicalFields = { "computerId", "windowsMachineGuid" };

        readonly ProgressBar progress = new ProgressBar();
        readonly StatusLine state = new StatusLine();

        readonly Panel header = new Panel();
        readonly Label hostname;
        readonly Label summary;
        readonly Pill eset = new Pill();
        readonly Pill inventory = new Pill();

        readonly PictureBox localIcon = new PictureBox();
        readonly Label localTitle;
        readonly Label localText;
        readonly LinkLabel showFile = new LinkLabel();
        readonly PictureBox cloudIcon = new PictureBox();
        readonly Label cloudText;
        readonly Button sync;

        readonly DataGridView grid;
        readonly TextBox comment = Ui.Input(new TextBox());
        readonly Button again;
        readonly TableLayoutPanel body = new TableLayoutPanel();
        // The comment is saved a moment after typing stops, and at once when the page or the window closes.
        readonly Timer commentTimer = new Timer();
        Collection collection;
        // The record as it was before this collection saved anything: what changed is shown against it.
        JsonObject before;
        bool started, busy, filling;

        public CollectPage()
        {
            Key = "collect";
            Title = L.T("Сбор информации о компьютере");
            NavTitle = L.T("Сбор информации");
            Description = L.T("Сведения об этом компьютере сохраняются в базу рядом с программой сами: при запуске и при любом изменении, в том числе комментария. В облако запись попадает при синхронизации.");
            Icon = Glyph.Computer;
            int em = Font.Height;

            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 30;
            progress.Height = em / 3;
            progress.Margin = new Padding(0, 0, 0, em / 2);
            state.Margin = new Padding(0, 0, 0, em / 2);

            // Header: the name, the model line and the marks of the web interface.
            hostname = Ui.Text("", new Font("Segoe UI Semibold", 15f), Theme.Text);
            summary = Ui.Text("", Font, Theme.Muted);
            FlowLayoutPanel pills = Ui.Row();
            pills.Controls.Add(eset);
            pills.Controls.Add(inventory);
            pills.Margin = new Padding(0, em / 3, 0, 0);
            ToolTip tips = new ToolTip();
            tips.SetToolTip(inventory, L.T("Отметку ставят на сайте; программа её только показывает и сохраняет при новом сборе."));
            FlowLayoutPanel headerStack = new FlowLayoutPanel();
            headerStack.FlowDirection = FlowDirection.TopDown;
            headerStack.WrapContents = false;
            headerStack.AutoSize = true;
            headerStack.Margin = new Padding(0);
            headerStack.Controls.Add(hostname);
            headerStack.Controls.Add(summary);
            headerStack.Controls.Add(pills);
            header.AutoSize = true;
            header.Margin = new Padding(0, 0, 0, em / 2);
            header.Controls.Add(headerStack);

            // Where the record is: the database next to the program, then the cloud.
            TableLayoutPanel storage = new TableLayoutPanel();
            storage.AutoSize = true;
            storage.ColumnCount = 4;
            storage.RowCount = 2;
            storage.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            storage.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            storage.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            storage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            storage.Margin = new Padding(0, 0, 0, em / 2);
            Font strong = new Font("Segoe UI Semibold", Font.SizeInPoints);
            localTitle = Ui.Text(AppPaths.OnRemovableDrive ? L.T("База на флешке") : L.T("База программы"), strong, Theme.Text);
            localText = Ui.Text("", Font, Theme.Muted);
            Label cloudTitle = Ui.Text(L.T("Облако"), strong, Theme.Text);
            cloudText = Ui.Text("", Font, Theme.Muted);
            foreach (PictureBox icon in new[] { localIcon, cloudIcon })
            {
                icon.Size = new Size(em + em / 4, em + em / 4);
                icon.SizeMode = PictureBoxSizeMode.CenterImage;
                icon.Margin = new Padding(0, em / 6, em / 3, 0);
            }
            foreach (Label l in new[] { localTitle, cloudTitle }) l.Margin = new Padding(0, em / 4, em, 0);
            foreach (Label l in new[] { localText, cloudText }) l.Margin = new Padding(0, em / 4, em, 0);
            showFile.Text = L.T("Показать файл в папке");
            showFile.AutoSize = true;
            showFile.LinkColor = showFile.ActiveLinkColor = Theme.Info;
            showFile.Margin = new Padding(0, em / 4, 0, 0);
            showFile.Visible = false;
            showFile.LinkClicked += delegate { if (collection != null) Ui.ShowInExplorer(collection.FilePath); };
            sync = Ui.Secondary(L.T("Синхронизировать"), Glyph.Sync, Font);
            sync.Margin = new Padding(0, 0, 0, em / 4);
            sync.Visible = false;
            sync.Click += delegate
            {
                if (Main == null) return;
                FlushComment();
                Main.Open("sync");
                SyncPage page = Main.Find("sync") as SyncPage;
                if (page != null) page.Start();
            };
            storage.Controls.Add(localIcon, 0, 0);
            storage.Controls.Add(localTitle, 1, 0);
            storage.Controls.Add(localText, 2, 0);
            storage.Controls.Add(showFile, 3, 0);
            storage.Controls.Add(cloudIcon, 0, 1);
            storage.Controls.Add(cloudTitle, 1, 1);
            storage.Controls.Add(cloudText, 2, 1);
            storage.Controls.Add(sync, 3, 1);

            // All values in one column; what this collection changed is marked and shows the previous value.
            grid = Ui.Grid(Font);
            grid.ColumnHeadersVisible = false;
            grid.Columns.Add("field", "");
            grid.Columns.Add("value", "");
            grid.Columns.Add("was", "");
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns[0].FillWeight = 24;
            grid.Columns[1].FillWeight = 46;
            grid.Columns[2].FillWeight = 30;
            grid.Columns[0].DefaultCellStyle.ForeColor = Theme.Muted;
            grid.Columns[2].DefaultCellStyle.ForeColor = Theme.Muted;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Margin = new Padding(0, 0, 0, em / 2);

            Label commentLabel = Ui.Text(L.T("Комментарий (необязательно): кабинет, ответственный, особенности. Сохраняется сам."), Font, Theme.Text);
            commentLabel.Margin = new Padding(0, 0, 0, em / 4);
            comment.MaxLength = 500;
            comment.Margin = new Padding(0, 0, 0, em / 2);
            commentTimer.Interval = 1000;
            commentTimer.Tick += delegate { commentTimer.Stop(); SaveChanges(); };
            comment.TextChanged += delegate
            {
                if (collection == null || filling) return;
                if (collection.HasChanges(comment.Text)) SetLocal(Glyph.More, L.T("комментарий сейчас сохранится…"), Theme.Muted);
                commentTimer.Stop();
                commentTimer.Start();
            };
            comment.KeyDown += delegate (object o, KeyEventArgs k)
            {
                if (k.KeyCode == Keys.Enter) { k.SuppressKeyPress = true; FlushComment(); }
            };
            comment.Leave += delegate { FlushComment(); };

            again = Ui.Secondary(L.T("Собрать заново"), Glyph.Refresh, Font);
            again.Click += delegate { Collect(); };
            Ui.Keys(again, "F5");
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(again);
            actions.Margin = new Padding(0, 0, 0, em / 3);

            body.Dock = DockStyle.Fill;
            body.ColumnCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(progress, false);
            AddRow(state, false);
            AddRow(header, false);
            AddRow(storage, false);
            AddRow(grid, true);
            AddRow(commentLabel, false);
            AddRow(comment, false);
            AddRow(actions, false);
            Controls.Add(body);
            ShowRecord();
            SetBusy(false);
        }

        void AddRow(Control c, bool fill)
        {
            body.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            // Auto-sized blocks (header, storage lines, rows of buttons, labels) keep their size; the rest fills the cell.
            bool autoSized = c is FlowLayoutPanel || c is LinkLabel || c is Label || c == header;
            if (c is TableLayoutPanel) c.Dock = DockStyle.Top;
            else if (!autoSized) c.Dock = DockStyle.Fill;
            // Explicit cell: a hidden control must keep its row, or the rows below shift into the wrong styles.
            body.RowCount = body.RowStyles.Count;
            body.Controls.Add(c, 0, body.RowStyles.Count - 1);
        }

        // Every opening reads the computer again: whatever changed since (name, address, domain) is saved at once.
        public override void Opened()
        {
            started = true;
            if (!busy) Collect();
            else comment.Focus();
        }

        // Called when the program starts on another section: the computer is read and saved in the background.
        public void StartInBackground()
        {
            if (started) return;
            started = true;
            Collect();
        }

        public override bool CanLeave()
        {
            FlushComment();
            return true;
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { Collect(); return true; }
            if (keys == (Keys.Control | Keys.S)) { FlushComment(); return true; }
            return false;
        }

        void SetBusy(bool value)
        {
            busy = value;
            progress.Visible = value;
            again.Enabled = !value;
            comment.Enabled = !value && collection != null;
        }

        void FlushComment()
        {
            if (!commentTimer.Enabled) return;
            commentTimer.Stop();
            SaveChanges();
        }

        async void Collect()
        {
            if (busy) return;
            FlushComment();
            collection = null;
            before = null;
            ShowRecord();
            state.Set(Glyph.More, L.T("Собираем информацию о компьютере…"), Theme.Muted);
            SetBusy(true);
            string dataPath = AppPaths.Data;
            try
            {
                Collection c = await Task.Run(delegate { return Collection.Start(dataPath); });
                if (c.Legacy != null && Parent == null)
                {
                    // Read in the background while another section is open: ask when the user comes here.
                    started = false;
                    state.Set(Glyph.Info, L.T("Найдена старая запись с таким же именем компьютера: откройте этот раздел, чтобы решить, чья она."), Theme.Warn);
                    return;
                }
                if (c.Legacy != null)
                {
                    string text = L.T("Найдена старая запись без UUID и серийного номера с таким же именем компьютера:\n\n") +
                        System.IO.Path.GetFileName(c.Legacy.Path) + "\n" +
                        L.T("Имя: ") + c.Legacy.Record.Text("hostname") + L.T("\nСобрана: ") + Fmt.Date(c.Legacy.Record.Text("collectedAt")) +
                        L.T("\nКомментарий: ") + (c.Legacy.Record.Text("comment") ?? "") +
                        L.T("\n\nЭто запись именно этого компьютера? Если да, она будет обновлена и привязана к этой установке Windows.");
                    if (Ui.Ask(FindForm(), L.T("Старая запись"), text, Glyph.Info, Theme.Info, L.T("Да, это этот компьютер"), L.T("Нет")) != 0)
                    {
                        state.Set(Glyph.Info, L.T("Сбор отменён: старую запись не привязали. Нажмите «Собрать заново», чтобы решить ещё раз."), Theme.Warn);
                        return;
                    }
                    c.BindLegacy(dataPath);
                }
                if (c.PreviousError != null)
                {
                    state.Set(Glyph.Warning, L.T("Не удалось прочитать сохранённую запись этого компьютера: ") + c.PreviousError, Theme.Bad);
                    return;
                }
                collection = c;
                before = c.Previous;
                state.Clear();
                filling = true;
                comment.Text = c.PreviousComment;
                filling = false;
                ShowRecord();
                busy = false;
                SaveChanges();
                if (Parent != null)
                {
                    comment.Focus();
                    comment.SelectionStart = comment.Text.Length;
                }
            }
            catch (Exception e)
            {
                state.Set(Glyph.Warning, L.T("Не удалось собрать информацию: ") + e.Message, Theme.Bad);
            }
            finally { SetBusy(false); }
        }

        // The header, the values and both storage lines for the collection (empty while nothing is read).
        void ShowRecord()
        {
            Collection c = collection;
            header.Visible = c != null;
            grid.Visible = c != null;
            if (c == null)
            {
                grid.Rows.Clear();
                SetLocal(null, "", Theme.Muted);
                SetCloud(null, "", Theme.Muted, false);
                showFile.Visible = false;
                return;
            }
            JsonObject current = c.Build(comment.Text, "");

            hostname.Text = Shown(Collection.Show(current, "hostname"));
            List<string> parts = new List<string>();
            string computer = Collection.Show(current, "computer");
            if (!string.IsNullOrEmpty(computer)) parts.Add(computer);
            if (!string.IsNullOrEmpty(current.Text("serial"))) parts.Add("S/N " + current.Text("serial"));
            if (!string.IsNullOrEmpty(current.Text("domain"))) parts.Add(current.Text("domain"));
            summary.Text = string.Join("  ·  ", parts);
            object esetValue = current["esetInstalled"];
            eset.Set("ESET: " + ComputerSnapshot.EsetText(esetValue).ToLowerInvariant(),
                esetValue is bool ? ((bool)esetValue ? BadgeKind.Ok : BadgeKind.Warn) : BadgeKind.Muted);
            bool inventoried = current["inventoried"] is bool && (bool)current["inventoried"];
            inventory.Set(L.T("Инвентаризация: ") + (inventoried ? L.T("да") : L.T("нет")), inventoried ? BadgeKind.Ok : BadgeKind.Muted);

            // Values against the record before this collection.
            Dictionary<string, Collection.Field> fields = new Dictionary<string, Collection.Field>();
            foreach (Collection.Field f in c.Compare(current))
            {
                f.Previous = before == null ? null : Collection.Show(before, f.Name);
                fields[f.Name] = f;
            }
            bool anyChange = false;
            List<Collection.Field> listed = new List<Collection.Field>();
            foreach (string name in ListedFields)
                if (fields.ContainsKey(name)) listed.Add(fields[name]);
            if (grid.Rows.Count != listed.Count) { grid.Rows.Clear(); foreach (Collection.Field f in listed) grid.Rows.Add(); }
            for (int i = 0; i < listed.Count; i++)
            {
                Collection.Field f = listed[i];
                DataGridViewRow row = grid.Rows[i];
                bool changed = before != null && f.Changed;
                if (changed) anyChange = true;
                bool technical = Array.IndexOf(TechnicalFields, f.Name) >= 0;
                row.Cells[0].Value = f.Label;
                row.Cells[1].Value = Shown(f.Current);
                row.Cells[2].Value = changed ? L.T("было: ") + Shown(f.Previous) : "";
                Color back = changed ? Theme.WarnLight : Theme.Surface;
                row.DefaultCellStyle.BackColor = back;
                row.DefaultCellStyle.SelectionBackColor = back;
                row.Cells[1].Style.ForeColor = string.IsNullOrEmpty(f.Current) || technical ? Theme.Muted : Theme.Text;
            }
            grid.Columns[2].Visible = anyChange;
            grid.ClearSelection();

            ShowStorage();
        }

        // The line of the database next to the program: when the record was saved.
        void ShowStorage()
        {
            Collection c = collection;
            if (c == null) return;
            JsonObject saved = c.Previous;
            if (saved == null)
                SetLocal(Glyph.Info, AutoSave ? L.T("записи ещё нет, сейчас будет создана") : L.T("записи этого компьютера ещё нет"), Theme.Warn);
            else if (c.HasChanges(comment.Text))
                SetLocal(Glyph.More, L.T("есть изменения, сейчас сохранятся"), Theme.Warn);
            else
                SetLocal(Glyph.Check, L.T("сохранено ") + Fmt.Date(saved.Text("collectedAt")), Theme.Good);
            showFile.Visible = saved != null;
            ShowCloud();
        }

        // The line of the cloud: whether the saved record is the one the cloud has.
        void ShowCloud()
        {
            Collection c = collection;
            if (c == null || c.Previous == null) { SetCloud(Glyph.Cloud, L.T("записи ещё нет в облаке"), Theme.Muted, false); return; }
            string apiUrl;
            try { apiUrl = CloudConfig.ReadApiUrl(AppPaths.Root); }
            catch (Exception) { SetCloud(Glyph.Cloud, L.T("облако не настроено в этой копии программы"), Theme.Muted, false); return; }
            CloudState cloud = CloudState.ForDisplay(AppPaths.Root);
            string status;
            try { status = cloud.StatusOf(Records.Load(c.FilePath, cloud)); }
            catch (Exception e) { SetCloud(Glyph.Warning, e.Message, Theme.Bad, false); return; }
            string last = L.T(" · последняя синхронизация: ") + (string.IsNullOrEmpty(cloud.LastSyncAt) ? L.T("не было") : Fmt.Date(cloud.LastSyncAt));
            if (status == CloudState.Synced)
                SetCloud(Glyph.Check, L.T("в облаке та же запись") + last, Theme.Good, false);
            else if (status == CloudState.Changed)
                SetCloud(Glyph.Upload, L.T("есть изменения, ещё не отправленные в облако") + last, Theme.Warn, true);
            else
                SetCloud(Glyph.Upload, L.T("записи ещё нет в облаке") + last, Theme.Warn, true);
        }

        void SetLocal(string glyph, string text, Color color)
        {
            SetIcon(localIcon, AppPaths.OnRemovableDrive ? Glyph.Usb : Glyph.Database, glyph == null ? Theme.Muted : color);
            localText.Text = text;
            localText.ForeColor = color == Theme.Muted ? Theme.Muted : color;
        }

        void SetCloud(string glyph, string text, Color color, bool canSync)
        {
            SetIcon(cloudIcon, Glyph.Cloud, glyph == null ? Theme.Muted : color);
            cloudText.Text = text;
            cloudText.ForeColor = color;
            sync.Visible = canSync;
        }

        void SetIcon(PictureBox box, string glyph, Color color)
        {
            Image old = box.Image;
            box.Image = Glyph.Image(glyph, Font.Height + Font.Height / 4, color);
            if (old != null) old.Dispose();
        }

        static string Shown(string value)
        {
            return string.IsNullOrEmpty(value) ? L.T("(не указано)") : value;
        }

        // Saves the record when it differs from the saved one.
        void SaveChanges()
        {
            if (collection == null || busy || !AutoSave || !collection.HasChanges(comment.Text)) { ShowStorage(); return; }
            bool isNew = collection.Previous == null;
            try
            {
                JsonObject saved = collection.Save(comment.Text);
                collection.Previous = saved;
                ShowRecord();
                SetLocal(Glyph.Check, (isNew ? L.T("новая запись сохранена в ") : L.T("сохранено в ")) + DateTime.Now.ToString("HH:mm"), Theme.Good);
            }
            catch (Exception e)
            {
                SetLocal(Glyph.Warning, L.T("не удалось сохранить: ") + e.Message, Theme.Bad);
            }
        }
    }
}
