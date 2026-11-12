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
        bool running;

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
            scanDocuments.Margin = new Padding(em * 2 / 3, Math.Max(0, (run.Height - scanDocuments.PreferredSize.Height) / 2), 0, 0);
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(run);
            actions.Controls.Add(scanDocuments);
            actions.Controls.Add(disableOneDrive);
            actions.Anchor = AnchorStyles.Left;

            TableLayoutPanel toolbar = new TableLayoutPanel();
            toolbar.ColumnCount = 2;
            toolbar.RowCount = 1;
            toolbar.AutoSize = true;
            toolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            toolbar.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            status.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            status.Margin = new Padding(em, 0, 0, 0);
            status.MinimumSize = new Size(em * 12, run.Height);
            toolbar.Controls.Add(actions, 0, 0);
            toolbar.Controls.Add(status, 1, 0);

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
            openFolderIcon = Glyph.Image(Glyph.Folder, em, Theme.Accent);

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
            details.Columns.Add("match", L.T("Совпадение"));
            details.Columns.Add("detail", L.T("Файл"));
            details.Columns.Add(ActionIconColumn(em * 2));
            details.Columns.Add(ActionIconColumn(em * 2));
            details.Columns[1].Width = em * 4;
            details.Columns[1].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            details.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            details.Columns[2].FillWeight = 40;
            details.Columns[2].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            details.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            details.Columns[3].FillWeight = 60;
            details.Columns[3].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            details.Columns[3].DefaultCellStyle.ForeColor = Theme.Text;
            details.Columns[3].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            details.RowTemplate.Height = em * 2;
            foreach (DataGridViewColumn column in details.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            SetDetailMode(false);
            details.CellPainting += PaintDetailChips;
            details.CellContentClick += delegate (object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && (e.ColumnIndex == 4 || e.ColumnIndex == 5)) OpenDetail(e.RowIndex, e.ColumnIndex == 5);
            };
            details.CellDoubleClick += delegate (object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.ColumnIndex < 4) OpenDetail(e.RowIndex, false);
            };
            details.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && details.CurrentRow != null)
                {
                    e.SuppressKeyPress = true;
                    OpenDetail(details.CurrentRow.Index, false);
                }
            };

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.BackColor = Theme.Border;
            split.Panel1.BackColor = Theme.Surface;
            split.Panel2.BackColor = Theme.Surface;
            split.Panel1.Controls.Add(grid);
            split.Panel2.Controls.Add(details);
            split.SplitterWidth = Math.Max(4, em / 4);
            split.Resize += delegate
            {
                if (split.Height > em * 12 && split.SplitterDistance < em * 6)
                    split.SplitterDistance = split.Height * 3 / 5;
            };

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
                if (!IsDisposed) { run.Enabled = true; scanDocuments.Enabled = true; UpdateActions(); }
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
            if (check == null) return;
            foreach (SecurityDetail item in check.Items)
            {
                bool file = item.FilePath.Length > 0;
                string match = file ? item.Note : item.Text;
                string fileName = file ? Path.GetFileName(item.Text) : "";
                if (file && string.IsNullOrEmpty(fileName)) fileName = item.Text;
                int index = details.Rows.Add(file ? FileIcon(item.FilePath) : null, item.FileType, match, fileName,
                    file ? openFileIcon : null, file ? openFolderIcon : null);
                DataGridViewRow row = details.Rows[index];
                row.Tag = item;
                if (file) row.Cells[2].Tag = FileChips.Parse(match);
                if (file)
                {
                    row.Cells[0].ToolTipText = item.Text;
                    row.Cells[2].ToolTipText = item.ToolTip;
                    row.Cells[3].ToolTipText = item.Text;
                    row.Cells[4].ToolTipText = L.T("Открыть");
                    row.Cells[5].ToolTipText = L.T("Папка");
                }
                if (item.FileType.Length > 0)
                {
                    row.Cells[1].Style.ForeColor = item.FileType == "WORD" ? Theme.Info : item.FileType == "EXCEL" ? Theme.Good : Theme.Warn;
                }
            }
            details.ClearSelection();
        }


        void PaintDetailChips(object sender, DataGridViewCellPaintingEventArgs e) { FileChips.Paint(details, e, 2); }

        void SetDetailMode(bool files)
        {
            details.Columns[0].Visible = files;
            details.Columns[1].Visible = files;
            details.Columns[2].HeaderText = files ? L.T("Совпадение") : L.T("Детали");
            details.Columns[2].FillWeight = files ? 40 : 100;
            details.Columns[3].Visible = files;
            details.Columns[4].Visible = files;
            details.Columns[5].Visible = files;
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
