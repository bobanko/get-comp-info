using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CompInfo
{
    // SUSpector keeps only its own harmless UI preferences next to the executable.
    static class SuspectorSettings
    {
        static string Path { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SUSpector.ini"); } }

        static Dictionary<string, string> Read()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(Path)) return values;
                foreach (string raw in File.ReadAllLines(Path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int equals = line.IndexOf('=');
                    if (equals > 0) values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                }
            }
            catch (Exception) { }
            return values;
        }

        public static string Get(string name, string fallback)
        {
            string value;
            return Read().TryGetValue(name, out value) && value.Length > 0 ? value : fallback;
        }

        public static void Set(string name, string value)
        {
            try
            {
                Dictionary<string, string> values = Read();
                values[name] = value;
                StringBuilder text = new StringBuilder("; SUSpector settings\r\n[settings]\r\n");
                foreach (string key in new[] { "theme", "language", "layout" })
                    if (values.ContainsKey(key)) text.Append(key).Append('=').Append(values[key]).Append("\r\n");
                File.WriteAllText(Path, text.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }

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
        public bool Reopen;
        // Language or theme changed in «О программе»: the window is built again and opens it once more.
        static bool reopenAbout;

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

            Button about = Ui.Secondary(L.T("О программе"), Glyph.Info, Font);
            about.Click += delegate { ShowAbout(); };
            Button update = Ui.Secondary(L.T("Обновление"), Glyph.Update, Font);
            update.Click += delegate { using (SuspectorUpdateDialog dialog = new SuspectorUpdateDialog()) dialog.ShowDialog(this); };
            update.Margin = new Padding(0, 0, em / 2, 0);
            about.Margin = new Padding(0);

            // The row is as tall as its buttons, so they are never cut off.
            FlowLayoutPanel footer = Ui.Row();
            footer.Dock = DockStyle.Bottom;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.Padding = new Padding(em, em / 3, em, em * 2 / 3);
            footer.BackColor = Theme.Back;
            footer.Controls.Add(about);
            footer.Controls.Add(update);
            Controls.Add(footer);

            Shown += delegate
            {
                page.Opened();
                if (reopenAbout) { reopenAbout = false; BeginInvoke((MethodInvoker)ShowAbout); }
            };
            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (page.Shortcut(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; }
            };
            FormClosing += delegate (object sender, FormClosingEventArgs e)
            {
                if (!Reopen && !page.CanLeave()) e.Cancel = true;
            };
        }

        void ShowAbout()
        {
            bool rebuild;
            using (SuspectorAboutDialog dialog = new SuspectorAboutDialog())
            {
                dialog.ShowDialog(this);
                rebuild = dialog.Rebuild;
            }
            if (rebuild) { reopenAbout = true; Rebuild(); }
        }

        void Rebuild()
        {
            if (!page.CanLeave()) return;
            Reopen = true;
            Close();
        }
    }

    // «О программе» of SUSpector, laid out like the one of comp-info (see About in Ui.cs).
    public class SuspectorAboutDialog : Form
    {
        static string[] Sections { get { return new[]
        {
            L.T("Проверки"),
            L.T("Проверяет имя и домен компьютера, антивирус, VPN, нежелательное ПО, OneDrive, локальных администраторов, пароли Windows и документы с признаками ДСК."),
            L.T("Подготовка"),
            L.T("Пользователь сам запускает исправляющие действия: можно отключить синхронизацию OneDrive без удаления локальных файлов и безвозвратно удалить найденные документы."),
            L.T("Конфиденциальность"),
            L.T("Проверка выполняется локально. Результаты и найденные файлы никуда не отправляются."),
            L.T("Установка не нужна"),
            L.T("SUSpector — один файл, он работает прямо с флешки. Рядом с ним сохраняется только SUSpector.ini с выбранными темой, языком и расположением таблиц."),
            L.T("Обновление"),
            L.T("Кнопка «Обновление» сверяет версию с сайтом разработчика и при согласии заменяет SUSpector.exe новой версией. Без интернета программа работает как обычно."),
            L.T("Клавиши"),
            L.T("F5 — запустить проверку заново. Enter или двойной щелчок по найденному документу — открыть его."),
        }; } }

        // Language or theme was changed: the main window must be built again.
        public bool Rebuild;

        public SuspectorAboutDialog()
        {
            Ui.Dialog(this);
            Text = L.T("О программе");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            int em = Font.Height;
            Padding = new Padding(em);

            FlowLayoutPanel text;
            TableLayoutPanel t = About.Build(Font, em * 26, Ui.SuspectorImage, "SUSpector",
                L.T("Помогает проверить и подготовить компьютер перед внешней проверкой безопасности."), null, Sections, out text);
            text.Controls.Add(About.SiteRow(Font, Glyph.Open, About.WebSite, null));

            // Language and theme: rarely changed, so they are here and not in the main window.
            Label appearance = About.Line(L.T("Язык и тема"), new Font(Font, FontStyle.Bold), Theme.Text, em * 26, 0);
            appearance.Margin = new Padding(appearance.Margin.Left, em / 2, appearance.Margin.Right, appearance.Margin.Bottom);
            text.Controls.Add(appearance);
            Segmented language = new Segmented(new[] { "УКР", "ENG", "РУС" }, false,
                new[] { "Українська", "English", "Русский" }, Array.IndexOf(L.Codes, L.Code));
            language.Size = new Size(em * 11, em * 3 / 2);
            language.Margin = new Padding(0, em / 4, em, 0);
            language.Changed += delegate (int index)
            {
                L.Choice = L.Codes[index];
                SuspectorSettings.Set("language", L.Codes[index]);
                Rebuild = true;
                Close();
            };
            string[] themes = { "light", "system", "dark" };
            Segmented theme = new Segmented(new[] { Glyph.Sun, Glyph.Theme, Glyph.Moon }, true,
                new[] { L.T("Светлая тема"), L.T("Как в Windows"), L.T("Тёмная тема") },
                Array.IndexOf(themes, Theme.Choice) < 0 ? 1 : Array.IndexOf(themes, Theme.Choice));
            theme.Size = new Size(em * 11, em * 3 / 2);
            theme.Margin = new Padding(0, em / 4, 0, 0);
            theme.Changed += delegate (int index)
            {
                Theme.Choice = themes[index];
                SuspectorSettings.Set("theme", themes[index]);
                Rebuild = true;
                Close();
            };
            FlowLayoutPanel choices = Ui.Row();
            choices.Controls.Add(language);
            choices.Controls.Add(theme);
            text.Controls.Add(choices);

            Button close = Ui.Primary(L.T("Закрыть"), null, Font);
            close.Anchor = AnchorStyles.Right;
            close.Margin = new Padding(0, em, 0, 0);
            close.Click += delegate { Close(); };
            t.Controls.Add(close, 1, 1);
            t.Location = new Point(em, em);
            t.Margin = new Padding(0, 0, em, em);
            Controls.Add(t);
            AcceptButton = close;
            CancelButton = close;
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
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            SuspectorUpdate.RemoveLeftovers(Application.ExecutablePath);

            Theme.Choice = Option(args, "--theme", 1) ?? SuspectorSettings.Get("theme", "system");
            L.Choice = Option(args, "--language", 1) ?? SuspectorSettings.Get("language", "system");
            SecurityPage.TableLayout = Option(args, "--layout", 1) ?? SuspectorSettings.Get("layout", "bottom");
            SecurityPage.LayoutSaved = delegate (string value) { SuspectorSettings.Set("layout", value); };
            string screenshot = Option(args, "--screenshot", 1);
            if (screenshot != null)
            {
                Theme.ApplyChoice();
                L.ApplyChoice();
                SuspectorForm form = new SuspectorForm();
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
                        Environment.Exit(0);
                    };
                    timer.Start();
                };
                Application.Run(form);
                return 0;
            }

            Rectangle? bounds = null;
            FormWindowState state = FormWindowState.Normal;
            while (true)
            {
                Theme.ApplyChoice();
                L.ApplyChoice();
                SuspectorForm form = new SuspectorForm();
                if (bounds.HasValue)
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Bounds = bounds.Value;
                    form.WindowState = state;
                }
                Application.Run(form);
                if (!form.Reopen) break;
                bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
                state = form.WindowState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
            }
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
