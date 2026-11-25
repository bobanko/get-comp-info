using System;
using System.Threading;
using System.Windows;

namespace CompInfo.Wpf
{
    // FlashStashBee: a background tray utility (FLASHSTASHBEE.md). There is no window by default — only the tray icon;
    // the settings window opens from its menu.
    public partial class App : Application
    {
        // Only harmless settings next to the program: theme, language, the window position and the copy options.
        public static readonly IniSettings Settings = new IniSettings("flashstashbee.ini", "FlashStashBee settings",
            "theme", "language", "window", "baseDir", "maxSizeOn", "maxSizeGb", "maxFileOn", "maxFileMb", "stubs",
            "extensions", "extensionsExclude", "zip", "excludeSerials");

        Mutex single;
        TrayController tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CrashReport.Install(this, "FlashStashBee", "flashstashbee@chagear.top");
            Dialog.AppTitle = "FlashStashBee";
            TitleIcon.Install();   // title bars show the tray's pixel drive

            WpfTheme.Start(StandaloneStart.Option(e.Args, "--theme", 1) ?? Settings.Get("theme", "system"));
            L.Choice = StandaloneStart.Option(e.Args, "--language", 1) ?? Settings.Get("language", "system");
            L.ApplyChoice();

            // Check the settings-window layout without the tray: FlashStashBee.exe --screenshot file.png [ms] [--about]
            if (Array.IndexOf(e.Args, "--screenshot") >= 0)
            {
                StandaloneStart.Run(this, e.Args, "FlashStashBee", Settings, delegate { return new SettingsWindow(Settings); });
                return;
            }

            // One instance in the tray per user.
            bool created;
            single = new Mutex(true, "CHAGEAR.flashstashbee.single", out created);
            if (!created) { Shutdown(); return; }

            // No main window: the program lives in the tray and exits only through the "Exit" menu item.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            tray = new TrayController(Settings);
            tray.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (tray != null) tray.Dispose();
            if (single != null) { try { single.ReleaseMutex(); } catch (Exception) { } single.Dispose(); }
            base.OnExit(e);
        }
    }
}
