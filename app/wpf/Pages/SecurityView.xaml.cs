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
using System.Windows.Data;
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
        // The found files: "list" (by importance) or "tree" (by user, then the place: bin, Claude, Документы…).
        public static string FileView = "list";
        public static Action<string> FileViewSaved;

        readonly ObservableCollection<CheckRow> checks = new ObservableCollection<CheckRow>();
        // the groups of the view by users that the user folded (PlaceGroup.Key), kept while the list is rebuilt
        readonly HashSet<string> folded = new HashSet<string>();
        ProfileFolders profiles;
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
            ListMode.ToolTip = L.T("Списком");
            TreeMode.ToolTip = L.T("По пользователям и папкам");
            System.Windows.Automation.AutomationProperties.SetName(ListMode, (string)ListMode.ToolTip);
            System.Windows.Automation.AutomationProperties.SetName(TreeMode, (string)TreeMode.ToolTip);
            (FileView == "tree" ? TreeMode : ListMode).IsChecked = true;
            CheckColumn.Header = L.T("Проверка");
            ResultColumn.Header = L.T("Результат");
            DetailsHeader.Text = L.T("Детали");
            TypeColumn.Header = L.T("Тип");
            TagsColumn.Header = L.T("Метки");
            MatchesColumn.Header = L.T("Совпадений");
            FileColumn.Header = L.T("Файл");
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
                if (documents)
                {
                    // the admin's list from the cloud (or its saved copy, or the built-in one); where it came from
                    // is the tooltip of «Сканировать документы»
                    string source = null;
                    SecurityAudit.Words = await Task.Run(() => SuspectorUpdate.LoadTriggerWords(App.Settings.Beside("SUSpector-words.json"), out source));
                    ScanDocuments.ToolTip = source;
                }
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
            FileViewSwitch.Visibility = files ? Visibility.Visible : Visibility.Collapsed;
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
            // without tags the column keeps no title and shrinks to nothing (a GridView column cannot be hidden)
            TagsColumn.Header = tags ? L.T("Метки") : "";
            Lines.ItemsSource = lines;
            Files.ItemsSource = FileView == "tree" ? ByPlace(found) : (System.Collections.IEnumerable)found;
        }

        void FileView_Checked(object sender, RoutedEventArgs e)
        {
            if (!ready) return;
            FileView = TreeMode.IsChecked == true ? "tree" : "list";
            object chosen = Files.SelectedItem;
            ShowDetails();
            var row = chosen as FileRow;
            if (row != null)
                foreach (object item in Files.Items)
                    if (item is FileRow && ((FileRow)item).Item == row.Item) { Files.SelectedItem = item; Files.ScrollIntoView(item); break; }
            if (FileViewSaved != null) FileViewSaved(FileView);
        }

        // The view by users: the found files grouped by their user (or disk), then by the place in it; users before
        // disks, the groups with the most important files first, the files in the order of the list. A file shows its
        // folders under the place.
        ListCollectionView ByPlace(List<FileRow> found)
        {
            if (profiles == null) profiles = ProfileFolders.Read();
            var groups = new Dictionary<string, PlaceGroup>();
            var order = new Dictionary<FileRow, int>();
            foreach (FileRow row in found)
            {
                order[row] = order.Count;
                DocumentPlace place = SecurityFiles.Place(row.Item, profiles);
                row.Owner = Group(groups, place.OwnerKey, 1, place.OwnerKind, place.Owner, place.OwnerKind == "user" ? place.OwnerPath : "");
                row.Place = Group(groups, place.FolderKey, 2, place.FolderKind, place.Folder, "");
                if (place.FolderKind == "app") row.Place.Glyph = place.Folder == "Claude" ? "" : place.Folder == "SupraWeed" ? Glyph.Lock : "";
                row.Folder = System.IO.Path.GetDirectoryName(place.Rest);
                foreach (PlaceGroup group in new[] { row.Owner, row.Place })
                {
                    group.Count++;
                    group.Level = Math.Max(group.Level, row.Item.Level);
                }
            }
            foreach (PlaceGroup group in groups.Values) group.Done();
            found.Sort(delegate (FileRow a, FileRow b)
            {
                int c = Compare(a.Owner, b.Owner);
                if (c == 0) c = Compare(a.Place, b.Place);
                return c != 0 ? c : order[a].CompareTo(order[b]);
            });
            var view = new ListCollectionView(found);
            view.GroupDescriptions.Add(new PropertyGroupDescription("Owner"));
            view.GroupDescriptions.Add(new PropertyGroupDescription("Place"));
            return view;
        }

        PlaceGroup Group(Dictionary<string, PlaceGroup> groups, string key, int depth, string kind, string label, string hint)
        {
            PlaceGroup group;
            if (!groups.TryGetValue(key, out group))
            {
                group = new PlaceGroup(key, folded) { Depth = depth, Kind = kind, Label = label, Hint = hint };
                groups.Add(key, group);
            }
            return group;
        }

        static int Compare(PlaceGroup a, PlaceGroup b)
        {
            if (a == b) return 0;
            int c = a.Rank.CompareTo(b.Rank);
            if (c == 0) c = b.Level.CompareTo(a.Level);
            if (c == 0) c = b.Count.CompareTo(a.Count);
            if (c == 0) c = string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
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
                    string users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                    c.Items.Add(new SecurityDetail(Path.Combine(users, "olena\\Desktop\\Накази\\наказ 17.docx"), "#Desktop · 6", "C:\\demo\\order.docx", "WORD", "6"));
                    c.Items.Add(new SecurityDetail(Path.Combine(users, "olena\\AppData\\Roaming\\Claude\\uploads\\штат.xlsx"), "#Claude · 2", "C:\\demo\\staff.xlsx", "EXCEL", "2"));
                    c.Items.Add(new SecurityDetail("D:\\Архів\\2025\\звіт.docx", "1", "C:\\demo\\archive.docx", "WORD", "1"));
                    int[] demoLevels = { TriggerWords.High, TriggerWords.High, TriggerWords.Medium, TriggerWords.Medium, TriggerWords.Low,
                                         TriggerWords.High, TriggerWords.Medium, TriggerWords.Low };
                    for (int i = 0; i < c.Items.Count; i++) c.Items[i].Level = demoLevels[i];
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

    // A group of the view by users (DocumentPlace): a user or a disk (Depth 1), a place in it (Depth 2). Folding is kept
    // in the page's set by Key, so a group stays folded when the list is rebuilt after a deletion.
    public class PlaceGroup : INotifyPropertyChanged
    {
        readonly HashSet<string> folded;

        public string Key { get; private set; }
        public int Depth { get; set; }
        public string Kind { get; set; }
        public string Label { get; set; }
        public string Hint { get; set; }
        public string Glyph { get; set; }
        // the colour of the icon: Accent, Bad, Muted or Folder (PlaceGroupHead of SecurityView.xaml)
        public string Tone { get; set; }
        public Thickness Indent { get { return new Thickness(Depth == 1 ? 8 : 30, 0, 8, 0); } }
        // the files in the group and the highest importance among them
        public int Count { get; set; }
        public int Level { get; set; }
        public object Badge { get; private set; }
        public string BadgeTip { get; private set; }
        // users first, then disks; the bin first among the places
        public int Rank { get { return Kind == "user" || Kind == "bin" ? 0 : Kind == "drive" || Kind == "app" ? 1 : 2; } }

        public PlaceGroup(string key, HashSet<string> folded) { Key = key; this.folded = folded; }

        public bool Expanded
        {
            get { return !folded.Contains(Key); }
            set
            {
                if (value) folded.Remove(Key); else folded.Add(Key);
                if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Expanded"));
            }
        }

        // After the files are counted: the icon, its colour and the number of files.
        public void Done()
        {
            if (Glyph == null)
                Glyph = Kind == "user" ? CompInfo.Wpf.Glyph.Person : Kind == "drive" ? "" : Kind == "bin" ? CompInfo.Wpf.Glyph.Delete
                      : Kind == "folder" ? CompInfo.Wpf.Glyph.Folder : Kind == "root" ? CompInfo.Wpf.Glyph.Document : CompInfo.Wpf.Glyph.Info;
            Tone = Kind == "user" || Kind == "app" ? "Accent" : Kind == "bin" ? "Bad" : Kind == "folder" ? "Folder" : "Muted";
            Badge = Chips.Level(Level, Count.ToString());
            BadgeTip = L.F("Файлов: {0}", Count) + "\r\n" + L.T("Важность: ") + TriggerWords.LevelName(Level);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    // A found document: a note «#tag #tag · count» gives the chips and the number of matches.
    public class FileRow
    {
        public readonly SecurityDetail Item;
        public ImageSource Icon { get; private set; }
        public string Type { get { return Item.FileType; } }
        public string Path { get { return Item.Text; } }
        public string Name { get; private set; }
        // in the view by users: the folders between the place and the file
        public string Folder { get; set; }
        public Visibility FolderVisibility { get { return string.IsNullOrEmpty(Folder) ? Visibility.Collapsed : Visibility.Visible; } }
        // the groups of the view by users: the user (or disk) and the place in it
        public PlaceGroup Owner { get; set; }
        public PlaceGroup Place { get; set; }
        public string Matches { get; private set; }
        // the number of matches in the colour of the document's importance (Chips.Level)
        public object MatchesBadge { get; private set; }
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
            MatchesBadge = CompInfo.Wpf.Chips.Level(item.Level, Matches);
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
