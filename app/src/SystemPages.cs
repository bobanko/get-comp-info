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

        public Label Add(string label, Control field)
        {
            int em = Font.Height;
            Label l = Ui.Text(label, Font, Theme.Muted);
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(0, em / 4, em, em / 4);
            field.Margin = new Padding(0, em / 6, 0, em / 6);
            if (field is TextBox && ((TextBox)field).Multiline) field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            else field.Anchor = AnchorStyles.Left;
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(l, 0, RowCount);
            Controls.Add(field, 1, RowCount);
            RowCount++;
            return l;
        }

        public void Show(Control field, bool visible)
        {
            field.Visible = visible;
            Control label = GetControlFromPosition(0, GetRow(field));
            if (label != null) label.Visible = visible;
        }
    }

    // «Настройка сети»: static address by the office profiles or DHCP (setup-lan-adapter.ps1).
    public class LanPage : Page
    {
        readonly FieldTable form = new FieldTable();
        readonly ComboBox network = Ui.Combo();
        readonly ComboBox segment = Ui.Combo();
        readonly ComboBox adapter = Ui.Combo();
        readonly TextBox octet = Ui.Input(new TextBox());
        readonly Label range;
        readonly FlowLayoutPanel octetRow = Ui.Row();
        readonly StatusLine profilesNote = new StatusLine();
        readonly Label summary;
        readonly Button apply;
        readonly StatusLine status = new StatusLine();
        readonly Label applied;
        NetworkProfiles profiles = new NetworkProfiles();
        List<Adapter> adapters = new List<Adapter>();
        bool running;

        class Item
        {
            public string Text;
            public object Value;
            public override string ToString() { return Text; }
        }

        public LanPage()
        {
            Key = "lan";
            Title = "Настройка сети";
            Description = "IP-адрес, маска, шлюз и DNS проводного адаптера по профилям корпусов из облака, или автоматическая настройка (DHCP).";
            Icon = Glyph.Network;
            NeedsAdmin = true;
            int em = Font.Height;
            Padding = new Padding(0);
            AutoScroll = true;

            profilesNote.Dock = DockStyle.Top;
            profilesNote.Padding = new Padding(0, 0, 0, em / 2);
            network.Width = segment.Width = em * 20;
            adapter.Width = em * 26;
            adapter.DropDownWidth = em * 34;
            octet.Width = em * 4;
            octet.MaxLength = 3;
            range = Ui.Text("", Font, Theme.Muted);
            range.Margin = new Padding(em / 2, em / 6, 0, 0);
            octetRow.Controls.Add(octet);
            octetRow.Controls.Add(range);
            form.Font = Font;
            form.Add("Сеть", network);
            form.Add("Сегмент", segment);
            form.Add("Адаптер", adapter);
            form.Add("Последний октет", octetRow);
            network.SelectedIndexChanged += delegate { NetworkChanged(); };
            segment.SelectedIndexChanged += delegate { UpdateSummary(); };
            adapter.SelectedIndexChanged += delegate { UpdateSummary(); };
            octet.TextChanged += delegate { UpdateSummary(); };
            octet.KeyPress += delegate (object o, KeyPressEventArgs k) { if (!char.IsDigit(k.KeyChar) && !char.IsControl(k.KeyChar)) k.Handled = true; };

            summary = Ui.Paragraph("", Font, Theme.Accent);
            summary.Padding = new Padding(0, em / 2, 0, em / 2);
            apply = Ui.Primary("Применить настройки", PowerShell.IsAdministrator() ? Glyph.Check : Glyph.Shield, Font);
            apply.Click += delegate { Apply(); };
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(apply);
            actions.Dock = DockStyle.Top;
            status.Dock = DockStyle.Top;
            status.Padding = new Padding(0, em / 2, 0, 0);
            applied = Ui.Paragraph("", Font, Theme.Text);

            Controls.Add(applied);
            Controls.Add(status);
            Controls.Add(actions);
            Controls.Add(summary);
            Controls.Add(form);
            Controls.Add(profilesNote);
        }

        public override bool CanLeave()
        {
            if (running) Ui.Info(FindForm(), "Настройка сети", "Дождитесь окончания настройки сети.");
            return !running;
        }

        public override void Opened()
        {
            if (running) return;
            profiles = NetworkProfiles.Read(AppPaths.File("network-profiles.json"));
            object selected = network.SelectedItem != null ? ((Item)network.SelectedItem).Text : null;
            network.Items.Clear();
            foreach (NetworkProfiles.Location l in profiles.Locations)
            {
                Item item = new Item();
                item.Text = l.Name + "  [" + l.ThirdOctet + "]";
                item.Value = l;
                network.Items.Add(item);
            }
            Item dhcp = new Item();
            dhcp.Text = "Интернет — автоматически (DHCP)";
            network.Items.Add(dhcp);
            network.SelectedIndex = 0;
            for (int i = 0; i < network.Items.Count; i++) if (((Item)network.Items[i]).Text == (string)selected) network.SelectedIndex = i;
            if (profiles.Missing)
                profilesNote.Set(Glyph.Warning, "Профили корпусов не загружены: синхронизируйтесь с облаком, когда будет интернет. Сейчас доступна только автоматическая настройка.", Theme.Warn);
            else profilesNote.Clear();
            LoadAdapters();
        }

        async void LoadAdapters()
        {
            adapter.Items.Clear();
            adapter.Enabled = false;
            try
            {
                adapters = await Task.Run(delegate { return LanSetup.WiredAdapters(); });
                foreach (Adapter a in adapters)
                {
                    bool good, bad;
                    Item item = new Item();
                    item.Text = a.Name + " — " + LanSetup.StatusText(a, out good, out bad) + "  (" + a.Description + ")";
                    item.Value = a;
                    adapter.Items.Add(item);
                }
                if (adapter.Items.Count > 0) adapter.SelectedIndex = 0;
                adapter.Enabled = adapters.Count > 0;
                if (adapters.Count == 0) status.Set(Glyph.Warning, "Проводные сетевые адаптеры не найдены.", Theme.Bad);
            }
            catch (Exception e) { status.Set(Glyph.Warning, "Не удалось получить список адаптеров: " + e.Message, Theme.Bad); }
            UpdateSummary();
        }

        NetworkProfiles.Location Office
        {
            get { return network.SelectedItem == null ? null : ((Item)network.SelectedItem).Value as NetworkProfiles.Location; }
        }

        void NetworkChanged()
        {
            NetworkProfiles.Location l = Office;
            segment.Items.Clear();
            if (l != null)
            {
                foreach (int s in l.Segments ?? profiles.Segments) segment.Items.Add(s);
                if (segment.Items.Count > 0) segment.SelectedIndex = 0;
                range.Text = "от " + l.FirstHostOctet + " до " + l.LastHostOctet;
            }
            form.Show(segment, l != null);
            form.Show(octetRow, l != null);
            UpdateSummary();
        }

        // The settings to apply, or null with the reason in error.
        bool Plan(out string ip, out string gateway, out string error)
        {
            ip = gateway = error = null;
            NetworkProfiles.Location l = Office;
            if (adapter.SelectedItem == null) { error = "Выберите адаптер."; return false; }
            if (l == null) return true;
            if (segment.SelectedItem == null) { error = "Выберите сегмент."; return false; }
            int last;
            if (!int.TryParse(octet.Text, out last) || last < l.FirstHostOctet || last > l.LastHostOctet)
            {
                error = "Введите последний октет от " + l.FirstHostOctet + " до " + l.LastHostOctet + ".";
                return false;
            }
            string prefix = profiles.FirstOctet + "." + segment.SelectedItem + "." + l.ThirdOctet;
            ip = prefix + "." + last;
            gateway = prefix + "." + l.GatewayOctet;
            return true;
        }

        void UpdateSummary()
        {
            string ip, gateway, error;
            bool ok = Plan(out ip, out gateway, out error);
            apply.Enabled = ok && !running;
            NetworkProfiles.Location l = Office;
            if (!ok) { summary.ForeColor = Theme.Muted; summary.Text = error; return; }
            summary.ForeColor = Theme.Accent;
            if (l == null)
            {
                summary.Text = "IP-адрес, маска, шлюз и DNS — автоматически от роутера (DHCP).\r\nСтатические IPv4-адреса, шлюз и вручную заданные DNS будут сброшены.";
                return;
            }
            List<string> lines = new List<string>();
            lines.Add("IP-адрес:  " + ip);
            lines.Add("Маска:  " + l.SubnetMask);
            lines.Add("Шлюз:  " + gateway);
            for (int i = 0; i < l.DnsServers.Count; i++) lines.Add("DNS " + (i + 1) + ":  " + l.DnsServers[i]);
            summary.Text = string.Join("\r\n", lines);
        }

        async void Apply()
        {
            string ip, gateway, error;
            if (!Plan(out ip, out gateway, out error)) return;
            Adapter a = (Adapter)((Item)adapter.SelectedItem).Value;
            NetworkProfiles.Location l = Office;
            string question = (l == null ? "Включить автоматическую настройку (DHCP)" : "Назначить адрес " + ip) + " на адаптере «" + a.Name + "»?\n\n" +
                "Текущее сетевое подключение может временно прерваться." + (PowerShell.IsAdministrator() ? "" : "\nWindows запросит права администратора.");
            if (Ui.Ask(FindForm(), "Настройка сети", question, Glyph.Network, Theme.Info, "Применить", "Отмена") != 0) return;
            running = true;
            apply.Enabled = false;
            applied.Text = "";
            status.Set(Glyph.More, "Применение настроек…", Theme.Muted);
            try
            {
                int prefixLength = l == null ? 0 : l.PrefixLength;
                List<string> dns = l == null ? new List<string>() : l.DnsServers;
                await Task.Run(delegate { LanSetup.Apply(a.Index, l == null, ip, prefixLength, gateway, dns); });
                status.Set(Glyph.Check, "Настройки сети применены." + (l == null ? " Получение настроек от роутера может занять некоторое время." : ""), Theme.Good);
                List<string[]> now = await Task.Run(delegate { return LanSetup.Current(a.Index); });
                List<string> lines = new List<string> { "Сейчас на адаптере «" + a.Name + "»:" };
                foreach (string[] line in now) lines.Add("   " + line[0] + ":  " + line[1]);
                applied.Text = string.Join("\r\n", lines);
            }
            catch (OperationCanceledException e) { status.Set(Glyph.Info, e.Message, Theme.Warn); }
            catch (Exception e) { status.Set(Glyph.Warning, "Не удалось завершить настройку сети: " + e.Message, Theme.Bad); }
            finally
            {
                running = false;
                UpdateSummary();
                LoadAdapters();
            }
        }
    }

    // «Имя компьютера» and «Домен» (set-computer-name.ps1, manage-computer-domain.ps1).
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
        readonly TextBox designation = Ui.Input(new TextBox());
        readonly Label preview;
        readonly TextBox localUser = Ui.Input(new TextBox()), localPassword = Ui.Input(new TextBox());
        readonly TextBox domainUser = Ui.Input(new TextBox()), domainPassword = Ui.Input(new TextBox());
        readonly Button apply;
        readonly StatusLine status = new StatusLine();
        ComputerState current;
        string operation;   // Rename, Join, Leave
        bool running;

        public ComputerPage(bool domainMode)
        {
            this.domainMode = domainMode;
            Key = domainMode ? "domain" : "name";
            Title = domainMode ? "Домен" : "Имя компьютера";
            Description = domainMode ? "Ввод компьютера в домен подразделения и вывод из него. Домен берётся из профилей сети в облаке. После изменения нужна перезагрузка."
                : "Переименование по правилам: " + policy.InternetPrefix + "-" + policy.UnitCode + "-… для интернета, " + policy.DomainPrefix + "-" + policy.UnitCode + "-… для доменной сети. После изменения нужна перезагрузка.";
            Icon = domainMode ? Glyph.Domain : Glyph.Tag;
            NeedsAdmin = true;
            int em = Font.Height;
            AutoScroll = true;

            now = Ui.Paragraph("", Font, Theme.Text);
            now.Padding = new Padding(0, 0, 0, em / 2);
            state.Dock = DockStyle.Top;
            state.Padding = new Padding(0, 0, 0, em / 2);
            stateActions.Dock = DockStyle.Top;
            stateActions.Padding = new Padding(0, 0, 0, em / 2);

            internet.Text = policy.InternetPrefix + " — интернет";
            domainNet.Text = policy.DomainPrefix + " — локальная доменная сеть";
            foreach (RadioButton r in new[] { internet, domainNet })
            {
                r.AutoSize = true;
                r.ForeColor = Theme.Text;
                r.Margin = new Padding(0, 0, em, 0);
                r.CheckedChanged += delegate { UpdatePreview(); };
                networkRow.Controls.Add(r);
            }
            designation.Width = em * 16;
            designation.MaxLength = 64;
            designation.TextChanged += delegate { UpdatePreview(); };
            preview = Ui.Text("", Font, Theme.Accent);
            localUser.Width = domainUser.Width = em * 16;
            localPassword.Width = domainPassword.Width = em * 16;
            localPassword.UseSystemPasswordChar = domainPassword.UseSystemPasswordChar = true;
            Ui.SetCue(localUser, ".\\имя");
            Ui.SetCue(domainUser, "имя или ДОМЕН\\имя");
            form.Font = Font;
            form.Add("Назначение сети", networkRow);
            form.Add("Обозначение", designation);
            form.Add("Полное имя", preview);
            form.Add("Локальный администратор", localUser);
            form.Add("Его пароль", localPassword);
            form.Add("Доменная учётная запись", domainUser);
            form.Add("Пароль", domainPassword);

            apply = Ui.Primary("Применить", Glyph.Check, Font);
            apply.Click += delegate { Apply(); };
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(apply);
            actions.Dock = DockStyle.Top;
            actions.Padding = new Padding(0, em / 2, 0, 0);
            Label hint = Ui.Paragraph("Пароли нигде не сохраняются. Транслитерация выполняется автоматически: «отдел 5» → OTDEL-5.", Font, Theme.Muted);
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

        public override bool CanLeave()
        {
            if (running) Ui.Info(FindForm(), Title, "Дождитесь окончания операции.");
            return !running;
        }

        public override void Opened()
        {
            if (!running) Refresh(true);
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
            stateActions.Controls.Clear();
            editor.Visible = false;
            state.Set(Glyph.More, "Чтение состояния компьютера…", Theme.Muted);
            try
            {
                current = await Task.Run(delegate { return ComputerState.Read(); });
            }
            catch (Exception e) { state.Set(Glyph.Warning, "Не удалось прочитать состояние компьютера: " + e.Message, Theme.Bad); return; }
            ComputerState s = current;
            policy = ComputerPolicy.Load();
            now.Text = "Сейчас: " + s.Name + "\r\nДомен / рабочая группа: " + s.Domain + (s.PartOfDomain ? " (член домена)" : " (рабочая группа)") +
                (s.PendingName != null ? "\r\nПосле перезагрузки имя станет: " + s.PendingName : "");
            // COMPINFO_UI_PREVIEW shows the form without rights (screenshots); the change itself still checks real rights.
            bool admin = PowerShell.IsAdministrator() || Environment.GetEnvironmentVariable("COMPINFO_UI_PREVIEW") == "1";

            // Result of the previous change, after the restart (Confirm-ComputerWizardState).
            if (s.Receipt != null && s.RebootedSinceReceipt && admin && verify)
            {
                bool matches = s.ReceiptMatches;
                string channelError = "";
                if (matches && s.PartOfDomain)
                {
                    state.Set(Glyph.More, "Проверка связи с доменом…", Theme.Muted);
                    try { if (!await Task.Run(delegate { return ComputerChange.TestSecureChannel(); })) { matches = false; channelError = "Доверительный канал с доменом не подтверждён."; } }
                    catch (Exception e) { matches = false; channelError = e.Message; }
                }
                if (matches)
                {
                    ComputerChange.ClearReceipt();
                    status.Set(Glyph.Check, "Изменение после перезагрузки подтверждено. Соберите сведения о компьютере, чтобы обновить запись в базе.", Theme.Good);
                    Refresh(false);
                    return;
                }
                state.Set(Glyph.Warning, "Ожидаемое состояние не подтверждено: предыдущее действие могло выполниться частично.\r\n" +
                    "Ожидалось: " + s.ReceiptValue("Name") + "; " + s.ReceiptValue("Domain") + "; член домена: " + (s.ReceiptMember ? "да" : "нет") + "\r\n" +
                    "Сейчас: " + s.Name + "; " + s.Domain + "; член домена: " + (s.PartOfDomain ? "да" : "нет") +
                    (channelError.Length > 0 ? "\r\n" + channelError : ""), Theme.Warn);
                StateButton("Подтвердить и разрешить повторную настройку", Glyph.Check, false, delegate
                {
                    try { ComputerChange.ClearReceipt(); Refresh(false); }
                    catch (Exception e) { status.Set(Glyph.Warning, e.Message, Theme.Bad); }
                });
                return;
            }
            if (s.HasUnfinishedChange)
            {
                state.Set(Glyph.Warning, s.Receipt != null && s.RebootedSinceReceipt
                    ? "После перезагрузки нужно проверить результат предыдущего изменения: для этого нужны права администратора."
                    : "Есть незавершённое изменение. Сначала перезагрузите компьютер и откройте этот раздел для проверки.", Theme.Warn);
                if (s.Receipt != null && s.RebootedSinceReceipt && !admin) AdminButton();
                else StateButton("Перезагрузить сейчас", Glyph.Power, true, delegate { RestartComputer(); });
                return;
            }
            if (s.DomainRole >= 4) { state.Set(Glyph.Warning, "Изменения на контроллере домена не поддерживаются.", Theme.Bad); return; }
            if (policy.DomainName == null && (domainMode || s.PartOfDomain))
            {
                state.Set(Glyph.Warning, ComputerPolicy.NoDomain, Theme.Warn);
                StateButton("Открыть синхронизацию", Glyph.Sync, true, delegate { if (Main != null) Main.Open("sync"); });
                return;
            }
            if (domainMode) now.Text += "\r\nДомен подразделения: " + policy.DomainName;
            if (s.PartOfDomain && !string.Equals(s.Domain, policy.DomainName, StringComparison.OrdinalIgnoreCase))
            {
                state.Set(Glyph.Warning, "Компьютер состоит в другом домене. Автоматический перенос не поддерживается.", Theme.Bad);
                return;
            }
            if (!admin)
            {
                state.Set(Glyph.Shield, "Для изменения нужны права администратора. Программа перезапустится с ними и откроет этот раздел.", Theme.Info);
                AdminButton();
                return;
            }
            state.Clear();
            BuildEditor(s);
        }

        void AdminButton()
        {
            StateButton("Продолжить с правами администратора", Glyph.Shield, true, delegate { if (Main != null) Main.RestartAsAdministrator(Key); });
        }

        void BuildEditor(ComputerState s)
        {
            operation = domainMode ? (s.PartOfDomain ? "Leave" : "Join") : "Rename";
            bool named = operation != "Leave";
            form.Show(networkRow, operation == "Rename" && !s.PartOfDomain);
            form.Show(designation, named);
            form.Show(preview, named);
            form.Show(localUser, operation == "Leave");
            form.Show(localPassword, operation == "Leave");
            bool needsDomain = operation != "Rename" || s.PartOfDomain;
            form.Show(domainUser, needsDomain);
            form.Show(domainPassword, needsDomain);
            localPassword.Text = domainPassword.Text = "";
            if (operation == "Rename") { internet.Checked = !s.PartOfDomain && !s.Name.StartsWith(policy.DomainPrefix + "-"); domainNet.Checked = !internet.Checked; }
            else { domainNet.Checked = true; }
            if (named)
            {
                string ip = null;
                try { ip = ComputerSnapshot.MainIpAddress(); } catch (Exception) { }
                string suggested = ComputerNaming.Suggested(s.Name, Prefix, policy, null, ip);
                designation.Text = suggested.Substring((Prefix + "-" + policy.UnitCode + "-").Length);
            }
            apply.Text = operation == "Rename" ? " Переименовать" : operation == "Join" ? " Ввести в " + policy.DomainName : " Вывести из " + policy.DomainName;
            editor.Visible = true;
            UpdatePreview();
        }

        string Prefix { get { return domainNet.Checked || operation != "Rename" ? policy.DomainPrefix : policy.InternetPrefix; } }

        string FullName { get { return Prefix + "-" + policy.UnitCode + "-" + ComputerNaming.Designation(designation.Text); } }

        void UpdatePreview()
        {
            if (operation == null || operation == "Leave") { apply.Enabled = !running; return; }
            string name = FullName;
            string issue = ComputerNaming.Issue(name, Prefix, policy);
            preview.Text = name + (issue != null ? "   — " + issue : "");
            preview.ForeColor = issue != null ? Theme.Bad : Theme.Accent;
            preview.MaximumSize = new Size(Math.Max(Font.Height * 10, form.Width - Font.Height * 12), 0);
            apply.Enabled = issue == null && !running;
        }

        async void Apply()
        {
            ComputerState s = current;
            ComputerChange.Request r = new ComputerChange.Request();
            r.Operation = operation;
            r.Name = operation == "Leave" ? s.Name : FullName;
            r.LocalUser = localUser.Text.Trim();
            r.LocalPassword = localPassword.Text;
            r.DomainUser = domainUser.Text.Trim();
            r.DomainPassword = domainPassword.Text;
            if (operation == "Leave" && r.LocalUser.Length == 0) { status.Set(Glyph.Warning, "Укажите локального администратора: доменные профили не преобразуются в локальные.", Theme.Bad); return; }
            if ((operation != "Rename" || s.PartOfDomain) && r.DomainUser.Length == 0) { status.Set(Glyph.Warning, "Укажите доменную учётную запись с правом выполнения операции.", Theme.Bad); return; }
            string targetDomain = operation == "Join" ? policy.DomainName : operation == "Leave" ? policy.WorkgroupName : s.Domain;
            string action = operation == "Rename" ? "Переименование" : operation == "Join" ? "Ввод в домен" : "Вывод из домена";
            string question = action + "\n\nИмя: " + s.Name + " → " + r.Name + "\nДомен / рабочая группа: " + s.Domain + " → " + targetDomain +
                "\n\nПотребуется перезагрузка. Новое состояние будет проверено при следующем открытии раздела.";
            if (Ui.Ask(FindForm(), Title, question, Glyph.Warning, Theme.Warn, "Применить изменения", "Назад") != 0) return;

            running = true;
            apply.Enabled = false;
            status.Set(Glyph.More, operation == "Rename" && !s.PartOfDomain ? "Переименование…" : "Проверка домена и выполнение команды Windows…", Theme.Muted);
            string dataPath = AppPaths.Data;
            try
            {
                bool changed = await Task.Run(delegate
                {
                    if (r.Operation != "Leave") ComputerChange.AssertInventoryReady(dataPath);
                    return ComputerChange.Apply(r, policy);
                });
                if (!changed) { status.Set(Glyph.Check, "Компьютер уже имеет это имя. Изменения не требуются.", Theme.Good); return; }
                status.Set(Glyph.Check, "Windows подтвердила команду. Ожидаемое имя: " + r.Name + "; домен / группа: " + targetDomain +
                    ".\r\nПосле перезагрузки откройте этот раздел для проверки, затем соберите сведения о компьютере.", Theme.Good);
                if (Ui.Ask(FindForm(), Title, "Для завершения нужна перезагрузка компьютера.", Glyph.Power, Theme.Info, "Перезагрузить сейчас", "Позже") == 0) RestartComputer();
            }
            catch (Exception e)
            {
                status.Set(Glyph.Warning, e.Message + "\r\nЕсли команда Windows уже запускалась, результат может быть частичным. Перезагрузите компьютер и откройте раздел для проверки.", Theme.Bad);
            }
            finally
            {
                r.LocalPassword = r.DomainPassword = null;
                localPassword.Text = domainPassword.Text = "";
                running = false;
                Refresh(false);
            }
        }

        void RestartComputer()
        {
            try { ComputerChange.Restart(); }
            catch (Exception e) { Ui.Error(FindForm(), "Не удалось перезагрузить компьютер: " + e.Message); }
        }
    }
}
