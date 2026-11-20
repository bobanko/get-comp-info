using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // «Кибербезопасность»: the local checks of the computer (SecurityPage.cs on WinForms). The checks run one after
    // another in the background; a chosen check shows its details, the ДСК check its found files with open, folder
    // and delete. Nothing is sent anywhere.
    public partial class SecurityView : UserControl, ISection
    {
        // Where the file table is: "bottom" (under the checks) or "right" (next to them).
        // The program that hosts the page loads it and saves a change through LayoutSaved.
        public static string TableLayout = "bottom";
        public static Action<string> LayoutSaved;

        readonly ObservableCollection<CheckRow> checks = new ObservableCollection<CheckRow>();
        bool running, ready;
        CancellationTokenSource cancel;

        public SecurityView()
        {
            InitializeComponent();
            Checks.ItemsSource = checks;
            Glyph.Label(RunButton, Glyph.Play, L.T("Запустить проверку"));
            RunButton.ToolTip = L.T("Запустить проверку") + "   (F5)";
            Glyph.Label(StopButton, Glyph.Cross, L.T("Остановить"));
            StopButton.ToolTip = L.T("Остановить") + "   (Esc)";
            ScanDocuments.Content = L.T("Сканировать документы");
            Glyph.Label(OneDriveButton, Glyph.Power, L.T("Отключить OneDrive"));
            Bottom.ToolTip = L.T("Таблицы одна под другой");
            Right.ToolTip = L.T("Таблицы рядом");
            System.Windows.Automation.AutomationProperties.SetName(Bottom, (string)Bottom.ToolTip);
            System.Windows.Automation.AutomationProperties.SetName(Right, (string)Right.ToolTip);
            CheckHeader.Text = L.T("Проверка");
            ResultHeader.Text = L.T("Результат");
            DetailsHeader.Text = L.T("Детали");
            TypeHeader.Text = L.T("Тип");
            TagsHeader.Text = L.T("Метки");
            MatchesHeader.Text = L.T("Совпадений");
            FileHeader.Text = L.T("Файл");
            (TableLayout == "right" ? Right : Bottom).IsChecked = true;
            ApplyLayout();
            ready = true;
            Status.Show(Severity.Info, Glyph.Info, L.T("Нажмите «Запустить проверку». Полный поиск документов может занять несколько минут."));
        }

        public void Opened() { if (!running) RunButton.Focus(); }

        // The window may close (or be built again) only when no check is running.
        public bool CanLeave()
        {
            if (running) Dialog.Show(Window.GetWindow(this), L.T("Остановите проверку или дождитесь её окончания."), Severity.Info, L.T("Проверка выполняется"));
            return !running;
        }

        public bool Shortcut(KeyEventArgs e)
        {
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None) { Run(); return true; }
            if (e.Key == Key.Escape && running) { Stop(); return true; }
            return false;
        }

        // The status line, for the host to show elsewhere (SUSpector: at the bottom left, in the row of its buttons).
        public StatusLine TakeStatus()
        {
            var parent = Status.Parent as Panel;
            if (parent != null) parent.Children.Remove(Status);
            Status.Margin = new Thickness(0, 0, 16, 0);
            Status.HorizontalAlignment = HorizontalAlignment.Left;
            Status.VerticalAlignment = VerticalAlignment.Center;
            return Status;
        }

        void Stop_Click(object sender, RoutedEventArgs e) { Stop(); }

        // The check stops at the next folder or document, or between the checks.
        public void Stop()
        {
            if (!running || cancel == null) return;
            cancel.Cancel();
            StopButton.IsEnabled = false;
            Status.Note(Glyph.More, L.T("Останавливается…"));
        }

        public void DevicesChanged() { }

        // F5 of the window.
        public void RunAgain() { Run(); }

        void Run_Click(object sender, RoutedEventArgs e) { Run(); }

        async void Run()
        {
            if (running) return;
            running = true;
            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            RunButton.Visibility = Visibility.Collapsed;
            StopButton.IsEnabled = true;
            StopButton.Visibility = Visibility.Visible;
            ScanDocuments.IsEnabled = false;
            bool documents = ScanDocuments.IsChecked == true;
            OneDriveButton.Visibility = Visibility.Collapsed;
            checks.Clear();
            List<SecurityCheck> plan = SecurityAudit.Plan(documents);
            foreach (SecurityCheck check in plan)
            {
                bool skipped = !documents && check.Key == "documents";
                checks.Add(new CheckRow(check, skipped ? "Info" : "Waiting"));
            }
            if (checks.Count > 0) Checks.SelectedIndex = 0;
            Progress.IsIndeterminate = false;
            Progress.Minimum = 0;
            Progress.Maximum = plan.Count;
            Progress.Value = 0;
            Progress.Visibility = Visibility.Visible;
            Status.Note(Glyph.More, L.T("Подготовка проверки…"));
            try
            {
                int completedCount = 0;
                Action<SecurityCheck> started = check => Later(() =>
                {
                    CheckRow row = Find(check.Key);
                    if (row != null)
                    {
                        row.Set(check, "Running", L.T("Проверяется…"));
                        if (Checks.SelectedItem == row) { ShowDetails(); UpdateActions(); }
                    }
                    if (check.Key != "documents")
                    {
                        Progress.IsIndeterminate = false;
                        Progress.Maximum = plan.Count;
                        Progress.Value = Math.Min(Progress.Maximum, completedCount);
                    }
                    Status.Note(Glyph.More, L.T("Выполняется: ") + check.Title + "…");
                });
                Action<SecurityCheck> completed = check => Later(() =>
                {
                    CheckRow row = Find(check.Key);
                    if (row != null)
                    {
                        row.Set(check, StateOf(check.Level), check.Summary);
                        if (Checks.SelectedItem == row) ShowDetails();
                    }
                    completedCount++;
                    Progress.IsIndeterminate = false;
                    Progress.Maximum = plan.Count;
                    Progress.Value = Math.Min(Progress.Maximum, completedCount);
                });
                Action<SecurityProgress> detailed = scan => Later(() =>
                {
                    if (scan.Indeterminate)
                    {
                        Progress.IsIndeterminate = true;
                        Status.Note(Glyph.More, L.F("Поиск Office-файлов: найдено {0}. Составляется список…", scan.Found));
                        return;
                    }
                    Progress.IsIndeterminate = false;
                    Progress.Maximum = Math.Max(1, scan.Total);
                    Progress.Value = Math.Min(Progress.Maximum, scan.Checked);
                    Status.Note(Glyph.More, L.F("Проверено {0} из {1} файлов; документов с совпадениями: {2}.", scan.Checked, scan.Total, scan.Matches));
                });
                List<SecurityCheck> results = await Task.Run(() => SecurityAudit.Run(started, completed, detailed, documents, token));
                int bad = 0, warnings = 0;
                foreach (SecurityCheck check in results)
                {
                    if (check.Level == SecurityLevel.Bad || check.Level == SecurityLevel.Error) bad++;
                    else if (check.Level == SecurityLevel.Warning) warnings++;
                }
                // the last updates of the checks come before the result
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                if (bad > 0)
                    Status.Show(Severity.Error, Glyph.Warning, L.F("Проверка завершена. Проблем: {0}, предупреждений: {1}.", bad, warnings));
                else if (warnings > 0)
                    Status.Show(Severity.Warning, Glyph.Warning, L.F("Проверка завершена без критических проблем. Предупреждений: {0}.", warnings));
                else Status.Show(Severity.Success, Glyph.Check, L.T("Проверка завершена: проблем не найдено."));
            }
            catch (OperationCanceledException)
            {
                // the updates already sent come first, then the checks that did not finish are marked
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                int done = 0;
                foreach (CheckRow row in checks)
                {
                    if (row.State == "Waiting" || row.State == "Running") row.Set(row.Check, "Info", L.T("Не проверено: проверку остановили"));
                    else done++;
                }
                ShowDetails();
                Status.Show(Severity.Warning, Glyph.Info, L.F("Проверка остановлена. Выполнено проверок: {0} из {1}.", done, checks.Count));
            }
            catch (Exception e)
            {
                Status.Show(Severity.Error, Glyph.Warning, L.T("Проверка прервана: ") + e.Message);
            }
            finally
            {
                running = false;
                cancel = null;
                Progress.Visibility = Visibility.Hidden;
                Progress.IsIndeterminate = false;
                StopButton.Visibility = Visibility.Collapsed;
                RunButton.Visibility = Visibility.Visible;
                RunButton.IsEnabled = true;
                ScanDocuments.IsEnabled = true;
                UpdateActions();
                UpdateBulkBar(Selected);
            }
        }

        // Updates from the worker thread, in their order.
        void Later(Action update) { Dispatcher.BeginInvoke(update); }

        CheckRow Find(string key)
        {
            foreach (CheckRow row in checks) if (row.Check.Key == key) return row;
            return null;
        }

        SecurityCheck Selected { get { var row = Checks.SelectedItem as CheckRow; return row == null ? null : row.Check; } }

        static string StateOf(SecurityLevel level)
        {
            return level == SecurityLevel.Good ? "Good" : level == SecurityLevel.Warning ? "Warning" :
                   level == SecurityLevel.Bad || level == SecurityLevel.Error ? "Bad" : "Info";
        }

        void Checks_SelectionChanged(object sender, SelectionChangedEventArgs e) { ShowDetails(); UpdateActions(); }

        void UpdateActions()
        {
            SecurityCheck check = Selected;
            OneDriveButton.Visibility = check != null && check.ActionKey == "disable-onedrive" ? Visibility.Visible : Visibility.Collapsed;
            OneDriveButton.IsEnabled = !running;
        }

        async void OneDrive_Click(object sender, RoutedEventArgs e)
        {
            if (running) return;
            if (!Dialog.Ask(Window.GetWindow(this),
                L.T("OneDrive будет остановлен и убран из автозапуска. Локальные файлы и настройка учётной записи не удаляются."),
                L.T("Отключить синхронизацию"), null, Severity.Warning, "OneDrive")) return;
            OneDriveButton.IsEnabled = RunButton.IsEnabled = false;
            Status.Note(Glyph.More, L.T("Отключение синхронизации OneDrive…"));
            try
            {
                await Task.Run(() => SecurityAudit.DisableOneDriveSync());
                SecurityCheck check = await Task.Run(() => SecurityAudit.CheckOneDriveNow());
                CheckRow row = Find("onedrive");
                if (row != null)
                {
                    row.Set(check, StateOf(check.Level), check.Summary);
                    if (Checks.SelectedItem == row) ShowDetails();
                }
                Status.Show(Severity.Success, Glyph.Check, L.T("Синхронизация OneDrive отключена. Файлы и настройка учётной записи сохранены."));
            }
            catch (Exception error) { Status.Show(Severity.Error, Glyph.Warning, L.T("Не удалось отключить OneDrive: ") + error.Message); }
            finally
            {
                RunButton.IsEnabled = true;
                UpdateActions();
            }
        }

        // ---------- details ----------

        void ShowDetails()
        {
            SecurityCheck check = Selected;
            bool files = SecurityFiles.HasFiles(check);
            TextDetails.Visibility = files ? Visibility.Collapsed : Visibility.Visible;
            FileDetails.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
            UpdateBulkBar(check);
            var lines = new List<LineRow>();
            var found = new List<FileRow>();
            bool tags = false;
            if (check != null)
                foreach (SecurityDetail item in check.Items)
                {
                    if (item.FilePath.Length == 0) lines.Add(new LineRow { Text = item.Text });
                    else
                    {
                        var row = new FileRow(item);
                        tags |= row.HasChips;
                        found.Add(row);
                    }
                }
            foreach (FileRow row in found) row.ChipsVisibility = tags ? Visibility.Visible : Visibility.Collapsed;
            TagsHeader.Visibility = tags ? Visibility.Visible : Visibility.Collapsed;
            Lines.ItemsSource = lines;
            Files.ItemsSource = found;
        }

        void UpdateBulkBar(SecurityCheck check)
        {
            BulkButtons.Children.Clear();
            foreach (string tag in SecurityFiles.BulkTags)
            {
                int skipped;
                List<SecurityDetail> files = SecurityFiles.Tagged(check, tag, out skipped);
                if (files.Count == 0) continue;
                if (BulkButtons.Children.Count == 0)
                {
                    var caption = new TextBlock { Text = L.T("Удалить все найденные:"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 4) };
                    caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                    BulkButtons.Children.Add(caption);
                }
                string label = tag.TrimStart('#');
                var button = new Button { Margin = new Thickness(0, 0, 6, 4), IsEnabled = !running, MinHeight = 28, Padding = new Thickness(10, 3, 10, 4),
                    ToolTip = L.F("Удалить безвозвратно все найденные документы с меткой «{0}»", label) };
                Glyph.Label(button, Glyph.Delete, label + " · " + files.Count);
                string chosen = tag;
                button.Click += delegate { DeleteTagged(chosen); };
                BulkButtons.Children.Add(button);
            }
            BulkBar.Visibility = BulkButtons.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        static FileRow RowOf(object sender) { return ((FrameworkElement)sender).DataContext as FileRow; }

        void Open_Click(object sender, RoutedEventArgs e) { Open(RowOf(sender), false); }
        void Folder_Click(object sender, RoutedEventArgs e) { Open(RowOf(sender), true); }
        void Delete_Click(object sender, RoutedEventArgs e) { Delete(RowOf(sender)); }

        void Files_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(Files, e.OriginalSource as DependencyObject) as ListBoxItem;
            // not on the buttons of the row: they do their own thing
            if (item != null && !(e.OriginalSource is DependencyObject && FindButton((DependencyObject)e.OriginalSource))) Open(item.DataContext as FileRow, false);
        }

        static bool FindButton(DependencyObject o)
        {
            for (; o != null && !(o is ListBoxItem); o = VisualTreeHelper.GetParent(o)) if (o is Button) return true;
            return false;
        }

        void Files_KeyDown(object sender, KeyEventArgs e)
        {
            var row = Files.SelectedItem as FileRow;
            if (row == null) return;
            if (e.Key == Key.Enter) { e.Handled = true; Open(row, false); }
            else if (e.Key == Key.Delete) { e.Handled = true; Delete(row); }
        }

        void Open(FileRow row, bool folder)
        {
            if (row == null) return;
            try
            {
                if (!File.Exists(row.Item.FilePath)) throw new FileNotFoundException(L.T("Файл больше не существует."), row.Item.FilePath);
                if (folder) Process.Start("explorer.exe", "/select,\"" + row.Item.FilePath + "\"");
                else Process.Start(new ProcessStartInfo(row.Item.FilePath) { UseShellExecute = true });
            }
            catch (Exception error) { Dialog.Show(Window.GetWindow(this), L.T("Не удалось открыть: ") + error.Message); }
        }

        void Delete(FileRow row)
        {
            SecurityCheck check = Selected;
            if (row == null || check == null || running || row.Protected) return;
            string name = Path.GetFileName(row.Item.Text);
            if (!Dialog.Ask(Window.GetWindow(this),
                L.F("Удалить «{0}» безвозвратно? Файл не попадёт в корзину, восстановить его будет нельзя.", name) + "\r\n\r\n" + row.Item.Text,
                L.T("Удалить"), null, Severity.Error, L.T("Удаление документа"))) return;
            RemoveFiles(check, new List<SecurityDetail> { row.Item }, Files.Items.IndexOf(row));
        }

        void DeleteTagged(string tag)
        {
            SecurityCheck check = Selected;
            if (check == null || running) return;
            int skipped;
            List<SecurityDetail> files = SecurityFiles.Tagged(check, tag, out skipped);
            if (files.Count == 0) return;
            string label = tag.TrimStart('#');
            string text = L.F("Удалить безвозвратно все найденные документы с меткой «{0}»: {1}? Файлы не попадут в корзину, восстановить их будет нельзя.", label, files.Count);
            if (skipped > 0) text += "\r\n\r\n" + L.F("Файлы с меткой SupraWeed не удаляются: {0}.", skipped);
            if (!Dialog.Ask(Window.GetWindow(this), text, L.T("Удалить все"), null, Severity.Error, L.T("Удаление документов"))) return;
            RemoveFiles(check, files, Math.Max(0, Files.SelectedIndex));
        }

        // Deletes the files for good and updates the check: its summary counts the deleted files.
        void RemoveFiles(SecurityCheck check, List<SecurityDetail> items, int keepIndex)
        {
            int removed = 0;
            var failed = new List<string>();
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
            if (removed > 0)
            {
                SecurityFiles.Removed(check, removed);
                var row = Checks.SelectedItem as CheckRow;
                if (row != null) row.Set(check, StateOf(check.Level), check.Summary);
                ShowDetails();
                int keep = Math.Min(keepIndex, Files.Items.Count - 1);
                if (keep >= 0) Files.SelectedIndex = keep;
            }
            Window owner = Window.GetWindow(this);
            if (failed.Count == 0)
                Status.Show(Severity.Success, Glyph.Check, removed == 1 ? L.T("Удалён файл: ") + lastName : L.F("Удалено файлов: {0}", removed));
            else if (items.Count == 1)
                Dialog.Show(owner, L.T("Не удалось удалить: ") + failed[0].Substring(failed[0].IndexOf(": ") + 2));
            else
            {
                Status.Show(Severity.Error, Glyph.Warning, L.F("Удалено файлов: {0}, не удалось удалить: {1}.", removed, failed.Count));
                Dialog.Show(owner, L.T("Не удалось удалить: ") + "\r\n" + string.Join("\r\n", failed.GetRange(0, Math.Min(10, failed.Count)).ToArray()));
            }
        }

        // ---------- the two tables: one under the other or side by side ----------

        void Layout_Checked(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            TableLayout = Right.IsChecked == true ? "right" : "bottom";
            ApplyLayout();
            if (LayoutSaved != null) LayoutSaved(TableLayout);
        }

        void ApplyLayout()
        {
            bool right = TableLayout == "right";
            Split.RowDefinitions.Clear();
            Split.ColumnDefinitions.Clear();
            if (right)
            {
                Split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9, GridUnitType.Star), MinWidth = 200 });
                Split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                Split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(11, GridUnitType.Star), MinWidth = 200 });
            }
            else
            {
                Split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 100 });
                Split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
                Split.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 100 });
            }
            foreach (UIElement part in new UIElement[] { ChecksFrame, Splitter, DetailsFrame })
            {
                int index = part == ChecksFrame ? 0 : part == Splitter ? 1 : 2;
                Grid.SetRow(part, right ? 0 : index);
                Grid.SetColumn(part, right ? index : 0);
            }
            Splitter.ResizeDirection = right ? GridResizeDirection.Columns : GridResizeDirection.Rows;
            Splitter.Cursor = right ? Cursors.SizeWE : Cursors.SizeNS;
        }

        // ---------- screenshots ----------

        // Only for the screenshots (--demo): made-up results of every kind, a check with found files chosen.
        public void Demo()
        {
            checks.Clear();
            foreach (SecurityCheck plan in SecurityAudit.Plan(true))
            {
                SecurityCheck c = new SecurityCheck(plan.Key, plan.Title, SecurityLevel.Good, L.T("Проверка завершена: проблем не найдено."), "");
                if (plan.Key == "onedrive") { c.Level = SecurityLevel.Warning; c.Summary = "OneDrive"; c.ActionKey = "disable-onedrive"; }
                if (plan.Key == "documents")
                {
                    c.Level = SecurityLevel.Bad;
                    c.Summary = L.F("Проверено {0} из {1} файлов; документов с совпадениями: {2}.", 812, 812, 5);
                    string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    c.Items.Add(new SecurityDetail(Path.Combine(docs, "Downloads\\report-2026.docx"), L.T("#даунлоадс") + " #Telegram · 3", "C:\\demo\\report-2026.docx", "WORD", "3"));
                    c.Items.Add(new SecurityDetail(Path.Combine(docs, "plan.xlsx"), L.T("#корзина") + " · 1", "C:\\demo\\plan.xlsx", "EXCEL", "1"));
                    c.Items.Add(new SecurityDetail(Path.Combine(docs, "SupraWeed\\template.docx"), "#SupraWeed · 2", "C:\\demo\\template.docx", "WORD", "2"));
                    c.Items.Add(new SecurityDetail(Path.Combine(docs, "ViberDownloads\\scan.pdf"), "#Viber #WhatsApp · 4", "C:\\demo\\scan.pdf", "PDF", "4"));
                    c.Items.Add(new SecurityDetail(Path.Combine(docs, "notes.txt"), "1", "C:\\demo\\notes.txt", "TXT", "1"));
                }
                checks.Add(new CheckRow(c, StateOf(c.Level)));
            }
            Checks.SelectedItem = Find("documents") ?? (checks.Count > 0 ? checks[0] : null);
            if (Checks.SelectedItem != null) Checks.ScrollIntoView(Checks.SelectedItem);
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            Status.Show(Severity.Error, Glyph.Warning, L.F("Проверка завершена. Проблем: {0}, предупреждений: {1}.", 1, 1));
        }
    }

    // One check in the table: its state (Waiting, Running, Good, Warning, Bad, Info) gives the icon and the colours.
    public class CheckRow : INotifyPropertyChanged
    {
        public SecurityCheck Check { get; private set; }
        public string State { get; private set; }
        public string Summary { get; private set; }
        public string Title { get { return Check.Title; } }

        public string Glyph
        {
            get
            {
                switch (State)
                {
                    case "Waiting": return CompInfo.Wpf.Glyph.Circle;
                    case "Running": return CompInfo.Wpf.Glyph.More;
                    case "Good": return CompInfo.Wpf.Glyph.Check;
                    case "Warning": return CompInfo.Wpf.Glyph.Warning;
                    case "Bad": return Check.Level == SecurityLevel.Error ? CompInfo.Wpf.Glyph.Warning : CompInfo.Wpf.Glyph.Cross;
                    default: return CompInfo.Wpf.Glyph.Info;
                }
            }
        }

        public CheckRow(SecurityCheck check, string state)
        {
            Check = check;
            State = state;
            Summary = check.Summary;
        }

        public void Set(SecurityCheck check, string state, string summary)
        {
            Check = check;
            State = state;
            Summary = summary;
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(""));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class LineRow { public string Text { get; set; } }

    // A found document: a note «#tag #tag · count» gives the chips and the number of matches.
    public class FileRow
    {
        public readonly SecurityDetail Item;
        public ImageSource Icon { get; private set; }
        public string Type { get { return Item.FileType; } }
        public string Path { get { return Item.Text; } }
        public string Name { get; private set; }
        public string Matches { get; private set; }
        public string MatchesTip { get { return Item.ToolTip; } }
        public object Chips { get; private set; }
        public string ChipsTip { get; private set; }
        public bool HasChips { get; private set; }
        public Visibility ChipsVisibility { get; set; }
        public bool Protected { get; private set; }
        public string OpenTip { get { return L.T("Открыть"); } }
        public string FolderTip { get { return L.T("Папка"); } }
        public string DeleteTip
        {
            get
            {
                return Protected ? L.T("Файлы SupraWeed не удаляются: это шаблоны и файлы самой программы, в них только слова-триггеры.")
                                 : L.T("Удалить (Delete)");
            }
        }

        public FileRow(SecurityDetail item)
        {
            Item = item;
            Icon = ShellIcons.Small(item.FilePath);
            string name = System.IO.Path.GetFileName(item.Text);
            Name = string.IsNullOrEmpty(name) ? item.Text : name;
            FileChips chips = FileChips.Parse(item.Note);
            Matches = chips != null ? chips.Text : item.Note;
            HasChips = chips != null;
            if (chips != null)
            {
                Chips = CompInfo.Wpf.Chips.Row(chips.Labels);
                ChipsTip = "#" + string.Join(" #", chips.Labels.ToArray());
            }
            Protected = SecurityFiles.Protected(item);
        }
    }
}
