using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // Two-column form: grey labels on the left, fields on the right.
    public class FieldTable : TableLayoutPanel
    {
        public FieldTable()
        {
            ColumnCount = 2;
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
            Margin = new Padding(0);
        }

        // Above zero: every row is this tall and its label and field are centred in it, so the form lines up on a grid.
        public int RowHeight;

        public Label Add(string label, Control field)
        {
            int em = Font.Height;
            Label l = Ui.Text(label, Font, Theme.Muted);
            l.Anchor = AnchorStyles.Left;
            l.Margin = RowHeight > 0 ? new Padding(0, 0, em, 0) : new Padding(0, em / 4, em, em / 4);
            field.Margin = RowHeight > 0 ? new Padding(0) : new Padding(0, em / 6, 0, em / 6);
            if (field is TextBox && ((TextBox)field).Multiline) field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            else field.Anchor = AnchorStyles.Left;
            RowStyles.Add(RowHeight > 0 ? new RowStyle(SizeType.Absolute, RowHeight) : new RowStyle(SizeType.AutoSize));
            Controls.Add(l, 0, RowCount);
            Controls.Add(field, 1, RowCount);
            RowCount++;
            return l;
        }

        public void Show(Control field, bool visible)
        {
            field.Visible = visible;
            int row = GetRow(field);
            Control label = GetControlFromPosition(0, row);
            if (label != null) label.Visible = visible;
            // A fixed-height row keeps its height when hidden: fold it.
            if (row >= 0 && row < RowStyles.Count && RowStyles[row].SizeType == SizeType.Absolute) RowStyles[row].Height = visible ? RowHeight : 0;
        }
    }

    // Domain account remembered in comp-info.ini when the user asks for it. The password is only masked (base64):
    // a flash drive goes from computer to computer, so Windows encryption tied to one user or PC cannot be used.
    public static class DomainCredentials
    {
        public static void Load(TextBox user, TextBox password, CheckBox remember)
        {
            string savedUser = Settings.Get("domainUser", "");
            string saved = Settings.Get("domainPassword", "");
            remember.Checked = savedUser.Length > 0;
            if (user.Text.Length == 0) user.Text = savedUser;
            password.Text = "";
            if (saved.StartsWith("b64:"))
            {
                try { password.Text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(saved.Substring(4))); } catch (Exception) { }
            }
        }

        public static void Save(string user, string password, bool remember)
        {
            Settings.Set("domainUser", remember ? user : "");
            Settings.Set("domainPassword", remember && !string.IsNullOrEmpty(password) ? "b64:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password)) : "");
        }
    }

    // The record of this computer in the local database, saved right after a change of name, domain or address.
    public static class RecordUpdate
    {
        // A line for the status of the page: saved, or why it was not. Runs on a worker thread.
        public static string Save(Collection.Expected expected, out bool saved)
        {
            saved = false;
            try
            {
                string skipped;
                JsonObject record = Collection.SaveAfterChange(AppPaths.Data, expected, out skipped);
                if (record == null) return L.T("Запись в локальной базе не обновлена: ") + skipped + ".";
                saved = true;
                string ip = record.Text("ip");
                return L.T("Запись в локальной базе обновлена: ") + record.Text("hostname") + (string.IsNullOrEmpty(ip) ? "" : ", " + ip) +
                    L.T(". Синхронизируйте базу, когда будет интернет.");
            }
            catch (Exception e) { return L.T("Запись в локальной базе не обновлена: ") + e.Message; }
        }
    }

    // «Настройка сети»: static address by the office profiles or DHCP (setup-lan-adapter.ps1).
    public class LanPage : Page
    {
        readonly ComboBox adapter = Ui.Combo();
        // The address is built from left to right: network . segment . building . computer.
        readonly TextBox first, second, third;
        readonly NumericUpDown host = new NumericUpDown();
        readonly Label range;
        readonly FlowLayoutPanel segmentList = new FlowLayoutPanel(), officeList = new FlowLayoutPanel();
        readonly List<RadioButton> segmentButtons = new List<RadioButton>(), officeButtons = new List<RadioButton>();
        int officeNameWidth;
        readonly StatusLine profilesNote = new StatusLine();
        readonly Label summary;
        readonly DataGridView compare;
        readonly Button apply;
        readonly StatusLine status = new StatusLine();
        LanSetup.State currentState;   // of the selected adapter, null while it is read
        string currentNote = "";
        NetworkProfiles profiles = new NetworkProfiles();
        List<Adapter> adapters = new List<Adapter>();
        bool running, filling;

        class Item
        {
            public string Text;
            public object Value;
            public override string ToString() { return Text; }
        }

        public LanPage()
        {
            Key = "lan";
            Title = L.T("Настройка сети");
            Description = L.T("IP-адрес, маска, шлюз и DNS проводного адаптера по профилям корпусов из облака, или автоматическая настройка (DHCP).");
            Icon = Glyph.Network;
            NeedsAdmin = true;
            int em = Font.Height;
            AutoScroll = true;

            profilesNote.Dock = DockStyle.Top;
            profilesNote.Padding = new Padding(0, 0, 0, em / 2);

            // Adapter first: everything below applies to it.
            FlowLayoutPanel adapterRow = Ui.Row();
            Label adapterLabel = Ui.Text(L.T("Адаптер"), Font, Theme.Muted);
            adapterLabel.Margin = new Padding(0, em / 4, em / 2, 0);
            adapter.Width = em * 28;
            adapter.DropDownWidth = em * 36;
            adapter.SelectedIndexChanged += delegate { UpdateSummary(); ReadCurrent(); };
            adapterRow.Controls.Add(adapterLabel);
            adapterRow.Controls.Add(adapter);
            adapterRow.Dock = DockStyle.Top;
            adapterRow.Padding = new Padding(0, 0, 0, em);

            // Octets in a row with dots between them, names above, choices below.
            Font big = new Font(Ui.MonoFont.FontFamily, 15f);
            first = Octet(big, true);
            second = Octet(big, true);
            third = Octet(big, true);
            host.Font = big;
            host.TextAlign = HorizontalAlignment.Center;
            host.Minimum = 0;
            host.Maximum = 255;
            host.Width = em * 5;
            host.Margin = new Padding(0);
            host.BorderStyle = BorderStyle.FixedSingle;
            host.BackColor = Theme.BadLight;
            host.ForeColor = Theme.Bad;
            Theme.Native(host);
            host.ValueChanged += delegate { UpdateSummary(); };
            host.TextChanged += delegate { UpdateSummary(); };
            range = Ui.Text("", Font, Theme.Muted);
            range.Anchor = AnchorStyles.Left;
            range.Margin = new Padding(em / 2, 0, 0, 0);

            TableLayoutPanel ip = new TableLayoutPanel();
            ip.AutoSize = true;
            ip.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ip.Dock = DockStyle.Top;
            ip.ColumnCount = 8;
            for (int c = 0; c < 8; c++) ip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            string[] heads = { L.T("Сеть"), "", L.T("Сегмент"), "", L.T("Корпус"), "", L.T("Компьютер") };
            for (int c = 0; c < heads.Length; c++)
            {
                if (heads[c].Length == 0) continue;
                Label h = Ui.Text(heads[c], new Font("Segoe UI Semibold", 9f), Theme.Muted);
                h.Margin = new Padding(0, 0, 0, em / 4);
                ip.Controls.Add(h, c, 0);
            }
            ip.Controls.Add(first, 0, 1);
            ip.Controls.Add(Dot(big), 1, 1);
            ip.Controls.Add(second, 2, 1);
            ip.Controls.Add(Dot(big), 3, 1);
            ip.Controls.Add(third, 4, 1);
            ip.Controls.Add(Dot(big), 5, 1);
            ip.Controls.Add(host, 6, 1);
            ip.Controls.Add(range, 7, 1);
            foreach (FlowLayoutPanel list in new[] { segmentList, officeList })
            {
                list.FlowDirection = FlowDirection.TopDown;
                list.WrapContents = false;
                list.AutoSize = true;
                list.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                list.Margin = new Padding(0, em / 2, em, 0);
            }
            ip.Controls.Add(segmentList, 2, 2);
            ip.Controls.Add(officeList, 4, 2);
            // Building names are long: their list may run under the computer column.
            ip.SetColumnSpan(officeList, 4);

            // What the adapter has now next to what it will have: differences are highlighted.
            compare = Ui.Grid(Font);
            compare.Columns.Add("field", "");
            compare.Columns.Add("now", L.T("Сейчас"));
            compare.Columns.Add("planned", L.T("После применения"));
            compare.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            compare.Columns[0].FillWeight = 20;
            compare.Columns[1].FillWeight = 40;
            compare.Columns[2].FillWeight = 40;
            compare.Columns[0].DefaultCellStyle.ForeColor = Theme.Muted;
            foreach (DataGridViewColumn c in compare.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            compare.ScrollBars = ScrollBars.None;
            compare.TabStop = false;
            compare.Dock = DockStyle.Top;
            compare.SelectionChanged += delegate { compare.ClearSelection(); };
            Panel compareBox = new Panel();
            compareBox.Dock = DockStyle.Top;
            compareBox.AutoSize = true;
            compareBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            compareBox.Padding = new Padding(0, em, 0, 0);
            compareBox.Controls.Add(compare);
            summary = Ui.Paragraph("", Font, Theme.Text);
            summary.Padding = new Padding(0, em / 2, 0, em / 2);
            apply = Ui.AdminPrimary(L.T("Применить настройки"), Font);
            apply.Click += delegate { Apply(); };
            Ui.Keys(apply, "Ctrl+Enter");
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(apply);
            actions.Dock = DockStyle.Top;
            status.Dock = DockStyle.Top;
            status.Padding = new Padding(0, em / 2, 0, 0);

            Controls.Add(status);
            Controls.Add(actions);
            Controls.Add(summary);
            Controls.Add(compareBox);
            Controls.Add(ip);
            Controls.Add(adapterRow);
            Controls.Add(profilesNote);
        }

        TextBox Octet(Font font, bool fixedValue)
        {
            int em = Font.Height;
            TextBox box = Ui.Input(new TextBox());
            box.Font = font;
            box.TextAlign = HorizontalAlignment.Center;
            box.Width = em * 4;
            box.Margin = new Padding(0);
            if (fixedValue)
            {
                // Filled in by the choices below; shown, not typed.
                box.ReadOnly = true;
                box.TabStop = false;
                box.BackColor = Theme.HeaderBack;
                box.Cursor = Cursors.Default;
            }
            return box;
        }

        Label Dot(Font font)
        {
            Label dot = Ui.Text(".", font, Theme.Muted);
            dot.Anchor = AnchorStyles.Bottom;
            dot.Margin = new Padding(Font.Height / 6, 0, Font.Height / 6, 0);
            return dot;
        }

        RadioButton Choice(string text, object value, FlowLayoutPanel list, List<RadioButton> group)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.Tag = value;
            r.Font = group == officeButtons
                ? new Font(Ui.MonoFont.FontFamily, 11f)
                : new Font(Font.FontFamily, Font.SizeInPoints + 1f);
            r.AutoSize = true;
            r.ForeColor = Theme.Text;
            r.Margin = new Padding(0, 0, 0, Font.Height / 6);
            r.CheckedChanged += delegate
            {
                if (!r.Checked || filling) return;
                if (group == officeButtons) OfficeChanged(); else UpdateSummary();
            };
            list.Controls.Add(r);
            group.Add(r);
            return r;
        }

        string OfficeColumns(string name, string octet, string hostRange)
        {
            return name.PadRight(officeNameWidth) + "   " + octet.PadLeft(8) + "   " + hostRange.PadLeft(10);
        }

        public override bool CanLeave()
        {
            if (running) Ui.Info(FindForm(), L.T("Настройка сети"), L.T("Дождитесь окончания настройки сети."));
            return !running;
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == (Keys.Control | Keys.Enter)) { if (apply.Enabled) Apply(); return true; }
            if (keys == Keys.F5) { if (!running) Opened(); return true; }
            return false;
        }

        public override void Opened()
        {
            if (running) return;
            profiles = NetworkProfiles.Read(AppPaths.File("network-profiles.json"));
            string selected = null;
            foreach (RadioButton r in officeButtons) if (r.Checked) selected = r.Text;
            filling = true;
            officeList.Controls.Clear();
            officeButtons.Clear();
            officeNameWidth = L.T("Название").Length;
            foreach (NetworkProfiles.Location l in profiles.Locations) officeNameWidth = Math.Max(officeNameWidth, l.Name.Length);
            Label officeHeader = Ui.Text(OfficeColumns(L.T("Название"), L.T("Октет"), L.T("Диапазон")),
                new Font(Ui.MonoFont.FontFamily, 11f, FontStyle.Bold), Theme.Muted);
            // Data rows start after the native radio glyph; align the header with their text.
            officeHeader.Margin = new Padding(SystemInformation.MenuCheckSize.Width + Font.Height / 4, 0, 0, Font.Height / 4);
            officeList.Controls.Add(officeHeader);
            foreach (NetworkProfiles.Location l in profiles.Locations)
                Choice(OfficeColumns(l.Name, l.ThirdOctet.ToString(), l.FirstHostOctet + "–" + l.LastHostOctet), l, officeList, officeButtons);
            Choice(L.T("Интернет — автоматически (DHCP)"), null, officeList, officeButtons);
            RadioButton keep = officeButtons.Find(delegate (RadioButton r) { return r.Text == selected; });
            (keep ?? officeButtons[0]).Checked = true;
            filling = false;
            first.Text = profiles.FirstOctet.ToString();
            if (profiles.Missing)
                profilesNote.Set(Glyph.Warning, L.T("Профили корпусов не загружены: синхронизируйтесь с облаком, когда будет интернет. Сейчас доступна только автоматическая настройка."), Theme.Warn);
            else profilesNote.Clear();
            OfficeChanged();
            LoadAdapters();
            if (Office != null) host.Focus(); else adapter.Focus();
        }

        Adapter SelectedAdapter
        {
            get { return adapter.SelectedItem == null ? null : (Adapter)((Item)adapter.SelectedItem).Value; }
        }

        async void LoadAdapters()
        {
            // After «Применить» the list is reloaded: stay on the adapter that was just configured.
            Adapter previous = SelectedAdapter;
            adapter.Items.Clear();
            adapter.Enabled = false;
            try
            {
                adapters = await Task.Run(delegate { return LanSetup.WiredAdapters(); });
                int select = 0;
                foreach (Adapter a in adapters)
                {
                    bool good, bad;
                    Item item = new Item();
                    item.Text = a.Name + " — " + LanSetup.StatusText(a, out good, out bad) + "  (" + a.Description + ")";
                    item.Value = a;
                    if (previous != null && a.Index == previous.Index) select = adapter.Items.Count;
                    adapter.Items.Add(item);
                }
                if (adapter.Items.Count > 0) adapter.SelectedIndex = select;
                adapter.Enabled = adapters.Count > 0;
                if (adapters.Count == 0) status.Set(Glyph.Warning, L.T("Проводные сетевые адаптеры не найдены."), Theme.Bad);
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось получить список адаптеров: ") + e.Message, Theme.Bad); }
            UpdateSummary();
        }

        int currentRead;

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
            try { state = await Task.Run(delegate { return LanSetup.Read(a.Guid); }); }
            catch (Exception) { state = null; }
            if (request != currentRead || IsDisposed || running) return;
            if (state == null) return;
            currentState = state;

            NetworkProfiles.Location location;
            int segment, last;
            if (state.Dhcp) Select(officeButtons, null);
            else if (profiles.Match(state.Ip, out location, out segment, out last))
            {
                Select(officeButtons, location);
                Select(segmentButtons, segment);
                host.Value = last;
            }
            else if (state.Ip.Length > 0 && !profiles.Missing)
                currentNote = L.T("Адрес не относится к профилям корпусов: выберите корпус и номер компьютера.");
            UpdateSummary();
        }

        static void Select(List<RadioButton> group, object value)
        {
            foreach (RadioButton r in group)
                if (Equals(r.Tag, value)) { r.Checked = true; return; }
        }

        NetworkProfiles.Location Office
        {
            get
            {
                foreach (RadioButton r in officeButtons) if (r.Checked) return r.Tag as NetworkProfiles.Location;
                return null;
            }
        }

        int? Segment
        {
            get
            {
                foreach (RadioButton r in segmentButtons) if (r.Checked) return (int)r.Tag;
                return null;
            }
        }

        void OfficeChanged()
        {
            NetworkProfiles.Location l = Office;
            int? previous = Segment;
            filling = true;
            segmentList.Controls.Clear();
            segmentButtons.Clear();
            if (l != null)
            {
                foreach (int s in l.Segments ?? profiles.Segments) Choice(s.ToString(), s, segmentList, segmentButtons);
                RadioButton keep = segmentButtons.Find(delegate (RadioButton r) { return previous.HasValue && (int)r.Tag == previous.Value; });
                if (keep == null && segmentButtons.Count > 0) keep = segmentButtons[0];
                if (keep != null) keep.Checked = true;
                range.Text = l.FirstHostOctet + "–" + l.LastHostOctet;
            }
            else range.Text = "";
            filling = false;
            // DHCP: the address comes from the router, the octets are not used.
            host.Enabled = l != null;
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
            host.BackColor = l == null ? Theme.HeaderBack : hostValid ? Theme.GoodLight : Theme.BadLight;
            host.ForeColor = l == null ? Theme.Muted : hostValid ? Theme.Good : Theme.Bad;
            string ip, gateway, error;
            bool ok = Plan(out ip, out gateway, out error);
            apply.Enabled = ok && !running;
            bool changes = FillCompare(l, ok ? ip : null);
            List<string> notes = new List<string>();
            if (!ok) notes.Add(error);
            else if (l == null) notes.Add(L.T("Статические IPv4-адреса, шлюз и вручную заданные DNS будут сброшены."));
            if (ok && currentState != null && !changes) notes.Add(L.T("Адаптер уже настроен так: применять нечего."));
            if (currentNote.Length > 0) notes.Add(currentNote);
            summary.ForeColor = ok ? Theme.Text : Theme.Muted;
            summary.Text = string.Join("\r\n", notes);
        }

        // Rows «now» and «after applying»; returns whether anything changes.
        bool FillCompare(NetworkProfiles.Location l, string plannedIp)
        {
            LanSetup.State s = currentState;
            bool dhcp = l == null;
            string auto = L.T("от роутера"), none = "—";
            string plannedGateway = l != null && Segment.HasValue ? profiles.Gateway(l, Segment.Value) : none;
            List<string[]> rows = new List<string[]>();
            List<bool> same = new List<bool>();
            Action<string, string, string, bool> add = delegate (string name, string now, string planned, bool equal)
            {
                rows.Add(new[] { name, now, planned });
                same.Add(equal);
            };
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

            compare.Rows.Clear();
            bool changes = false;
            for (int i = 0; i < rows.Count; i++)
            {
                int n = compare.Rows.Add(rows[i][0], rows[i][1], rows[i][2]);
                DataGridViewRow row = compare.Rows[n];
                Color back = same[i] ? Theme.Surface : Theme.WarnLight;
                row.DefaultCellStyle.BackColor = row.DefaultCellStyle.SelectionBackColor = back;
                row.Cells[1].Style.Font = row.Cells[2].Style.Font = Ui.MonoFont;
                row.Cells[1].Style.ForeColor = rows[i][1] == none ? Theme.Muted : Theme.Text;
                row.Cells[2].Style.ForeColor = rows[i][2] == none ? Theme.Muted : same[i] ? Theme.Text : Theme.Accent;
                if (!same[i]) changes = true;
            }
            compare.Height = compare.ColumnHeadersHeight + compare.RowTemplate.Height * rows.Count + 2;
            compare.ClearSelection();
            return changes;
        }

        async void Apply()
        {
            string ip, gateway, error;
            if (!Plan(out ip, out gateway, out error)) return;
            Adapter a = (Adapter)((Item)adapter.SelectedItem).Value;
            NetworkProfiles.Location l = Office;
            string question = (l == null ? L.T("Включить автоматическую настройку (DHCP)") : L.T("Назначить адрес ") + ip) + L.T(" на адаптере «") + a.Name + "»?\n\n" +
                L.T("Текущее сетевое подключение может временно прерваться.") + (PowerShell.IsAdministrator() ? "" : L.T("\nWindows запросит права администратора."));
            if (Ui.Ask(FindForm(), L.T("Настройка сети"), question, Glyph.Network, Theme.Info, L.T("Применить"), L.T("Отмена")) != 0) return;
            running = true;
            apply.Enabled = false;
            status.Set(Glyph.More, L.T("Применение настроек…"), Theme.Muted);
            try
            {
                int prefixLength = l == null ? 0 : l.PrefixLength;
                List<string> dns = l == null ? new List<string>() : l.DnsServers;
                LanSetup.State before = null;
                await Task.Run(delegate
                {
                    try { before = LanSetup.Read(a.Guid); } catch (Exception) { }
                    LanSetup.Apply(a.Index, l == null, ip, prefixLength, gateway, dns);
                });
                status.Set(Glyph.Check, L.T("Настройки сети применены.") + (l == null ? L.T(" Получение настроек от роутера может занять некоторое время.") : ""), Theme.Good);
                // The new address goes to the record of this computer at once; with DHCP the old static one is dropped.
                Collection.Expected expected = new Collection.Expected();
                expected.SetIp = l != null;
                expected.Ip = ip;
                if (l == null && before != null && !before.Dhcp && before.Ip.Length > 0) expected.OldIps = new List<string> { before.Ip };
                bool saved = false;
                string note = await Task.Run(delegate { return RecordUpdate.Save(expected, out saved); });
                status.Set(Glyph.Check, L.T("Настройки сети применены.") + (l == null ? L.T(" Получение настроек от роутера может занять некоторое время.") : "") + "\r\n" + note,
                    saved ? Theme.Good : Theme.Warn);
                // The reloaded adapter list below shows the new settings in the «Сейчас» column.
            }
            catch (OperationCanceledException e) { status.Set(Glyph.Info, e.Message, Theme.Warn); }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось завершить настройку сети: ") + e.Message, Theme.Bad); }
            finally
            {
                running = false;
                UpdateSummary();
                LoadAdapters();
            }
        }
    }
    // «Имя компьютера» and «Домен» (set-computer-name.ps1, manage-computer-domain.ps1).
    // Joining first sets a free office address from the database and gives the domain name prefix; leaving needs only
    // a local administrator, gives back the internet name prefix and turns the network back to DHCP.
    public class ComputerPage : Page
    {
        readonly bool domainMode;
        ComputerPolicy policy = ComputerPolicy.Load();
        readonly Label now;
        readonly StatusLine state = new StatusLine();
        readonly FlowLayoutPanel stateActions = Ui.Row();
        readonly Panel editor = new Panel();
        readonly FieldTable form = new FieldTable();
        readonly RadioButton internet = new RadioButton(), domainNet = new RadioButton();
        readonly FlowLayoutPanel networkRow = Ui.Row();
        readonly CheckBox renameOnLeave = new CheckBox();
        readonly TextBox designation = Ui.Input(new TextBox());
        readonly Label preview;
        // Joining: the address in the domain network.
        readonly ComboBox lanAdapter = Ui.Combo(), office = Ui.Combo(), segment = Ui.Combo();
        readonly FlowLayoutPanel ipRow = Ui.Row(), syncRow = Ui.Row();
        readonly Label ipStart, ipNote, syncInfo, profilesNote;
        readonly NumericUpDown host = new NumericUpDown();
        readonly Button nextFree, syncNow;
        // Leaving: adapters with a static address go back to DHCP.
        readonly CheckBox dhcpOnLeave = new CheckBox();
        readonly TextBox localUser = Ui.Input(new TextBox()), localPassword = Ui.Input(new TextBox());
        readonly TextBox domainUser = Ui.Input(new TextBox()), domainPassword = Ui.Input(new TextBox());
        readonly CheckBox remember = new CheckBox();
        readonly Button apply;
        readonly StatusLine status = new StatusLine();
        ComputerState current;
        string operation;   // Rename, Join, Leave
        bool running, syncing, filling, designationEdited;
        NetworkProfiles profiles = new NetworkProfiles();
        AddressBook book = new AddressBook();
        List<Adapter> wired = new List<Adapter>();
        readonly Dictionary<int, LanSetup.State> wiredStates = new Dictionary<int, LanSetup.State>();
        List<Adapter> staticAdapters = new List<Adapter>();
        readonly List<string> staticIps = new List<string>();   // their addresses, dropped from the record after the switch to DHCP
        // Addresses Windows found in use on the network during this session, though the database has no owner for them.
        readonly HashSet<string> takenInNetwork = new HashSet<string>(StringComparer.Ordinal);
        JsonObject handoff;   // form filled before the restart with administrator rights
        const string KeepNetwork = "keep";

        class Item
        {
            public string Text;
            public object Value;
            public override string ToString() { return Text; }
        }

        public ComputerPage(bool domainMode)
        {
            this.domainMode = domainMode;
            Key = domainMode ? "domain" : "name";
            Title = domainMode ? L.T("Домен") : L.T("Имя компьютера");
            Description = domainMode ? L.T("Ввод в домен gu.gvardia.mvs со свободным IP-адресом из базы и вывод в рабочую группу WORKGROUP с возвратом сети на DHCP. Префикс имени меняется вместе с доменом. После изменения нужна перезагрузка.")
                : L.T("Переименование по правилам: ") + policy.InternetPrefix + "-" + policy.UnitCode + L.T("-… для интернета, ") + policy.DomainPrefix + "-" + policy.UnitCode + L.T("-… для доменной сети. После изменения нужна перезагрузка.");
            Icon = domainMode ? Glyph.Domain : Glyph.Tag;
            // The page opens normally; the shield marks that applying a change needs elevation.
            NeedsAdmin = true;
            int em = Font.Height;
            AutoScroll = true;

            now = Ui.Paragraph("", Font, Theme.Text);
            now.Padding = new Padding(0, 0, 0, em / 2);
            state.Dock = DockStyle.Top;
            state.Padding = new Padding(0, 0, 0, em / 2);
            stateActions.Dock = DockStyle.Top;
            stateActions.Padding = new Padding(0, 0, 0, em / 2);

            internet.Text = policy.InternetPrefix + L.T(" — интернет");
            domainNet.Text = policy.DomainPrefix + L.T(" — локальная доменная сеть");
            foreach (RadioButton r in new[] { internet, domainNet })
            {
                r.AutoSize = true;
                r.ForeColor = Theme.Text;
                r.Margin = new Padding(0, 0, em, 0);
                r.CheckedChanged += delegate { UpdatePreview(); };
                networkRow.Controls.Add(r);
            }
            renameOnLeave.Text = L.T("Сменить префикс на ") + policy.InternetPrefix + L.T(" (интернет)");
            Check(renameOnLeave);
            renameOnLeave.CheckedChanged += delegate { if (!filling) UpdateRows(); };
            designation.Width = em * 9;
            designation.MaxLength = 64;
            designation.TextChanged += delegate { if (!filling) designationEdited = true; UpdatePreview(); };
            preview = Ui.Text("", Font, Theme.Accent);

            lanAdapter.Width = em * 20;
            lanAdapter.DropDownWidth = em * 34;
            lanAdapter.SelectedIndexChanged += delegate { if (!filling) AdapterChanged(); };
            office.Width = em * 20;
            office.DropDownWidth = em * 24;
            office.SelectedIndexChanged += delegate { if (!filling) OfficeChanged(); };
            segment.Width = em * 5;
            segment.SelectedIndexChanged += delegate { if (!filling) PickFree(-1); };
            Font mono = new Font(Ui.MonoFont.FontFamily, 11f);
            ipStart = Ui.Text("", mono, Theme.Text);
            ipStart.Anchor = AnchorStyles.Left;
            host.Font = mono;
            host.Minimum = 0;
            host.Maximum = 255;
            host.Width = em * 4;
            host.Margin = new Padding(0, 0, em / 2, 0);
            host.BorderStyle = BorderStyle.FixedSingle;
            host.BackColor = Theme.Surface;
            host.ForeColor = Theme.Text;
            Theme.Native(host);
            host.ValueChanged += delegate { if (!filling) AddressChanged(); };
            nextFree = Ui.Secondary(L.T("Следующий свободный"), null, Font);
            nextFree.Click += delegate { PickFree((int)host.Value); };
            nextFree.Padding = new Padding(em / 2, 0, em / 2, 0);
            ipNote = Ui.Text("", Font, Theme.Muted);
            ipNote.Anchor = AnchorStyles.Left;
            ipRow.Controls.Add(ipStart);
            ipRow.Controls.Add(host);
            ipRow.Controls.Add(nextFree);
            ipRow.Controls.Add(ipNote);
            syncNow = Ui.Secondary(L.T("Синхронизировать базу"), Glyph.Sync, Font);
            syncNow.Click += delegate { SyncDatabase(); };
            syncNow.Padding = new Padding(em / 2, 0, em / 2, 0);
            new ToolTip().SetToolTip(syncNow, L.T("Свободный адрес выбирается по локальной копии базы. Синхронизируйте её, пока есть интернет, чтобы видеть адреса, выданные коллегами."));
            syncInfo = Ui.Text("", Font, Theme.Muted);
            syncInfo.Anchor = AnchorStyles.Left;
            syncRow.Controls.Add(syncNow);
            syncRow.Controls.Add(syncInfo);
            profilesNote = Ui.Text(L.T("Профили сети не загружены: синхронизируйте базу или настройте сеть в разделе «Настройка сети»."), Font, Theme.Warn);
            dhcpOnLeave.Text = L.T("Переключить сеть на DHCP");
            Check(dhcpOnLeave);
            dhcpOnLeave.CheckedChanged += delegate { if (!filling) UpdatePreview(); };

            localUser.Width = domainUser.Width = em * 9;
            localPassword.Width = domainPassword.Width = em * 9;
            localPassword.UseSystemPasswordChar = domainPassword.UseSystemPasswordChar = true;
            Ui.SetCue(localUser, L.T(".\\имя"));
            Ui.SetCue(domainUser, L.T("имя или ДОМЕН\\имя"));
            form.Font = Font;
            form.RowHeight = em * 7 / 4;
            form.Add(L.T("Назначение сети"), networkRow);
            form.Add(L.T("Имя"), renameOnLeave);
            form.Add(L.T("Обозначение"), designation);
            form.Add(L.T("Полное имя"), preview);
            form.Add(L.T("База"), syncRow);
            form.Add(L.T("Сеть"), profilesNote);
            form.Add(L.T("Адаптер"), lanAdapter);
            form.Add(L.T("Корпус"), office);
            form.Add(L.T("Сегмент"), segment);
            form.Add(L.T("IP-адрес"), ipRow);
            form.Add(L.T("Локальный администратор"), localUser);
            form.Add(L.T("Его пароль"), localPassword);
            form.Add(L.T("Сеть"), dhcpOnLeave);
            form.Add(L.T("Доменная учётная запись"), domainUser);
            form.Add(L.T("Пароль"), domainPassword);
            remember.Text = L.T("Запомнить в comp-info.ini");
            remember.AutoSize = true;
            remember.ForeColor = Theme.Text;
            ToolTip rememberTip = new ToolTip();
            rememberTip.SetToolTip(remember, L.T("Пароль хранится рядом с программой лишь замаскированным, это не шифрование: тот, кто получит флешку, получит и эту учётную запись."));
            form.Add("", remember);

            apply = Ui.AdminPrimary(L.T("Применить"), Font);
            apply.Click += delegate { Apply(false); };
            Ui.Keys(apply, "Ctrl+Enter");
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(apply);
            actions.Dock = DockStyle.Top;
            actions.Padding = new Padding(0, em / 2, 0, 0);
            Label hint = Ui.Paragraph(L.T("Пароль локального администратора не сохраняется. Доменный — только если отмечено «Запомнить»: тогда он лежит в comp-info.ini лишь замаскированным, и флешку с ним нужно беречь. Транслитерация выполняется автоматически: «отдел 5» → OTDEL-5."), Font, Theme.Muted);
            hint.Padding = new Padding(0, em / 3, 0, 0);
            editor.Dock = DockStyle.Top;
            editor.AutoSize = true;
            editor.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            editor.Controls.Add(hint);
            editor.Controls.Add(actions);
            editor.Controls.Add(form);
            status.Dock = DockStyle.Top;
            status.Padding = new Padding(0, em / 2, 0, 0);

            Controls.Add(status);
            Controls.Add(editor);
            Controls.Add(stateActions);
            Controls.Add(state);
            Controls.Add(now);
        }

        void Check(CheckBox c)
        {
            c.AutoSize = true;
            c.ForeColor = Theme.Text;
            c.Checked = true;
        }

        public override bool CanLeave()
        {
            if (running || syncing) Ui.Info(FindForm(), Title, L.T("Дождитесь окончания операции."));
            return !running && !syncing;
        }

        public override void Resume(JsonObject form) { handoff = form; }

        public override void Opened()
        {
            if (!running && !syncing) Refresh(true);
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == (Keys.Control | Keys.Enter)) { if (editor.Visible && apply.Enabled) Apply(false); return true; }
            if (keys == Keys.F5) { if (!running && !syncing) Refresh(true); return true; }
            return false;
        }

        Button StateButton(string text, string glyph, bool primary, EventHandler click)
        {
            Button b = primary ? Ui.Primary(text, glyph, Font) : Ui.Secondary(text, glyph, Font);
            b.Click += click;
            stateActions.Controls.Add(b);
            return b;
        }

        async void Refresh(bool verify)
        {
            // A handed-over form is used once, and only if the page comes to the same operation.
            JsonObject resumed = handoff;
            handoff = null;
            stateActions.Controls.Clear();
            editor.Visible = false;
            state.Set(Glyph.More, L.T("Чтение состояния компьютера…"), Theme.Muted);
            try
            {
                current = await Task.Run(delegate { return ComputerState.Read(); });
            }
            catch (Exception e) { state.Set(Glyph.Warning, L.T("Не удалось прочитать состояние компьютера: ") + e.Message, Theme.Bad); return; }
            ComputerState s = current;
            policy = ComputerPolicy.Load();
            now.Text = L.T("Сейчас: ") + s.Name + L.T("\r\nДомен / рабочая группа: ") + s.Domain + (s.PartOfDomain ? L.T(" (член домена)") : L.T(" (рабочая группа)")) +
                (s.PendingName != null ? L.T("\r\nПосле перезагрузки имя станет: ") + s.PendingName : "");
            // COMPINFO_UI_PREVIEW shows the form without rights (screenshots); the change itself still checks real rights.
            bool admin = PowerShell.IsAdministrator() || Environment.GetEnvironmentVariable("COMPINFO_UI_PREVIEW") == "1";

            // Result of the previous change, after the restart (Confirm-ComputerWizardState).
            if (s.Receipt != null && s.RebootedSinceReceipt && admin && verify)
            {
                bool matches = s.ReceiptMatches;
                string channelError = "";
                if (matches && s.PartOfDomain)
                {
                    state.Set(Glyph.More, L.T("Проверка связи с доменом…"), Theme.Muted);
                    try { if (!await Task.Run(delegate { return ComputerChange.TestSecureChannel(); })) { matches = false; channelError = L.T("Доверительный канал с доменом не подтверждён."); } }
                    catch (Exception e) { matches = false; channelError = e.Message; }
                }
                if (matches)
                {
                    ComputerChange.ClearReceipt();
                    // The real values after the restart (the address from DHCP, for example) replace the expected ones.
                    state.Set(Glyph.More, L.T("Обновление записи в локальной базе…"), Theme.Muted);
                    bool saved = false;
                    string note = await Task.Run(delegate { return RecordUpdate.Save(null, out saved); });
                    status.Set(Glyph.Check, L.T("Изменение после перезагрузки подтверждено.") + "\r\n" + note, saved ? Theme.Good : Theme.Warn);
                    Refresh(false);
                    return;
                }
                state.Set(Glyph.Warning, L.T("Ожидаемое состояние не подтверждено: предыдущее действие могло выполниться частично.\r\n") +
                    L.T("Ожидалось: ") + s.ReceiptValue("Name") + "; " + s.ReceiptValue("Domain") + L.T("; член домена: ") + (s.ReceiptMember ? L.T("да") : L.T("нет")) + "\r\n" +
                    L.T("Сейчас: ") + s.Name + "; " + s.Domain + L.T("; член домена: ") + (s.PartOfDomain ? L.T("да") : L.T("нет")) +
                    (channelError.Length > 0 ? "\r\n" + channelError : ""), Theme.Warn);
                StateButton(L.T("Подтвердить и разрешить повторную настройку"), Glyph.Check, false, delegate
                {
                    try { ComputerChange.ClearReceipt(); Refresh(false); }
                    catch (Exception e) { status.Set(Glyph.Warning, e.Message, Theme.Bad); }
                });
                return;
            }
            if (s.HasUnfinishedChange)
            {
                state.Set(Glyph.Warning, s.Receipt != null && s.RebootedSinceReceipt
                    ? L.T("После перезагрузки нужно проверить результат предыдущего изменения: для этого нужны права администратора.")
                    : L.T("Есть незавершённое изменение. Сначала перезагрузите компьютер и откройте этот раздел для проверки."), Theme.Warn);
                if (s.Receipt != null && s.RebootedSinceReceipt && !admin) AdminButton();
                else StateButton(L.T("Перезагрузить сейчас"), Glyph.Power, true, delegate { RestartComputer(); });
                return;
            }
            if (s.DomainRole >= 4) { state.Set(Glyph.Warning, L.T("Изменения на контроллере домена не поддерживаются."), Theme.Bad); return; }
            if (domainMode) now.Text += L.T("\r\nДомен подразделения: ") + policy.DomainName;
            if (s.PartOfDomain && !string.Equals(s.Domain, policy.DomainName, StringComparison.OrdinalIgnoreCase))
            {
                state.Set(Glyph.Warning, L.T("Компьютер состоит в другом домене. Автоматический перенос не поддерживается."), Theme.Bad);
                return;
            }
            state.Clear();
            await BuildEditor(s);
            if (resumed != null && resumed.Text("page") == Key && resumed.Text("operation") == operation) Fill(resumed);
        }

        void AdminButton()
        {
            StateButton(L.T("Продолжить с правами администратора"), Glyph.Shield, true, delegate { if (Main != null) Main.RestartAsAdministrator(Key); });
        }

        async Task BuildEditor(ComputerState s)
        {
            operation = domainMode ? (s.PartOfDomain ? "Leave" : "Join") : "Rename";
            designationEdited = false;
            filling = true;
            localPassword.Text = "";
            // Remembered domain account (comp-info.ini), if the user chose to keep it.
            DomainCredentials.Load(domainUser, domainPassword, remember);
            if (operation == "Rename") { internet.Checked = !s.PartOfDomain && !s.Name.StartsWith(policy.DomainPrefix + "-"); domainNet.Checked = !internet.Checked; }
            else domainNet.Checked = operation == "Join";
            renameOnLeave.Checked = dhcpOnLeave.Checked = true;
            filling = false;
            apply.Text = operation == "Rename" ? L.T(" Переименовать") : operation == "Join" ? L.T(" Ввести в ") + policy.DomainName : L.T(" Вывести из ") + policy.DomainName;
            UpdateRows();
            editor.Visible = true;
            status.Clear();

            string ip = null;
            if (operation == "Join") await LoadJoinData();
            else if (operation == "Leave") await LoadLeaveData();
            if (operation != "Join") { try { ip = ComputerSnapshot.MainIpAddress(); } catch (Exception) { } }
            SuggestDesignation(ip ?? PlannedAddress());
            designationEdited = false;
            UpdatePreview();
            if (designation.Visible && operation != "Leave") designation.Focus();
            else if (operation == "Leave") { if (localUser.Text.Length > 0) localPassword.Focus(); else localUser.Focus(); }
        }

        void SuggestDesignation(string ip)
        {
            string suggested = ComputerNaming.Suggested(current.Name, Prefix, policy, null, ip);
            filling = true;
            designation.Text = suggested.Substring((Prefix + "-" + policy.UnitCode + "-").Length);
            filling = false;
        }

        // Rows of the operation: the name for all but a leave without renaming, network and database for joining,
        // local administrator and DHCP for leaving, domain account where the domain is contacted.
        void UpdateRows()
        {
            bool join = operation == "Join", leave = operation == "Leave";
            bool named = !leave || renameOnLeave.Checked;
            form.Show(networkRow, operation == "Rename" && current != null && !current.PartOfDomain);
            form.Show(renameOnLeave, leave);
            form.Show(designation, named);
            form.Show(preview, named);
            form.Show(syncRow, join);
            form.Show(profilesNote, join && profiles.Missing);
            bool lan = join && !profiles.Missing;
            form.Show(lanAdapter, lan);
            form.Show(office, lan);
            bool keep = lan && Office == null;
            form.Show(segment, lan && !keep);
            form.Show(ipRow, lan && !keep);
            form.Show(localUser, leave);
            form.Show(localPassword, leave);
            form.Show(dhcpOnLeave, leave && staticAdapters.Count > 0);
            bool needsDomain = join || (operation == "Rename" && current != null && current.PartOfDomain);
            form.Show(domainUser, needsDomain);
            form.Show(domainPassword, needsDomain);
            form.Show(remember, needsDomain);
            UpdatePreview();
        }

        // ---------- leaving ----------

        async Task LoadLeaveData()
        {
            List<LanSetup.State> states = null;
            string admin = "";
            try
            {
                staticAdapters = await Task.Run(delegate { return LanSetup.StaticAdapters(out states); });
                admin = await Task.Run(delegate { return ComputerChange.DefaultLocalAdministrator(); });
            }
            catch (Exception e) { staticAdapters = new List<Adapter>(); status.Set(Glyph.Warning, L.T("Не удалось прочитать сетевые адаптеры: ") + e.Message, Theme.Warn); }
            List<string> names = new List<string>();
            staticIps.Clear();
            for (int i = 0; i < staticAdapters.Count; i++)
            {
                bool hasStatic = states != null && states[i].Ip.Length > 0 && !states[i].Dhcp;
                names.Add(staticAdapters[i].Name + (hasStatic ? " (" + states[i].Ip + ")" : ""));
                if (hasStatic) staticIps.Add(states[i].Ip);
            }
            dhcpOnLeave.Text = L.T("Переключить на DHCP: ") + string.Join(", ", names);
            if (localUser.Text.Trim().Length == 0) localUser.Text = admin;
            UpdateRows();
        }

        // ---------- joining: database, adapter, office, address ----------

        async Task LoadJoinData()
        {
            profiles = NetworkProfiles.Read(AppPaths.File("network-profiles.json"));
            ShowSyncInfo();
            string dataPath = AppPaths.Data;
            try
            {
                book = await Task.Run(delegate { return AddressBook.Read(dataPath, OwnRecord(dataPath)); });
                wired = await Task.Run(delegate
                {
                    List<Adapter> list = LanSetup.WiredAdapters();
                    wiredStates.Clear();
                    foreach (Adapter a in list) { try { wiredStates[a.Index] = LanSetup.Read(a.Guid); } catch (Exception) { } }
                    return list;
                });
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось прочитать базу или сетевые адаптеры: ") + e.Message, Theme.Warn); }

            filling = true;
            office.Items.Clear();
            foreach (NetworkProfiles.Location l in profiles.Locations) office.Items.Add(new Item { Text = l.Name, Value = l });
            office.Items.Add(new Item { Text = L.T("— не менять настройки сети"), Value = KeepNetwork });
            lanAdapter.Items.Clear();
            int select = 0;
            foreach (Adapter a in wired)
            {
                bool good, bad;
                LanSetup.State st = StateOf(a);
                // Prefer the adapter that already has an office address.
                NetworkProfiles.Location l; int seg, last;
                if (st != null && !st.Dhcp && profiles.Match(st.Ip, out l, out seg, out last) && select == 0 && lanAdapter.Items.Count > 0) select = lanAdapter.Items.Count;
                lanAdapter.Items.Add(new Item { Text = a.Name + " — " + LanSetup.StatusText(a, out good, out bad) + "  (" + a.Description + ")", Value = a });
            }
            if (lanAdapter.Items.Count > 0) lanAdapter.SelectedIndex = select;
            filling = false;
            if (wired.Count == 0 && !profiles.Missing) status.Set(Glyph.Warning, L.T("Проводные сетевые адаптеры не найдены."), Theme.Bad);
            AdapterChanged();
        }

        // This computer's record in the database: its own address is not someone else's.
        static string OwnRecord(string dataPath)
        {
            try
            {
                Dictionary<string, object> product = Wmi.First(Wmi.Cimv2, "SELECT UUID FROM Win32_ComputerSystemProduct");
                Dictionary<string, object> bios = Wmi.First(Wmi.Cimv2, "SELECT SerialNumber FROM Win32_BIOS");
                return Identity.Find(Wmi.Str(product, "UUID"), (Wmi.Str(bios, "SerialNumber") ?? "").Trim(), Identity.WindowsInstallationId(), dataPath).Path;
            }
            catch (Exception) { return null; }
        }

        void ShowSyncInfo()
        {
            string last = null;
            try { last = CloudState.ForDisplay(AppPaths.Root).LastSyncAt; } catch (Exception) { }
            DateTime when;
            bool known = DateTime.TryParse(last ?? "", out when);
            bool old = !known || (DateTime.Now - when).TotalHours > 12;
            syncInfo.Text = (known ? L.T("последняя синхронизация: ") + Fmt.Date(last) : L.T("синхронизации ещё не было")) +
                (old ? L.T("\r\nсинхронизируйте, пока есть интернет") : "");
            syncInfo.ForeColor = old ? Theme.Warn : Theme.Muted;
        }

        Adapter SelectedAdapter { get { return lanAdapter.SelectedItem == null ? null : (Adapter)((Item)lanAdapter.SelectedItem).Value; } }

        LanSetup.State StateOf(Adapter a)
        {
            LanSetup.State s;
            return a != null && wiredStates.TryGetValue(a.Index, out s) ? s : null;
        }

        NetworkProfiles.Location Office { get { return office.SelectedItem == null ? null : ((Item)office.SelectedItem).Value as NetworkProfiles.Location; } }

        int? Segment { get { return segment.SelectedItem == null ? (int?)null : (int)((Item)segment.SelectedItem).Value; } }

        // The adapter's own office address is kept when nobody else in the database has it; otherwise the first free one.
        void AdapterChanged()
        {
            if (profiles.Missing || office.Items.Count == 0) { UpdateRows(); return; }
            LanSetup.State st = StateOf(SelectedAdapter);
            NetworkProfiles.Location l; int seg, last;
            if (st != null && !st.Dhcp && profiles.Match(st.Ip, out l, out seg, out last))
            {
                SelectOffice(l, seg);
                if (book.Owner(st.Ip) == null) SetHost(last); else PickFree(-1);
            }
            else
            {
                if (office.SelectedItem == null) SelectOffice(profiles.Locations[0], -1);
                PickFree(-1);
            }
            UpdateRows();
        }

        void SelectOffice(NetworkProfiles.Location l, int seg)
        {
            filling = true;
            foreach (Item i in office.Items) if (ReferenceEquals(i.Value, l)) office.SelectedItem = i;
            FillSegments(seg);
            filling = false;
        }

        void FillSegments(int keep)
        {
            NetworkProfiles.Location l = Office;
            int? previous = keep >= 0 ? keep : Segment;
            segment.Items.Clear();
            if (l == null) return;
            foreach (int s in l.Segments ?? profiles.Segments)
            {
                segment.Items.Add(new Item { Text = s.ToString(), Value = s });
                if (previous.HasValue && previous.Value == s) segment.SelectedIndex = segment.Items.Count - 1;
            }
            if (segment.SelectedIndex < 0 && segment.Items.Count > 0) segment.SelectedIndex = 0;
            host.Minimum = l.FirstHostOctet;
            host.Maximum = Math.Max(l.FirstHostOctet, l.LastHostOctet);
        }

        void OfficeChanged()
        {
            filling = true;
            FillSegments(-1);
            filling = false;
            if (Office != null) PickFree(-1);
            UpdateRows();
        }

        // First free address after `after` (-1: from the start of the range).
        void PickFree(int after)
        {
            NetworkProfiles.Location l = Office;
            if (l == null || !Segment.HasValue) { AddressChanged(); return; }
            int free = book.FirstFree(profiles, l, Segment.Value, after, takenInNetwork);
            SetHost(free >= 0 ? free : (after >= 0 ? after : l.FirstHostOctet));
        }

        void SetHost(int value)
        {
            filling = true;
            host.Value = Math.Max(host.Minimum, Math.Min(host.Maximum, value));
            filling = false;
            AddressChanged();
        }

        string PlannedAddress()
        {
            NetworkProfiles.Location l = Office;
            if (operation != "Join" || l == null || !Segment.HasValue) return null;
            return profiles.Address(l, Segment.Value, (int)host.Value);
        }

        void AddressChanged()
        {
            NetworkProfiles.Location l = Office;
            if (l == null || !Segment.HasValue) { ipStart.Text = ""; ipNote.Text = ""; UpdatePreview(); return; }
            ipStart.Text = profiles.FirstOctet + "." + Segment.Value + "." + l.ThirdOctet + ".";
            string ip = PlannedAddress();
            string owner = book.Owner(ip);
            LanSetup.State st = StateOf(SelectedAdapter);
            bool good = false;
            if (owner != null) ipNote.Text = L.T("занят: ") + owner;
            else if (takenInNetwork.Contains(ip)) ipNote.Text = L.T("занят в сети");
            else if (st != null && !st.Dhcp && st.Ip == ip) { ipNote.Text = L.T("текущий адрес ПК"); good = true; }
            else if (book.FirstFree(profiles, l, Segment.Value, -1, takenInNetwork) < 0) ipNote.Text = L.T("свободных нет");
            else { ipNote.Text = L.T("свободен по базе"); good = true; }
            ipNote.ForeColor = good ? Theme.Good : Theme.Bad;
            // A name made from the address follows it until the technician types a designation.
            if (!designationEdited && current != null && !System.Text.RegularExpressions.Regex.IsMatch(current.Name, "^[A-Z]{2,3}-\\d{3}-.+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                SuggestDesignation(ip);
            UpdatePreview();
        }

        async void SyncDatabase()
        {
            if (syncing || running) return;
            syncing = true;
            syncNow.Enabled = apply.Enabled = false;
            status.Set(Glyph.More, L.T("Подключение к облаку…"), Theme.Muted);
            try
            {
                SyncCounts c = await SyncDialogs.Run(this, delegate (string text) { status.Set(Glyph.More, text, Theme.Muted); });
                if (c == null) status.Set(Glyph.Info, L.T("Синхронизация отменена: ключ не введён."), Theme.Warn);
                else if (c.Errors.Count == 0) status.Set(Glyph.Check, L.T("База синхронизирована: получено новых ") + c.ReceivedNew + L.T(", обновлено ") + c.ReceivedUpdated + L.T(". Адрес выбран заново."), Theme.Good);
                else status.Set(Glyph.Warning, L.T("Синхронизация завершена с ошибками (") + c.Errors.Count + "): " + c.Errors[0], Theme.Warn);
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Синхронизация не завершена: ") + e.Message, Theme.Bad); }
            finally
            {
                syncing = false;
                syncNow.Enabled = true;
            }
            if (operation == "Join" && !running) await LoadJoinData();
            UpdatePreview();
        }

        // ---------- name, apply ----------

        string Prefix { get { return operation == "Leave" ? policy.InternetPrefix : domainNet.Checked || operation != "Rename" ? policy.DomainPrefix : policy.InternetPrefix; } }

        string FullName { get { return Prefix + "-" + policy.UnitCode + "-" + ComputerNaming.Designation(designation.Text); } }

        bool Renames { get { return operation != "Leave" || renameOnLeave.Checked; } }

        void UpdatePreview()
        {
            if (operation == null) return;
            string issue = null;
            if (Renames)
            {
                string name = FullName;
                issue = ComputerNaming.Issue(name, Prefix, policy);
                preview.Text = name + (issue != null ? "   — " + issue : "");
                preview.ForeColor = issue != null ? Theme.Bad : Theme.Accent;
                preview.MaximumSize = new Size(Math.Max(Font.Height * 10, form.Width - Font.Height * 12), 0);
            }
            bool networkReady = operation != "Join" || profiles.Missing || Office == null || (SelectedAdapter != null && Segment.HasValue);
            apply.Enabled = issue == null && networkReady && !running && !syncing;
        }

        ComputerChange.NetworkPlan NetworkPlan()
        {
            NetworkProfiles.Location l = Office;
            Adapter a = SelectedAdapter;
            if (operation != "Join" || profiles.Missing || l == null || a == null || !Segment.HasValue) return null;
            ComputerChange.NetworkPlan n = new ComputerChange.NetworkPlan();
            n.AdapterIndex = a.Index;
            n.AdapterName = a.Name;
            n.AdapterGuid = a.Guid;
            n.Ip = PlannedAddress();
            n.Mask = l.SubnetMask;
            n.PrefixLength = l.PrefixLength;
            n.Gateway = profiles.Gateway(l, Segment.Value);
            n.Dns = new List<string>(l.DnsServers);
            return n;
        }

        // Everything typed and chosen, for the copy started with administrator rights.
        JsonObject FormData()
        {
            JsonObject o = new JsonObject();
            o["page"] = Key;
            o["operation"] = operation;
            o["designation"] = designation.Text;
            o["internet"] = internet.Checked;
            o["renameOnLeave"] = renameOnLeave.Checked;
            o["dhcpOnLeave"] = dhcpOnLeave.Checked;
            o["localUser"] = localUser.Text;
            o["localPassword"] = localPassword.Text;
            o["domainUser"] = domainUser.Text;
            o["domainPassword"] = domainPassword.Text;
            o["remember"] = remember.Checked;
            Adapter a = SelectedAdapter;
            o["adapter"] = a != null ? a.Guid : null;
            o["office"] = Office != null ? Office.Name : office.SelectedItem != null ? KeepNetwork : null;
            o["segment"] = Segment.HasValue ? (object)Segment.Value : null;
            o["host"] = (int)host.Value;
            return o;
        }

        // Puts the handed-over form back and goes on with the change without asking again.
        void Fill(JsonObject o)
        {
            filling = true;
            if (o["internet"] is bool) { internet.Checked = (bool)o["internet"]; domainNet.Checked = !internet.Checked; }
            if (o["renameOnLeave"] is bool) renameOnLeave.Checked = (bool)o["renameOnLeave"];
            if (o["dhcpOnLeave"] is bool) dhcpOnLeave.Checked = (bool)o["dhcpOnLeave"];
            designation.Text = o.Text("designation") ?? "";
            localUser.Text = o.Text("localUser") ?? "";
            localPassword.Text = o.Text("localPassword") ?? "";
            domainUser.Text = o.Text("domainUser") ?? "";
            domainPassword.Text = o.Text("domainPassword") ?? "";
            if (o["remember"] is bool) remember.Checked = (bool)o["remember"];
            filling = false;
            designationEdited = true;
            if (operation == "Join" && !profiles.Missing)
            {
                foreach (Item i in lanAdapter.Items)
                    if (((Adapter)i.Value).Guid == o.Text("adapter")) { filling = true; lanAdapter.SelectedItem = i; filling = false; }
                string officeName = o.Text("office");
                if (officeName == KeepNetwork)
                {
                    filling = true;
                    foreach (Item i in office.Items) if (KeepNetwork.Equals(i.Value)) office.SelectedItem = i;
                    filling = false;
                }
                else
                {
                    NetworkProfiles.Location l = profiles.Locations.Find(delegate (NetworkProfiles.Location x) { return x.Name == officeName; });
                    int seg;
                    if (l != null && int.TryParse(o.Text("segment"), out seg)) SelectOffice(l, seg);
                    int last;
                    if (int.TryParse(o.Text("host"), out last)) SetHost(last);
                }
            }
            UpdateRows();
            if (apply.Enabled) Apply(true);
        }

        async void Apply(bool confirmed)
        {
            ComputerState s = current;
            ComputerChange.Request r = new ComputerChange.Request();
            r.Operation = operation;
            r.Name = operation == "Leave" && !renameOnLeave.Checked ? s.Name : FullName;
            r.LocalUser = localUser.Text.Trim();
            r.LocalPassword = localPassword.Text;
            r.DomainUser = domainUser.Text.Trim();
            r.DomainPassword = domainPassword.Text;
            r.Network = NetworkPlan();
            if (operation == "Leave" && dhcpOnLeave.Checked) r.DhcpAdapters = new List<Adapter>(staticAdapters);
            bool needsDomain = operation == "Join" || (operation == "Rename" && s.PartOfDomain);
            if (needsDomain) DomainCredentials.Save(r.DomainUser, r.DomainPassword, remember.Checked);
            if (operation == "Leave" && r.LocalUser.Length == 0) { status.Set(Glyph.Warning, L.T("Укажите локального администратора: доменные профили не преобразуются в локальные."), Theme.Bad); return; }
            if (needsDomain && r.DomainUser.Length == 0) { status.Set(Glyph.Warning, L.T("Укажите доменную учётную запись с правом выполнения операции."), Theme.Bad); return; }
            string targetDomain = operation == "Join" ? policy.DomainName : operation == "Leave" ? policy.WorkgroupName : s.Domain;
            if (!confirmed)
            {
                string action = operation == "Rename" ? L.T("Переименование") : operation == "Join" ? L.T("Ввод в домен") : L.T("Вывод из домена");
                string question = action + L.T("\n\nИмя: ") + s.Name + " → " + r.Name + L.T("\nДомен / рабочая группа: ") + s.Domain + " → " + targetDomain;
                if (r.Network != null)
                {
                    string owner = book.Owner(r.Network.Ip);
                    question += L.T("\nСеть: «") + r.Network.AdapterName + "» → " + r.Network.Ip + "/" + r.Network.PrefixLength + L.T(", шлюз ") + r.Network.Gateway +
                        (r.Network.Dns.Count > 0 ? ", DNS " + string.Join(", ", r.Network.Dns) : "") +
                        (owner != null ? L.T("\nВнимание: в базе этот адрес у ") + owner + "." : "");
                }
                if (r.DhcpAdapters.Count > 0)
                {
                    List<string> names = new List<string>();
                    foreach (Adapter a in r.DhcpAdapters) names.Add(a.Name);
                    question += L.T("\nСеть: ") + string.Join(", ", names) + L.T(" → автоматически (DHCP)");
                }
                if (operation == "Leave") question += L.T("\n\nДоменная учётная запись не нужна: объект компьютера в домене останется как есть.");
                question += L.T("\n\nПотребуется перезагрузка. Новое состояние будет проверено при следующем открытии раздела.");
                if (Ui.Ask(FindForm(), Title, question, Glyph.Warning, Theme.Warn, L.T("Применить изменения"), L.T("Назад")) != 0) return;
            }

            // Viewing and filling the form does not need UAC. Ask only after the user has confirmed a real change;
            // the new copy gets the whole form and goes on by itself.
            if (!PowerShell.IsAdministrator()) { if (Main != null) Main.RestartAsAdministrator(Key, FormData()); return; }

            running = true;
            apply.Enabled = syncNow.Enabled = false;
            status.Set(Glyph.More, L.T("Выполнение…"), Theme.Muted);
            r.Progress = delegate (string text) { BeginInvoke(new MethodInvoker(delegate { status.Set(Glyph.More, text, Theme.Muted); })); };
            string dataPath = AppPaths.Data;
            ComputerPolicy p = policy;
            bool refresh = true;
            try
            {
                bool changed = await Task.Run(delegate
                {
                    if (r.Operation != "Leave" || r.Name != s.Name) ComputerChange.AssertInventoryReady(dataPath);
                    return ComputerChange.Apply(r, p);
                });
                if (!changed) { status.Set(Glyph.Check, L.T("Компьютер уже имеет это имя. Изменения не требуются."), Theme.Good); return; }
                // The record gets the values that will be there after the restart: name, domain, address.
                status.Set(Glyph.More, L.T("Обновление записи в локальной базе…"), Theme.Muted);
                Collection.Expected expected = new Collection.Expected();
                expected.Hostname = r.Name;
                if (r.Operation != "Rename") { expected.PartOfDomain = r.Operation == "Join"; expected.Domain = targetDomain; }
                if (r.Network != null) { expected.SetIp = true; expected.Ip = r.Network.Ip; }
                if (r.DhcpAdapters.Count > 0) expected.OldIps = new List<string>(staticIps);
                bool saved = false;
                string note = await Task.Run(delegate { return RecordUpdate.Save(expected, out saved); });
                if (!saved) r.Warnings.Add(note);
                status.Set(r.Warnings.Count > 0 ? Glyph.Warning : Glyph.Check, L.T("Windows подтвердила команду. Ожидаемое имя: ") + r.Name + L.T("; домен / группа: ") + targetDomain +
                    (r.Network != null ? L.T("; адрес ") + r.Network.Ip : "") +
                    L.T(".\r\nПосле перезагрузки откройте этот раздел для проверки: запись в базе обновится ещё раз уже по факту.") +
                    (saved ? "\r\n" + note : "") +
                    (r.Warnings.Count > 0 ? "\r\n" + string.Join("\r\n", r.Warnings) : ""), r.Warnings.Count > 0 ? Theme.Warn : Theme.Good);
                if (Ui.Ask(FindForm(), Title, L.T("Для завершения нужна перезагрузка компьютера."), Glyph.Power, Theme.Info, L.T("Перезагрузить сейчас"), L.T("Позже")) == 0) RestartComputer();
            }
            catch (Exception e)
            {
                // Windows found the new address in use: remember it and offer the next free one.
                if (e is ComputerChange.AddressInUseException)
                {
                    takenInNetwork.Add(r.Network.Ip);
                    refresh = false;
                    PickFree((int)host.Value);
                }
                status.Set(Glyph.Warning, e.Message + L.T("\r\nЕсли команда Windows уже запускалась, результат может быть частичным. Перезагрузите компьютер и откройте раздел для проверки."), Theme.Bad);
            }
            finally
            {
                r.LocalPassword = r.DomainPassword = null;
                localPassword.Text = "";
                running = false;
                syncNow.Enabled = true;
                if (refresh) Refresh(false); else UpdatePreview();
            }
        }

        void RestartComputer()
        {
            try { ComputerChange.Restart(); }
            catch (Exception e) { Ui.Error(FindForm(), L.T("Не удалось перезагрузить компьютер: ") + e.Message); }
        }
    }
}
