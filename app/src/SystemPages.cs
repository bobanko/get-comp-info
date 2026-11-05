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
            Title = L.T("Настройка сети");
            Description = L.T("IP-адрес, маска, шлюз и DNS проводного адаптера по профилям корпусов из облака, или автоматическая настройка (DHCP).");
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
            form.Add(L.T("Сеть"), network);
            form.Add(L.T("Сегмент"), segment);
            form.Add(L.T("Адаптер"), adapter);
            form.Add(L.T("Последний октет"), octetRow);
            network.SelectedIndexChanged += delegate { NetworkChanged(); };
            segment.SelectedIndexChanged += delegate { UpdateSummary(); };
            adapter.SelectedIndexChanged += delegate { UpdateSummary(); };
            octet.TextChanged += delegate { UpdateSummary(); };
            octet.KeyPress += delegate (object o, KeyPressEventArgs k) { if (!char.IsDigit(k.KeyChar) && !char.IsControl(k.KeyChar)) k.Handled = true; };

            summary = Ui.Paragraph("", Font, Theme.Accent);
            summary.Padding = new Padding(0, em / 2, 0, em / 2);
            apply = Ui.Primary(L.T("Применить настройки"), PowerShell.IsAdministrator() ? Glyph.Check : Glyph.Shield, Font);
            apply.Click += delegate { Apply(); };
            Ui.Keys(apply, "Ctrl+Enter");
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
            network.Focus();
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
            dhcp.Text = L.T("Интернет — автоматически (DHCP)");
            network.Items.Add(dhcp);
            network.SelectedIndex = 0;
            for (int i = 0; i < network.Items.Count; i++) if (((Item)network.Items[i]).Text == (string)selected) network.SelectedIndex = i;
            if (profiles.Missing)
                profilesNote.Set(Glyph.Warning, L.T("Профили корпусов не загружены: синхронизируйтесь с облаком, когда будет интернет. Сейчас доступна только автоматическая настройка."), Theme.Warn);
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
                if (adapters.Count == 0) status.Set(Glyph.Warning, L.T("Проводные сетевые адаптеры не найдены."), Theme.Bad);
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось получить список адаптеров: ") + e.Message, Theme.Bad); }
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
                range.Text = L.T("от ") + l.FirstHostOctet + L.T(" до ") + l.LastHostOctet;
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
            if (adapter.SelectedItem == null) { error = L.T("Выберите адаптер."); return false; }
            if (l == null) return true;
            if (segment.SelectedItem == null) { error = L.T("Выберите сегмент."); return false; }
            int last;
            if (!int.TryParse(octet.Text, out last) || last < l.FirstHostOctet || last > l.LastHostOctet)
            {
                error = L.T("Введите последний октет от ") + l.FirstHostOctet + L.T(" до ") + l.LastHostOctet + ".";
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
                summary.Text = L.T("IP-адрес, маска, шлюз и DNS — автоматически от роутера (DHCP).\r\nСтатические IPv4-адреса, шлюз и вручную заданные DNS будут сброшены.");
                return;
            }
            List<string> lines = new List<string>();
            lines.Add(L.T("IP-адрес:  ") + ip);
            lines.Add(L.T("Маска:  ") + l.SubnetMask);
            lines.Add(L.T("Шлюз:  ") + gateway);
            for (int i = 0; i < l.DnsServers.Count; i++) lines.Add("DNS " + (i + 1) + ":  " + l.DnsServers[i]);
            summary.Text = string.Join("\r\n", lines);
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
            applied.Text = "";
            status.Set(Glyph.More, L.T("Применение настроек…"), Theme.Muted);
            try
            {
                int prefixLength = l == null ? 0 : l.PrefixLength;
                List<string> dns = l == null ? new List<string>() : l.DnsServers;
                await Task.Run(delegate { LanSetup.Apply(a.Index, l == null, ip, prefixLength, gateway, dns); });
                status.Set(Glyph.Check, L.T("Настройки сети применены.") + (l == null ? L.T(" Получение настроек от роутера может занять некоторое время.") : ""), Theme.Good);
                List<string[]> now = await Task.Run(delegate { return LanSetup.Current(a.Index); });
                List<string> lines = new List<string> { L.T("Сейчас на адаптере «") + a.Name + "»:" };
                foreach (string[] line in now) lines.Add("   " + line[0] + ":  " + line[1]);
                applied.Text = string.Join("\r\n", lines);
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
        readonly CheckBox remember = new CheckBox();
        readonly Button apply;
        readonly StatusLine status = new StatusLine();
        ComputerState current;
        string operation;   // Rename, Join, Leave
        bool running;

        public ComputerPage(bool domainMode)
        {
            this.domainMode = domainMode;
            Key = domainMode ? "domain" : "name";
            Title = domainMode ? L.T("Домен") : L.T("Имя компьютера");
            Description = domainMode ? L.T("Ввод компьютера в домен подразделения и вывод из него. Домен берётся из профилей сети в облаке. После изменения нужна перезагрузка.")
                : L.T("Переименование по правилам: ") + policy.InternetPrefix + "-" + policy.UnitCode + L.T("-… для интернета, ") + policy.DomainPrefix + "-" + policy.UnitCode + L.T("-… для доменной сети. После изменения нужна перезагрузка.");
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
            designation.Width = em * 16;
            designation.MaxLength = 64;
            designation.TextChanged += delegate { UpdatePreview(); };
            preview = Ui.Text("", Font, Theme.Accent);
            localUser.Width = domainUser.Width = em * 16;
            localPassword.Width = domainPassword.Width = em * 16;
            localPassword.UseSystemPasswordChar = domainPassword.UseSystemPasswordChar = true;
            Ui.SetCue(localUser, L.T(".\\имя"));
            Ui.SetCue(domainUser, L.T("имя или ДОМЕН\\имя"));
            form.Font = Font;
            form.Add(L.T("Назначение сети"), networkRow);
            form.Add(L.T("Обозначение"), designation);
            form.Add(L.T("Полное имя"), preview);
            form.Add(L.T("Локальный администратор"), localUser);
            form.Add(L.T("Его пароль"), localPassword);
            form.Add(L.T("Доменная учётная запись"), domainUser);
            form.Add(L.T("Пароль"), domainPassword);
            remember.Text = L.T("Запомнить в comp-info.ini");
            remember.AutoSize = true;
            remember.ForeColor = Theme.Text;
            ToolTip rememberTip = new ToolTip();
            rememberTip.SetToolTip(remember, L.T("Пароль хранится рядом с программой лишь замаскированным, это не шифрование: тот, кто получит флешку, получит и эту учётную запись."));
            form.Add("", remember);

            apply = Ui.Primary(L.T("Применить"), Glyph.Check, Font);
            apply.Click += delegate { Apply(); };
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

        public override bool CanLeave()
        {
            if (running) Ui.Info(FindForm(), Title, L.T("Дождитесь окончания операции."));
            return !running;
        }

        public override void Opened()
        {
            if (!running) Refresh(true);
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == (Keys.Control | Keys.Enter)) { if (editor.Visible && apply.Enabled) Apply(); return true; }
            if (keys == Keys.F5) { if (!running) Refresh(true); return true; }
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
                    status.Set(Glyph.Check, L.T("Изменение после перезагрузки подтверждено. Соберите сведения о компьютере, чтобы обновить запись в базе."), Theme.Good);
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
            if (policy.DomainName == null && (domainMode || s.PartOfDomain))
            {
                state.Set(Glyph.Warning, ComputerPolicy.NoDomain, Theme.Warn);
                StateButton(L.T("Открыть синхронизацию"), Glyph.Sync, true, delegate { if (Main != null) Main.Open("sync"); });
                return;
            }
            if (domainMode) now.Text += L.T("\r\nДомен подразделения: ") + policy.DomainName;
            if (s.PartOfDomain && !string.Equals(s.Domain, policy.DomainName, StringComparison.OrdinalIgnoreCase))
            {
                state.Set(Glyph.Warning, L.T("Компьютер состоит в другом домене. Автоматический перенос не поддерживается."), Theme.Bad);
                return;
            }
            if (!admin)
            {
                state.Set(Glyph.Shield, L.T("Для изменения нужны права администратора. Программа перезапустится с ними и откроет этот раздел."), Theme.Info);
                AdminButton();
                return;
            }
            state.Clear();
            BuildEditor(s);
        }

        void AdminButton()
        {
            StateButton(L.T("Продолжить с правами администратора"), Glyph.Shield, true, delegate { if (Main != null) Main.RestartAsAdministrator(Key); });
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
            form.Show(remember, needsDomain);
            localPassword.Text = "";
            // Remembered domain account (comp-info.ini), if the user chose to keep it.
            DomainCredentials.Load(domainUser, domainPassword, remember);
            if (operation == "Rename") { internet.Checked = !s.PartOfDomain && !s.Name.StartsWith(policy.DomainPrefix + "-"); domainNet.Checked = !internet.Checked; }
            else { domainNet.Checked = true; }
            if (named)
            {
                string ip = null;
                try { ip = ComputerSnapshot.MainIpAddress(); } catch (Exception) { }
                string suggested = ComputerNaming.Suggested(s.Name, Prefix, policy, null, ip);
                designation.Text = suggested.Substring((Prefix + "-" + policy.UnitCode + "-").Length);
            }
            apply.Text = operation == "Rename" ? L.T(" Переименовать") : operation == "Join" ? L.T(" Ввести в ") + policy.DomainName : L.T(" Вывести из ") + policy.DomainName;
            editor.Visible = true;
            UpdatePreview();
            if (designation.Visible) designation.Focus(); else localUser.Focus();
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
            if (operation != "Rename" || s.PartOfDomain) DomainCredentials.Save(r.DomainUser, r.DomainPassword, remember.Checked);
            if (operation == "Leave" && r.LocalUser.Length == 0) { status.Set(Glyph.Warning, L.T("Укажите локального администратора: доменные профили не преобразуются в локальные."), Theme.Bad); return; }
            if ((operation != "Rename" || s.PartOfDomain) && r.DomainUser.Length == 0) { status.Set(Glyph.Warning, L.T("Укажите доменную учётную запись с правом выполнения операции."), Theme.Bad); return; }
            string targetDomain = operation == "Join" ? policy.DomainName : operation == "Leave" ? policy.WorkgroupName : s.Domain;
            string action = operation == "Rename" ? L.T("Переименование") : operation == "Join" ? L.T("Ввод в домен") : L.T("Вывод из домена");
            string question = action + L.T("\n\nИмя: ") + s.Name + " → " + r.Name + L.T("\nДомен / рабочая группа: ") + s.Domain + " → " + targetDomain +
                L.T("\n\nПотребуется перезагрузка. Новое состояние будет проверено при следующем открытии раздела.");
            if (Ui.Ask(FindForm(), Title, question, Glyph.Warning, Theme.Warn, L.T("Применить изменения"), L.T("Назад")) != 0) return;

            running = true;
            apply.Enabled = false;
            status.Set(Glyph.More, operation == "Rename" && !s.PartOfDomain ? L.T("Переименование…") : L.T("Проверка домена и выполнение команды Windows…"), Theme.Muted);
            string dataPath = AppPaths.Data;
            try
            {
                bool changed = await Task.Run(delegate
                {
                    if (r.Operation != "Leave") ComputerChange.AssertInventoryReady(dataPath);
                    return ComputerChange.Apply(r, policy);
                });
                if (!changed) { status.Set(Glyph.Check, L.T("Компьютер уже имеет это имя. Изменения не требуются."), Theme.Good); return; }
                status.Set(Glyph.Check, L.T("Windows подтвердила команду. Ожидаемое имя: ") + r.Name + L.T("; домен / группа: ") + targetDomain +
                    L.T(".\r\nПосле перезагрузки откройте этот раздел для проверки, затем соберите сведения о компьютере."), Theme.Good);
                if (Ui.Ask(FindForm(), Title, L.T("Для завершения нужна перезагрузка компьютера."), Glyph.Power, Theme.Info, L.T("Перезагрузить сейчас"), L.T("Позже")) == 0) RestartComputer();
            }
            catch (Exception e)
            {
                status.Set(Glyph.Warning, e.Message + L.T("\r\nЕсли команда Windows уже запускалась, результат может быть частичным. Перезагрузите компьютер и откройте раздел для проверки."), Theme.Bad);
            }
            finally
            {
                r.LocalPassword = r.DomainPassword = null;
                localPassword.Text = "";
                running = false;
                Refresh(false);
            }
        }

        void RestartComputer()
        {
            try { ComputerChange.Restart(); }
            catch (Exception e) { Ui.Error(FindForm(), L.T("Не удалось перезагрузить компьютер: ") + e.Message); }
        }
    }
}
