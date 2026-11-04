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

            MainForm form = new MainForm();
            string page = Option(args, "--page", 1);
            if (page != null && form.Find(page) != null) form.Shown += delegate { form.Open(form.Find(page)); };

            string shot = Option(args, "--screenshot", 1);
            if (shot != null) return Screenshot(form, shot, Option(args, "--screenshot", 2), Option(args, "--screenshot", 3));
            Application.Run(form);
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
            form.Shown += delegate
            {
                form.Open(target);
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
                    form.Close();
                };
                t.Start();
            };
            Application.Run(form);
            return 0;
        }

        static void Crash(Exception e)
        {
            MessageBox.Show("Непредвиденная ошибка:\n\n" + (e != null ? e.Message : "неизвестно") +
                "\n\nСообщите о ней разработчику. Подробности:\n" + (e != null ? e.GetType().Name : ""), "CHAGEAR Тулбокс", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
