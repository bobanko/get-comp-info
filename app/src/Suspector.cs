using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace CompInfo
{
    // Minimal page host for the standalone SUSpector build. The main program uses its own Page
    // implementation and does not compile this file.
    public class Page : UserControl
    {
        public string Key, Title, NavTitle, Description, Icon;
        public bool NeedsAdmin, NeedsInternet;

        public Page()
        {
            Font = Ui.BaseFont;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
        }

        public virtual void Opened() { }
        public virtual bool Shortcut(Keys keys) { return false; }
        public virtual bool CanLeave() { return true; }
    }

    public class SuspectorForm : Form
    {
        readonly SecurityPage page = new SecurityPage();

        public SuspectorForm()
        {
            Text = "SUSpector " + AppVersion.Text;
            Ui.WindowIcon(this);
            Font = Ui.BaseFont;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Theme.TitleBar(this);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            int em = Font.Height;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(em * 56, screen.Width * 9 / 10), Math.Min(em * 34, screen.Height * 9 / 10));
            MinimumSize = new Size(em * 42, em * 28);

            Label title = new Label();
            title.Text = "SUSpector";
            title.Font = new Font("Segoe UI Semibold", 15f);
            title.ForeColor = Theme.Text;
            title.Dock = DockStyle.Top;
            title.Height = title.Font.Height + em / 6;

            Label description = Ui.Paragraph(
                L.T("Проверка и подготовка компьютера перед внешней проверкой. Результаты остаются на этом компьютере."),
                Font, Theme.Muted);
            description.Dock = DockStyle.Top;
            description.Padding = new Padding(0, 0, 0, em / 2);

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(em, em * 3 / 4, em, em * 3 / 4);
            content.BackColor = Theme.Back;
            content.Controls.Add(page);
            content.Controls.Add(description);
            content.Controls.Add(title);
            Controls.Add(content);

            Shown += delegate { page.Opened(); };
            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (page.Shortcut(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; }
            };
            FormClosing += delegate (object sender, FormClosingEventArgs e) { if (!page.CanLeave()) e.Cancel = true; };
        }
    }

    static class SuspectorProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e) { Crash(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e) { Crash(e.ExceptionObject as Exception); };

            Theme.Choice = Option(args, "--theme", 1) ?? "system";
            L.Choice = Option(args, "--language", 1) ?? "system";
            Theme.ApplyChoice();
            L.ApplyChoice();

            SuspectorForm form = new SuspectorForm();
            string screenshot = Option(args, "--screenshot", 1);
            if (screenshot != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.ShowInTaskbar = false;
                int delay;
                if (!int.TryParse(Option(args, "--screenshot", 2), out delay)) delay = 700;
                form.Shown += delegate
                {
                    System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                    timer.Interval = Math.Max(100, delay);
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        using (Bitmap image = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height));
                            image.Save(screenshot, ImageFormat.Png);
                        }
                        form.Close();
                    };
                    timer.Start();
                };
            }
            Application.Run(form);
            return 0;
        }

        static string Option(string[] args, string name, int offset)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + offset < args.Length ? args[index + offset] : null;
        }

        static void Crash(Exception error)
        {
            MessageBox.Show(
                L.T("Непредвиденная ошибка:") + "\n\n" + (error == null ? L.T("неизвестно") : error.Message),
                "SUSpector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
