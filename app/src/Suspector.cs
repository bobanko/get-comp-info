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
                foreach (string key in new[] { "theme", "language" })
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

            string[] themes = { "light", "system", "dark" };
            Segmented theme = new Segmented(new[] { Glyph.Sun, Glyph.Theme, Glyph.Moon }, true,
                new[] { L.T("Светлая тема"), L.T("Как в Windows"), L.T("Тёмная тема") },
                Array.IndexOf(themes, Theme.Choice) < 0 ? 1 : Array.IndexOf(themes, Theme.Choice));
            theme.Width = em * 11;
            theme.Height = em * 3 / 2;
            theme.Anchor = AnchorStyles.Left;
            theme.Changed += delegate (int index)
            {
                Theme.Choice = themes[index];
                SuspectorSettings.Set("theme", themes[index]);
                Rebuild();
            };
            Segmented language = new Segmented(new[] { "УКР", "ENG", "РУС" }, false,
                new[] { "Українська", "English", "Русский" }, Array.IndexOf(L.Codes, L.Code));
            language.Width = em * 11;
            language.Height = em * 3 / 2;
            language.Anchor = AnchorStyles.Left;
            language.Changed += delegate (int index)
            {
                L.Choice = L.Codes[index];
                SuspectorSettings.Set("language", L.Codes[index]);
                Rebuild();
            };
            Button about = Ui.Secondary(L.T("О программе"), Glyph.Info, Font);
            about.Click += delegate { using (SuspectorAboutDialog dialog = new SuspectorAboutDialog()) dialog.ShowDialog(this); };
            Button update = Ui.Secondary(L.T("Обновление"), Glyph.Update, Font);
            update.Click += delegate { using (SuspectorUpdateDialog dialog = new SuspectorUpdateDialog()) dialog.ShowDialog(this); };
            FlowLayoutPanel footerActions = Ui.Row();
            footerActions.Anchor = AnchorStyles.Right;
            footerActions.Controls.Add(update);
            footerActions.Controls.Add(about);

            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = em * 5 / 2;
            footer.ColumnCount = 4;
            footer.RowCount = 1;
            footer.Padding = new Padding(em, em / 3, em, em / 2);
            footer.BackColor = Theme.Back;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, em * 12));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, em * 12));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, em * 3 / 2));
            footer.Controls.Add(language, 0, 0);
            footer.Controls.Add(theme, 1, 0);
            footer.Controls.Add(footerActions, 3, 0);
            Controls.Add(footer);

            Shown += delegate { page.Opened(); };
            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (page.Shortcut(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; }
            };
            FormClosing += delegate (object sender, FormClosingEventArgs e)
            {
                if (!Reopen && !page.CanLeave()) e.Cancel = true;
            };
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
            L.T("SUSpector — один файл, он работает прямо с флешки. Рядом с ним сохраняется только SUSpector.ini с выбранными темой и языком."),
            L.T("Обновление"),
            L.T("Кнопка «Обновление» сверяет версию с сайтом разработчика и при согласии заменяет SUSpector.exe новой версией. Без интернета программа работает как обычно."),
            L.T("Клавиши"),
            L.T("F5 — запустить проверку заново. Enter или двойной щелчок по найденному документу — открыть его."),
        }; } }

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
            text.Controls.Add(About.FolderRow(Font, AppDomain.CurrentDomain.BaseDirectory));

            Button close = Ui.Primary(L.T("Закрыть"), null, Font);
            close.Anchor = AnchorStyles.Right;
            close.Margin = new Padding(0, em, 0, 0);
            close.Click += delegate { Close(); };
            t.Controls.Add(close, 1, 1);
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
