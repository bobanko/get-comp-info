using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace CompInfo.Wpf
{
    // SUSpector: the cybersecurity check of the toolbox as a standalone program for users (SUSPECTOR.md).
    public partial class App : Application
    {
        // Only harmless UI preferences: theme, language, where the file table is, window place.
        // Always written App.Settings: a bare «Settings» is the comp-info.ini of the toolbox (Lang.cs).
        public static readonly IniSettings Settings = new IniSettings("SUSpector.ini", "SUSpector settings", "theme", "language", "layout", "view", "window");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CrashReport.Install(this, "SUSpector", "suspector@chagear.top");
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            SuspectorUpdate.RemoveLeftovers(Process.GetCurrentProcess().MainModule.FileName);
            SecurityView.TableLayout = StandaloneStart.Option(e.Args, "--layout", 1) ?? Settings.Get("layout", "bottom");
            SecurityView.LayoutSaved = value => Settings.Set("layout", value);
            SecurityView.FileView = StandaloneStart.Option(e.Args, "--view", 1) ?? Settings.Get("view", "list");
            SecurityView.FileViewSaved = value => Settings.Set("view", value);
            StandaloneStart.Run(this, e.Args, "SUSpector", Settings, () => new SuspectorWindow());
        }
    }

    public class SuspectorWindow : StandaloneWindow
    {
        SearchWindow search;

        public SuspectorWindow() : this(new SecurityView(), new Button(), new Button()) { }

        // «Поиск по документам», «Обновление» and «О программе» at the bottom right: the page has its own toolbar at the top.
        SuspectorWindow(SecurityView page, Button find, Button update)
            : base("[SUS]pector", "Suspector",
                L.T("Проверка и подготовка компьютера перед внешней проверкой. Результаты остаются на этом компьютере."),
                page, true, find, update)
        {
            Width = 1040; Height = 660;
            SetBottomLeft(page.TakeStatus());
            MinWidth = 780; MinHeight = 520;
            About = AboutDialog;
            CanLeave = page.CanLeave;

            Glyph.Label(update, Glyph.Update, L.T("Обновление"));
            update.Click += delegate
            {
                var dialog = new UpdateWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                dialog.ShowDialog();
            };
            Glyph.Label(find, Glyph.Search, L.T("Поиск по документам"));
            find.Click += delegate { OpenSearch(); };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.F5) { e.Handled = true; page.RunAgain(); }
                else if (e.Key == Key.Escape) { e.Handled = true; page.Stop(); }
                else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; OpenSearch(); }
            };
            Loaded += delegate { if (StandaloneStart.Demo) page.Demo(); else page.Opened(); };
        }

        // One search window: it is closed together with this one (also when the language changes and the window is built again).
        void OpenSearch()
        {
            if (search == null)
            {
                search = new SearchWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                search.Closed += delegate { search = null; };
                search.Show();
            }
            else
            {
                if (search.WindowState == WindowState.Minimized) search.WindowState = WindowState.Normal;
                search.Activate();
            }
        }

        static AboutWindow AboutDialog()
        {
            return new AboutWindow(new BitmapImage(new Uri("pack://application:,,,/suspector.png")), "[SUS]pector", "Suspector", "suspector@chagear.top", "suspector",
                L.T("Подготовка компьютера к внешней проверке безопасности"), Sections, App.Settings.Set,
                L.T("Показывает, к чему придерутся на проверке, и помогает это исправить. Без вашей команды ничего не меняет."), Icons);
        }

        // short, as «Про програму» of HashPot: a heading and one sentence per section, an icon before each
        static readonly string[] Icons = { Glyph.Shield, Glyph.Broom, Glyph.Search, Glyph.Lock, Glyph.Usb, Glyph.Keyboard };

        static string[] Sections { get { return new[]
        {
            L.T("Проверки"),
            L.T("Имя и домен, антивирус, VPN, нежелательное ПО, OneDrive, администраторы, пароли Windows, документы с признаками ДСК."),
            L.T("Подготовка"),
            L.T("Отключить синхронизацию OneDrive и удалить найденные документы — только по вашей команде."),
            L.T("Поиск по документам"),
            L.T("Найдёт потерянный рабочий файл по фразе в имени или в тексте."),
            L.T("Только на этом компьютере"),
            L.T("Результаты никуда не отправляются. Из интернета — только обновления и список слов для поиска ДСК."),
            L.T("Установка не нужна"),
            L.T("Один файл, работает прямо с флешки. Кнопка «Обновление» сама заменит его новой версией."),
            L.T("Клавиши"),
            L.T("F5 — проверить заново, Ctrl+F — поиск, Enter — открыть найденный файл."),
        }; } }
    }
}
