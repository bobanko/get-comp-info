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
        public static readonly IniSettings Settings = new IniSettings("SUSpector.ini", "SUSpector settings", "theme", "language", "layout", "window");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            SuspectorUpdate.RemoveLeftovers(Process.GetCurrentProcess().MainModule.FileName);
            SecurityView.TableLayout = StandaloneStart.Option(e.Args, "--layout", 1) ?? Settings.Get("layout", "bottom");
            SecurityView.LayoutSaved = value => Settings.Set("layout", value);
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
            return new AboutWindow(new BitmapImage(new Uri("pack://application:,,,/suspector.png")), "[SUS]pector", "Suspector", "suspector@chagear.top",
                L.T("Помогает проверить и подготовить компьютер перед внешней проверкой безопасности."), Sections, App.Settings.Set);
        }

        static string[] Sections { get { return new[]
        {
            L.T("Проверки"),
            L.T("Проверяет имя и домен компьютера, антивирус, VPN, нежелательное ПО, OneDrive, локальных администраторов, пароли Windows и документы с признаками ДСК."),
            L.T("Подготовка"),
            L.T("Пользователь сам запускает исправляющие действия: можно отключить синхронизацию OneDrive без удаления локальных файлов и безвозвратно удалить найденные документы."),
            L.T("Поиск по документам"),
            L.T("Находит потерянный рабочий файл по фразе в имени или в тексте документов Word, Excel, PowerPoint, LibreOffice, RTF и текстовых файлов на локальных дисках и в корзине."),
            L.T("Конфиденциальность"),
            L.T("Проверка выполняется локально. Результаты и найденные файлы никуда не отправляются."),
            L.T("Установка не нужна"),
            L.T("SUSpector — один файл, он работает прямо с флешки. Выбранные тема, язык и расположение таблиц сохраняются в SUSpector.ini: на флешке — рядом с программой, на компьютере — в папке профиля (%APPDATA%\\CHAGEAR)."),
            L.T("Обновление"),
            L.T("Кнопка «Обновление» сверяет версию с сайтом разработчика и при согласии заменяет SUSpector.exe новой версией. Без интернета программа работает как обычно."),
            L.T("Клавиши"),
            L.T("F5 — запустить проверку заново, Ctrl+F — поиск по документам. Enter или двойной щелчок по найденному документу — открыть его."),
        }; } }
    }
}
