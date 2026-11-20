using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // «Поиск по документам» (SearchPage.cs on WinForms): any phrase in file names and in the text of Word, Excel,
    // PowerPoint, LibreOffice, RTF and text files on the local disks and in the Recycle Bin (DocumentSearch.cs).
    public partial class SearchView : UserControl, ISection
    {
        readonly ObservableCollection<HitRow> hits = new ObservableCollection<HitRow>();
        readonly string allDisks = L.T("Все локальные диски");
        CancellationTokenSource cancel;
        bool running;

        public SearchView()
        {
            InitializeComponent();
            Hits.ItemsSource = hits;
            Phrase.Tag = L.T("Слово или фраза");
            System.Windows.Automation.AutomationProperties.SetName(Phrase, L.T("Слово или фраза"));
            Glyph.Label(FindButton, Glyph.Search, L.T("Найти"));
            Glyph.Label(StopButton, Glyph.Cross, L.T("Остановить"));
            WhereLabel.Text = L.T("Где");
            Where.Items.Add(allDisks);
            Where.SelectedIndex = 0;
            Glyph.Label(PickButton, Glyph.Folder, L.T("Папка…"));
            Contents.Content = L.T("в тексте документов");
            Recycle.Content = L.T("в корзине");
            TypeHeader.Text = L.T("Тип");
            WhereHeader.Text = L.T("Где · совпадений");
            FileHeader.Text = L.T("Файл");
            FragmentHeader.Text = L.T("Фрагмент");
            Status.Show(Severity.Info, Glyph.Info, L.T("Введите фразу и нажмите «Найти». Регистр и переносы строк не учитываются."));
            UpdateButtons();
        }

        public void Opened() { if (!running) { Phrase.Focus(); Phrase.SelectAll(); } }

        public bool Shortcut(KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { Phrase.Focus(); Phrase.SelectAll(); return true; }
            if (e.Key == Key.Escape && running) { cancel.Cancel(); return true; }
            return false;
        }

        public bool CanLeave()
        {
            if (running) Dialog.Show(Window.GetWindow(this), L.T("Остановите поиск или дождитесь его окончания."), Severity.Info, L.T("Поиск выполняется"));
            return !running;
        }

        public void DevicesChanged() { }

        void UpdateButtons()
        {
            FindButton.IsEnabled = !running && DocumentSearch.Normalize(Phrase.Text).Length > 0;
            StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            Phrase.IsEnabled = Where.IsEnabled = PickButton.IsEnabled = Contents.IsEnabled = Recycle.IsEnabled = !running;
        }

        void Phrase_TextChanged(object sender, TextChangedEventArgs e) { UpdateButtons(); }

        void Phrase_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { e.Handled = true; Find(); }
        }

        void Find_Click(object sender, RoutedEventArgs e) { Find(); }
        void Stop_Click(object sender, RoutedEventArgs e) { if (cancel != null) cancel.Cancel(); }

        // The folder dialog of Windows (WPF on .NET Framework has none of its own).
        void Pick_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = L.T("Папка для поиска (со всеми вложенными папками)");
                dialog.ShowNewFolderButton = false;
                if (Where.SelectedIndex > 0) dialog.SelectedPath = (string)Where.SelectedItem;
                if (dialog.ShowDialog(new Owner(Window.GetWindow(this))) != System.Windows.Forms.DialogResult.OK) return;
                while (Where.Items.Count > 1) Where.Items.RemoveAt(1);
                Where.Items.Add(dialog.SelectedPath);
                Where.SelectedIndex = 1;
                // A single folder is not the whole computer: the Recycle Bin is usually not wanted then.
                Recycle.IsChecked = false;
            }
        }

        class Owner : System.Windows.Forms.IWin32Window
        {
            public Owner(Window w) { Handle = w == null ? IntPtr.Zero : new WindowInteropHelper(w).Handle; }
            public IntPtr Handle { get; private set; }
        }

        async void Find()
        {
            if (running) return;
            var options = new SearchOptions { Phrase = Phrase.Text };
            if (DocumentSearch.Normalize(options.Phrase).Length == 0) return;
            options.Contents = Contents.IsChecked == true;
            options.Recycle = Recycle.IsChecked == true;
            bool everywhere = Where.SelectedIndex <= 0;
            if (!everywhere && !Directory.Exists((string)Where.SelectedItem))
            {
                Status.Show(Severity.Error, Glyph.Warning, L.T("Папка не найдена: ") + Where.SelectedItem);
                return;
            }
            options.Roots = everywhere ? DocumentSearch.LocalDisks() : new List<string> { (string)Where.SelectedItem };

            running = true;
            cancel = new CancellationTokenSource();
            CancellationToken token = cancel.Token;
            hits.Clear();
            UpdateButtons();
            Progress.IsIndeterminate = true;
            Progress.Visibility = Visibility.Visible;
            Status.Note(Glyph.More, L.T("Поиск файлов…"));
            try
            {
                Action<SearchProgress> report = p => Dispatcher.BeginInvoke(new Action(() => ShowProgress(p)));
                Action<SearchHit> found = hit => Dispatcher.BeginInvoke(new Action(() => hits.Add(new HitRow(hit))));
                SearchResult result = await Task.Run(() => DocumentSearch.Run(options, token, report, found));
                // the last found files come before the result
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                ShowResult(result);
            }
            catch (Exception e) { Status.Show(Severity.Error, Glyph.Warning, L.T("Поиск прерван: ") + e.Message); }
            finally
            {
                running = false;
                cancel.Dispose();
                cancel = null;
                Progress.Visibility = Visibility.Hidden;
                Progress.IsIndeterminate = false;
                UpdateButtons();
                Phrase.Focus();
            }
        }

        void ShowProgress(SearchProgress p)
        {
            if (!running) return;
            if (p.Collecting)
            {
                Progress.IsIndeterminate = true;
                Status.Note(Glyph.More, L.F("Просмотрено файлов: {0}; найдено: {1}…", p.Found, p.Matches));
                return;
            }
            Progress.IsIndeterminate = false;
            Progress.Maximum = Math.Max(1, p.Total);
            Progress.Value = Math.Min(Progress.Maximum, p.Checked);
            Status.Note(Glyph.More, L.F("Чтение документов: {0} из {1}; найдено: {2}…", p.Checked, p.Total, p.Matches));
        }

        void ShowResult(SearchResult r)
        {
            string text = L.F("Просмотрено файлов: {0}, документов прочитано: {1}. Найдено: {2}.", r.Files, r.Documents, r.MatchCount);
            var notes = new List<string>();
            if (r.Limit) notes.Add(L.F("показаны первые {0}", DocumentSearch.MaxHits));
            if (r.SkippedFolders > 0) notes.Add(L.F("нет доступа к папкам: {0}", r.SkippedFolders));
            if (r.Unreadable > 0) notes.Add(L.F("не удалось прочитать: {0}", r.Unreadable));
            if (r.RecycleError.Length > 0) notes.Add(L.T("корзина не проверена: ") + r.RecycleError);
            if (notes.Count > 0) text += " " + string.Join("; ", notes.ToArray()) + ".";
            if (r.Cancelled) Status.Show(Severity.Warning, Glyph.Info, L.T("Поиск остановлен. ") + text);
            else if (r.MatchCount == 0) Status.Note(Glyph.Info, text);
            else Status.Show(Severity.Success, Glyph.Check, text);
        }

        static HitRow RowOf(object sender) { return ((FrameworkElement)sender).DataContext as HitRow; }
        void Open_Click(object sender, RoutedEventArgs e) { Open(RowOf(sender), false); }
        void Folder_Click(object sender, RoutedEventArgs e) { Open(RowOf(sender), true); }

        void Hits_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(Hits, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item == null) return;
            for (var o = e.OriginalSource as DependencyObject; o != null && o != item; o = VisualTreeHelper.GetParent(o)) if (o is Button) return;
            Open(item.DataContext as HitRow, false);
        }

        void Hits_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Hits.SelectedItem != null) { e.Handled = true; Open(Hits.SelectedItem as HitRow, false); }
        }

        void Open(HitRow row, bool folder)
        {
            if (row == null) return;
            try
            {
                if (!File.Exists(row.Hit.Path)) throw new FileNotFoundException(L.T("Файл больше не существует."), row.Hit.Path);
                if (folder) Process.Start("explorer.exe", "/select,\"" + row.Hit.Path + "\"");
                else Process.Start(new ProcessStartInfo(row.Hit.Path) { UseShellExecute = true });
            }
            catch (Exception error) { Dialog.Show(Window.GetWindow(this), L.T("Не удалось открыть: ") + error.Message); }
        }
    }

    // A found file: «#tag #tag · count» gives the chips and the count.
    public class HitRow
    {
        public readonly SearchHit Hit;
        public ImageSource Icon { get; private set; }
        public string Type { get { return Hit.Type; } }
        public string Path { get { return Hit.DisplayPath; } }
        public string Name { get { return System.IO.Path.GetFileName(Hit.DisplayPath); } }
        public string Fragment { get { return Hit.Fragment; } }
        public string Tip { get { return Hit.ToolTip; } }
        public object Chips { get; private set; }
        public string Count { get; private set; }
        public string OpenTip { get { return L.T("Открыть"); } }
        public string FolderTip { get { return L.T("Папка"); } }

        public HitRow(SearchHit hit)
        {
            Hit = hit;
            Icon = ShellIcons.Small(hit.DisplayPath);
            FileChips chips = FileChips.Parse(hit.Note);
            Count = chips != null ? chips.Text : hit.Note;
            if (chips != null) Chips = CompInfo.Wpf.Chips.Row(chips.Labels);
        }
    }
}
