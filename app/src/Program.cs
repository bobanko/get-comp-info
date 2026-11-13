using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace CompInfo
{
    static class Program
    {
        // comp-info.exe [--page <section>] [--screenshot <section> <file.png> [milliseconds]]
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object o, ThreadExceptionEventArgs e) { Crash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object o, UnhandledExceptionEventArgs e) { Crash(e.ExceptionObject as Exception); };
            // TLS 1.2 is not always on by default in .NET Framework.
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            ProgramUpdate.RemoveLeftovers();

            Theme.Choice = Option(args, "--theme", 1) ?? Settings.Get("theme", "system");
            L.Choice = Option(args, "--language", 1) ?? Settings.Get("language", "system");
            SecurityPage.TableLayout = Settings.Get("securityLayout", "bottom");
            SecurityPage.LayoutSaved = delegate (string value) { Settings.Set("securityLayout", value); };
            Theme.ColorBlind = (Option(args, "--colorblind", 1) ?? Settings.Get("colorblind", "off")) == "on";
            string page = Option(args, "--page", 1);
            string shot = Option(args, "--screenshot", 1);
            if (shot != null)
            {
                Theme.ApplyChoice();
                L.ApplyChoice();
                return Screenshot(new MainForm(), shot, Option(args, "--screenshot", 2), Option(args, "--screenshot", 3));
            }

            // Started with administrator rights to finish a change filled in the copy without them.
            string handoffPipe = Option(args, "--handoff", 1);
            JsonObject handoff = handoffPipe != null ? AdminHandoff.Receive(handoffPipe) : null;

            // The window is built again after a theme change, on the same section and place.
            Rectangle? bounds = null;
            FormWindowState windowState = FormWindowState.Normal;
            while (true)
            {
                Theme.ApplyChoice();
                L.ApplyChoice();
                MainForm form = new MainForm();
                form.StartPage = page;
                form.Handoff = handoff;
                handoff = null;
                if (bounds.HasValue)
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Bounds = bounds.Value;
                    form.WindowState = windowState;
                }
                Application.Run(form);
                if (form.ReopenPage == null) break;
                page = form.ReopenPage;
                bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
                windowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            }
            return 0;
        }

        static string Option(string[] args, string name, int offset)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + offset < args.Length ? args[i + offset] : null;
        }

        // Development aid: opens a section off screen, waits for it to load and saves the window as PNG.
        static int Screenshot(MainForm form, string page, string file, string delay)
        {
            Page target = form.Find(page);
            if (target == null || file == null) { Console.Error.WriteLine("Unknown section or no file."); return 2; }
            int wait = delay != null ? int.Parse(delay) : 3000;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            form.StartPage = page;
            form.Shown += delegate
            {
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = wait;
                t.Tick += delegate
                {
                    t.Stop();
                    using (Bitmap b = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(b, new Rectangle(0, 0, form.Width, form.Height));
                        b.Save(file, ImageFormat.Png);
                    }
                    DatabasePage database = target as DatabasePage;
                    if (database != null) database.SaveCardImage(System.IO.Path.ChangeExtension(file, null) + "-card.png");
                    if (target is SyncPage)
                    {
                        string stem = System.IO.Path.ChangeExtension(file, null);
                        JsonObject local = Json.ParseObject("{\"computerId\":\"A1\",\"hostname\":\"FR-042-PC\",\"comment\":\"кабинет 12\",\"ip\":\"10.1.1.5\",\"collectedAt\":\"2026-11-04T10:00:00+02:00\"}");
                        JsonObject cloud = Json.ParseObject("{\"computerId\":\"A1\",\"hostname\":\"FR-042-PC\",\"comment\":\"кабинет 14, Петров\",\"ip\":\"10.1.1.5\",\"collectedAt\":\"2026-11-02T09:00:00+02:00\",\"model\":\"82YU\"}");
                        SaveImage(new ConflictDialog("FR-042-PC — A1.json | " + L.T("в облаке изменил(а)") + " admin@example, 2026-11-04 12:00", local, cloud), stem + "-conflict.png");
                        SaveImage(new KeyDialog("https://example/api/client", false, L.T("Ключ из cloud-key.txt не подошёл: ") + "revoked"), stem + "-key.png");
                        SaveImage(new AskDialog(L.T("Запись удалена в облаке"), "PC — A1.json\n\n" + L.T("Запись удалена в облаке: ") + "admin, 2026-11-04 12:00.", Glyph.Warning, Theme.Warn,
                            new[] { L.T("Восстановить в облаке"), L.T("Удалить локальную запись"), L.T("Отложить") }), stem + "-ask.png");
                    }
                    form.ReopenPage = null;
                    Environment.Exit(0);
                };
                t.Start();
            };
            Application.Run(form);
            return 0;
        }

        static void SaveImage(Form f, string file)
        {
            using (f)
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000);
                f.Show();
                Application.DoEvents();
                using (Bitmap b = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                    b.Save(file, ImageFormat.Png);
                }
                f.Close();
            }
        }

        static void Crash(Exception e)
        {
            MessageBox.Show(L.T("Непредвиденная ошибка:") + "\n\n" + (e != null ? e.Message : L.T("неизвестно")) +
                "\n\n" + L.T("Сообщите о ней разработчику. Подробности:") + "\n" + (e != null ? e.GetType().Name : ""), L.T("CHAGEAR Тулбокс"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
