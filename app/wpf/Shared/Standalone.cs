using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CompInfo.Wpf
{
    // The window of a standalone program (youSBee now, SUSpector later), as StandaloneForm of the WinForms Standalone.cs:
    // the name and one line about it on the left, the buttons in the top right corner (the last one «О программе»),
    // their right edge at the right edge of the page; the page fills the rest.
    public class StandaloneWindow : Window
    {
        // «О программе» of this program; StandaloneStart opens it and builds the window again after a language change.
        public Func<AboutWindow> About;
        internal Action<StandaloneWindow> ShowAbout;

        // A disk or another device was connected or removed (WM_DEVICECHANGE).
        public event Action DevicesChanged;

        public StandaloneWindow(string name, string highlightKey, string description, FrameworkElement page, params Button[] buttons)
        {
            Title = Brand.Plain(name) + " " + AppVersion.Text;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
            Content = root;

            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            names.Children.Add(Brand.Title(name, highlightKey, 20));
            var line = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 2, 0, 0) };
            line.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            names.Children.Add(line);
            header.Children.Add(names);

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0) };
            foreach (Button b in buttons) { b.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(b); }
            var about = new Button();
            Glyph.Label(about, Glyph.Info, L.T("О программе"));
            about.Click += delegate { if (ShowAbout != null) ShowAbout(this); };
            row.Children.Add(about);
            Grid.SetColumn(row, 1);
            header.Children.Add(row);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(page);

            WpfTheme.DarkTitle(this);
            SourceInitialized += delegate
            {
                HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (source != null) source.AddHook(DeviceHook);
            };
        }

        IntPtr DeviceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DEVICECHANGE = 0x219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
            if (msg == WM_DEVICECHANGE)
            {
                int ev = wParam.ToInt32();
                if ((ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE) && DevicesChanged != null) DevicesChanged();
            }
            return IntPtr.Zero;
        }
    }

    // Start-up of a standalone program (StandaloneApp of the WinForms Standalone.cs): theme and language from the
    // command line or the ini, the place of the window from the last run, the screenshot mode, the window built
    // again after a language change, and errors shown in a dialog of the program.
    public static class StandaloneStart
    {
        static IniSettings settings;
        static Func<StandaloneWindow> create;
        static bool replacing;

        // Screenshots only (--screenshot … --demo): the page shows made-up data with every kind of row.
        public static bool Demo { get; private set; }

        // youSBee.exe [--theme light|dark|system] [--language uk|en|ru] [--screenshot file.png [ms]] [--about] [--width N] [--demo]
        public static void Run(Application app, string[] args, string name, IniSettings ini, Func<StandaloneWindow> make)
        {
            settings = ini;
            create = make;
            Dialog.AppTitle = name;
            // Until the main window is shown, a message must not become the "main window" and end the program on closing.
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += (s, e) => { e.Handled = true; Crash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                app.Dispatcher.Invoke(new Action(() => Crash(e.ExceptionObject as Exception)));

            WpfTheme.Start(Option(args, "--theme", 1) ?? settings.Get("theme", "system"));
            L.Choice = Option(args, "--language", 1) ?? settings.Get("language", "system");
            L.ApplyChoice();

            string screenshot = Option(args, "--screenshot", 1);
            if (screenshot != null)
            {
                Demo = Array.IndexOf(args, "--demo") >= 0;
                Screenshot(app, args, screenshot);
                return;
            }

            StandaloneWindow window = Build();
            string state;
            int[] bounds = ReadBounds(settings.Get("window", ""), out state);
            if (bounds != null)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.SourceInitialized += delegate { Place(window, bounds); };
                if (state == "max") window.WindowState = WindowState.Maximized;
            }
            window.Show();
        }

        static StandaloneWindow Build()
        {
            StandaloneWindow w = create();
            w.ShowAbout = OpenAbout;
            w.Closed += delegate
            {
                if (replacing) return;
                SaveBounds(w);
                Application.Current.Shutdown();
            };
            Application.Current.MainWindow = w;
            return w;
        }

        static void OpenAbout(StandaloneWindow owner)
        {
            AboutWindow about = owner.About();
            about.Owner = owner;
            about.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            about.ShowDialog();
            if (about.LanguageChanged) Rebuild(owner);
        }

        // A new language: the window is built again in the same place and «О программе» opens once more.
        static void Rebuild(StandaloneWindow old)
        {
            L.ApplyChoice();
            StandaloneWindow w = Build();
            Rect place = old.WindowState == WindowState.Normal ? new Rect(old.Left, old.Top, old.Width, old.Height) : old.RestoreBounds;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = place.Left; w.Top = place.Top; w.Width = place.Width; w.Height = place.Height;
            w.WindowState = old.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            w.Show();
            replacing = true;
            old.Close();
            replacing = false;
            w.Dispatcher.BeginInvoke(new Action(() => OpenAbout(w)), DispatcherPriority.Loaded);
        }

        static void Crash(Exception error)
        {
            Window owner = Application.Current == null ? null : Application.Current.MainWindow;
            Dialog.Show(owner, L.T("Непредвиденная ошибка:") + "\n\n" + (error == null ? L.T("неизвестно") : error.Message));
        }

        public static string Option(string[] args, string name, int offset)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + offset < args.Length ? args[index + offset] : null;
        }

        // ---------- the place of the window ----------
        // "x,y,width,height[,max]" in the pixels of the screen, as the WinForms programs write it, so the place
        // stays when the WPF version takes over the same ini.

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int Size; public RECT Monitor, Work; public int Flags; }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        // null when missing, broken or off every screen.
        static int[] ReadBounds(string value, out string state)
        {
            state = "";
            string[] parts = (value ?? "").Split(',');
            int x, y, w, h;
            if (parts.Length < 4 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y) ||
                !int.TryParse(parts[2], out w) || !int.TryParse(parts[3], out h) || w < 200 || h < 150) return null;
            RECT r = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
            IntPtr monitor = MonitorFromRect(ref r, 0);   // MONITOR_DEFAULTTONULL
            if (monitor == IntPtr.Zero) return null;
            MONITORINFO info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(monitor, ref info)) return null;
            int seenW = Math.Min(r.Right, info.Work.Right) - Math.Max(r.Left, info.Work.Left);
            int seenH = Math.Min(r.Bottom, info.Work.Bottom) - Math.Max(r.Top, info.Work.Top);
            if (seenW < 100 || seenH < 50) return null;
            if (parts.Length > 4 && parts[4] == "max") state = "max";
            return new[] { x, y, w, h };
        }

        static void Place(Window w, int[] b)
        {
            const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
            SetWindowPos(new WindowInteropHelper(w).Handle, IntPtr.Zero, b[0], b[1], b[2], b[3], SWP_NOZORDER | SWP_NOACTIVATE);
        }

        static void SaveBounds(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            int x, y, width, height;
            if (w.WindowState == WindowState.Normal && h != IntPtr.Zero)
            {
                RECT r;
                if (!GetWindowRect(h, out r)) return;
                x = r.Left; y = r.Top; width = r.Right - r.Left; height = r.Bottom - r.Top;
            }
            else
            {
                // maximized or minimized: the normal place, from device-independent units to the pixels of its screen
                Rect b = w.RestoreBounds;
                if (b.IsEmpty) return;
                double sx = 1, sy = 1;
                PresentationSource source = PresentationSource.FromVisual(w);
                if (source != null && source.CompositionTarget != null)
                {
                    sx = source.CompositionTarget.TransformToDevice.M11;
                    sy = source.CompositionTarget.TransformToDevice.M22;
                }
                x = (int)Math.Round(b.Left * sx); y = (int)Math.Round(b.Top * sy);
                width = (int)Math.Round(b.Width * sx); height = (int)Math.Round(b.Height * sy);
            }
            settings.Set("window", string.Format("{0},{1},{2},{3}{4}", x, y, width, height,
                w.WindowState == WindowState.Maximized ? ",max" : ""));
        }

        // ---------- screenshots for checking the layout ----------

        // Opens the window (or, with --about, «О программе») off the screen and saves a picture of it after a pause.
        static void Screenshot(Application app, string[] args, string file)
        {
            int delay;
            if (!int.TryParse(Option(args, "--screenshot", 2), out delay)) delay = 1500;
            StandaloneWindow main = Build();
            main.WindowStartupLocation = WindowStartupLocation.Manual;
            main.Left = -20000; main.Top = -20000;
            main.ShowInTaskbar = false;
            main.ShowActivated = false;
            // --width N: the layout of a narrow (or wide) window
            double width;
            if (double.TryParse(Option(args, "--width", 1), out width)) { main.MinWidth = 0; main.Width = width; }
            main.Show();
            Window target = main;
            if (Array.IndexOf(args, "--about") >= 0)
            {
                AboutWindow about = main.About();
                about.Owner = main;
                about.WindowStartupLocation = WindowStartupLocation.Manual;
                about.Left = -20000; about.Top = -20000;
                about.ShowActivated = false;
                about.Show();
                target = about;
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(100, delay)) };
            timer.Tick += delegate
            {
                timer.Stop();
                try { Save(target, file); }
                finally { Environment.Exit(0); }
            };
            timer.Start();
        }

        // The client area of the window in its real pixels (the title bar is drawn by Windows and is not in the picture).
        static void Save(Window w, string file)
        {
            var content = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            DpiScale dpi = VisualTreeHelper.GetDpi(w);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(file)) png.Save(stream);
        }
    }
}
