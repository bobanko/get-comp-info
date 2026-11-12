using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    public class SecurityPage : Page
    {
        readonly Button run;
        readonly Button disableOneDrive;
        readonly CheckBox scanDocuments = new CheckBox();
        readonly ProgressBar progress = new ProgressBar();
        readonly DataGridView grid;
        readonly DataGridView details;
        readonly StatusLine status = new StatusLine();
        readonly Dictionary<SecurityLevel, Bitmap> icons = new Dictionary<SecurityLevel, Bitmap>();
        readonly Dictionary<string, Bitmap> fileIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Bitmap waitingIcon;
        readonly Bitmap runningIcon;
        readonly Bitmap openFileIcon;
        readonly Bitmap openFolderIcon;
        readonly Bitmap deleteIcon;
        readonly SplitContainer split = new SplitContainer();
        readonly FlowLayoutPanel bulkBar = Ui.Row();
        bool running;

        // Where the file table is: "bottom" (under the checks) or "right" (next to them).
        // The program that hosts the page loads it and saves a change through LayoutSaved.
        public static string TableLayout = "bottom";
        public static Action<string> LayoutSaved;

        // Location tags whose documents can be deleted all at once.
        static string[] BulkTags
        {
            get { return new[] { L.T("#корзина"), L.T("#даунлоадс"), L.T("#телеграм"), "#WhatsApp", "#Viber", "#Claude" }; }
        }

        public SecurityPage()
        {
            Key = "security";
            Title = L.T("Проверка кибербезопасности");
            NavTitle = L.T("Кибербезопасность");
            Description = L.T("Локальная проверка домена, антивируса, VPN, нежелательного ПО, OneDrive, документов и паролей Windows. Результаты никуда не отправляются.");
            Icon = Glyph.Shield;
            int em = Font.Height;

            run = Ui.Primary(L.T("Запустить проверку"), Glyph.Play, Font);
            run.Click += delegate { Run(); };
            Ui.Keys(run, "F5");
            disableOneDrive = Ui.Secondary(L.T("Отключить OneDrive"), Glyph.Power, Font);
            disableOneDrive.Visible = false;
            disableOneDrive.Click += delegate { DisableOneDrive(); };
            // The slow full disk scan can be left out of a run; the step is then shown as skipped.
            scanDocuments.Text = L.T("Сканировать документы");
            scanDocuments.Checked = true;
            scanDocuments.AutoSize = true;
            scanDocuments.ForeColor = Theme.Text;
            scanDocuments.Margin = new Padding(em * 2 / 3, 0, em / 2, 0);
            run.Margin = new Padding(0);
            disableOneDrive.Margin = new Padding(em / 2, 0, 0, 0);

            // Like the panel buttons of VS Code: the file table under the checks or next to them.
            Segmented layout = new Segmented(new[] { Glyph.DockBottom, Glyph.DockRight }, true,
                new[] { L.T("Таблицы одна под другой"), L.T("Таблицы рядом") }, TableLayout == "right" ? 1 : 0);
            layout.Font = Font;
            layout.Size = new Size(em * 4, run.Height);
            layout.Margin = new Padding(em / 2, 0, 0, 0);
            layout.Changed += delegate (int index)
            {
                TableLayout = index == 1 ? "right" : "bottom";
                ApplyLayout();
                if (LayoutSaved != null) LayoutSaved(TableLayout);
            };

            // One row, every control centred on the same line.
            TableLayoutPanel toolbar = new TableLayoutPanel();
            toolbar.ColumnCount = 5;
            toolbar.RowCount = 1;
            toolbar.AutoSize = true;
            toolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            toolbar.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);
            for (int i = 0; i < 3; i++) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            foreach (Control c in new Control[] { run, scanDocuments, disableOneDrive, layout }) c.Anchor = AnchorStyles.Left;
            status.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            status.Margin = new Padding(em, 0, 0, 0);
            status.MinimumSize = new Size(em * 12, run.Height);
            toolbar.Controls.Add(run, 0, 0);
            toolbar.Controls.Add(scanDocuments, 1, 0);
            toolbar.Controls.Add(disableOneDrive, 2, 0);
            toolbar.Controls.Add(status, 3, 0);
            toolbar.Controls.Add(layout, 4, 0);

            progress.Style = ProgressBarStyle.Continuous;
            progress.Height = em / 3;
            progress.Dock = DockStyle.Top;
            progress.Visible = false;

            icons[SecurityLevel.Good] = Glyph.Image(Glyph.Check, em, Theme.Good);
            icons[SecurityLevel.Warning] = Glyph.Image(Glyph.Warning, em, Theme.Warn);
            icons[SecurityLevel.Bad] = Glyph.Image(Glyph.Cross, em, Theme.Bad);
            icons[SecurityLevel.Info] = Glyph.Image(Glyph.Info, em, Theme.Info);
            icons[SecurityLevel.Error] = Glyph.Image(Glyph.Warning, em, Theme.Bad);
            waitingIcon = Glyph.Image(Glyph.Circle, em, Theme.Border);
            runningIcon = Glyph.Image(Glyph.More, em, Theme.Info);
            openFileIcon = Glyph.Image(Glyph.Open, em, Theme.Accent);
            openFolderIcon = Glyph.Image(Glyph.Folder, em, Theme.Folder);
            deleteIcon = Glyph.Image(Glyph.Delete, em, Theme.Bad);

            grid = Ui.Grid(Font);
            grid.Dock = DockStyle.Fill;
            DataGridViewImageColumn state = new DataGridViewImageColumn();
            state.HeaderText = "";
            state.Width = em * 2;
            state.DefaultCellStyle.NullValue = null;
            grid.Columns.Add(state);
            grid.Columns.Add("check", L.T("Проверка"));
            grid.Columns.Add("result", L.T("Результат"));
            grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.SelectionChanged += delegate { ShowDetails(); UpdateActions(); };

            details = Ui.Grid(Font);
            details.Dock = DockStyle.Fill;
            DataGridViewImageColumn detailIcon = new DataGridViewImageColumn();
            detailIcon.HeaderText = "";
            detailIcon.Width = em * 2;
            detailIcon.DefaultCellStyle.NullValue = null;
            details.Columns.Add(detailIcon);
            details.Columns.Add("type", L.T("Тип"));
            details.Columns.Add("tags", L.T("Метки"));
            details.Columns.Add("matches", L.T("Совпадений"));
            details.Columns.Add("detail", L.T("Файл"));
            details.Columns.Add(ActionIconColumn(em * 2));
            details.Columns.Add(ActionIconColumn(em * 2));
            details.Columns.Add(ActionIconColumn(em * 2));
            details.Columns[1].Width = em * 4;
            details.Columns[1].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            // The tag column is as wide as its widest set of chips (ShowDetails measures it).
            details.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            details.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            details.Columns[3].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            details.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            details.Columns[4].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            details.RowTemplate.Height = em * 2;
            foreach (DataGridViewColumn column in details.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            SetDetailMode(false);
            details.CellPainting += PaintDetailChips;
            details.CellContentClick += delegate (object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && (e.ColumnIndex == 5 || e.ColumnIndex == 6)) OpenDetail(e.RowIndex, e.ColumnIndex == 6);
                else if (e.RowIndex >= 0 && e.ColumnIndex == 7) DeleteDetail(e.RowIndex);
            };
            details.CellDoubleClick += delegate (object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.ColumnIndex < 5) OpenDetail(e.RowIndex, false);
            };
            details.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && details.CurrentRow != null)
                {
                    e.SuppressKeyPress = true;
                    OpenDetail(details.CurrentRow.Index, false);
                }
                else if (e.KeyCode == Keys.Delete && details.CurrentRow != null)
                {
                    e.SuppressKeyPress = true;
                    DeleteDetail(details.CurrentRow.Index);
                }
            };

            // «Delete all found with the tag …» buttons over the file table; shown only when such files exist.
            bulkBar.Dock = DockStyle.Top;
            bulkBar.WrapContents = true;
            bulkBar.AutoSize = true;
            bulkBar.Padding = new Padding(em / 3, em / 3, 0, em / 3);
            bulkBar.BackColor = Theme.Surface;
            bulkBar.Visible = false;

            split.Dock = DockStyle.Fill;
            split.BackColor = Theme.Border;
            split.Panel1.BackColor = Theme.Surface;
            split.Panel2.BackColor = Theme.Surface;
            split.Panel1.Controls.Add(grid);
            split.Panel2.Controls.Add(details);
            split.Panel2.Controls.Add(bulkBar);
            split.SplitterWidth = Math.Max(4, em / 4);
            split.Resize += delegate { KeepSplitSensible(); };
            ApplyLayout();

            status.Set(Glyph.Info, L.T("Нажмите «Запустить проверку». Полный поиск документов может занять несколько минут."), Theme.Info);

            Controls.Add(split);
            Controls.Add(progress);
            Controls.Add(toolbar);
        }

        public override void Opened() { if (!running) run.Focus(); }

        public override bool CanLeave()
        {
            if (running) Ui.Info(FindForm(), L.T("Проверка выполняется"), L.T("Дождитесь окончания проверки."));
            return !running;
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { Run(); return true; }
            return false;
        }

        async void Run()
        {
            if (running) return;
            running = true;
            run.Enabled = false;
            scanDocuments.Enabled = false;
            bool documents = scanDocuments.Checked;
            disableOneDrive.Visible = false;
            grid.Rows.Clear();
            details.Rows.Clear();
            List<SecurityCheck> plan = SecurityAudit.Plan(documents);
            foreach (SecurityCheck check in plan)
            {
                bool skipped = !documents && check.Key == "documents";
                int index = grid.Rows.Add(skipped ? icons[SecurityLevel.Info] : waitingIcon, check.Title, check.Summary);
                DataGridViewRow row = grid.Rows[index];
                row.Tag = check;
                row.Cells[2].Style.ForeColor = Theme.Muted;
            }
            if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
            progress.Minimum = 0;
            progress.Maximum = plan.Count;
            progress.Value = 0;
            progress.Style = ProgressBarStyle.Continuous;
            progress.MarqueeAnimationSpeed = 0;
            progress.Visible = true;
            status.Set(Glyph.More, L.T("Подготовка проверки…"), Theme.Muted);
            try
            {
                int completedCount = 0;
                Action<SecurityCheck> started = delegate (SecurityCheck check)
                {
                    UpdateFromWorker(delegate
                    {
                        DataGridViewRow row = FindRow(check.Key);
                        if (row != null)
                        {
                            row.Cells[0].Value = runningIcon;
                            row.Cells[2].Value = L.T("Проверяется…");
                            row.Cells[2].Style.ForeColor = Theme.Info;
                            row.Tag = check;
                            if (grid.CurrentRow == row) { ShowDetails(); UpdateActions(); }
                        }
                        if (check.Key != "documents")
                        {
                            progress.Style = ProgressBarStyle.Continuous;
                            progress.MarqueeAnimationSpeed = 0;
                            progress.Minimum = 0;
                            progress.Maximum = plan.Count;
                            progress.Value = Math.Min(progress.Maximum, completedCount);
                        }
                        status.Set(Glyph.More, L.T("Выполняется: ") + check.Title + "…", Theme.Muted);
                    });
                };
                Action<SecurityCheck> completed = delegate (SecurityCheck check)
                {
                    UpdateFromWorker(delegate
                    {
                        DataGridViewRow row = FindRow(check.Key);
                        if (row != null)
                        {
                            row.Cells[0].Value = icons[check.Level];
                            row.Cells[2].Value = check.Summary;
                            row.Cells[2].Style.ForeColor = ColorOf(check.Level);
                            row.Tag = check;
                            if (grid.CurrentRow == row) ShowDetails();
                        }
                        completedCount++;
                        progress.Style = ProgressBarStyle.Continuous;
                        progress.MarqueeAnimationSpeed = 0;
                        progress.Minimum = 0;
                        progress.Maximum = plan.Count;
                        progress.Value = Math.Min(progress.Maximum, completedCount);
                    });
                };
                Action<SecurityProgress> detailedProgress = delegate (SecurityProgress scan)
                {
                    UpdateFromWorker(delegate
                    {
                        if (scan.Indeterminate)
                        {
                            progress.Style = ProgressBarStyle.Marquee;
                            progress.MarqueeAnimationSpeed = 28;
                            status.Set(Glyph.More, L.F("Поиск Office-файлов: найдено {0}. Составляется список…", scan.Found), Theme.Muted);
                            return;
                        }

                        progress.Style = ProgressBarStyle.Continuous;
                        progress.MarqueeAnimationSpeed = 0;
                        progress.Minimum = 0;
                        progress.Maximum = Math.Max(1, scan.Total);
                        progress.Value = Math.Min(progress.Maximum, scan.Checked);
                        status.Set(Glyph.More,
                            L.F("Проверено {0} из {1} файлов; документов с совпадениями: {2}.", scan.Checked, scan.Total, scan.Matches),
                            Theme.Muted);
                    });
                };
                List<SecurityCheck> checks = await Task.Run(delegate { return SecurityAudit.Run(started, completed, detailedProgress, documents); });
                if (IsDisposed) return;
                int bad = 0, warnings = 0;
                foreach (SecurityCheck check in checks)
                {
                    if (check.Level == SecurityLevel.Bad || check.Level == SecurityLevel.Error) bad++;
                    else if (check.Level == SecurityLevel.Warning) warnings++;
                }
                if (bad > 0)
                    status.Set(Glyph.Warning, L.F("Проверка завершена. Проблем: {0}, предупреждений: {1}.", bad, warnings), Theme.Bad);
                else if (warnings > 0)
                    status.Set(Glyph.Warning, L.F("Проверка завершена без критических проблем. Предупреждений: {0}.", warnings), Theme.Warn);
                else status.Set(Glyph.Check, L.T("Проверка завершена: проблем не найдено."), Theme.Good);
            }
            catch (Exception e)
            {
                if (!IsDisposed) status.Set(Glyph.Warning, L.T("Проверка прервана: ") + e.Message, Theme.Bad);
            }
            finally
            {
                running = false;
                if (!IsDisposed) { run.Enabled = true; scanDocuments.Enabled = true; UpdateActions(); UpdateBulkBar(grid.CurrentRow == null ? null : grid.CurrentRow.Tag as SecurityCheck); }
            }
        }

        void UpdateFromWorker(Action update)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { Invoke(update); }
            catch (InvalidOperationException) { }
        }

        DataGridViewRow FindRow(string key)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                SecurityCheck check = row.Tag as SecurityCheck;
                if (check != null && check.Key == key) return row;
            }
            return null;
        }

        void UpdateActions()
        {
            SecurityCheck check = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as SecurityCheck;
            disableOneDrive.Visible = check != null && check.ActionKey == "disable-onedrive";
            disableOneDrive.Enabled = !running;
        }

        async void DisableOneDrive()
        {
            if (running) return;
            if (Ui.Ask(FindForm(), "OneDrive",
                L.T("OneDrive будет остановлен и убран из автозапуска. Локальные файлы и настройка учётной записи не удаляются."),
                Glyph.Warning, Theme.Warn, L.T("Отключить синхронизацию"), L.T("Отмена")) != 0) return;
            disableOneDrive.Enabled = run.Enabled = false;
            status.Set(Glyph.More, L.T("Отключение синхронизации OneDrive…"), Theme.Muted);
            try
            {
                await Task.Run(delegate { SecurityAudit.DisableOneDriveSync(); });
                SecurityCheck check = await Task.Run(delegate { return SecurityAudit.CheckOneDriveNow(); });
                DataGridViewRow row = FindRow("onedrive");
                if (row != null)
                {
                    row.Cells[0].Value = icons[check.Level];
                    row.Cells[2].Value = check.Summary;
                    row.Cells[2].Style.ForeColor = ColorOf(check.Level);
                    row.Tag = check;
                    if (grid.CurrentRow == row) ShowDetails();
                }
                status.Set(Glyph.Check, L.T("Синхронизация OneDrive отключена. Файлы и настройка учётной записи сохранены."), Theme.Good);
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось отключить OneDrive: ") + e.Message, Theme.Bad); }
            finally
            {
                run.Enabled = true;
                UpdateActions();
            }
        }

        void ShowDetails()
        {
            SecurityCheck check = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as SecurityCheck;
            details.Rows.Clear();
            bool files = false;
            if (check != null)
                foreach (SecurityDetail item in check.Items) if (item.FilePath.Length > 0) { files = true; break; }
            SetDetailMode(files);
            UpdateBulkBar(check);
            if (check == null) return;
            int em = Font.Height, tagWidth = 0;
            foreach (SecurityDetail item in check.Items)
            {
                bool file = item.FilePath.Length > 0;
                // A file's note is «#tag #tag · count»: the chips go to their column, the count to the next one.
                FileChips chips = file ? FileChips.Parse(item.Note) : null;
                string count = !file ? "" : chips != null ? chips.Text : item.Note;
                if (chips != null) { chips.Text = ""; tagWidth = Math.Max(tagWidth, FileChips.Width(chips.Labels, em)); }
                string fileName = file ? Path.GetFileName(item.Text) : item.Text;
                if (file && string.IsNullOrEmpty(fileName)) fileName = item.Text;
                int index = details.Rows.Add(file ? FileIcon(item.FilePath) : null, item.FileType, "", count, fileName,
                    file ? openFileIcon : null, file ? openFolderIcon : null, file ? deleteIcon : null);
                DataGridViewRow row = details.Rows[index];
                row.Tag = item;
                row.Cells[2].Tag = chips;
                if (file)
                {
                    row.Cells[0].ToolTipText = item.Text;
                    if (chips != null) row.Cells[2].ToolTipText = "#" + string.Join(" #", chips.Labels.ToArray());
                    row.Cells[3].ToolTipText = item.ToolTip;
                    row.Cells[4].ToolTipText = item.Text;
                    row.Cells[5].ToolTipText = L.T("Открыть");
                    row.Cells[6].ToolTipText = L.T("Папка");
                    row.Cells[7].ToolTipText = L.T("Удалить (Delete)");
                }
                if (item.FileType.Length > 0)
                {
                    row.Cells[1].Style.ForeColor = item.FileType == "WORD" ? Theme.Info : item.FileType == "EXCEL" ? Theme.Good : Theme.Warn;
                }
            }
            details.Columns[2].Visible = files && tagWidth > 0;
            if (tagWidth > 0)
            {
                int header = TextRenderer.MeasureText(details.Columns[2].HeaderText, details.ColumnHeadersDefaultCellStyle.Font ?? Font).Width + em;
                details.Columns[2].Width = Math.Max(header, tagWidth);
            }
            details.ClearSelection();
        }


        void PaintDetailChips(object sender, DataGridViewCellPaintingEventArgs e) { FileChips.Paint(details, e, 2); }

        // Found files fill all columns; other checks show their details as plain text in the last one.
        void SetDetailMode(bool files)
        {
            foreach (int column in new[] { 0, 1, 2, 3, 5, 6, 7 }) details.Columns[column].Visible = files;
            details.Columns[4].HeaderText = files ? L.T("Файл") : L.T("Детали");
            details.Columns[4].DefaultCellStyle.Font = files ? new Font("Segoe UI Semibold", 9f) : Font;
            details.Columns[4].DefaultCellStyle.ForeColor = Theme.Text;
        }

        void ApplyLayout()
        {
            split.Orientation = TableLayout == "right" ? Orientation.Vertical : Orientation.Horizontal;
            int size = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            if (size > split.SplitterWidth * 4) split.SplitterDistance = split.Orientation == Orientation.Vertical ? size * 9 / 20 : size * 3 / 5;
        }

        void KeepSplitSensible()
        {
            int em = Font.Height;
            if (split.Orientation == Orientation.Horizontal)
            {
                if (split.Height > em * 12 && split.SplitterDistance < em * 6) split.SplitterDistance = split.Height * 3 / 5;
            }
            else if (split.Width > em * 24 && split.SplitterDistance < em * 10) split.SplitterDistance = split.Width * 9 / 20;
        }

        static List<string> TagsOf(SecurityDetail item)
        {
            FileChips chips = FileChips.Parse(item.Note);
            return chips == null ? new List<string>() : chips.Labels;
        }

        // Found files with the tag that may be deleted all at once (SupraWeed files never are); skipped counts the SupraWeed ones.
        static List<SecurityDetail> Tagged(SecurityCheck check, string tag, out int skipped)
        {
            List<SecurityDetail> result = new List<SecurityDetail>();
            skipped = 0;
            if (check == null) return result;
            string label = tag.TrimStart('#');
            foreach (SecurityDetail item in check.Items)
            {
                if (item.FilePath.Length == 0) continue;
                List<string> tags = TagsOf(item);
                if (!tags.Contains(label)) continue;
                if (tags.Contains("SupraWeed")) skipped++;
                else result.Add(item);
            }
            return result;
        }

        void UpdateBulkBar(SecurityCheck check)
        {
            bulkBar.SuspendLayout();
            foreach (Control old in bulkBar.Controls) old.Dispose();
            bulkBar.Controls.Clear();
            int em = Font.Height;
            foreach (string tag in BulkTags)
            {
                int skipped;
                List<SecurityDetail> files = Tagged(check, tag, out skipped);
                if (files.Count == 0) continue;
                if (bulkBar.Controls.Count == 0)
                {
                    Label caption = Ui.Text(L.T("Удалить все найденные:"), Font, Theme.Muted);
                    caption.Margin = new Padding(0, em / 3, em / 2, 0);
                    bulkBar.Controls.Add(caption);
                }
                string label = tag.TrimStart('#');
                Button button = Ui.Secondary(label + " · " + files.Count, Glyph.Delete, Font);
                button.Margin = new Padding(0, 0, em / 3, em / 4);
                button.Enabled = !running;
                button.Click += delegate { DeleteTagged(tag); };
                new ToolTip().SetToolTip(button, L.F("Удалить безвозвратно все найденные документы с меткой «{0}»", label));
                bulkBar.Controls.Add(button);
            }
            bulkBar.Visible = bulkBar.Controls.Count > 0;
            bulkBar.ResumeLayout();
        }

        static DataGridViewImageColumn ActionIconColumn(int width)
        {
            DataGridViewImageColumn column = new DataGridViewImageColumn();
            column.HeaderText = "";
            column.Width = width;
            column.DefaultCellStyle.NullValue = null;
            column.ImageLayout = DataGridViewImageCellLayout.Normal;
            return column;
        }

        Bitmap FileIcon(string path)
        {
            string key = Path.GetExtension(path) ?? "";
            Bitmap icon;
            if (!fileIcons.TryGetValue(key, out icon))
            {
                icon = NativeFileIcon.Small(path);
                fileIcons[key] = icon;
            }
            return icon;
        }

        void OpenDetail(int rowIndex, bool folder)
        {
            SecurityDetail item = details.Rows[rowIndex].Tag as SecurityDetail;
            if (item == null || item.FilePath.Length == 0) return;
            try
            {
                if (!File.Exists(item.FilePath)) throw new FileNotFoundException(L.T("Файл больше не существует."), item.FilePath);
                if (folder) Process.Start("explorer.exe", "/select,\"" + item.FilePath + "\"");
                else
                {
                    ProcessStartInfo info = new ProcessStartInfo(item.FilePath);
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
            }
            catch (Exception e) { Ui.Error(FindForm(), L.T("Не удалось открыть: ") + e.Message); }
        }

        void DeleteDetail(int rowIndex)
        {
            SecurityDetail item = details.Rows[rowIndex].Tag as SecurityDetail;
            DataGridViewRow checkRow = grid.CurrentRow;
            SecurityCheck check = checkRow == null ? null : checkRow.Tag as SecurityCheck;
            if (item == null || item.FilePath.Length == 0 || check == null || running) return;
            string name = Path.GetFileName(item.Text);
            if (Ui.Ask(FindForm(), L.T("Удаление документа"),
                L.F("Удалить «{0}» безвозвратно? Файл не попадёт в корзину, восстановить его будет нельзя.", name) + "\r\n\r\n" + item.Text,
                Glyph.Delete, Theme.Bad, L.T("Удалить"), L.T("Отмена")) != 0) return;
            RemoveFiles(check, checkRow, new List<SecurityDetail> { item }, rowIndex);
        }

        void DeleteTagged(string tag)
        {
            DataGridViewRow checkRow = grid.CurrentRow;
            SecurityCheck check = checkRow == null ? null : checkRow.Tag as SecurityCheck;
            if (check == null || running) return;
            int skipped;
            List<SecurityDetail> files = Tagged(check, tag, out skipped);
            if (files.Count == 0) return;
            string label = tag.TrimStart('#');
            string text = L.F("Удалить безвозвратно все найденные документы с меткой «{0}»: {1}? Файлы не попадут в корзину, восстановить их будет нельзя.", label, files.Count);
            if (skipped > 0) text += "\r\n\r\n" + L.F("Файлы с меткой SupraWeed не удаляются: {0}.", skipped);
            if (Ui.Ask(FindForm(), L.T("Удаление документов"), text, Glyph.Delete, Theme.Bad, L.T("Удалить все"), L.T("Отмена")) != 0) return;
            RemoveFiles(check, checkRow, files, details.CurrentRow == null ? 0 : details.CurrentRow.Index);
        }

        // Deletes the files for good and updates the check: its summary counts the deleted files.
        void RemoveFiles(SecurityCheck check, DataGridViewRow checkRow, List<SecurityDetail> items, int rowIndex)
        {
            int removed = 0;
            List<string> failed = new List<string>();
            string lastName = "";
            foreach (SecurityDetail item in items)
            {
                string name = Path.GetFileName(item.Text);
                try { SecurityAudit.DeleteDocument(item.FilePath); }
                catch (FileNotFoundException) { }
                catch (Exception e) { failed.Add(name + ": " + e.Message); continue; }
                check.Items.Remove(item);
                removed++;
                lastName = name;
            }
            if (removed > 0) AfterDeletion(check, checkRow, removed, rowIndex);
            if (failed.Count == 0)
                status.Set(Glyph.Check, removed == 1 ? L.T("Удалён файл: ") + lastName : L.F("Удалено файлов: {0}", removed), Theme.Good);
            else if (items.Count == 1)
                Ui.Error(FindForm(), L.T("Не удалось удалить: ") + failed[0].Substring(failed[0].IndexOf(": ") + 2));
            else
            {
                status.Set(Glyph.Warning, L.F("Удалено файлов: {0}, не удалось удалить: {1}.", removed, failed.Count), Theme.Bad);
                Ui.Error(FindForm(), L.T("Не удалось удалить: ") + "\r\n" + string.Join("\r\n", failed.GetRange(0, Math.Min(10, failed.Count)).ToArray()));
            }
        }

        void AfterDeletion(SecurityCheck check, DataGridViewRow checkRow, int removed, int rowIndex)
        {
            if (check.ScanSummary == null) check.ScanSummary = check.Summary;
            check.DeletedFiles += removed;
            bool anyLeft = false;
            foreach (SecurityDetail left in check.Items) if (left.FilePath.Length > 0) { anyLeft = true; break; }
            if (!anyLeft && check.Level == SecurityLevel.Bad)
            {
                check.Level = SecurityLevel.Warning;
                check.Summary = L.F("Найденные документы удалены ({0}). Запустите проверку снова, чтобы убедиться.", check.DeletedFiles);
            }
            else check.Summary = check.ScanSummary + L.F("; удалено: {0}", check.DeletedFiles);
            checkRow.Cells[0].Value = icons[check.Level];
            checkRow.Cells[2].Value = check.Summary;
            checkRow.Cells[2].Style.ForeColor = ColorOf(check.Level);
            ShowDetails();
            int keep = Math.Min(rowIndex, details.Rows.Count - 1);
            if (keep >= 0) details.CurrentCell = details.Rows[keep].Cells[4];
        }

        static Color ColorOf(SecurityLevel level)
        {
            if (level == SecurityLevel.Good) return Theme.Good;
            if (level == SecurityLevel.Warning) return Theme.Warn;
            if (level == SecurityLevel.Bad || level == SecurityLevel.Error) return Theme.Bad;
            return Theme.Info;
        }

    }

    static class NativeFileIcon
    {
        const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string displayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string typeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyIcon(IntPtr icon);

        public static Bitmap Small(string path)
        {
            SHFILEINFO info = new SHFILEINFO();
            try
            {
                IntPtr result = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                using (Icon icon = (Icon)Icon.FromHandle(info.hIcon).Clone()) return icon.ToBitmap();
            }
            catch (Exception) { return null; }
            finally { if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon); }
        }
    }
}
