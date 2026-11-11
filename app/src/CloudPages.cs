using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // Connection with the personal key: from cloud-key.txt, or asked from the user.
    public static class CloudSession
    {
        // The client with a checked key, or null when the user cancels.
        public static async Task<CloudClient> Connect(IWin32Window owner)
        {
            string root = AppPaths.Root;
            string apiUrl = CloudConfig.ReadApiUrl(root);
            string fileKey = CloudClient.KeyFromFile(root);
            string error = null;
            if (fileKey != null)
            {
                CloudClient client = new CloudClient(apiUrl, fileKey);
                string reason = null;
                JsonObject me = await Task.Run(delegate { string r; JsonObject m = client.Me(out r); reason = r; return m; });
                if (me != null) { client.User = me; return client; }
                error = L.T("Ключ из cloud-key.txt не подошёл: ") + reason;
            }
            using (KeyDialog dialog = new KeyDialog(apiUrl, fileKey != null, error))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                if (dialog.SaveKey) CloudClient.SaveKey(root, dialog.Client.Key);
                return dialog.Client;
            }
        }
    }

    // Asks the personal key; checks it with the cloud before closing.
    public class KeyDialog : Form
    {
        readonly string apiUrl;
        readonly TextBox key = Ui.Input(new TextBox());
        readonly CheckBox save = new CheckBox();
        readonly StatusLine status = new StatusLine();
        readonly Button connect;
        public CloudClient Client;
        public bool SaveKey { get { return save.Checked; } }

        public KeyDialog(string apiUrl, bool replacing, string error)
        {
            this.apiUrl = apiUrl;
            Ui.Dialog(this);
            Text = L.T("Ключ доступа к облаку");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            int em = Font.Height;
            Padding = new Padding(em);
            ClientSize = new Size(em * 30, em * 15);

            Label intro = Ui.Paragraph(L.T("Введите личный ключ доступа. Его создают в веб-интерфейсе облака (раздел «Ключи»)."), Font, Theme.Text);
            intro.Padding = new Padding(0, 0, 0, em / 2);
            key.UseSystemPasswordChar = true;
            key.Dock = DockStyle.Top;
            save.Text = replacing ? L.T("Заменить ключ в cloud-key.txt") : L.T("Сохранить ключ в этой копии программы");
            save.ForeColor = Theme.Text;
            save.AutoSize = true;
            save.Dock = DockStyle.Top;
            save.Padding = new Padding(0, em / 2, 0, 0);
            Label warning = Ui.Paragraph(L.T("Тот, кто получит флешку с сохранённым ключом, сможет синхронизироваться от вашего имени. ") +
                L.T("Если такая флешка потеряется, сразу отзовите ключ в веб-интерфейсе."), Font, Theme.Muted);
            status.Dock = DockStyle.Top;
            status.Padding = new Padding(0, em / 2, 0, 0);
            if (error != null) status.Set(Glyph.Warning, error, Theme.Bad);

            connect = Ui.Primary(L.T("Подключиться"), Glyph.Cloud, Font);
            connect.Click += delegate { Check(); };
            Button cancel = Ui.Secondary(L.T("Отмена"), null, Font);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            CancelButton = cancel;
            AcceptButton = connect;
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(connect);
            actions.Controls.Add(cancel);
            actions.Dock = DockStyle.Bottom;

            Controls.Add(status);
            Controls.Add(warning);
            Controls.Add(save);
            Controls.Add(key);
            Controls.Add(intro);
            Controls.Add(actions);
        }

        async void Check()
        {
            string value = key.Text.Trim();
            if (value.Length == 0) { status.Set(Glyph.Warning, L.T("Ключ не может быть пустым."), Theme.Bad); return; }
            connect.Enabled = false;
            status.Set(Glyph.More, L.T("Проверка ключа…"), Theme.Muted);
            try
            {
                CloudClient client = new CloudClient(apiUrl, value);
                string reason = null;
                JsonObject me = await Task.Run(delegate { string r; JsonObject m = client.Me(out r); reason = r; return m; });
                if (me == null) { status.Set(Glyph.Warning, reason, Theme.Bad); return; }
                client.User = me;
                Client = client;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception e) { status.Set(Glyph.Warning, e.Message, Theme.Bad); }
            finally { if (!IsDisposed) connect.Enabled = true; }
        }
    }

    // «Синхронизация с облаком» (sync-cloud.ps1).
    public class SyncPage : Page, ISyncQuestions
    {
        readonly Label server;
        readonly Button start;
        readonly ProgressBar progress = new ProgressBar();
        readonly StatusLine step = new StatusLine();
        readonly DataGridView counts;
        readonly TextBox errors = Ui.Input(new TextBox());
        readonly StatusLine newVersion = new StatusLine();
        bool running;

        public SyncPage()
        {
            Key = "sync";
            Title = L.T("Синхронизация с облаком");
            NavTitle = L.T("Синхронизация");
            Description = L.T("Отправляет новые и изменённые записи в облако и загружает базу из облака. Нужен интернет и личный ключ доступа.");
            Icon = Glyph.Sync;
            NeedsInternet = true;
            int em = Font.Height;

            server = Ui.Paragraph("", Font, Theme.Muted);
            server.Padding = new Padding(0, 0, 0, em / 2);
            start = Ui.Primary(L.T("Синхронизировать"), Glyph.Sync, Font);
            start.Click += delegate { Run(); };
            Ui.Keys(start, "F5");
            FlowLayoutPanel toolbar = Ui.Row();
            toolbar.Controls.Add(start);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);
            progress.Style = ProgressBarStyle.Marquee;
            progress.Height = em / 3;
            progress.Dock = DockStyle.Top;
            progress.Visible = false;
            step.Dock = DockStyle.Top;
            step.Padding = new Padding(0, em / 3, 0, em / 3);

            counts = Ui.Grid(Font);
            counts.Dock = DockStyle.Top;
            counts.ColumnHeadersVisible = false;
            counts.Columns.Add("name", "");
            counts.Columns.Add("value", "");
            counts.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            counts.Columns[1].Width = em * 5;
            counts.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            counts.Height = (em + em / 2) * 8 + 2;
            counts.ScrollBars = ScrollBars.None;
            counts.Visible = false;
            counts.TabStop = false;

            errors.Multiline = true;
            errors.ReadOnly = true;
            errors.ScrollBars = ScrollBars.Vertical;
            errors.ForeColor = Theme.Bad;
            errors.Dock = DockStyle.Fill;
            errors.Visible = false;
            newVersion.Dock = DockStyle.Top;
            newVersion.Padding = new Padding(0, em / 2, 0, em / 2);
            newVersion.Cursor = Cursors.Hand;
            newVersion.Click += delegate { if (Main != null) Main.Open("update"); };

            Controls.Add(errors);
            Controls.Add(newVersion);
            Controls.Add(counts);
            Controls.Add(step);
            Controls.Add(progress);
            Controls.Add(toolbar);
            Controls.Add(server);
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { Run(); return true; }
            return false;
        }

        public override void Opened()
        {
            if (running) return;
            start.Focus();
            try
            {
                CloudState state = CloudState.ForDisplay(AppPaths.Root);
                server.Text = L.T("Облако: ") + CloudConfig.ReadApiUrl(AppPaths.Root) + L.T("\r\nПоследняя синхронизация этой копии: ") +
                    (string.IsNullOrEmpty(state.LastSyncAt) ? L.T("не было") : Fmt.Date(state.LastSyncAt));
            }
            catch (Exception e) { server.Text = e.Message; }
        }

        public override bool CanLeave()
        {
            if (running) Ui.Info(FindForm(), L.T("Синхронизация"), L.T("Дождитесь окончания синхронизации."));
            return !running;
        }

        async void Run()
        {
            if (running) return;
            running = true;
            start.Enabled = false;
            counts.Visible = errors.Visible = false;
            newVersion.Clear();
            step.Set(Glyph.More, L.T("Подключение к облаку…"), Theme.Muted);
            progress.Visible = true;
            try
            {
                CloudClient client = await CloudSession.Connect(FindForm());
                if (client == null) { step.Set(Glyph.Info, L.T("Синхронизация отменена: ключ не введён."), Theme.Warn); return; }
                step.Set(Glyph.Person, L.T("Пользователь: ") + CloudClient.UserText(client.User), Theme.Muted);
                CloudSync sync = new CloudSync(AppPaths.Root, client, this);
                SyncCounts c = await Task.Run(delegate { return sync.Run(); });
                ShowCounts(c);
                if (c.Errors.Count == 0) step.Set(Glyph.Check, L.T("Синхронизация завершена."), Theme.Good);
                else step.Set(Glyph.Warning, L.T("Синхронизация завершена с ошибками (") + c.Errors.Count + ").", Theme.Warn);
            }
            catch (Exception e)
            {
                step.Set(Glyph.Warning, L.T("Синхронизация не завершена: ") + e.Message + L.T("\r\nУже обработанные записи сохранены. После устранения причины запустите синхронизацию ещё раз."), Theme.Bad);
            }
            finally
            {
                progress.Visible = false;
                running = false;
                start.Enabled = true;
                Opened();
            }
        }

        void ShowCounts(SyncCounts c)
        {
            counts.Rows.Clear();
            AddCount(L.T("Отправлено новых"), c.PushedNew, false);
            AddCount(L.T("Отправлено изменённых"), c.PushedUpdated, false);
            AddCount(L.T("Совпало с облаком"), c.PushedSame, false);
            AddCount(L.T("Получено новых из облака"), c.ReceivedNew, false);
            AddCount(L.T("Обновлено из облака"), c.ReceivedUpdated, false);
            AddCount(L.T("Удалено по облаку"), c.Deleted, false);
            AddCount(L.T("Конфликтов отложено"), c.Deferred, true);
            string profiles = c.NetworkProfiles == "updated" ? L.T("обновлены") : c.NetworkProfiles == "unchanged" ? L.T("без изменений") : c.NetworkProfiles == "missing" ? L.T("в облаке не заданы") : L.T("не загружены");
            int i = counts.Rows.Add(L.T("Профили сети"), profiles);
            counts.Rows[i].Cells[1].Style.ForeColor = c.NetworkProfiles == "updated" ? Theme.Info : Theme.Muted;
            counts.ClearSelection();
            counts.Visible = true;
            if (c.Errors.Count > 0)
            {
                errors.Text = L.T("Ошибки:\r\n") + string.Join("\r\n", c.Errors);
                errors.Visible = true;
            }
            if (c.NewVersion != null)
                newVersion.Set(Glyph.Update, L.T("Доступна новая версия программы ") + c.NewVersion + L.T(": раздел «Обновление» (нажмите здесь)."), Theme.Info);
        }

        void AddCount(string name, int value, bool warnWhenAny)
        {
            int i = counts.Rows.Add(name, value);
            counts.Rows[i].Cells[1].Style.ForeColor = value == 0 ? Theme.Muted : warnWhenAny ? Theme.Warn : Theme.Info;
            counts.Rows[i].Cells[0].Style.ForeColor = value == 0 ? Theme.Muted : Theme.Text;
        }

        // ---------- questions from the sync thread ----------

        T OnUi<T>(Func<T> f) { return (T)Invoke(f); }

        public void Progress(string text)
        {
            BeginInvoke(new MethodInvoker(delegate { step.Set(Glyph.More, text, Theme.Muted); }));
        }

        public string ResolveConflict(string title, JsonObject local, JsonObject cloud, out JsonObject result)
        {
            JsonObject chosen = null;
            string action = OnUi(delegate
            {
                using (ConflictDialog d = new ConflictDialog(title, local, cloud))
                {
                    d.ShowDialog(FindForm());
                    chosen = d.Result;
                    return d.Action;
                }
            });
            result = chosen;
            return action;
        }

        public int DeletedInCloud(string title, string details)
        {
            return OnUi(delegate
            {
                int a = Ui.Ask(FindForm(), L.T("Запись удалена в облаке"), title + "\n\n" + details, Glyph.Warning, Theme.Warn,
                    L.T("Восстановить в облаке"), L.T("Удалить локальную запись"), L.T("Отложить"));
                return a < 0 ? 2 : a;
            });
        }
    }

    // Conflict between the local record and the cloud version: whole versions or field by field (Resolve-SyncRecord).
    public class ConflictDialog : Form
    {
        readonly JsonObject local, cloud;
        readonly List<string> fields;
        readonly List<string> different;
        readonly HashSet<string> fromCloud = new HashSet<string>();
        readonly DataGridView grid;
        public string Action = "defer";
        public JsonObject Result;

        public ConflictDialog(string title, JsonObject local, JsonObject cloud)
        {
            this.local = local;
            this.cloud = cloud;
            fields = RecordDiff.AllFields(local, cloud);
            different = RecordDiff.Differences(local, cloud);
            Ui.Dialog(this);
            Text = L.T("Конфликт с облаком");
            int em = Font.Height;
            Padding = new Padding(em);
            ClientSize = new Size(em * 46, em * 30);
            MinimumSize = new Size(em * 34, em * 22);

            Label head = Ui.Paragraph(title, new Font("Segoe UI Semibold", 10.5f), Theme.Text);
            Label hint = Ui.Paragraph(L.T("Запись изменили и здесь, и в облаке. Различия выделены. Щёлкните значение, которое нужно оставить, ") +
                L.T("или выберите версию целиком. Для разных дат более новая — зелёная."), Font, Theme.Muted);
            hint.Padding = new Padding(0, em / 4, 0, em / 2);

            grid = Ui.Grid(Font);
            grid.Dock = DockStyle.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.Columns.Add("field", L.T("Поле"));
            grid.Columns.Add("local", L.T("Локально (эта копия)"));
            grid.Columns.Add("cloud", L.T("В облаке"));
            grid.Columns[0].FillWeight = 24; grid.Columns[1].FillWeight = 38; grid.Columns[2].FillWeight = 38;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.DefaultCellStyle.SelectionBackColor = grid.DefaultCellStyle.BackColor;
            grid.CellClick += delegate (object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 1) return;
                string name = fields[e.RowIndex];
                if (!different.Contains(name)) return;
                if (e.ColumnIndex == 2) fromCloud.Add(name); else fromCloud.Remove(name);
                Repaint();
            };
            foreach (string name in fields) grid.Rows.Add(RecordDiff.Label(name), RecordDiff.ValueText(local, name), RecordDiff.ValueText(cloud, name));
            Repaint();

            Button chosen = Ui.Primary(L.T("Сохранить выбранное"), Glyph.Check, Font);
            chosen.Click += delegate { Finish("merged", RecordDiff.Merge(local, cloud, fromCloud)); };
            Button takeCloud = Ui.Secondary(L.T("Всё из облака"), Glyph.Cloud, Font);
            takeCloud.Click += delegate { Finish("cloud", cloud); };
            Button takeLocal = Ui.Secondary(L.T("Всё локальное"), Glyph.Upload, Font);
            takeLocal.Click += delegate { Finish("local", local); };
            Button defer = Ui.Secondary(L.T("Отложить"), null, Font);
            defer.Click += delegate { Finish("defer", null); };
            FlowLayoutPanel actions = Ui.Row();
            actions.WrapContents = true;
            actions.Controls.Add(chosen);
            actions.Controls.Add(takeCloud);
            actions.Controls.Add(takeLocal);
            actions.Controls.Add(defer);
            actions.Dock = DockStyle.Bottom;
            actions.Padding = new Padding(0, em / 2, 0, 0);

            Controls.Add(grid);
            Controls.Add(hint);
            Controls.Add(head);
            Controls.Add(actions);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); grid.ClearSelection(); grid.CurrentCell = null; }

        void Finish(string action, JsonObject result)
        {
            Action = action;
            Result = result;
            Close();
        }

        static DateTimeOffset? DateOf(JsonObject r, string name)
        {
            DateTimeOffset d;
            if (DateTimeOffset.TryParse(r.Text(name) ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d)) return d;
            return null;
        }

        void Repaint()
        {
            for (int i = 0; i < fields.Count; i++)
            {
                string name = fields[i];
                DataGridViewRow row = grid.Rows[i];
                bool diff = different.Contains(name);
                bool cloudWins = fromCloud.Contains(name);
                foreach (DataGridViewCell c in row.Cells)
                {
                    c.Style.ForeColor = diff ? Theme.Text : Theme.Muted;
                    c.Style.BackColor = c.Style.SelectionBackColor = Theme.Surface;
                }
                if (!diff) continue;
                row.Cells[0].Style.BackColor = row.Cells[0].Style.SelectionBackColor = Theme.WarnLight;
                DataGridViewCell keep = row.Cells[cloudWins ? 2 : 1], drop = row.Cells[cloudWins ? 1 : 2];
                keep.Style.BackColor = keep.Style.SelectionBackColor = Theme.GoodLight;
                drop.Style.ForeColor = Theme.Muted;
                if (name.EndsWith("At"))
                {
                    DateTimeOffset? a = DateOf(local, name), b = DateOf(cloud, name);
                    if (a.HasValue && b.HasValue && a.Value != b.Value)
                    {
                        row.Cells[1].Style.ForeColor = a.Value > b.Value ? Theme.Good : Theme.Bad;
                        row.Cells[2].Style.ForeColor = b.Value > a.Value ? Theme.Good : Theme.Bad;
                    }
                }
                keep.ToolTipText = L.T("Будет сохранено");
                drop.ToolTipText = L.T("Щёлкните, чтобы оставить это значение");
            }
        }
    }

    // «Обновление программы» (update-program.ps1).
    public class UpdatePage : Page
    {
        readonly Label versions;
        readonly Button check;
        readonly Button install;
        readonly Button restart;
        readonly Label notes;
        readonly StatusLine status = new StatusLine();
        readonly ProgressBar progress = new ProgressBar();
        CloudClient client;
        JsonObject release;
        bool running;

        public UpdatePage()
        {
            Key = "update";
            Title = L.T("Обновление программы");
            NeedsInternet = true;
            NavTitle = L.T("Обновление");
            Description = L.T("Проверяет новую версию в облаке и устанавливает её в эту копию программы. Данные (папка data) не меняются.");
            Icon = Glyph.Update;
            int em = Font.Height;

            versions = Ui.Paragraph(L.T("Установлена версия: ") + AppVersion.Text, Font, Theme.Text);
            versions.Padding = new Padding(0, 0, 0, em / 2);
            check = Ui.Primary(L.T("Проверить обновления"), Glyph.Refresh, Font);
            check.Click += delegate { Check(); };
            Ui.Keys(check, "F5");
            install = Ui.Primary(L.T("Установить"), Glyph.Update, Font);
            install.Visible = false;
            install.Click += delegate { Install(); };
            restart = Ui.Primary(L.T("Перезапустить программу"), Glyph.Power, Font);
            restart.Visible = false;
            restart.Click += delegate { Restart(); };
            FlowLayoutPanel toolbar = Ui.Row();
            toolbar.Controls.Add(check);
            toolbar.Controls.Add(install);
            toolbar.Controls.Add(restart);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);
            progress.Style = ProgressBarStyle.Marquee;
            progress.Height = em / 3;
            progress.Dock = DockStyle.Top;
            progress.Visible = false;
            status.Dock = DockStyle.Top;
            status.Padding = new Padding(0, em / 3, 0, em / 3);
            notes = Ui.Paragraph("", Font, Theme.Text);

            Controls.Add(notes);
            Controls.Add(status);
            Controls.Add(progress);
            Controls.Add(toolbar);
            Controls.Add(versions);
        }

        public override bool CanLeave()
        {
            if (running) Ui.Info(FindForm(), L.T("Обновление"), L.T("Дождитесь окончания обновления."));
            return !running;
        }

        public override void Opened() { check.Focus(); }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { if (check.Enabled) Check(); return true; }
            return false;
        }

        bool IsDevelopmentCopy
        {
            get { return Directory.Exists(Path.Combine(AppPaths.Root, ".git")); }
        }

        void Busy(bool value)
        {
            running = value;
            progress.Visible = value;
            check.Enabled = install.Enabled = !value;
        }

        async void Check()
        {
            Busy(true);
            install.Visible = false;
            notes.Text = "";
            status.Set(Glyph.More, L.T("Проверка версии в облаке…"), Theme.Muted);
            try
            {
                client = await CloudSession.Connect(FindForm());
                if (client == null) { status.Set(Glyph.Info, L.T("Проверка отменена: ключ не введён."), Theme.Warn); return; }
                release = await Task.Run(delegate { return client.LatestRelease(); });
                if (release == null) { status.Set(Glyph.Info, L.T("В облаке ещё нет опубликованных версий программы."), Theme.Warn); return; }
                string version = release.Text("version");
                Version current = ProgramUpdate.Parse(AppVersion.Text), latest = ProgramUpdate.Parse(version);
                DateTimeOffset published;
                string date = DateTimeOffset.TryParse(release.Text("publishedAt") ?? "", out published) ? L.T(" от ") + published.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "";
                versions.Text = L.T("Установлена версия: ") + AppVersion.Text + L.T("\r\nВерсия в облаке: ") + version + date;
                if (current != null && latest != null && current == latest)
                {
                    status.Set(Glyph.Check, L.T("Установлена последняя версия."), Theme.Good);
                    return;
                }
                bool rollback = current != null && latest != null && latest < current;
                List<string> lines = new List<string>();
                IList list = release["notes"] as IList;
                if (list != null) foreach (object n in list) if (n != null && Convert.ToString(n).Length > 0) lines.Add("•  " + n);
                notes.Text = lines.Count > 0 ? L.T("Что нового:\r\n") + string.Join("\r\n", lines.GetRange(0, Math.Min(15, lines.Count))) +
                    (lines.Count > 15 ? L.T("\r\n   и ещё ") + (lines.Count - 15) : "") : "";
                if (IsDevelopmentCopy)
                {
                    status.Set(Glyph.Info, L.T("Это копия программы для разработки (есть папка .git). Обновляйте её через Git."), Theme.Warn);
                    return;
                }
                status.Set(rollback ? Glyph.Warning : Glyph.Update, rollback ? L.T("Администратор сделал текущей более старую версию: установка будет откатом.") :
                    L.T("Доступна новая версия программы."), rollback ? Theme.Warn : Theme.Info);
                install.Text = L.T(" Установить версию ") + version;
                install.Visible = true;
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось проверить обновления: ") + e.Message, Theme.Bad); }
            finally { Busy(false); }
        }

        async void Install()
        {
            if (release == null || client == null) return;
            string version = release.Text("version");
            Busy(true);
            try
            {
                string root = AppPaths.Root;
                string updateRoot = Path.Combine(root, ".update");
                Directory.CreateDirectory(updateRoot);
                string zip = Path.Combine(updateRoot, "comp-info-" + version + ".zip");
                status.Set(Glyph.More, L.T("Загрузка версии ") + version + "…", Theme.Muted);
                string sha = (release.Text("sha256") ?? "").ToLowerInvariant();
                await Task.Run(delegate
                {
                    client.Download(client.ApiUrl + "/program/" + version + "/package", zip);
                    if (ProgramUpdate.Sha256(zip) != sha)
                    {
                        File.Delete(zip);
                        throw new InvalidDataException(L.T("Архив повреждён при загрузке: контрольная сумма не совпадает. Запустите обновление ещё раз."));
                    }
                });
                status.Set(Glyph.More, L.T("Установка…"), Theme.Muted);
                ProgramUpdate.Result result = await Task.Run(delegate { return ProgramUpdate.Install(root, zip, version, AppVersion.Text); });
                // The program keeps no network addresses itself: refresh their cache while online.
                string profiles = "";
                try { if (await Task.Run(delegate { return client.UpdateNetworkProfiles(root); }) == "updated") profiles = L.T("\r\nПрофили сети обновлены."); }
                catch (Exception e) { profiles = L.T("\r\nПрофили сети не загружены: ") + e.Message + L.T(". Они обновятся при синхронизации."); }
                install.Visible = false;
                status.Set(Glyph.Check, L.T("Программа обновлена до версии ") + version + L.T(". Файлов установлено: ") + result.Installed +
                    (result.Removed.Count > 0 ? L.T(". Удалены устаревшие: ") + string.Join(", ", result.Removed) : "") +
                    L.T(".\r\nПрежняя версия сохранена в ") + result.Backup + profiles + L.T("\r\nПерезапустите программу, чтобы работать в новой версии."), Theme.Good);
                restart.Visible = true;
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Обновление не выполнено: ") + e.Message, Theme.Bad); }
            finally { Busy(false); }
        }

        void Restart()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--page update") { WorkingDirectory = AppPaths.Root, UseShellExecute = false });
                Main.ReopenPage = null;
                Main.Close();
            }
            catch (Exception e) { Ui.Error(FindForm(), L.T("Не удалось перезапустить программу: ") + e.Message); }
        }
    }
}
