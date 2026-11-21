using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CompInfo.Wpf
{
    // youSBee: serial numbers of USB flash drives (YOUSBEE.md), the «USB-накопители» page in its own window.
    public partial class App : Application
    {
        // Only harmless UI preferences: theme, language, window place and whether it stays on top.
        // Always written App.Settings: a bare «Settings» is the comp-info.ini of the toolbox (Lang.cs).
        public static readonly IniSettings Settings = new IniSettings("youSBee.ini", "youSBee settings", "theme", "language", "window", "pinned");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CrashReport.Install(this, "youSBee", "yousbee@chagear.top");
            StandaloneStart.Run(this, e.Args, "youSBee", Settings, () => new YouSBeeWindow());
        }
    }

    public class YouSBeeWindow : StandaloneWindow
    {
        public YouSBeeWindow() : this(new UsbView { ShowToolbar = false }, new Button(), new Button()) { }

        // «Обновить», «О программе» and the pin sit in the top right corner, so the page's own row with «Обновить» is hidden.
        // The pin is the last one, at the very edge of the window.
        YouSBeeWindow(UsbView page, Button pin, Button refresh)
            : base("you[SBee]", "Honey", L.T("Серийные номера флешек."), page, refresh)
        {
            AddAtEdge(pin);
            // A small utility: the table has only a few short columns.
            // Room for about five drives: rarely more than three are connected at once.
            Width = 660; Height = 380;
            MinWidth = 520; MinHeight = 320;
            About = AboutDialog;

            // On top of other windows, so it stays visible while the number goes into a request in the browser.
            pin.SetResourceReference(StyleProperty, "IconButton");
            Topmost = App.Settings.Get("pinned", "off") == "on";
            ShowPin(pin);
            pin.Click += delegate
            {
                Topmost = !Topmost;
                App.Settings.Set("pinned", Topmost ? "on" : "off");
                ShowPin(pin);
            };

            Glyph.Label(refresh, Glyph.Refresh, L.T("Обновить"));
            refresh.ToolTip = L.T("Обновить") + "   (F5)";
            refresh.Click += delegate { page.RefreshList(); };
            KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.F5) { e.Handled = true; page.RefreshList(); } };

            DevicesChanged += page.DevicesChanged;
            Loaded += delegate { if (StandaloneStart.Demo) page.Demo(); else page.Opened(); };
        }

        // The filled pin of the icon font has no needle: the outline pin is drawn over the fill.
        void ShowPin(Button pin)
        {
            var glyph = new Grid();
            if (Topmost) glyph.Children.Add(Glyph.Block(Glyph.Pinned, 15, null));
            glyph.Children.Add(Glyph.Block(Glyph.Pin, 15, null));
            pin.Content = glyph;
            pin.Tag = Topmost ? "on" : null;
            pin.ToolTip = Topmost ? L.T("Окно поверх остальных — нажмите, чтобы открепить") : L.T("Закрепить окно поверх остальных");
            System.Windows.Automation.AutomationProperties.SetName(pin, (string)pin.ToolTip);
        }

        static AboutWindow AboutDialog()
        {
            return new AboutWindow(new BitmapImage(new Uri("pack://application:,,,/yousbee.png")), "you[SBee]", "Honey", "yousbee@chagear.top",
                L.T("Показывает серийные номера флешек и USB-дисков, чтобы их было удобно переписать или вставить в заявку."), Sections,
                App.Settings.Set);
        }

        static string[] Sections { get { return new[]
        {
            L.T("Как пользоваться"),
            L.T("Подключите флешку — она сразу появится в списке. Кнопка «Копировать», двойной щелчок или Ctrl+C копирует серийный номер."),
            L.T("Если номера нет"),
            L.T("Некоторые накопители не сообщают серийный номер Windows: тогда в строке так и написано."),
            L.T("Конфиденциальность"),
            L.T("Программа ничего не отправляет и не меняет: она только читает сведения о дисках из Windows."),
            L.T("Установка не нужна"),
            L.T("youSBee — один файл, он работает прямо с флешки. Права администратора не нужны. Выбранные тема и язык сохраняются в youSBee.ini: на флешке — рядом с программой, на компьютере — в папке профиля (%APPDATA%\\CHAGEAR)."),
            L.T("Клавиши"),
            L.T("F5 — обновить список. Ctrl+C — скопировать серийный номер выбранной флешки."),
        }; } }
    }
}
