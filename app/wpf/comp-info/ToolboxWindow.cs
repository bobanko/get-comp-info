using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CompInfo.Wpf
{
    // A section of the toolbox: its place in the menu and its page, built when first opened.
    public class Section
    {
        public string Key, Title, NavTitle, Description, Icon, IconKey;
        public bool NeedsAdmin, NeedsInternet, Bottom;
        public Func<FrameworkElement> Create;
        FrameworkElement view;

        public FrameworkElement View { get { return view ?? (view = Create()); } }
        public ISection Page { get { return View as ISection; } }
    }

    // The main window of the toolbox (MainForm.cs on WinForms): the brand and the section menu on the left,
    // the title, description and page of the open section on the right.
    // Keys: Ctrl+1…9 the sections, Ctrl+0 «О программе», Ctrl+Tab / Ctrl+PageDown the next one, Ctrl+Shift+Tab /
    // Ctrl+PageUp the previous one; everything else (F5, Ctrl+S, Ctrl+F…) goes to the open section.
    public class ToolboxWindow : Window
    {
        // Colour-blind mode: the brand is black and white. A click on the brand switches it.
        public static bool ColorBlind;

        // Closed to be replaced by a new window (language, colour-blind mode): the program goes on.
        public bool Replaced;

        readonly List<Section> sections;
        readonly Dictionary<Section, Button> items = new Dictionary<Section, Button>();
        readonly TextBlock title = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold };
        readonly TextBlock description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 2, 0, 12) };
        readonly ContentControl host = new ContentControl { Focusable = false };
        Section current;

        public ToolboxWindow(string startPage)
        {
            Title = L.T("CHAGEAR Тулбокс") + " " + AppVersion.Text + (PowerShell.IsAdministrator() ? " — " + L.T("администратор") : "");
            Width = 1040; Height = 660;
            MinWidth = 815; MinHeight = 575;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");

            sections = Sections(this);
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            Content = root;

            // Left: brand, version and the sections; the ones that are also separate programs and «О программе» at the bottom.
            var menu = new DockPanel { Margin = new Thickness(8, 12, 8, 10) };
            var side = new Border { Child = menu, BorderThickness = new Thickness(0, 0, 1, 0) };
            side.SetResourceReference(Border.BackgroundProperty, "Surface");
            side.SetResourceReference(Border.BorderBrushProperty, "Border");
            root.Children.Add(side);
            var brand = Brand();
            DockPanel.SetDock(brand, Dock.Top);
            menu.Children.Add(brand);
            var top = new StackPanel();
            var bottom = new StackPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            menu.Children.Add(bottom);
            menu.Children.Add(top);
            for (int i = 0; i < sections.Count; i++)
            {
                Section s = sections[i];
                var item = NavItem(s, s.Key == "about" ? "Ctrl+0" : i < 9 ? "Ctrl+" + (i + 1) : null);
                items[s] = item;
                (s.Bottom ? bottom : top).Children.Add(item);
            }

            // Right: the title and description of the section, the section itself.
            var right = new DockPanel { Margin = new Thickness(20, 14, 18, 14) };
            Grid.SetColumn(right, 1);
            System.Windows.Automation.AutomationProperties.SetAutomationId(title, "SectionTitle");
            DockPanel.SetDock(title, Dock.Top);
            DockPanel.SetDock(description, Dock.Top);
            description.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            right.Children.Add(title);
            right.Children.Add(description);
            right.Children.Add(host);
            root.Children.Add(right);

            WpfTheme.DarkTitle(this);
            PreviewKeyDown += Keys;
            Closing += (s, e) => { if (!Replaced && current != null && current.Page != null && !current.Page.CanLeave()) e.Cancel = true; };
            SourceInitialized += delegate
            {
                HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (source != null) source.AddHook(DeviceHook);
            };
            Loaded += delegate
            {
                Section start = Find(startPage ?? "") ?? sections[0];
                var resumable = start.View as Resumable;
                if (Handoff != null && resumable != null) resumable.Resume(Handoff);
                Handoff = null;
                Open(start);
                // The record of this computer is read and saved right after the start, whatever section opens first.
                var collect = Find("collect").View as CollectView;
                if (collect != null && start.Key != "collect") collect.StartInBackground();
            };
        }

        // Form handed over by the copy without administrator rights (--handoff), for the start section.
        public JsonObject Handoff;

        // Starts the program again with administrator rights on the given section; this copy closes.
        // form: the filled form of the section, handed to the new copy through a pipe open only to Administrators
        // (AdminHandoff of ComputerForms.cs), so the change goes on there without filling the form again.
        public async void RestartAsAdministrator(string page, JsonObject form)
        {
            string pipeName = null;
            System.IO.Pipes.NamedPipeServerStream pipe = null;
            if (form != null)
            {
                try { pipe = AdminHandoff.Create(out pipeName); }
                catch (Exception) { pipe = null; pipeName = null; }
            }
            var info = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--page " + page + (pipeName != null ? " --handoff " + pipeName : ""))
            { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppPaths.Root };
            try { Process.Start(info); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (pipe != null) pipe.Dispose();
                // 1223: the user said no in the UAC window
                if (ex.NativeErrorCode != 1223) Dialog.Show(this, L.T("Не удалось запустить программу с правами администратора: ") + ex.Message);
                return;
            }
            if (pipe != null)
            {
                Hide();
                bool sent = false;
                try { sent = await AdminHandoff.Send(pipe, form, 60000); }
                catch (Exception) { sent = false; }
                finally { pipe.Dispose(); }
                if (!sent) Dialog.Show(null, L.T("Окно администратора не получило заполненную форму. Заполните её там ещё раз."));
            }
            Replaced = false;
            Close();
        }

        // «Синхронизировать» of the collection: the synchronization opens (and starts once it is moved to WPF).
        public void OpenSync()
        {
            Section sync = Find("sync");
            Open(sync);
            var page = sync.View as SyncStarter;
            if (page != null && current == sync) page.Start();
        }

        // The sections in the order of comp-info.exe: the domain is the main operation; network and name are steps
        // of their own when needed.
        static List<Section> Sections(ToolboxWindow window)
        {
            ComputerPolicy policy = ComputerPolicy.Load();
            var list = new List<Section>
            {
                new Section { Key = "collect", Title = L.T("Сбор информации о компьютере"), NavTitle = L.T("Сбор информации"), Icon = Glyph.Computer,
                    Create = () => new CollectView { SyncRequested = window.OpenSync },
                    Description = L.T("Сведения об этом компьютере сохраняются в базу рядом с программой сами: при запуске и при любом изменении, в том числе комментария. В облако запись попадает при синхронизации.") },
                new Section { Key = "domain", Title = L.T("Домен"), Icon = Glyph.Domain, NeedsAdmin = true,
                    Create = () => new ComputerView(true) { RestartAsAdministrator = window.RestartAsAdministrator },
                    Description = L.T("Ввод в домен gu.gvardia.mvs со свободным IP-адресом из базы и вывод в рабочую группу WORKGROUP с возвратом сети на DHCP. Префикс имени меняется вместе с доменом. После изменения нужна перезагрузка.") },
                new Section { Key = "lan", Title = L.T("Настройка сети"), Icon = Glyph.Network, NeedsAdmin = true, Create = () => new LanView(),
                    Description = L.T("IP-адрес, маска, шлюз и DNS проводного адаптера по профилям корпусов из облака, или автоматическая настройка (DHCP).") },
                new Section { Key = "name", Title = L.T("Имя компьютера"), Icon = Glyph.Tag, NeedsAdmin = true,
                    Create = () => new ComputerView(false) { RestartAsAdministrator = window.RestartAsAdministrator },
                    Description = L.T("Переименование по правилам: ") + policy.InternetPrefix + "-" + policy.UnitCode + L.T("-… для интернета, ") +
                        policy.DomainPrefix + "-" + policy.UnitCode + L.T("-… для доменной сети. После изменения нужна перезагрузка.") },
                new Section { Key = "ping", Title = L.T("Быстрый ping"), Icon = Glyph.Ping, Create = () => new PingView(),
                    Description = L.T("Свой IP, шлюз, DNS, затем домен (в сети корпуса) или интернет. Один запрос на узел, до 2 секунд, остановка при первом сбое.") },
                new Section { Key = "search", Title = L.T("Поиск по документам"), NavTitle = L.T("Поиск документов"), Icon = Glyph.Search, Create = () => new SearchView(),
                    Description = L.T("Ищет фразу в именах файлов и в тексте документов Word, Excel, PowerPoint, LibreOffice, RTF и текстовых файлов на локальных дисках и в корзине. PDF — только по имени.") },
                new Section { Key = "database", Title = L.T("База устройств"), Icon = Glyph.Database, Create = () => new DatabaseView(),
                    Description = L.T("Записи, сохранённые в этой копии программы. Двойной щелчок или Enter открывает карточку компьютера.") },
                new Section { Key = "sync", Title = L.T("Синхронизация с облаком"), NavTitle = L.T("Синхронизация"), Icon = Glyph.Sync, NeedsInternet = true,
                    Create = () => new SyncView { UpdateRequested = () => window.Open(window.Find("update")) },
                    Description = L.T("Отправляет новые и изменённые записи в облако и загружает базу из облака. Нужен интернет и личный ключ доступа.") },
                new Section { Key = "update", Title = L.T("Обновление программы"), NavTitle = L.T("Обновление"), Icon = Glyph.Update, NeedsInternet = true, Create = () => new UpdateView(),
                    Description = L.T("Проверяет новую версию в облаке и устанавливает её в эту копию программы. Данные (папка data) не меняются.") },
                new Section { Key = "about", Title = L.T("О программе"), Icon = Glyph.Info, Bottom = true, Create = () => new ToolboxAbout(window),
                    Description = L.T("Версия, назначение программы и связь с разработчиком.") },
            };
            return list;
        }

        public Section Find(string key)
        {
            foreach (Section s in sections) if (s.Key == key) return s;
            return null;
        }

        public string CurrentKey { get { return current == null ? null : current.Key; } }

        public void Open(Section s)
        {
            if (s == null) return;
            if (current != s)
            {
                if (current != null && current.Page != null && !current.Page.CanLeave()) return;
                current = s;
                host.Content = s.View;
                title.Text = s.Title;
                description.Text = s.Description;
                foreach (KeyValuePair<Section, Button> pair in items) Mark(pair.Key, pair.Value, pair.Key == s);
            }
            if (s.Page != null) Dispatcher.BeginInvoke(new Action(() => s.Page.Opened()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // A new language or the colour-blind mode: built again on the same section (if the open one may be left).
        public void Rebuild()
        {
            if (current != null && current.Page != null && !current.Page.CanLeave()) return;
            App.Rebuild(this, CurrentKey);
        }

        void Keys(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.Control && key >= Key.D0 && key <= Key.D9)
            {
                int n = key - Key.D0;
                Section s = n == 0 ? Find("about") : n <= sections.Count - 1 ? sections[n - 1] : null;
                if (s != null) Open(s);
                e.Handled = true;
                return;
            }
            bool ctrl = (mods & ModifierKeys.Control) != 0, shift = (mods & ModifierKeys.Shift) != 0;
            if (ctrl && (mods & ModifierKeys.Alt) == 0 && (key == Key.Tab || key == Key.PageDown || key == Key.PageUp) && current != null)
            {
                int step = key == Key.PageUp || (key == Key.Tab && shift) ? -1 : 1;
                Open(sections[(sections.IndexOf(current) + step + sections.Count) % sections.Count]);
                e.Handled = true;
                return;
            }
            if (current != null && current.Page != null && current.Page.Shortcut(e)) e.Handled = true;
        }

        IntPtr DeviceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DEVICECHANGE = 0x219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
            if (msg == WM_DEVICECHANGE && current != null && current.Page != null)
            {
                int ev = wParam.ToInt32();
                if (ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE) current.Page.DevicesChanged();
            }
            return IntPtr.Zero;
        }

        // ---------- the menu ----------

        Button NavItem(Section s, string shortcut)
        {
            var item = new Button { ToolTip = s.Title + (shortcut != null ? "   (" + shortcut + ")" : "") };
            item.SetResourceReference(StyleProperty, "NavItem");
            System.Windows.Automation.AutomationProperties.SetName(item, s.NavTitle ?? s.Title);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = Glyph.Block(s.Icon, 16, s.IconKey ?? "Muted");
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(icon);
            var text = new TextBlock { Text = s.NavTitle ?? s.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            // a shield: applying a change needs administrator rights (green with a check when this copy has them);
            // a globe: the section needs the internet
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 8, 0) };
            if (s.NeedsAdmin)
            {
                bool admin = PowerShell.IsAdministrator();
                var shield = new Grid { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                shield.Children.Add(Glyph.Block(Glyph.Shield, 15, admin ? "AdminReady" : "Muted"));
                if (admin)
                {
                    var check = Glyph.Block(Glyph.Check, 8, "AdminReady");
                    check.HorizontalAlignment = HorizontalAlignment.Center;
                    check.FontWeight = FontWeights.Bold;
                    shield.Children.Add(check);
                }
                badges.Children.Add(shield);
            }
            if (s.NeedsInternet)
            {
                var globe = Glyph.Block(Glyph.Globe, 12, "Accent");
                globe.Margin = new Thickness(6, 0, 0, 0);
                badges.Children.Add(globe);
            }
            Grid.SetColumn(badges, 2);
            grid.Children.Add(badges);
            item.Content = grid;
            item.Click += delegate { Open(s); };
            return item;
        }

        static void Mark(Section s, Button item, bool on)
        {
            item.Tag = on ? "on" : null;
            var grid = (Grid)item.Content;
            var icon = (TextBlock)grid.Children[0];
            var text = (TextBlock)grid.Children[1];
            if (s.IconKey == null) icon.SetResourceReference(TextBlock.ForegroundProperty, on ? "Accent" : "Muted");
            text.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            if (on) text.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            else text.ClearValue(TextBlock.ForegroundProperty);
        }

        // «CHAGEAR ⚙ ТУЛБОКС» in rainbow letters, like the console header, and the version under it.
        // A click switches the colour-blind mode (grey letters and gear): the colours flow over, letter by letter,
        // without building the window again (user's choice 21.11.2026).
        readonly List<TextBlock> brandLetters = new List<TextBlock>();
        Image greyGear;
        FrameworkElement brandBox;

        FrameworkElement Brand()
        {
            var letters = new StackPanel { Orientation = Orientation.Horizontal };
            string word = "CHAGEAR " + L.T("ТУЛБОКС");
            brandLetters.Clear();
            foreach (char ch in word)
            {
                if (ch == ' ')
                {
                    // the gear of the console header, between the two words: here the program icon; its grey copy on top
                    var gears = new Grid { Width = 28, Height = 28, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
                    var gear = new Image { Source = Gear(false) };
                    greyGear = new Image { Source = Gear(true), Opacity = ColorBlind ? 1 : 0 };
                    foreach (Image i in new[] { gear, greyGear }) { RenderOptions.SetBitmapScalingMode(i, BitmapScalingMode.HighQuality); gears.Children.Add(i); }
                    letters.Children.Add(gears);
                    continue;
                }
                var t = new TextBlock { Text = ch.ToString(), FontFamily = new FontFamily("Segoe UI Black"), FontSize = 14.667, VerticalAlignment = VerticalAlignment.Center };
                t.SetResourceReference(TextBlock.ForegroundProperty, LetterKey(brandLetters.Count));
                brandLetters.Add(t);
                letters.Children.Add(t);
            }
            var version = new TextBlock { Text = L.T("версия") + " " + AppVersion.Text + (PowerShell.IsAdministrator() ? " · " + L.T("админ") : ""), Margin = new Thickness(0, 2, 0, 0) };
            version.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            var brand = new StackPanel { Margin = new Thickness(6, 0, 0, 14), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            brand.Children.Add(letters);
            brand.Children.Add(version);
            brandBox = brand;
            BrandTip();
            brand.MouseLeftButtonUp += delegate
            {
                ColorBlind = !ColorBlind;
                Settings.Set("colorblind", ColorBlind ? "on" : "off");
                SwitchBrandColours();
            };
            return brand;
        }

        static string LetterKey(int index) { return ColorBlind ? "Text" : "Rainbow" + (index % 6); }

        void BrandTip()
        {
            brandBox.ToolTip = L.T("Папка программы:") + " " + AppPaths.Root + "\n" +
                (ColorBlind ? L.T("Нажмите, чтобы выключить режим для дальтоников") : L.T("Нажмите, чтобы включить режим для дальтоников"));
        }

        // Each letter goes to its new colour a little after the one before it, the gear fades into grey or back.
        // At the end the letters follow the theme again (their colour is a resource of the palette).
        void SwitchBrandColours()
        {
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            for (int i = 0; i < brandLetters.Count; i++)
            {
                TextBlock t = brandLetters[i];
                string key = LetterKey(i);
                var from = t.Foreground as SolidColorBrush;
                var to = TryFindResource(key) as SolidColorBrush;
                if (from == null || to == null) { t.SetResourceReference(TextBlock.ForegroundProperty, key); continue; }
                var brush = new SolidColorBrush(from.Color);
                t.Foreground = brush;
                var colour = new System.Windows.Media.Animation.ColorAnimation(to.Color, TimeSpan.FromMilliseconds(420))
                {
                    BeginTime = TimeSpan.FromMilliseconds(35 * i),
                    EasingFunction = ease,
                };
                colour.Completed += delegate { t.SetResourceReference(TextBlock.ForegroundProperty, key); };
                brush.BeginAnimation(SolidColorBrush.ColorProperty, colour);
            }
            if (greyGear != null)
                greyGear.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(ColorBlind ? 1 : 0, TimeSpan.FromMilliseconds(500))
                { BeginTime = TimeSpan.FromMilliseconds(35 * 7), EasingFunction = ease });
            BrandTip();
        }
        // The gear (app\brand.png); in the colour-blind mode in shades of grey.
        static ImageSource Gear(bool grey)
        {
            var source = new BitmapImage(new Uri("pack://application:,,,/brand.png"));
            if (!grey) return source;
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var pixels = new byte[stride * h];
            bgra.CopyPixels(pixels, stride, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte y = (byte)(pixels[i] * 0.114 + pixels[i + 1] * 0.587 + pixels[i + 2] * 0.299);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = y;
            }
            var result = BitmapSource.Create(w, h, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
    }

    // «О программе» of the toolbox: the picture (another one in Russian), language and theme under it,
    // name, purpose, sections, contacts, the web interface, the site and the folder of the program.
    public class ToolboxAbout : ScrollViewer, ISection
    {
        public const string WebInterface = "comp-info.chagear.top";

        public ToolboxAbout(ToolboxWindow window)
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = false;
            string picture = L.Code == "ru" ? "face-ru.png" : "face.png";
            string name = L.T("CHAGEAR Тулбокс");
            int space = name.IndexOf(' ');
            // «CHAGEAR [Тулбокс]»: the word after the brand is coloured, as on chagear.top
            string marked = space < 0 ? name : name.Substring(0, space + 1) + "[" + name.Substring(space + 1) + "]";
            var about = new AboutContent(new BitmapImage(new Uri("pack://application:,,,/" + picture)), marked, "Toolbox", "toolbox@chagear.top", "toolbox",
                L.T("Учёт компьютеров при подготовке и выдаче сотрудникам"),
                L.T("Собирает сведения о компьютере, хранит их на флешке и синхронизирует с облачной базой."),
                Sections, Settings.Set, delegate { window.Rebuild(); }, 520, Icons)
            { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10) };
            about.AddLink(Glyph.Cloud, WebInterface, L.T("веб-интерфейс базы"), delegate { AboutContent.OpenSite(WebInterface); }, true);
            about.AddLink(Glyph.Folder, L.T("Открыть папку программы"), null, delegate
            {
                try { Process.Start("explorer.exe", "\"" + AppPaths.Root + "\""); } catch (Exception) { }
            });
            Content = about;
        }

        // short, with an icon before every section, as «О программе» of SUSpector and HashPot
        static readonly string[] Icons = { Glyph.Computer, Glyph.Network, Glyph.Cloud, Glyph.Usb, Glyph.Shield, Glyph.Keyboard };

        static string[] Sections { get { return new[]
        {
            L.T("Сбор"),
            L.T("Имя, серийный номер, сеть и ESET — одна запись на компьютер в папке data."),
            L.T("Настройка"),
            L.T("Сеть по профилям корпусов, имя компьютера и домен, проверка связи, поиск по документам."),
            L.T("Облако"),
            L.T("Общая база с коллегами, по личному ключу из веб-интерфейса. Удалённое на флешке из облака не пропадает."),
            L.T("Установка не нужна"),
            L.T("Работает прямо с флешки, без интернета тоже. Выгрузки CSV — в папке exports."),
            L.T("Права администратора"),
            L.T("Нужны только разделам со щитом в меню: сеть, имя и домен."),
            L.T("Клавиши"),
            L.T("Ctrl+1…9 — разделы, F5 — обновить, Ctrl+S — сохранить, Ctrl+F — поиск, Ctrl+E — CSV, Ctrl+Enter — применить."),
        }; } }

        public void Opened() { }
        public bool Shortcut(KeyEventArgs e) { return false; }
        public bool CanLeave() { return true; }
        public void DevicesChanged() { }
    }
}
