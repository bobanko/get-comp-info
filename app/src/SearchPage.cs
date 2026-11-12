using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // «Поиск по документам»: any phrase in file names and in the text of Word, Excel, PowerPoint, LibreOffice,
    // RTF and text files on the local disks and in the Recycle Bin, shown like the ДСК check results.
    public class SearchPage : Page
    {
        readonly TextBox phrase = Ui.Input(new TextBox());
        readonly Button find, stop, pick;
        readonly ComboBox where = Ui.Combo();
        readonly CheckBox contents = new CheckBox(), recycle = new CheckBox();
        readonly ProgressBar progress = new ProgressBar();
        readonly StatusLine status = new StatusLine();
        readonly DataGridView grid;
        readonly Dictionary<string, Bitmap> fileIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        readonly Bitmap openFileIcon, openFolderIcon;
        readonly string allDisks = L.T("Все локальные диски");
        CancellationTokenSource cancel;
        bool running;

        public SearchPage()
        {
            Key = "search";
            Title = L.T("Поиск по документам");
            NavTitle = L.T("Поиск");
            Description = L.T("Ищет фразу в именах файлов и в тексте документов Word, Excel, PowerPoint, LibreOffice, RTF и текстовых файлов на локальных дисках и в корзине. PDF — только по имени.");
            Icon = Glyph.Search;
            int em = Font.Height;

            phrase.Width = em * 24;
            phrase.Margin = new Padding(0, 0, em / 2, 0);
            Ui.SetCue(phrase, L.T("Слово или фраза"));
            phrase.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Find(); }
            };
            phrase.TextChanged += delegate { UpdateButtons(); };
            find = Ui.Primary(L.T("Найти"), Glyph.Search, Font);
            find.Click += delegate { Find(); };
            stop = Ui.Secondary(L.T("Остановить"), Glyph.Cross, Font);
            stop.Click += delegate { if (cancel != null) cancel.Cancel(); };
            stop.Visible = false;
            FlowLayoutPanel searchRow = Ui.Row();
            searchRow.Dock = DockStyle.Top;
            searchRow.Padding = new Padding(0, 0, 0, em / 2);
            searchRow.Controls.Add(phrase);
            searchRow.Controls.Add(find);
            searchRow.Controls.Add(stop);
            // Centre the text box against the taller button.
            phrase.Margin = new Padding(0, Math.Max(0, (find.Height - phrase.Height) / 2), em / 2, 0);

            Label whereLabel = Ui.Text(L.T("Где"), Font, Theme.Muted);
            whereLabel.Margin = new Padding(0, em / 4, em / 2, 0);
            where.Width = em * 13;
            where.DropDownWidth = em * 30;
            where.Items.Add(allDisks);
            where.SelectedIndex = 0;
            pick = Ui.Secondary(L.T("Папка…"), Glyph.Folder, Font);
            pick.Click += delegate { PickFolder(); };
            foreach (CheckBox box in new[] { contents, recycle })
            {
                box.AutoSize = true;
                box.Checked = true;
                box.ForeColor = Theme.Text;
                box.Margin = new Padding(em * 2 / 3, em / 4, 0, 0);
            }
            contents.Text = L.T("в тексте документов");
            recycle.Text = L.T("в корзине");
            FlowLayoutPanel optionsRow = Ui.Row();
            optionsRow.Dock = DockStyle.Top;
            optionsRow.Padding = new Padding(0, 0, 0, em / 2);
            optionsRow.Controls.Add(whereLabel);
            optionsRow.Controls.Add(where);
            optionsRow.Controls.Add(pick);
            optionsRow.Controls.Add(contents);
            optionsRow.Controls.Add(recycle);

            progress.Height = em / 3;
            progress.Dock = DockStyle.Top;
            progress.Visible = false;
            status.Dock = DockStyle.Top;
            status.Padding = new Padding(0, em / 4, 0, em / 2);
            status.Set(Glyph.Info, L.T("Введите фразу и нажмите «Найти». Регистр и переносы строк не учитываются."), Theme.Info);

            openFileIcon = Glyph.Image(Glyph.Open, em, Theme.Accent);
            openFolderIcon = Glyph.Image(Glyph.Folder, em, Theme.Accent);
            grid = Ui.Grid(Font);
            grid.Dock = DockStyle.Fill;
            DataGridViewImageColumn icon = new DataGridViewImageColumn();
            icon.HeaderText = "";
            icon.Width = em * 2;
            icon.DefaultCellStyle.NullValue = null;
            grid.Columns.Add(icon);
            grid.Columns.Add("type", L.T("Тип"));
            grid.Columns.Add("where", L.T("Где · совпадений"));
            grid.Columns.Add("file", L.T("Файл"));
            grid.Columns.Add("fragment", L.T("Фрагмент"));
            grid.Columns.Add(ActionColumn(em * 2));
            grid.Columns.Add(ActionColumn(em * 2));
            grid.Columns[1].Width = em * 4;
            grid.Columns[1].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            grid.Columns[2].Width = em * 11;
            grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[3].FillWeight = 40;
            grid.Columns[3].DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            grid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[4].FillWeight = 60;
            grid.Columns[4].DefaultCellStyle.ForeColor = Theme.Muted;
            grid.RowTemplate.Height = em * 2;
            grid.CellPainting += delegate (object o, DataGridViewCellPaintingEventArgs e) { FileChips.Paint(grid, e, 2); };
            grid.CellContentClick += delegate (object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && (e.ColumnIndex == 5 || e.ColumnIndex == 6)) Open(e.RowIndex, e.ColumnIndex == 6);
            };
            grid.CellDoubleClick += delegate (object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.ColumnIndex < 5) Open(e.RowIndex, false);
            };
            grid.KeyDown += delegate (object o, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && grid.CurrentRow != null) { e.SuppressKeyPress = true; Open(grid.CurrentRow.Index, false); }
            };

            Controls.Add(grid);
            Controls.Add(status);
            Controls.Add(progress);
            Controls.Add(optionsRow);
            Controls.Add(searchRow);
            UpdateButtons();
        }

        public override void Opened() { if (!running) { phrase.Focus(); phrase.SelectAll(); } }

        public override bool CanLeave()
        {
            if (running) Ui.Info(FindForm(), L.T("Поиск выполняется"), L.T("Остановите поиск или дождитесь его окончания."));
            return !running;
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == (Keys.Control | Keys.F)) { phrase.Focus(); phrase.SelectAll(); return true; }
            if (keys == Keys.Escape && running) { cancel.Cancel(); return true; }
            return false;
        }

        void UpdateButtons()
        {
            find.Enabled = !running && DocumentSearch.Normalize(phrase.Text).Length > 0;
            stop.Visible = running;
            phrase.Enabled = where.Enabled = pick.Enabled = contents.Enabled = recycle.Enabled = !running;
        }

        void PickFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = L.T("Папка для поиска (со всеми вложенными папками)");
                dialog.ShowNewFolderButton = false;
                if (where.SelectedIndex > 0) dialog.SelectedPath = (string)where.SelectedItem;
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                while (where.Items.Count > 1) where.Items.RemoveAt(1);
                where.Items.Add(dialog.SelectedPath);
                where.SelectedIndex = 1;
                // A single folder is not the whole computer: the Recycle Bin is usually not wanted then.
                recycle.Checked = false;
            }
        }

        async void Find()
        {
            if (running) return;
            SearchOptions options = new SearchOptions();
            options.Phrase = phrase.Text;
            if (DocumentSearch.Normalize(options.Phrase).Length == 0) return;
            options.Contents = contents.Checked;
            options.Recycle = recycle.Checked;
            bool everywhere = where.SelectedIndex <= 0;
            if (!everywhere && !Directory.Exists((string)where.SelectedItem))
            {
                status.Set(Glyph.Warning, L.T("Папка не найдена: ") + where.SelectedItem, Theme.Bad);
                return;
            }
            options.Roots = everywhere ? DocumentSearch.LocalDisks() : new List<string> { (string)where.SelectedItem };

            running = true;
            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            grid.Rows.Clear();
            UpdateButtons();
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 28;
            progress.Visible = true;
            status.Set(Glyph.More, L.T("Поиск файлов…"), Theme.Muted);
            try
            {
                Action<SearchProgress> report = delegate (SearchProgress p) { FromWorker(delegate { ShowProgress(p); }); };
                Action<SearchHit> found = delegate (SearchHit hit) { FromWorker(delegate { AddRow(hit); }); };
                SearchResult result = await Task.Run(delegate { return DocumentSearch.Run(options, token, report, found); });
                if (IsDisposed) return;
                ShowResult(result);
            }
            catch (Exception e) { if (!IsDisposed) status.Set(Glyph.Warning, L.T("Поиск прерван: ") + e.Message, Theme.Bad); }
            finally
            {
                running = false;
                cancel.Dispose();
                cancel = null;
                if (!IsDisposed)
                {
                    progress.Visible = false;
                    UpdateButtons();
                    phrase.Focus();
                }
            }
        }

        void ShowProgress(SearchProgress p)
        {
            if (p.Collecting)
            {
                progress.Style = ProgressBarStyle.Marquee;
                status.Set(Glyph.More, L.F("Просмотрено файлов: {0}; найдено: {1}…", p.Found, p.Matches), Theme.Muted);
                return;
            }
            progress.Style = ProgressBarStyle.Continuous;
            progress.Maximum = Math.Max(1, p.Total);
            progress.Value = Math.Min(progress.Maximum, p.Checked);
            status.Set(Glyph.More, L.F("Чтение документов: {0} из {1}; найдено: {2}…", p.Checked, p.Total, p.Matches), Theme.Muted);
        }

        void ShowResult(SearchResult r)
        {
            string text = L.F("Просмотрено файлов: {0}, документов прочитано: {1}. Найдено: {2}.", r.Files, r.Documents, r.MatchCount);
            List<string> notes = new List<string>();
            if (r.Limit) notes.Add(L.F("показаны первые {0}", DocumentSearch.MaxHits));
            if (r.SkippedFolders > 0) notes.Add(L.F("нет доступа к папкам: {0}", r.SkippedFolders));
            if (r.Unreadable > 0) notes.Add(L.F("не удалось прочитать: {0}", r.Unreadable));
            if (r.RecycleError.Length > 0) notes.Add(L.T("корзина не проверена: ") + r.RecycleError);
            if (notes.Count > 0) text += " " + string.Join("; ", notes.ToArray()) + ".";
            if (r.Cancelled) status.Set(Glyph.Info, L.T("Поиск остановлен. ") + text, Theme.Warn);
            else if (r.MatchCount == 0) status.Set(Glyph.Info, text, Theme.Muted);
            else status.Set(Glyph.Check, text, Theme.Good);
        }

        void AddRow(SearchHit hit)
        {
            int index = grid.Rows.Add(FileIcon(hit.DisplayPath), hit.Type, hit.Note, Path.GetFileName(hit.DisplayPath), hit.Fragment, openFileIcon, openFolderIcon);
            DataGridViewRow row = grid.Rows[index];
            row.Tag = hit;
            row.Cells[2].Tag = FileChips.Parse(hit.Note);
            row.Cells[0].ToolTipText = row.Cells[3].ToolTipText = hit.DisplayPath;
            row.Cells[2].ToolTipText = row.Cells[4].ToolTipText = hit.ToolTip;
            row.Cells[5].ToolTipText = L.T("Открыть");
            row.Cells[6].ToolTipText = L.T("Папка");
            row.Cells[1].Style.ForeColor = hit.Type == "WORD" ? Theme.Info : hit.Type == "EXCEL" ? Theme.Good : hit.Type == "PPT" ? Theme.Warn : Theme.Muted;
            if (grid.Rows.Count == 1) grid.ClearSelection();
        }

        void FromWorker(Action update)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(update); }
            catch (InvalidOperationException) { }
        }

        static DataGridViewImageColumn ActionColumn(int width)
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

        void Open(int rowIndex, bool folder)
        {
            SearchHit hit = grid.Rows[rowIndex].Tag as SearchHit;
            if (hit == null) return;
            try
            {
                if (!File.Exists(hit.Path)) throw new FileNotFoundException(L.T("Файл больше не существует."), hit.Path);
                if (folder) Process.Start("explorer.exe", "/select,\"" + hit.Path + "\"");
                else
                {
                    ProcessStartInfo info = new ProcessStartInfo(hit.Path);
                    info.UseShellExecute = true;
                    Process.Start(info);
                }
            }
            catch (Exception e) { Ui.Error(FindForm(), L.T("Не удалось открыть: ") + e.Message); }
        }
    }
}
