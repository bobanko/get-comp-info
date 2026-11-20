using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // «База устройств» (DatabasePage.cs on WinForms): the records of this copy with the search in any alphabet,
    // the filters of the web device list, the sort by a column title, the card of a computer and the CSV export.
    public partial class DatabaseView : UserControl, ISection
    {
        readonly DeviceList list = new DeviceList();
        readonly TextBlock[] arrows = new TextBlock[9];
        List<RecordFile> shown = new List<RecordFile>();
        List<string> words = new List<string>();
        CurrentComputer me;
        int sortColumn;
        bool descending, ready;

        public DatabaseView()
        {
            InitializeComponent();
            Search.Tag = L.T("Поиск по всем полям, кириллицей или латиницей");
            System.Windows.Automation.AutomationProperties.SetName(Search, (string)Search.Tag);
            foreach (string s in DeviceList.EsetFilters) EsetFilter.Items.Add(s);
            foreach (string s in DeviceList.InventoryFilters) InventoryFilter.Items.Add(s);
            foreach (string s in list.RepeatFilters) RepeatsFilter.Items.Add(s);
            foreach (ComboBox c in new[] { EsetFilter, InventoryFilter, RepeatsFilter }) c.SelectedIndex = 0;
            Glyph.Label(ReloadButton, Glyph.Refresh, L.T("Обновить"));
            ReloadButton.ToolTip = L.T("Обновить") + "   (F5)";
            Glyph.Label(ExportButton, Glyph.Export, L.T("Экспорт в CSV"));
            ExportButton.ToolTip = L.T("Экспорт в CSV") + "   (Ctrl+E)";
            Legend.Text = L.T("Подсвечен этот компьютер. Красным — повторы имени, IP или серийного номера, жёлтым — имя не по правилам, зелёным — собрано за 7 дней.");
            BuildHeader();
            Table.SizeChanged += delegate { FitColumns(); };
            ready = true;
        }

        // Column titles: a click sorts by the column (again — the other way); ESET and the comment are not sortable.
        void BuildHeader()
        {
            string[] titles = DeviceList.Columns;
            var widths = (ColumnWidths)Resources["Widths"];
            for (int i = 0; i < titles.Length; i++)
            {
                var column = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
                if (i < DeviceList.CommentColumn) column.SetBinding(ColumnDefinition.WidthProperty, new System.Windows.Data.Binding("W" + i) { Source = widths });
                Header.ColumnDefinitions.Add(column);
                // the title is cut with "…" when the column is narrower; the arrow of the sort order after it
                var title = new Grid();
                title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                title.Children.Add(new TextBlock { Text = titles[i], ToolTip = titles[i], TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                arrows[i] = Glyph.Block("", 9, null);
                arrows[i].Margin = new Thickness(5, 2, 0, 0);
                Grid.SetColumn(arrows[i], 1);
                title.Children.Add(arrows[i]);
                var b = new Button { Content = title, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(i == 0 ? 8 : -4, 0, i < 5 ? 8 : 6, 0), IsEnabled = DeviceList.Sortable(i) };
                b.SetResourceReference(StyleProperty, "HeaderButton");
                System.Windows.Automation.AutomationProperties.SetName(b, titles[i]);
                int index = i;
                b.Click += delegate
                {
                    if (sortColumn == index) descending = !descending; else { sortColumn = index; descending = false; }
                    Fill();
                };
                Grid.SetColumn(b, i);
                Header.Children.Add(b);
            }
        }

        public void Opened()
        {
            Reload();
            Search.Focus();
        }

        public bool Shortcut(KeyEventArgs e)
        {
            ModifierKeys mods = Keyboard.Modifiers;
            if (e.Key == Key.F5 && mods == ModifierKeys.None) { Reload(); return true; }
            if (e.Key == Key.F && mods == ModifierKeys.Control) { Search.Focus(); Search.SelectAll(); return true; }
            if (e.Key == Key.E && mods == ModifierKeys.Control) { Export(); return true; }
            // From the search box, arrows move through the table without leaving the keyboard.
            if (Search.IsKeyboardFocused && mods == ModifierKeys.None && (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Enter) && shown.Count > 0)
            {
                int i = Math.Max(0, Table.SelectedIndex);
                if (e.Key == Key.Enter) { OpenCard(i); return true; }
                if (Table.SelectedIndex >= 0) i = Math.Max(0, Math.Min(shown.Count - 1, i + (e.Key == Key.Down ? 1 : -1)));
                Select(i);
                return true;
            }
            return false;
        }

        public bool CanLeave() { return true; }
        public void DevicesChanged() { }

        void Reload()
        {
            if (me == null) me = CurrentComputer.Read();
            string selected = Table.SelectedIndex >= 0 && Table.SelectedIndex < shown.Count ? shown[Table.SelectedIndex].Path : null;
            list.Load(AppPaths.Root);
            if (list.Warnings.Count > 0)
                Notes.Show(Severity.Warning, Glyph.Warning, L.T("Пропущены файлы, которые не удалось прочитать (") + list.Warnings.Count + "): " + string.Join("; ", list.Warnings));
            else Notes.Clear();
            string[] repeats = list.RepeatFilters;
            int chosen = RepeatsFilter.SelectedIndex;
            ready = false;
            for (int i = 0; i < repeats.Length; i++) RepeatsFilter.Items[i] = repeats[i];
            RepeatsFilter.SelectedIndex = chosen;
            ready = true;
            Fill();
            if (selected != null) Select(shown.FindIndex(r => r.Path == selected));
        }

        void Search_TextChanged(object sender, TextChangedEventArgs e) { if (ready) Fill(); }
        void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (ready) Fill(); }
        void Reload_Click(object sender, RoutedEventArgs e) { Reload(); }
        void Export_Click(object sender, RoutedEventArgs e) { Export(); }

        void Fill()
        {
            words = LooseSearch.Words(Search.Text);
            shown = list.Shown(words, EsetFilter.SelectedIndex, InventoryFilter.SelectedIndex, RepeatsFilter.SelectedIndex, sortColumn, descending);
            bool filtered = words.Count > 0 || EsetFilter.SelectedIndex > 0 || InventoryFilter.SelectedIndex > 0 || RepeatsFilter.SelectedIndex > 0;
            var rows = new List<DeviceRow>();
            foreach (RecordFile r in shown) rows.Add(new DeviceRow(r, list, words, me != null && me.Matches(r.Data)));
            Table.ItemsSource = rows;
            FitColumns();
            for (int i = 0; i < arrows.Length; i++) arrows[i].Text = i != sortColumn ? "" : descending ? "" : "";
            CountText.Text = (filtered ? L.T("Найдено: ") + shown.Count + L.T(" из ") + list.All.Count : L.T("Записей: ") + list.All.Count) +
                L.T("   ·   синхронизация: ") + list.LastSync;
        }

        // Short values are shown in full and the comment takes the rest; in a narrow window the serial number, the user,
        // ESET, the cloud and the name give way in this order (as FitColumns of the WinForms page); the IP stays whole.
        void FitColumns()
        {
            double available = Table.ActualWidth - 12;
            if (available <= 0) return;
            var normal = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var semibold = new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var bold = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            var mono = new Typeface((FontFamily)FindResource("MonoFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            Func<string, Typeface, double, double> width = (text, face, size) => string.IsNullOrEmpty(text) ? 0 :
                new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Brushes.Black, dip).WidthIncludingTrailingWhitespace;
            string[] titles = DeviceList.Columns;
            var wanted = new double[8];
            // the values decide; the titles get what is left (they are cut with "…" otherwise)
            var header = new double[8];
            for (int i = 0; i < 8; i++) { header[i] = width(titles[i], semibold, FontSize) + (i == 0 ? 28 : 22) + 14; wanted[i] = 40; }
            foreach (object o in Table.Items)
            {
                var r = (DeviceRow)o;
                wanted[0] = Math.Max(wanted[0], width(r.Name.Text, semibold, FontSize) + 26);
                wanted[1] = Math.Max(wanted[1], width(r.Ip.Text, mono, 13) + 14);
                wanted[2] = Math.Max(wanted[2], width(r.Serial.Text, mono, 13) + 14);
                wanted[3] = Math.Max(wanted[3], width(r.User.Text, normal, FontSize) + 14);
                wanted[4] = Math.Max(wanted[4], width(r.Collected, r.Recent ? bold : normal, FontSize) + 14);
                wanted[5] = Math.Max(wanted[5], width(r.CloudText, normal, 12) + 28);
                wanted[6] = Math.Max(wanted[6], width(r.EsetText, normal, 12) + 28);
                wanted[7] = Math.Max(wanted[7], width(r.InventoryText, normal, 12) + 28);
            }
            double deficit = -available + 72;
            foreach (double w in wanted) deficit += w;
            foreach (double[] shrink in new[] { new[] { 2, 90.0 }, new[] { 3, 72.0 }, new[] { 6, 90.0 }, new[] { 5, 90.0 }, new[] { 0, 108.0 } })
            {
                if (deficit <= 0) break;
                int c = (int)shrink[0];
                double cut = Math.Min(deficit, Math.Max(0, wanted[c] - shrink[1]));
                wanted[c] -= cut;
                deficit -= cut;
            }
            // the room left goes to the titles cut with "…", from the left
            double spare = available - 72;
            foreach (double w in wanted) spare -= w;
            for (int i = 0; i < 8 && spare > 0; i++)
            {
                double grow = Math.Min(spare, Math.Max(0, header[i] - wanted[i]));
                wanted[i] += grow;
                spare -= grow;
            }
            ((ColumnWidths)Resources["Widths"]).Set(wanted);
        }

        void Select(int index)
        {
            if (index < 0 || index >= shown.Count) return;
            Table.SelectedIndex = index;
            Table.ScrollIntoView(Table.SelectedItem);
        }

        void Table_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(Table, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null) OpenCard(Table.ItemContainerGenerator.IndexFromContainer(item));
        }

        void Table_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Table.SelectedIndex >= 0) { e.Handled = true; OpenCard(Table.SelectedIndex); }
        }

        void OpenCard(int index)
        {
            if (index < 0 || index >= shown.Count) return;
            // The card works on its own copy: a reload replaces the list.
            var records = new List<RecordFile>(shown);
            var card = new RecordWindow(records, index, list.Cloud) { Owner = Window.GetWindow(this) };
            card.ShowDialog();
            string path = records[card.Index].Path;
            if (card.Changed) Reload();
            Select(shown.FindIndex(r => r.Path == path));
        }

        // Screenshots: the card of the first shown record (comp-info --screenshot database file.png → file-card.png).
        public void SaveCardImage(string file)
        {
            if (shown.Count == 0) return;
            var card = new RecordWindow(new List<RecordFile>(shown), 0, list.Cloud)
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            card.Show();
            card.UpdateLayout();
            StandaloneStart.Save(card, file);
            card.Close();
        }

        void Export()
        {
            string path = CsvExport.DefaultPath();
            Window owner = Window.GetWindow(this);
            try
            {
                int n = CsvExport.Write(AppPaths.Data, path, CloudState.ForDisplay(AppPaths.Root), ';');
                if (Dialog.Ask(owner, L.T("Экспортировано устройств: ") + n + "\n\n" + path, L.T("Открыть папку"), L.T("Закрыть"), Severity.Success, L.T("Экспорт в CSV")))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception e) { Dialog.Show(owner, L.T("Экспорт не выполнен: ") + e.Message); }
        }
    }

    // The widths of the table columns 0…7 (the comment takes the rest), shared by the header and every row.
    public class ColumnWidths : System.ComponentModel.INotifyPropertyChanged
    {
        readonly GridLength[] widths = new GridLength[8];

        public ColumnWidths() { for (int i = 0; i < widths.Length; i++) widths[i] = GridLength.Auto; }

        public GridLength W0 { get { return widths[0]; } }
        public GridLength W1 { get { return widths[1]; } }
        public GridLength W2 { get { return widths[2]; } }
        public GridLength W3 { get { return widths[3]; } }
        public GridLength W4 { get { return widths[4]; } }
        public GridLength W5 { get { return widths[5]; } }
        public GridLength W6 { get { return widths[6]; } }
        public GridLength W7 { get { return widths[7]; } }

        public void Set(double[] values)
        {
            for (int i = 0; i < widths.Length; i++) widths[i] = new GridLength(Math.Max(0, Math.Round(values[i])));
            if (PropertyChanged != null) PropertyChanged(this, new System.ComponentModel.PropertyChangedEventArgs(""));
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    // One record in the table, with the marks of the web table.
    public class DeviceRow
    {
        public readonly RecordFile Record;
        public MarkedText Name { get; private set; }
        public MarkedText Ip { get; private set; }
        public MarkedText Serial { get; private set; }
        public MarkedText User { get; private set; }
        public MarkedText Comment { get; private set; }
        public string CommentTip { get; private set; }
        public string NameState { get; private set; }
        public string NameTip { get; private set; }
        public bool IpRepeated { get; private set; }
        public string IpTip { get; private set; }
        public bool SerialRepeated { get; private set; }
        public string SerialTip { get; private set; }
        public string Collected { get; private set; }
        public string CollectedTip { get; private set; }
        public bool Recent { get; private set; }
        public string CloudText { get; private set; }
        public string CloudKind { get; private set; }
        public string EsetText { get; private set; }
        public string EsetKind { get; private set; }
        public string InventoryText { get; private set; }
        public string InventoryKind { get; private set; }
        public string InventoryTip { get { return L.T("Отметку ставят на сайте"); } }
        public bool Mine { get; private set; }

        public DeviceRow(RecordFile r, DeviceList list, List<string> words, bool mine)
        {
            Record = r;
            JsonObject d = r.Data;
            Name = new MarkedText(d.Text("hostname"), words);
            Ip = new MarkedText((d.Text("ip") ?? "").Trim(), words);
            Serial = new MarkedText(d.Text("serial"), words);
            User = new MarkedText(d.Text("username"), words);
            string comment = d.Text("comment") ?? "";
            Comment = new MarkedText(comment, words);
            CommentTip = comment.Length > 0 ? comment : null;
            string collected = Fmt.Date(d.Text("collectedAt"));
            Collected = collected.Length > 10 ? collected.Substring(0, 10) : collected;
            Recent = DeviceList.Recent(d.Text("collectedAt"));
            CollectedTip = Recent ? collected + " — " + L.T("собрано за последние 7 дней") : collected;
            CloudText = L.T(r.CloudStatus);
            CloudKind = r.CloudStatus == CloudState.Synced ? "Good" : r.CloudStatus == CloudState.Changed ? "Warn" : "Muted";
            object eset = d["esetInstalled"];
            EsetText = ComputerSnapshot.EsetText(eset);
            EsetKind = eset == null ? "Muted" : (eset is bool && (bool)eset) ? "Good" : "Warn";
            bool inventoried = DeviceList.Inventoried(d);
            InventoryText = inventoried ? L.T("да") : L.T("нет");
            InventoryKind = inventoried ? "Good" : "Muted";
            // Hostname: not by the rules — yellow, repeated — red (wins), as in the web table.
            var notes = new List<string>();
            string issue = DeviceList.HostIssue(d.Text("hostname"));
            if (issue != null) { NameState = "Warn"; notes.Add(issue); }
            if (list.RepeatedHost(d)) { NameState = "Bad"; notes.Insert(0, L.T("Это имя есть у нескольких устройств")); }
            Mine = mine;
            if (mine) notes.Insert(0, L.T("Этот компьютер"));
            NameTip = notes.Count > 0 ? string.Join("\n", notes) : null;
            IpRepeated = list.RepeatedIp(d);
            IpTip = IpRepeated ? L.T("Этот IP есть у нескольких устройств") : null;
            SerialRepeated = list.RepeatedSerial(d);
            SerialTip = SerialRepeated ? L.T("Этот серийный номер есть у нескольких устройств") : null;
        }
    }
}
