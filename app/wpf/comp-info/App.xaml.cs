using System;
using System.Windows;
using System.Windows.Threading;

namespace CompInfo.Wpf
{
    // The toolbox on WPF: comp-info-wpf.exe [--page <section>] [--theme light|dark|system] [--language uk|en|ru]
    // [--colorblind on|off] [--screenshot <section> <file.png> [ms]]. Settings are those of comp-info.exe (comp-info.ini).
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            string[] args = e.Args;
            // Until the main window is shown, a message must not become the "main window" and end the program on closing.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dialog.AppTitle = L.T("CHAGEAR Тулбокс");
            DispatcherUnhandledException += (s, x) => { x.Handled = true; Crash(x.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (s, x) => Dispatcher.Invoke(new Action(() => Crash(x.ExceptionObject as Exception)));
            // TLS 1.2 is not always on by default in .NET Framework.
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            // the files the previous update left (as comp-info.exe on WinForms did)
            ProgramUpdate.RemoveLeftovers();

            // comp-info.ini of this copy (Settings of Lang.cs), shared with comp-info.exe.
            WpfTheme.Start(StandaloneStart.Option(args, "--theme", 1) ?? Settings.Get("theme", "system"));
            L.Choice = StandaloneStart.Option(args, "--language", 1) ?? Settings.Get("language", "system");
            L.ApplyChoice();
            ToolboxWindow.ColorBlind = (StandaloneStart.Option(args, "--colorblind", 1) ?? Settings.Get("colorblind", "off")) == "on";

            string shot = StandaloneStart.Option(args, "--screenshot", 1);
            if (shot != null) { CollectView.AutoSave = false; Screenshot(shot, StandaloneStart.Option(args, "--screenshot", 2), StandaloneStart.Option(args, "--screenshot", 3)); return; }

            // Started with administrator rights to finish a change filled in the copy without them.
            string handoffPipe = StandaloneStart.Option(args, "--handoff", 1);
            JsonObject handoff = handoffPipe != null ? AdminHandoff.Receive(handoffPipe) : null;
            Open(new ToolboxWindow(StandaloneStart.Option(args, "--page", 1)) { Handoff = handoff });
        }

        static void Open(ToolboxWindow w)
        {
            Current.MainWindow = w;
            w.Closed += delegate { if (!w.Replaced) Current.Shutdown(); };
            w.Show();
        }

        // A new language or the colour-blind mode: the window is built again on the same section and place.
        public static void Rebuild(ToolboxWindow old, string page)
        {
            L.ApplyChoice();
            var w = new ToolboxWindow(page);
            Rect place = old.WindowState == WindowState.Normal ? new Rect(old.Left, old.Top, old.Width, old.Height) : old.RestoreBounds;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = place.Left; w.Top = place.Top; w.Width = place.Width; w.Height = place.Height;
            w.WindowState = old.WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            Open(w);
            old.Replaced = true;
            old.Close();
        }

        // Development aid, as comp-info.exe --screenshot: a section off the screen, saved as PNG after a pause.
        void Screenshot(string page, string file, string delay)
        {
            int wait;
            if (file == null) { Shutdown(2); return; }
            if (!int.TryParse(delay, out wait)) wait = 2000;
            var w = new ToolboxWindow(page) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false };
            Current.MainWindow = w;
            w.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(100, wait)) };
            timer.Tick += delegate
            {
                timer.Stop();
                try
                {
                    StandaloneStart.Save(w, file);
                    // as comp-info.exe: the database also gives the card of its first record
                    var database = w.Find(page) == null ? null : w.Find(page).View as DatabaseView;
                    if (database != null) database.SaveCardImage(System.IO.Path.ChangeExtension(file, null) + "-card.png");
                    // the synchronization also gives its windows: a conflict, the key, a record deleted in the cloud
                    if (page == "sync") SaveSyncWindows(w, System.IO.Path.ChangeExtension(file, null));
                }
                finally { Environment.Exit(0); }
            };
            timer.Start();
        }

        static void SaveSyncWindows(Window owner, string stem)
        {
            JsonObject local = Json.ParseObject("{\"computerId\":\"A1\",\"hostname\":\"FR-042-PC\",\"comment\":\"кабинет 12\",\"ip\":\"10.1.1.5\",\"collectedAt\":\"2026-11-04T10:00:00+02:00\"}");
            JsonObject cloud = Json.ParseObject("{\"computerId\":\"A1\",\"hostname\":\"FR-042-PC\",\"comment\":\"кабинет 14, Петров\",\"ip\":\"10.1.1.5\",\"collectedAt\":\"2026-11-02T09:00:00+02:00\",\"model\":\"82YU\"}");
            string title = "FR-042-PC — A1.json | " + L.T("в облаке изменил(а)") + " admin@example, 2026-11-04 12:00";
            SaveWindow(new ConflictWindow(title, local, cloud, Recency.CloudNewer(local, null, cloud, null)), stem + "-conflict.png");
            SaveWindow(new ConflictWindow(title, cloud, local, Recency.CloudNewer(cloud, null, local, null)), stem + "-conflict-cloud.png");
            SaveWindow(new KeyWindow("https://example/api/client", false, L.T("Ключ из cloud-key.txt не подошёл: ") + "revoked"), stem + "-key.png");
            SaveWindow(new DialogWindow(owner, L.T("Запись удалена в облаке"), "PC — A1.json\n\n" + L.T("Запись удалена в облаке: ") + "admin, 2026-11-04 12:00.",
                Severity.Warning, new[] { L.T("Восстановить в облаке"), L.T("Удалить локальную запись"), L.T("Отложить") }), stem + "-ask.png");
        }

        // A window off the screen, saved as PNG.
        static void SaveWindow(Window w, string file)
        {
            w.Owner = null;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -20000; w.Top = -20000;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            w.Show();
            w.UpdateLayout();
            StandaloneStart.Save(w, file);
            w.Close();
        }

        static void Crash(Exception error)
        {
            Window owner = Current == null ? null : Current.MainWindow;
            Dialog.Show(owner, L.T("Непредвиденная ошибка:") + "\n\n" + (error == null ? L.T("неизвестно") : error.Message) +
                "\n\n" + L.T("Сообщите о ней разработчику. Подробности:") + "\n" + (error == null ? "" : error.GetType().Name));
        }
    }
}
