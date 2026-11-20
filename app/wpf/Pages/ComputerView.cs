using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CompInfo.Wpf
{
    // «Имя компьютера» and «Домен» (ComputerPage of SystemPages.cs on WinForms; set-computer-name.ps1, manage-computer-domain.ps1).
    // Joining first sets a free office address from the database and gives the domain name prefix; leaving needs only
    // a local administrator, gives back the internet name prefix and turns the network back to DHCP.
    // The page opens and is filled without administrator rights; after «Применить» and the confirmation the program starts
    // again with them on this section and hands the whole form over (AdminHandoff), the new copy goes on by itself.
    // The change itself is ComputerChange of SystemChanges.cs, the same Windows PowerShell commands as the console version.
    public class ComputerView : ScrollViewer, ISection, Resumable
    {
        // Starts the program again with administrator rights on a section, handing over a form (null: none).
        public Action<string, JsonObject> RestartAsAdministrator;

        readonly bool domainMode;
        readonly string key;
        ComputerPolicy policy = ComputerPolicy.Load();
        readonly TextBlock now = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        readonly StatusLine state = new StatusLine { Margin = new Thickness(0, 0, 0, 10) };
        readonly StackPanel stateActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        readonly StackPanel editor = new StackPanel { Visibility = Visibility.Collapsed };
        readonly FormGrid form = new FormGrid();
        readonly RadioButton internet = new RadioButton { Margin = new Thickness(0, 0, 18, 0) }, domainNet = new RadioButton();
        readonly StackPanel networkRow = new StackPanel { Orientation = Orientation.Horizontal };
        readonly CheckBox renameOnLeave = new CheckBox { IsChecked = true };
        readonly TextBox designation = new TextBox { Width = 170, MaxLength = 64 };
        readonly TextBlock preview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
        // Joining: the address in the domain network.
        readonly ComboBox lanAdapter = new ComboBox { Width = 380 }, office = new ComboBox { Width = 380 }, segment = new ComboBox { Width = 100 };
        readonly StackPanel ipRow = new StackPanel { Orientation = Orientation.Horizontal }, syncRow = new StackPanel { Orientation = Orientation.Horizontal };
        readonly TextBlock ipStart = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 15 }, ipNote = new TextBlock { VerticalAlignment = VerticalAlignment.Center },
            syncInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, profilesNote = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly TextBox host = new TextBox { Width = 70, FontSize = 15, TextAlignment = TextAlignment.Center, MaxLength = 3, Margin = new Thickness(0, 0, 8, 0) };
        readonly Button nextFree = new Button { Margin = new Thickness(0, 0, 10, 0) }, syncNow = new Button { Margin = new Thickness(0, 0, 10, 0) };
        // Leaving: adapters with a static address go back to DHCP.
        readonly CheckBox dhcpOnLeave = new CheckBox { IsChecked = true };
        readonly TextBox localUser = new TextBox { Width = 220 }, domainUser = new TextBox { Width = 220 };
        readonly PasswordBox localPassword = new PasswordBox { Width = 220 }, domainPassword = new PasswordBox { Width = 220 };
        readonly CheckBox remember = new CheckBox();
        readonly Button apply = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        readonly StatusLine status = new StatusLine { Margin = new Thickness(0, 10, 0, 0) };
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

        string Title { get { return domainMode ? L.T("Домен") : L.T("Имя компьютера"); } }

        public ComputerView(bool domainMode)
        {
            this.domainMode = domainMode;
            key = domainMode ? "domain" : "name";
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            var page = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            Content = page;
            page.Children.Add(now);
            page.Children.Add(state);
            page.Children.Add(stateActions);
            page.Children.Add(editor);
            page.Children.Add(status);

            internet.Content = policy.InternetPrefix + L.T(" — интернет");
            domainNet.Content = policy.DomainPrefix + L.T(" — локальная доменная сеть");
            string group = Guid.NewGuid().ToString("N");
            foreach (RadioButton r in new[] { internet, domainNet })
            {
                r.GroupName = group;
                r.Checked += delegate { UpdatePreview(); };
                networkRow.Children.Add(r);
            }
            renameOnLeave.Content = L.T("Сменить префикс на ") + policy.InternetPrefix + L.T(" (интернет)");
            renameOnLeave.Click += delegate { if (!filling) UpdateRows(); };
            designation.TextChanged += delegate { if (!filling) designationEdited = true; UpdatePreview(); };
            System.Windows.Automation.AutomationProperties.SetName(designation, L.T("Обозначение"));

            lanAdapter.SelectionChanged += delegate { if (!filling) AdapterChanged(); };
            office.SelectionChanged += delegate { if (!filling) OfficeChanged(); };
            segment.SelectionChanged += delegate { if (!filling) PickFree(-1); };
            ipStart.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            host.SetResourceReference(FontFamilyProperty, "MonoFont");
            host.TextChanged += delegate { if (!filling) AddressChanged(); };
            host.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Up && e.Key != Key.Down) return;
                SetHost(Host + (e.Key == Key.Up ? 1 : -1));
                e.Handled = true;
            };
            Glyph.Label(nextFree, Glyph.Forward, L.T("Следующий свободный"));
            nextFree.Click += delegate { PickFree(Host); };
            ipRow.Children.Add(ipStart);
            ipRow.Children.Add(host);
            ipRow.Children.Add(nextFree);
            ipRow.Children.Add(ipNote);
            Glyph.Label(syncNow, Glyph.Sync, L.T("Синхронизировать базу"));
            syncNow.ToolTip = L.T("Свободный адрес выбирается по локальной копии базы. Синхронизируйте её, пока есть интернет, чтобы видеть адреса, выданные коллегами.");
            syncNow.Click += delegate { SyncDatabase(); };
            syncRow.Children.Add(syncNow);
            syncRow.Children.Add(syncInfo);
            profilesNote.Text = L.T("Профили сети не загружены: синхронизируйте базу или настройте сеть в разделе «Настройка сети».");
            profilesNote.SetResourceReference(TextBlock.ForegroundProperty, "Warn");
            dhcpOnLeave.Content = L.T("Переключить сеть на DHCP");
            dhcpOnLeave.Click += delegate { if (!filling) UpdatePreview(); };
            localUser.Tag = L.T(".\\имя");
            domainUser.Tag = L.T("имя или ДОМЕН\\имя");

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
            remember.Content = L.T("Запомнить в comp-info.ini");
            remember.ToolTip = L.T("Пароль хранится рядом с программой лишь замаскированным, это не шифрование: тот, кто получит флешку, получит и эту учётную запись.");
            form.Add("", remember);
            foreach (PasswordBox p in new[] { localPassword, domainPassword }) System.Windows.Automation.AutomationProperties.SetName(p, L.T("Пароль"));
            editor.Children.Add(form);

            apply.SetResourceReference(StyleProperty, "AccentButton");
            apply.Margin = new Thickness(0, 10, 0, 0);
            apply.Click += delegate { Apply(false); };
            editor.Children.Add(apply);
            var hint = new TextBlock { Text = Brand.Keep(L.T("Пароль локального администратора не сохраняется. Доменный — только если отмечено «Запомнить»: тогда он лежит в comp-info.ini лишь замаскированным, и флешку с ним нужно беречь. Транслитерация выполняется автоматически: «отдел 5» → OTDEL-5.")),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            editor.Children.Add(hint);
        }

        int Host { get { int n; return int.TryParse(host.Text, out n) ? n : 0; } }

        public bool CanLeave()
        {
            if (running || syncing) Dialog.Show(Window.GetWindow(this), L.T("Дождитесь окончания операции."), Severity.Info, Title);
            return !running && !syncing;
        }

        public void Resume(JsonObject form) { handoff = form; }

        public void Opened()
        {
            if (!running && !syncing) Refresh(true);
        }

        public bool Shortcut(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { if (editor.IsVisible && apply.IsEnabled) Apply(false); return true; }
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None) { if (!running && !syncing) Refresh(true); return true; }
            return false;
        }

        public void DevicesChanged() { }

        void StateButton(string text, string glyph, bool primary, Action click)
        {
            var b = new Button { Margin = new Thickness(0, 0, 8, 0) };
            Glyph.Label(b, glyph, text);
            if (primary) b.SetResourceReference(StyleProperty, "AccentButton");
            b.Click += delegate { click(); };
            stateActions.Children.Add(b);
        }

        async void Refresh(bool verify)
        {
            // A handed-over form is used once, and only if the page comes to the same operation.
            JsonObject resumed = handoff;
            handoff = null;
            stateActions.Children.Clear();
            editor.Visibility = Visibility.Collapsed;
            state.Note(Glyph.More, L.T("Чтение состояния компьютера…"));
            try { current = await Task.Run(() => ComputerState.Read()); }
            catch (Exception e) { state.Show(Severity.Error, Glyph.Warning, L.T("Не удалось прочитать состояние компьютера: ") + e.Message); return; }
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
                    state.Note(Glyph.More, L.T("Проверка связи с доменом…"));
                    try { if (!await Task.Run(() => ComputerChange.TestSecureChannel())) { matches = false; channelError = L.T("Доверительный канал с доменом не подтверждён."); } }
                    catch (Exception e) { matches = false; channelError = e.Message; }
                }
                if (matches)
                {
                    ComputerChange.ClearReceipt();
                    // The real values after the restart (the address from DHCP, for example) replace the expected ones.
                    state.Note(Glyph.More, L.T("Обновление записи в локальной базе…"));
                    bool saved = false;
                    string note = await Task.Run(() => RecordUpdate.Save(null, out saved));
                    status.Show(saved ? Severity.Success : Severity.Warning, Glyph.Check, L.T("Изменение после перезагрузки подтверждено.") + "\r\n" + note);
                    Refresh(false);
                    return;
                }
                state.Show(Severity.Warning, Glyph.Warning, L.T("Ожидаемое состояние не подтверждено: предыдущее действие могло выполниться частично.\r\n") +
                    L.T("Ожидалось: ") + s.ReceiptValue("Name") + "; " + s.ReceiptValue("Domain") + L.T("; член домена: ") + (s.ReceiptMember ? L.T("да") : L.T("нет")) + "\r\n" +
                    L.T("Сейчас: ") + s.Name + "; " + s.Domain + L.T("; член домена: ") + (s.PartOfDomain ? L.T("да") : L.T("нет")) +
                    (channelError.Length > 0 ? "\r\n" + channelError : ""));
                StateButton(L.T("Подтвердить и разрешить повторную настройку"), Glyph.Check, false, () =>
                {
                    try { ComputerChange.ClearReceipt(); Refresh(false); }
                    catch (Exception e) { status.Show(Severity.Error, Glyph.Warning, e.Message); }
                });
                return;
            }
            if (s.HasUnfinishedChange)
            {
                state.Show(Severity.Warning, Glyph.Warning, s.Receipt != null && s.RebootedSinceReceipt
                    ? L.T("После перезагрузки нужно проверить результат предыдущего изменения: для этого нужны права администратора.")
                    : L.T("Есть незавершённое изменение. Сначала перезагрузите компьютер и откройте этот раздел для проверки."));
                if (s.Receipt != null && s.RebootedSinceReceipt && !admin)
                    StateButton(L.T("Продолжить с правами администратора"), Glyph.Shield, true, () => { if (RestartAsAdministrator != null) RestartAsAdministrator(key, null); });
                else StateButton(L.T("Перезагрузить сейчас"), Glyph.Power, true, RestartComputer);
                return;
            }
            if (s.DomainRole >= 4) { state.Show(Severity.Error, Glyph.Warning, L.T("Изменения на контроллере домена не поддерживаются.")); return; }
            if (domainMode) now.Text += L.T("\r\nДомен подразделения: ") + policy.DomainName;
            if (s.PartOfDomain && !string.Equals(s.Domain, policy.DomainName, StringComparison.OrdinalIgnoreCase))
            {
                state.Show(Severity.Error, Glyph.Warning, L.T("Компьютер состоит в другом домене. Автоматический перенос не поддерживается."));
                return;
            }
            state.Clear();
            await BuildEditor(s);
            if (resumed != null && resumed.Text("page") == key && resumed.Text("operation") == operation) Fill(resumed);
        }

        async Task BuildEditor(ComputerState s)
        {
            operation = domainMode ? (s.PartOfDomain ? "Leave" : "Join") : "Rename";
            designationEdited = false;
            filling = true;
            localPassword.Password = "";
            // Remembered domain account (comp-info.ini), if the user chose to keep it.
            string savedUser, savedPassword;
            bool keep;
            DomainAccount.Load(out savedUser, out savedPassword, out keep);
            remember.IsChecked = keep;
            if (domainUser.Text.Length == 0) domainUser.Text = savedUser;
            domainPassword.Password = savedPassword;
            if (operation == "Rename") { internet.IsChecked = !s.PartOfDomain && !s.Name.StartsWith(policy.DomainPrefix + "-"); domainNet.IsChecked = internet.IsChecked != true; }
            else { domainNet.IsChecked = operation == "Join"; internet.IsChecked = operation != "Join"; }
            renameOnLeave.IsChecked = dhcpOnLeave.IsChecked = true;
            filling = false;
            AdminGlyph.Label(apply, operation == "Rename" ? L.T(" Переименовать").Trim() : operation == "Join" ? L.T(" Ввести в ").Trim() + " " + policy.DomainName : L.T(" Вывести из ").Trim() + " " + policy.DomainName);
            apply.ToolTip = "Ctrl+Enter";
            UpdateRows();
            editor.Visibility = Visibility.Visible;
            status.Clear();

            string ip = null;
            if (operation == "Join") await LoadJoinData();
            else if (operation == "Leave") await LoadLeaveData();
            if (operation != "Join") { try { ip = ComputerSnapshot.MainIpAddress(); } catch (Exception) { } }
            SuggestDesignation(ip ?? PlannedAddress());
            designationEdited = false;
            UpdatePreview();
            if (designation.IsVisible && operation != "Leave") designation.Focus();
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
            bool named = !leave || renameOnLeave.IsChecked == true;
            form.Show(networkRow, operation == "Rename" && current != null && !current.PartOfDomain);
            form.Show(renameOnLeave, leave);
            form.Show(designation, named);
            form.Show(preview, named);
            form.Show(syncRow, join);
            form.Show(profilesNote, join && profiles.Missing);
            bool lan = join && !profiles.Missing;
            form.Show(lanAdapter, lan);
            form.Show(office, lan);
            bool keepNetwork = lan && Office == null;
            form.Show(segment, lan && !keepNetwork);
            form.Show(ipRow, lan && !keepNetwork);
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
                staticAdapters = await Task.Run(() => LanSetup.StaticAdapters(out states));
                admin = await Task.Run(() => ComputerChange.DefaultLocalAdministrator());
            }
            catch (Exception e) { staticAdapters = new List<Adapter>(); status.Show(Severity.Warning, Glyph.Warning, L.T("Не удалось прочитать сетевые адаптеры: ") + e.Message); }
            var names = new List<string>();
            staticIps.Clear();
            for (int i = 0; i < staticAdapters.Count; i++)
            {
                bool hasStatic = states != null && states[i].Ip.Length > 0 && !states[i].Dhcp;
                names.Add(staticAdapters[i].Name + (hasStatic ? " (" + states[i].Ip + ")" : ""));
                if (hasStatic) staticIps.Add(states[i].Ip);
            }
            dhcpOnLeave.Content = L.T("Переключить на DHCP: ") + string.Join(", ", names);
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
                book = await Task.Run(() => AddressBook.Read(dataPath, OwnRecord(dataPath)));
                wired = await Task.Run(() =>
                {
                    List<Adapter> list = LanSetup.WiredAdapters();
                    var states = new Dictionary<int, LanSetup.State>();
                    foreach (Adapter a in list) { try { states[a.Index] = LanSetup.Read(a.Guid); } catch (Exception) { } }
                    lock (wiredStates) { wiredStates.Clear(); foreach (var p in states) wiredStates[p.Key] = p.Value; }
                    return list;
                });
            }
            catch (Exception e) { status.Show(Severity.Warning, Glyph.Warning, L.T("Не удалось прочитать базу или сетевые адаптеры: ") + e.Message); }

            filling = true;
            office.Items.Clear();
            foreach (NetworkProfiles.Location l in profiles.Locations) office.Items.Add(new Choice { Text = l.Name, Value = l });
            office.Items.Add(new Choice { Text = L.T("— не менять настройки сети"), Value = KeepNetwork });
            lanAdapter.Items.Clear();
            int select = 0;
            foreach (Adapter a in wired)
            {
                bool good, bad;
                LanSetup.State st = StateOf(a);
                // Prefer the adapter that already has an office address.
                NetworkProfiles.Location l; int seg, last;
                if (st != null && !st.Dhcp && profiles.Match(st.Ip, out l, out seg, out last) && select == 0 && lanAdapter.Items.Count > 0) select = lanAdapter.Items.Count;
                lanAdapter.Items.Add(new Choice { Text = a.Name + " — " + LanSetup.StatusText(a, out good, out bad) + "  (" + a.Description + ")", Value = a });
            }
            if (lanAdapter.Items.Count > 0) lanAdapter.SelectedIndex = select;
            filling = false;
            if (wired.Count == 0 && !profiles.Missing) status.Show(Severity.Error, Glyph.Warning, L.T("Проводные сетевые адаптеры не найдены."));
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
            syncInfo.SetResourceReference(TextBlock.ForegroundProperty, old ? "Warn" : "Muted");
        }

        Adapter SelectedAdapter { get { var c = lanAdapter.SelectedItem as Choice; return c == null ? null : (Adapter)c.Value; } }

        LanSetup.State StateOf(Adapter a)
        {
            LanSetup.State s;
            lock (wiredStates) return a != null && wiredStates.TryGetValue(a.Index, out s) ? s : null;
        }

        NetworkProfiles.Location Office { get { var c = office.SelectedItem as Choice; return c == null ? null : c.Value as NetworkProfiles.Location; } }

        int? Segment { get { var c = segment.SelectedItem as Choice; return c == null ? (int?)null : (int)c.Value; } }

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
            foreach (Choice i in office.Items) if (ReferenceEquals(i.Value, l)) office.SelectedItem = i;
            FillSegments(seg);
            filling = false;
        }

        int hostMin, hostMax = 255;

        void FillSegments(int keep)
        {
            NetworkProfiles.Location l = Office;
            int? previous = keep >= 0 ? keep : Segment;
            segment.Items.Clear();
            if (l == null) return;
            foreach (int s in l.Segments ?? profiles.Segments)
            {
                segment.Items.Add(new Choice { Text = s.ToString(), Value = s });
                if (previous.HasValue && previous.Value == s) segment.SelectedIndex = segment.Items.Count - 1;
            }
            if (segment.SelectedIndex < 0 && segment.Items.Count > 0) segment.SelectedIndex = 0;
            hostMin = l.FirstHostOctet;
            hostMax = Math.Max(l.FirstHostOctet, l.LastHostOctet);
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
            host.Text = Math.Max(hostMin, Math.Min(hostMax, value)).ToString();
            filling = false;
            AddressChanged();
        }

        string PlannedAddress()
        {
            NetworkProfiles.Location l = Office;
            if (operation != "Join" || l == null || !Segment.HasValue) return null;
            return profiles.Address(l, Segment.Value, Host);
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
            if (Host < hostMin || Host > hostMax) ipNote.Text = L.T("Введите последний октет от ") + hostMin + L.T(" до ") + hostMax + ".";
            else if (owner != null) ipNote.Text = L.T("занят: ") + owner;
            else if (takenInNetwork.Contains(ip)) ipNote.Text = L.T("занят в сети");
            else if (st != null && !st.Dhcp && st.Ip == ip) { ipNote.Text = L.T("текущий адрес ПК"); good = true; }
            else if (book.FirstFree(profiles, l, Segment.Value, -1, takenInNetwork) < 0) ipNote.Text = L.T("свободных нет");
            else { ipNote.Text = L.T("свободен по базе"); good = true; }
            ipNote.SetResourceReference(TextBlock.ForegroundProperty, good ? "Good" : "Bad");
            // A name made from the address follows it until the technician types a designation.
            if (!designationEdited && current != null && !System.Text.RegularExpressions.Regex.IsMatch(current.Name, "^[A-Z]{2,3}-\\d{3}-.+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                SuggestDesignation(ip);
            UpdatePreview();
        }

        async void SyncDatabase()
        {
            if (syncing || running) return;
            syncing = true;
            syncNow.IsEnabled = apply.IsEnabled = false;
            status.Note(Glyph.More, L.T("Подключение к облаку…"));
            try
            {
                Window owner = Window.GetWindow(this);
                CloudClient client = await CloudConnect.Connect(owner);
                if (client == null) status.Show(Severity.Warning, Glyph.Info, L.T("Синхронизация отменена: ключ не введён."));
                else
                {
                    var sync = new CloudSync(AppPaths.Root, client, new SyncQuestions(owner, text => status.Note(Glyph.More, text)));
                    SyncCounts c = await Task.Run(() => sync.Run());
                    if (c.Errors.Count == 0) status.Show(Severity.Success, Glyph.Check, L.T("База синхронизирована: получено новых ") + c.ReceivedNew + L.T(", обновлено ") + c.ReceivedUpdated + L.T(". Адрес выбран заново."));
                    else status.Show(Severity.Warning, Glyph.Warning, L.T("Синхронизация завершена с ошибками (") + c.Errors.Count + "): " + c.Errors[0]);
                }
            }
            catch (Exception e) { status.Show(Severity.Error, Glyph.Warning, L.T("Синхронизация не завершена: ") + e.Message); }
            finally
            {
                syncing = false;
                syncNow.IsEnabled = true;
            }
            if (operation == "Join" && !running) await LoadJoinData();
            UpdatePreview();
        }

        // ---------- name, apply ----------

        string Prefix { get { return operation == "Leave" ? policy.InternetPrefix : domainNet.IsChecked == true || operation != "Rename" ? policy.DomainPrefix : policy.InternetPrefix; } }

        string FullName { get { return Prefix + "-" + policy.UnitCode + "-" + ComputerNaming.Designation(designation.Text); } }

        bool Renames { get { return operation != "Leave" || renameOnLeave.IsChecked == true; } }

        void UpdatePreview()
        {
            if (operation == null) return;
            string issue = null;
            if (Renames)
            {
                string name = FullName;
                issue = ComputerNaming.Issue(name, Prefix, policy);
                preview.Text = name + (issue != null ? "   — " + issue : "");
                preview.SetResourceReference(TextBlock.ForegroundProperty, issue != null ? "Bad" : "Accent");
            }
            bool hostOk = Host >= hostMin && Host <= hostMax;
            bool networkReady = operation != "Join" || profiles.Missing || Office == null || (SelectedAdapter != null && Segment.HasValue && hostOk);
            apply.IsEnabled = issue == null && networkReady && !running && !syncing;
        }

        ComputerChange.NetworkPlan NetworkPlan()
        {
            NetworkProfiles.Location l = Office;
            Adapter a = SelectedAdapter;
            if (operation != "Join" || profiles.Missing || l == null || a == null || !Segment.HasValue) return null;
            return new ComputerChange.NetworkPlan
            {
                AdapterIndex = a.Index, AdapterName = a.Name, AdapterGuid = a.Guid, Ip = PlannedAddress(), Mask = l.SubnetMask,
                PrefixLength = l.PrefixLength, Gateway = profiles.Gateway(l, Segment.Value), Dns = new List<string>(l.DnsServers),
            };
        }

        // Everything typed and chosen, for the copy started with administrator rights.
        JsonObject FormData()
        {
            var o = new JsonObject();
            o["page"] = key;
            o["operation"] = operation;
            o["designation"] = designation.Text;
            o["internet"] = internet.IsChecked == true;
            o["renameOnLeave"] = renameOnLeave.IsChecked == true;
            o["dhcpOnLeave"] = dhcpOnLeave.IsChecked == true;
            o["localUser"] = localUser.Text;
            o["localPassword"] = localPassword.Password;
            o["domainUser"] = domainUser.Text;
            o["domainPassword"] = domainPassword.Password;
            o["remember"] = remember.IsChecked == true;
            Adapter a = SelectedAdapter;
            o["adapter"] = a != null ? a.Guid : null;
            o["office"] = Office != null ? Office.Name : office.SelectedItem != null ? KeepNetwork : null;
            o["segment"] = Segment.HasValue ? (object)Segment.Value : null;
            o["host"] = Host;
            return o;
        }

        // Puts the handed-over form back and goes on with the change without asking again.
        void Fill(JsonObject o)
        {
            filling = true;
            if (o["internet"] is bool) { internet.IsChecked = (bool)o["internet"]; domainNet.IsChecked = internet.IsChecked != true; }
            if (o["renameOnLeave"] is bool) renameOnLeave.IsChecked = (bool)o["renameOnLeave"];
            if (o["dhcpOnLeave"] is bool) dhcpOnLeave.IsChecked = (bool)o["dhcpOnLeave"];
            designation.Text = o.Text("designation") ?? "";
            localUser.Text = o.Text("localUser") ?? "";
            localPassword.Password = o.Text("localPassword") ?? "";
            domainUser.Text = o.Text("domainUser") ?? "";
            domainPassword.Password = o.Text("domainPassword") ?? "";
            if (o["remember"] is bool) remember.IsChecked = (bool)o["remember"];
            filling = false;
            designationEdited = true;
            if (operation == "Join" && !profiles.Missing)
            {
                foreach (Choice i in lanAdapter.Items)
                    if (((Adapter)i.Value).Guid == o.Text("adapter")) { filling = true; lanAdapter.SelectedItem = i; filling = false; }
                string officeName = o.Text("office");
                if (officeName == KeepNetwork)
                {
                    filling = true;
                    foreach (Choice i in office.Items) if (KeepNetwork.Equals(i.Value)) office.SelectedItem = i;
                    filling = false;
                }
                else
                {
                    NetworkProfiles.Location l = profiles.Locations.Find(x => x.Name == officeName);
                    int seg;
                    if (l != null && int.TryParse(o.Text("segment"), out seg)) SelectOffice(l, seg);
                    int last;
                    if (int.TryParse(o.Text("host"), out last)) SetHost(last);
                }
            }
            UpdateRows();
            if (apply.IsEnabled) Apply(true);
        }

        async void Apply(bool confirmed)
        {
            ComputerState s = current;
            Window owner = Window.GetWindow(this);
            var r = new ComputerChange.Request
            {
                Operation = operation,
                Name = operation == "Leave" && renameOnLeave.IsChecked != true ? s.Name : FullName,
                LocalUser = localUser.Text.Trim(), LocalPassword = localPassword.Password,
                DomainUser = domainUser.Text.Trim(), DomainPassword = domainPassword.Password,
                Network = NetworkPlan(),
            };
            if (operation == "Leave" && dhcpOnLeave.IsChecked == true) r.DhcpAdapters = new List<Adapter>(staticAdapters);
            bool needsDomain = operation == "Join" || (operation == "Rename" && s.PartOfDomain);
            if (needsDomain) DomainAccount.Save(r.DomainUser, r.DomainPassword, remember.IsChecked == true);
            if (operation == "Leave" && r.LocalUser.Length == 0) { status.Show(Severity.Error, Glyph.Warning, L.T("Укажите локального администратора: доменные профили не преобразуются в локальные.")); return; }
            if (needsDomain && r.DomainUser.Length == 0) { status.Show(Severity.Error, Glyph.Warning, L.T("Укажите доменную учётную запись с правом выполнения операции.")); return; }
            string targetDomain = operation == "Join" ? policy.DomainName : operation == "Leave" ? policy.WorkgroupName : s.Domain;
            if (!confirmed)
            {
                string action = operation == "Rename" ? L.T("Переименование") : operation == "Join" ? L.T("Ввод в домен") : L.T("Вывод из домена");
                string question = action + L.T("\n\nИмя: ") + s.Name + " → " + r.Name + L.T("\nДомен / рабочая группа: ") + s.Domain + " → " + targetDomain;
                if (r.Network != null)
                {
                    string addressOwner = book.Owner(r.Network.Ip);
                    question += L.T("\nСеть: «") + r.Network.AdapterName + "» → " + r.Network.Ip + "/" + r.Network.PrefixLength + L.T(", шлюз ") + r.Network.Gateway +
                        (r.Network.Dns.Count > 0 ? ", DNS " + string.Join(", ", r.Network.Dns) : "") +
                        (addressOwner != null ? L.T("\nВнимание: в базе этот адрес у ") + addressOwner + "." : "");
                }
                if (r.DhcpAdapters.Count > 0)
                {
                    var names = new List<string>();
                    foreach (Adapter a in r.DhcpAdapters) names.Add(a.Name);
                    question += L.T("\nСеть: ") + string.Join(", ", names) + L.T(" → автоматически (DHCP)");
                }
                if (operation == "Leave") question += L.T("\n\nДоменная учётная запись не нужна: объект компьютера в домене останется как есть.");
                question += L.T("\n\nПотребуется перезагрузка. Новое состояние будет проверено при следующем открытии раздела.");
                if (!Dialog.Ask(owner, question, L.T("Применить изменения"), L.T("Назад"), Severity.Warning, Title)) return;
            }

            // Viewing and filling the form does not need UAC. Ask only after the user has confirmed a real change;
            // the new copy gets the whole form and goes on by itself.
            if (!PowerShell.IsAdministrator()) { if (RestartAsAdministrator != null) RestartAsAdministrator(key, FormData()); return; }

            running = true;
            apply.IsEnabled = syncNow.IsEnabled = false;
            status.Note(Glyph.More, L.T("Выполнение…"));
            r.Progress = text => Dispatcher.BeginInvoke(new Action(() => status.Note(Glyph.More, text)));
            string dataPath = AppPaths.Data;
            ComputerPolicy p = policy;
            bool refresh = true;
            try
            {
                bool changed = await Task.Run(() =>
                {
                    if (r.Operation != "Leave" || r.Name != s.Name) ComputerChange.AssertInventoryReady(dataPath);
                    return ComputerChange.Apply(r, p);
                });
                if (!changed) { status.Show(Severity.Success, Glyph.Check, L.T("Компьютер уже имеет это имя. Изменения не требуются.")); return; }
                // The record gets the values that will be there after the restart: name, domain, address.
                status.Note(Glyph.More, L.T("Обновление записи в локальной базе…"));
                var expected = new Collection.Expected { Hostname = r.Name };
                if (r.Operation != "Rename") { expected.PartOfDomain = r.Operation == "Join"; expected.Domain = targetDomain; }
                if (r.Network != null) { expected.SetIp = true; expected.Ip = r.Network.Ip; }
                if (r.DhcpAdapters.Count > 0) expected.OldIps = new List<string>(staticIps);
                bool saved = false;
                string note = await Task.Run(() => RecordUpdate.Save(expected, out saved));
                if (!saved) r.Warnings.Add(note);
                status.Show(r.Warnings.Count > 0 ? Severity.Warning : Severity.Success, r.Warnings.Count > 0 ? Glyph.Warning : Glyph.Check,
                    L.T("Windows подтвердила команду. Ожидаемое имя: ") + r.Name + L.T("; домен / группа: ") + targetDomain +
                    (r.Network != null ? L.T("; адрес ") + r.Network.Ip : "") +
                    L.T(".\r\nПосле перезагрузки откройте этот раздел для проверки: запись в базе обновится ещё раз уже по факту.") +
                    (saved ? "\r\n" + note : "") + (r.Warnings.Count > 0 ? "\r\n" + string.Join("\r\n", r.Warnings) : ""));
                if (Dialog.Ask(owner, L.T("Для завершения нужна перезагрузка компьютера."), L.T("Перезагрузить сейчас"), L.T("Позже"), Severity.Info, Title)) RestartComputer();
            }
            catch (Exception e)
            {
                // Windows found the new address in use: remember it and offer the next free one.
                if (e is ComputerChange.AddressInUseException)
                {
                    takenInNetwork.Add(r.Network.Ip);
                    refresh = false;
                    PickFree(Host);
                }
                status.Show(Severity.Error, Glyph.Warning, e.Message + L.T("\r\nЕсли команда Windows уже запускалась, результат может быть частичным. Перезагрузите компьютер и откройте раздел для проверки."));
            }
            finally
            {
                r.LocalPassword = r.DomainPassword = null;
                localPassword.Password = "";
                running = false;
                syncNow.IsEnabled = true;
                if (refresh) Refresh(false); else UpdatePreview();
            }
        }

        void RestartComputer()
        {
            try { ComputerChange.Restart(); }
            catch (Exception e) { Dialog.Show(Window.GetWindow(this), L.T("Не удалось перезагрузить компьютер: ") + e.Message); }
        }
    }
}
