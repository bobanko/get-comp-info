using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // «Настройка сети» (LanPage of SystemPages.cs on WinForms): a static address by the office profiles or DHCP on a wired
    // adapter. The address is built from left to right: network . segment . building . computer. The form starts from what
    // the adapter has now; a table shows «now» against «after applying». The command itself asks for administrator rights
    // (LanSetup.Apply runs Windows PowerShell elevated), so the page needs no restart.
    public class LanView : ScrollViewer, ISection
    {
        readonly StatusLine profilesNote = new StatusLine { Margin = new Thickness(0, 0, 0, 10) };
        readonly ComboBox adapter = new ComboBox { Width = 560, MaxDropDownHeight = 320 };
        readonly TextBox first = Octet(), second = Octet(), third = Octet();
        readonly TextBox host = new TextBox { Width = 76, FontSize = 20, TextAlignment = TextAlignment.Center, MaxLength = 3 };
        readonly TextBlock range = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        readonly StackPanel segmentList = new StackPanel { Margin = new Thickness(0, 8, 16, 0) }, officeList = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        readonly List<RadioButton> segmentButtons = new List<RadioButton>(), officeButtons = new List<RadioButton>();
        readonly Grid compare = new Grid();
        readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        readonly Button apply = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        readonly StatusLine status = new StatusLine { Margin = new Thickness(0, 10, 0, 0) };
        string group = Guid.NewGuid().ToString("N");
        LanSetup.State currentState;   // of the selected adapter, null while it is read
        string currentNote = "";
        NetworkProfiles profiles = new NetworkProfiles();
        List<Adapter> adapters = new List<Adapter>();
        int officeNameWidth, currentRead;
        bool running, filling;

        public LanView()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            var page = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            Content = page;
            page.Children.Add(profilesNote);

            // Adapter first: everything below applies to it.
            var adapterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            var adapterLabel = new TextBlock { Text = L.T("Адаптер"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            adapterLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            adapterLabel.MouseLeftButtonDown += delegate { adapter.Focus(); };
            System.Windows.Automation.AutomationProperties.SetName(adapter, L.T("Адаптер"));
            adapter.SelectionChanged += delegate { if (!filling) { UpdateSummary(); ReadCurrent(); } };
            adapterRow.Children.Add(adapterLabel);
            adapterRow.Children.Add(adapter);
            page.Children.Add(adapterRow);

            // Octets in a row with dots between them, names above, choices below.
            var ip = new Grid();
            for (int c = 0; c < 8; c++) ip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = c < 4 ? "lan" + c : null });
            ip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] heads = { L.T("Сеть"), "", L.T("Сегмент"), "", L.T("Корпус"), "", L.T("Компьютер") };
            for (int c = 0; c < heads.Length; c++)
            {
                if (heads[c].Length == 0) continue;
                var h = new TextBlock { Text = heads[c], FontWeight = FontWeights.SemiBold, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
                h.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                Place(ip, h, 0, c);
            }
            host.SetResourceReference(FontFamilyProperty, "MonoFont");
            System.Windows.Automation.AutomationProperties.SetName(host, L.T("Компьютер"));
            host.TextChanged += delegate { if (!filling) UpdateSummary(); };
            host.PreviewKeyDown += (s, e) =>
            {
                // ↑ / ↓ change the number, as the number field of the WinForms page
                if (e.Key != Key.Up && e.Key != Key.Down) return;
                int n;
                if (!int.TryParse(host.Text, out n)) n = 0;
                n = Math.Max(0, Math.Min(255, n + (e.Key == Key.Up ? 1 : -1)));
                host.Text = n.ToString();
                host.CaretIndex = host.Text.Length;
                e.Handled = true;
            };
            Place(ip, first, 1, 0);
            Place(ip, Dot(), 1, 1);
            Place(ip, second, 1, 2);
            Place(ip, Dot(), 1, 3);
            Place(ip, third, 1, 4);
            Place(ip, Dot(), 1, 5);
            Place(ip, host, 1, 6);
            range.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            Place(ip, range, 1, 7);
            // The choices under the address: the segments under the segment, the buildings from the building on.
            // A grid of their own, sharing only the widths of the first columns: the long building names must not
            // widen the columns of the address.
            var choices = new Grid();
            for (int c = 0; c < 4; c++) choices.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "lan" + c });
            choices.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Place(choices, segmentList, 0, 2);
            Place(choices, officeList, 0, 4);
            var address = new StackPanel();
            Grid.SetIsSharedSizeScope(address, true);
            address.Children.Add(ip);
            address.Children.Add(choices);
            page.Children.Add(address);

            // What the adapter has now next to what it will have: differences are highlighted.
            compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20, GridUnitType.Star) });
            compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40, GridUnitType.Star) });
            compare.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40, GridUnitType.Star) });
            var frame = new Border { Child = compare, Margin = new Thickness(0, 16, 0, 0) };
            frame.SetResourceReference(StyleProperty, "TableFrame");
            page.Children.Add(frame);
            page.Children.Add(summary);
            AdminGlyph.Label(apply, L.T("Применить настройки"));
            apply.SetResourceReference(StyleProperty, "AccentButton");
            apply.ToolTip = L.T("Применить настройки") + "   (Ctrl+Enter)";
            apply.Click += delegate { Apply(); };
            page.Children.Add(apply);
            page.Children.Add(status);
        }

        static TextBox Octet()
        {
            // Filled in by the choices below; shown, not typed.
            var box = new TextBox { Width = 76, FontSize = 20, TextAlignment = TextAlignment.Center, IsReadOnly = true, IsTabStop = false, Cursor = Cursors.Arrow };
            box.SetResourceReference(FontFamilyProperty, "MonoFont");
            box.SetResourceReference(BackgroundProperty, "HeaderBack");
            return box;
        }

        static TextBlock Dot()
        {
            var dot = new TextBlock { Text = ".", FontSize = 22, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 2) };
            dot.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            return dot;
        }

        static void Place(Grid g, UIElement e, int row, int column)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, column);
            g.Children.Add(e);
        }

        RadioButton AddChoice(string text, object value, Panel list, List<RadioButton> buttons, bool mono)
        {
            var r = new RadioButton { Content = text, Tag = value, GroupName = group + (buttons == officeButtons ? "o" : "s"), Margin = new Thickness(0, 0, 0, 6) };
            if (mono) { r.SetResourceReference(FontFamilyProperty, "MonoFont"); r.FontSize = 14; }
            else r.FontSize = 14.5;
            r.Checked += delegate
            {
                if (filling) return;
                if (buttons == officeButtons) OfficeChanged(); else UpdateSummary();
            };
            list.Children.Add(r);
            buttons.Add(r);
            return r;
        }

        string OfficeColumns(string name, string octet, string hostRange)
        {
            return name.PadRight(officeNameWidth) + "   " + octet.PadLeft(8) + "   " + hostRange.PadLeft(10);
        }

        public bool CanLeave()
        {
            if (running) Dialog.Show(Window.GetWindow(this), L.T("Дождитесь окончания настройки сети."), Severity.Info, L.T("Настройка сети"));
            return !running;
        }

        public bool Shortcut(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { if (apply.IsEnabled) Apply(); return true; }
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None) { if (!running) Opened(); return true; }
            return false;
        }

        public void DevicesChanged() { }

        public void Opened()
        {
            if (running) return;
            profiles = NetworkProfiles.Read(AppPaths.File("network-profiles.json"));
            string selected = null;
            foreach (RadioButton r in officeButtons) if (r.IsChecked == true) selected = (string)r.Content;
            filling = true;
            officeList.Children.Clear();
            officeButtons.Clear();
            group = Guid.NewGuid().ToString("N");
            officeNameWidth = L.T("Название").Length;
            foreach (NetworkProfiles.Location l in profiles.Locations) officeNameWidth = Math.Max(officeNameWidth, l.Name.Length);
            var header = new TextBlock { Text = OfficeColumns(L.T("Название"), L.T("Октет"), L.T("Диапазон")), FontWeight = FontWeights.Bold, FontSize = 14,
                Margin = new Thickness(28, 0, 0, 6) };
            header.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            header.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            officeList.Children.Add(header);
            foreach (NetworkProfiles.Location l in profiles.Locations)
                AddChoice(OfficeColumns(l.Name, l.ThirdOctet.ToString(), l.FirstHostOctet + "–" + l.LastHostOctet), l, officeList, officeButtons, true);
            AddChoice(L.T("Интернет — автоматически (DHCP)"), null, officeList, officeButtons, false);
            RadioButton keep = officeButtons.Find(r => (string)r.Content == selected);
            (keep ?? officeButtons[0]).IsChecked = true;
            filling = false;
            first.Text = profiles.FirstOctet.ToString();
            if (profiles.Missing)
                profilesNote.Show(Severity.Warning, Glyph.Warning, L.T("Профили корпусов не загружены: синхронизируйтесь с облаком, когда будет интернет. Сейчас доступна только автоматическая настройка."));
            else profilesNote.Clear();
            OfficeChanged();
            LoadAdapters();
            if (Office != null) host.Focus(); else adapter.Focus();
        }

        Adapter SelectedAdapter { get { var c = adapter.SelectedItem as Choice; return c == null ? null : (Adapter)c.Value; } }

        async void LoadAdapters()
        {
            // After «Применить» the list is reloaded: stay on the adapter that was just configured.
            Adapter previous = SelectedAdapter;
            filling = true;
            adapter.Items.Clear();
            filling = false;
            adapter.IsEnabled = false;
            try
            {
                adapters = await Task.Run(() => LanSetup.WiredAdapters());
                int select = 0;
                filling = true;
                foreach (Adapter a in adapters)
                {
                    bool good, bad;
                    if (previous != null && a.Index == previous.Index) select = adapter.Items.Count;
                    adapter.Items.Add(new Choice { Text = a.Name + " — " + LanSetup.StatusText(a, out good, out bad) + "  (" + a.Description + ")", Value = a });
                }
                filling = false;
                if (adapter.Items.Count > 0) adapter.SelectedIndex = select;
                adapter.IsEnabled = adapters.Count > 0;
                if (adapters.Count == 0) status.Show(Severity.Error, Glyph.Warning, L.T("Проводные сетевые адаптеры не найдены."));
            }
            catch (Exception e) { filling = false; status.Show(Severity.Error, Glyph.Warning, L.T("Не удалось получить список адаптеров: ") + e.Message); }
            UpdateSummary();
        }

        // Shows what the selected adapter is set to now and starts the form from it: the office, segment and
        // computer number of its address, or DHCP. An address outside the office profiles leaves the choices as they are.
        async void ReadCurrent()
        {
            int request = ++currentRead;
            Adapter a = SelectedAdapter;
            currentState = null;
            currentNote = "";
            UpdateSummary();
            if (a == null) return;
            LanSetup.State state;
            try { state = await Task.Run(() => LanSetup.Read(a.Guid)); }
            catch (Exception) { state = null; }
            if (request != currentRead || running || state == null) return;
            currentState = state;
            NetworkProfiles.Location location;
            int segment, last;
            if (state.Dhcp) Select(officeButtons, null);
            else if (profiles.Match(state.Ip, out location, out segment, out last))
            {
                Select(officeButtons, location);
                Select(segmentButtons, segment);
                host.Text = last.ToString();
            }
            else if (state.Ip.Length > 0 && !profiles.Missing)
                currentNote = L.T("Адрес не относится к профилям корпусов: выберите корпус и номер компьютера.");
            UpdateSummary();
        }

        static void Select(List<RadioButton> buttons, object value)
        {
            foreach (RadioButton r in buttons)
                if (Equals(r.Tag, value)) { r.IsChecked = true; return; }
        }

        NetworkProfiles.Location Office
        {
            get
            {
                foreach (RadioButton r in officeButtons) if (r.IsChecked == true) return r.Tag as NetworkProfiles.Location;
                return null;
            }
        }

        int? Segment
        {
            get
            {
                foreach (RadioButton r in segmentButtons) if (r.IsChecked == true) return (int)r.Tag;
                return null;
            }
        }

        void OfficeChanged()
        {
            NetworkProfiles.Location l = Office;
            int? previous = Segment;
            filling = true;
            segmentList.Children.Clear();
            segmentButtons.Clear();
            if (l != null)
            {
                foreach (int s in l.Segments ?? profiles.Segments) AddChoice(s.ToString(), s, segmentList, segmentButtons, false);
                RadioButton keep = segmentButtons.Find(r => previous.HasValue && (int)r.Tag == previous.Value);
                if (keep == null && segmentButtons.Count > 0) keep = segmentButtons[0];
                if (keep != null) keep.IsChecked = true;
                range.Text = l.FirstHostOctet + "–" + l.LastHostOctet;
            }
            else range.Text = "";
            filling = false;
            // DHCP: the address comes from the router, the octets are not used.
            host.IsEnabled = l != null;
            UpdateSummary();
        }

        // The settings to apply, or false with the reason in error.
        bool Plan(out string ip, out string gateway, out string error)
        {
            ip = gateway = error = null;
            NetworkProfiles.Location l = Office;
            if (adapter.SelectedItem == null) { error = L.T("Выберите адаптер."); return false; }
            if (l == null) return true;
            if (!Segment.HasValue) { error = L.T("Выберите сегмент."); return false; }
            int last;
            if (!int.TryParse(host.Text, out last) || last < l.FirstHostOctet || last > l.LastHostOctet)
            {
                error = L.T("Введите последний октет от ") + l.FirstHostOctet + L.T(" до ") + l.LastHostOctet + ".";
                return false;
            }
            string prefix = profiles.FirstOctet + "." + Segment.Value + "." + l.ThirdOctet;
            ip = prefix + "." + last;
            gateway = prefix + "." + l.GatewayOctet;
            return true;
        }

        void UpdateSummary()
        {
            NetworkProfiles.Location l = Office;
            second.Text = l != null && Segment.HasValue ? Segment.Value.ToString() : "—";
            third.Text = l != null ? l.ThirdOctet.ToString() : "—";
            int last;
            bool hostValid = l != null && int.TryParse(host.Text, out last) && last >= l.FirstHostOctet && last <= l.LastHostOctet;
            host.SetResourceReference(BackgroundProperty, l == null ? "HeaderBack" : hostValid ? "GoodBack" : "BadBack");
            host.SetResourceReference(ForegroundProperty, l == null ? "Muted" : hostValid ? "Good" : "Bad");
            string ip, gateway, error;
            bool ok = Plan(out ip, out gateway, out error);
            apply.IsEnabled = ok && !running;
            bool changes = FillCompare(l, ok ? ip : null);
            var notes = new List<string>();
            if (!ok) notes.Add(error);
            else if (l == null) notes.Add(L.T("Статические IPv4-адреса, шлюз и вручную заданные DNS будут сброшены."));
            if (ok && currentState != null && !changes) notes.Add(L.T("Адаптер уже настроен так: применять нечего."));
            if (currentNote.Length > 0) notes.Add(currentNote);
            summary.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Text" : "Muted");
            summary.Text = string.Join("\r\n", notes);
        }

        // Rows «now» and «after applying»; returns whether anything changes.
        bool FillCompare(NetworkProfiles.Location l, string plannedIp)
        {
            LanSetup.State s = currentState;
            bool dhcp = l == null;
            string auto = L.T("от роутера"), none = "—";
            string plannedGateway = l != null && Segment.HasValue ? profiles.Gateway(l, Segment.Value) : none;
            var rows = new List<string[]>();
            var same = new List<bool>();
            Action<string, string, string, bool> add = (name, now, planned, equal) => { rows.Add(new[] { name, now, planned }); same.Add(equal); };
            add(L.T("Режим"), s == null ? none : s.Dhcp ? L.T("автоматически (DHCP)") : L.T("вручную"),
                dhcp ? L.T("автоматически (DHCP)") : L.T("вручную"), s != null && s.Dhcp == dhcp);
            add(L.T("IP-адрес"), s == null ? none : s.Ip.Length > 0 ? s.Ip : s.Dhcp ? L.T("ещё не получен от роутера") : none,
                dhcp ? auto : plannedIp ?? none, s != null && (dhcp ? s.Dhcp : !s.Dhcp && s.Ip == plannedIp));
            add(L.T("Маска"), s == null || s.Mask.Length == 0 ? none : s.Mask,
                dhcp ? auto : l.SubnetMask, s != null && (dhcp ? s.Dhcp : !s.Dhcp && s.Mask == l.SubnetMask));
            add(L.T("Шлюз"), s == null || s.Gateway.Length == 0 ? none : s.Gateway,
                dhcp ? auto : plannedGateway, s != null && (dhcp ? s.Dhcp : !s.Dhcp && s.Gateway == plannedGateway));
            int dnsRows = Math.Max(1, Math.Max(s == null ? 0 : s.Dns.Count, dhcp ? 1 : l.DnsServers.Count));
            for (int i = 0; i < dnsRows; i++)
            {
                string now = s != null && i < s.Dns.Count ? s.Dns[i] + (s.DnsManual ? "" : L.T(" (от роутера)")) : none;
                string planned = dhcp ? (i == 0 ? auto : none) : i < l.DnsServers.Count ? l.DnsServers[i] : none;
                bool equal = s != null && (dhcp ? !s.DnsManual : s.DnsManual && i < s.Dns.Count && i < l.DnsServers.Count && s.Dns[i] == l.DnsServers[i] ||
                    s.DnsManual && i >= s.Dns.Count && i >= l.DnsServers.Count);
                add("DNS " + (i + 1), now, planned, equal);
            }

            compare.Children.Clear();
            compare.RowDefinitions.Clear();
            AddCompareRow(new[] { "", L.T("Сейчас"), L.T("После применения") }, true, true, none);
            bool changes = false;
            for (int i = 0; i < rows.Count; i++)
            {
                AddCompareRow(rows[i], false, same[i], none);
                if (!same[i]) changes = true;
            }
            return changes;
        }

        void AddCompareRow(string[] cells, bool header, bool same, string none)
        {
            int row = compare.RowDefinitions.Count;
            compare.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1) };
            line.SetResourceReference(Border.BorderBrushProperty, header ? "Border" : "Divider");
            if (header) line.SetResourceReference(Border.BackgroundProperty, "HeaderBack");
            else if (!same) line.SetResourceReference(Border.BackgroundProperty, "WarnBack");
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, 3);
            compare.Children.Add(line);
            for (int c = 0; c < 3; c++)
            {
                var t = new TextBlock { Text = cells[c], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(c == 0 ? 14 : 0, 7, 10, 7) };
                if (header) { t.FontWeight = FontWeights.SemiBold; t.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); }
                else if (c == 0) t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                else
                {
                    t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                    t.SetResourceReference(TextBlock.ForegroundProperty, cells[c] == none ? "Muted" : c == 2 && !same ? "Accent" : "Text");
                }
                Grid.SetRow(t, row);
                Grid.SetColumn(t, c);
                compare.Children.Add(t);
            }
        }

        async void Apply()
        {
            string ip, gateway, error;
            if (!Plan(out ip, out gateway, out error)) return;
            Adapter a = SelectedAdapter;
            NetworkProfiles.Location l = Office;
            string question = (l == null ? L.T("Включить автоматическую настройку (DHCP)") : L.T("Назначить адрес ") + ip) + L.T(" на адаптере «") + a.Name + "»?\n\n" +
                L.T("Текущее сетевое подключение может временно прерваться.") + (PowerShell.IsAdministrator() ? "" : L.T("\nWindows запросит права администратора."));
            if (!Dialog.Ask(Window.GetWindow(this), question, L.T("Применить"), L.T("Отмена"), Severity.Info, L.T("Настройка сети"))) return;
            running = true;
            apply.IsEnabled = false;
            status.Note(Glyph.More, L.T("Применение настроек…"));
            try
            {
                int prefixLength = l == null ? 0 : l.PrefixLength;
                List<string> dns = l == null ? new List<string>() : l.DnsServers;
                LanSetup.State before = null;
                await Task.Run(() =>
                {
                    try { before = LanSetup.Read(a.Guid); } catch (Exception) { }
                    LanSetup.Apply(a.Index, l == null, ip, prefixLength, gateway, dns);
                });
                string done = L.T("Настройки сети применены.") + (l == null ? L.T(" Получение настроек от роутера может занять некоторое время.") : "");
                status.Show(Severity.Success, Glyph.Check, done);
                // The new address goes to the record of this computer at once; with DHCP the old static one is dropped.
                var expected = new Collection.Expected { SetIp = l != null, Ip = ip };
                if (l == null && before != null && !before.Dhcp && before.Ip.Length > 0) expected.OldIps = new List<string> { before.Ip };
                bool saved = false;
                string note = await Task.Run(() => RecordUpdate.Save(expected, out saved));
                status.Show(saved ? Severity.Success : Severity.Warning, Glyph.Check, done + "\r\n" + note);
            }
            catch (OperationCanceledException e) { status.Show(Severity.Warning, Glyph.Info, e.Message); }
            catch (Exception e) { status.Show(Severity.Error, Glyph.Warning, L.T("Не удалось завершить настройку сети: ") + e.Message); }
            finally
            {
                running = false;
                UpdateSummary();
                LoadAdapters();
            }
        }
    }
}
