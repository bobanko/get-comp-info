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
    // Shared frame of the standalone programs (SUSpector, youSBee): one page of the toolbox in its own window,
    // «О программе» with language and theme, preferences in an ini file next to the exe.
    // comp-info.exe does not compile this file: it has its own Page and main window.

    // UI preferences of a standalone program, kept next to the executable.
    public class IniSettings
    {
        readonly string fileName, header;
        readonly string[] keys;

        public IniSettings(string fileName, string header, params string[] keys)
        {
            this.fileName = fileName; this.header = header; this.keys = keys;
        }

        public string FileName { get { return fileName; } }
        string Path { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName); } }

        Dictionary<string, string> Read()
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

        public string Get(string name, string fallback)
        {
            string value;
            return Read().TryGetValue(name, out value) && value.Length > 0 ? value : fallback;
        }

        public void Set(string name, string value)
        {
            try
            {
                Dictionary<string, string> values = Read();
                values[name] = value;
                StringBuilder text = new StringBuilder("; " + header + "\r\n[settings]\r\n");
                foreach (string key in keys)
                    if (values.ContainsKey(key)) text.Append(key).Append('=').Append(values[key]).Append("\r\n");
                File.WriteAllText(Path, text.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }   // a read-only flash drive simply keeps the defaults
        }
    }

    // Minimal page host of the standalone programs. The main program uses its own Page (MainForm.cs).
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
        // A disk or another device was connected or removed.
        public virtual void DevicesChanged() { }
    }

    // The window of a standalone program: name, one line about it, the page, and buttons at the bottom.
    public class StandaloneForm : Form
    {
        readonly Page page;
        readonly Func<StandaloneAboutDialog> about;
        public bool Reopen;
        // Language or theme changed in «О программе»: the window is built again and opens it once more.
        static bool reopenAbout;

        // name marks its coloured part in square brackets: «[SUS]pector», «you[SBee]».
        public StandaloneForm(string name, Color highlight, string description, Page page, Func<StandaloneAboutDialog> about, params Button[] buttons)
        {
            this.page = page;
            this.about = about;
            Text = TitleText.Plain(name) + " " + AppVersion.Text;
            Ui.WindowIcon(this);
            Font = Ui.BaseFont;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Theme.TitleBar(this);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            int em = Font.Height;

            TitleText title = new TitleText(name, new Font("Segoe UI Semibold", 15f), highlight);
            title.Dock = DockStyle.Top;

            Label line = Ui.Paragraph(description, Font, Theme.Muted);
            line.Dock = DockStyle.Top;
            line.Padding = new Padding(0, 0, 0, em / 2);

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(em, em * 3 / 4, em, em * 3 / 4);
            content.BackColor = Theme.Back;
            content.Controls.Add(page);
            content.Controls.Add(line);
            content.Controls.Add(title);
            Controls.Add(content);

            Button aboutButton = Ui.Secondary(L.T("О программе"), Glyph.Info, Font);
            aboutButton.Click += delegate { ShowAbout(); };
            aboutButton.Margin = new Padding(0);

            // The row is as tall as its buttons, so they are never cut off.
            FlowLayoutPanel footer = Ui.Row();
            footer.Dock = DockStyle.Bottom;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.Padding = new Padding(em, em / 3, em, em * 2 / 3);
            footer.BackColor = Theme.Back;
            footer.Controls.Add(aboutButton);
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                buttons[i].Margin = new Padding(0, 0, em / 2, 0);
                footer.Controls.Add(buttons[i]);
            }
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

        // Window size in font heights, limited to 90 % of the screen.
        protected void SizeInEm(int width, int height, int minWidth, int minHeight)
        {
            int em = Font.Height;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(em * width, screen.Width * 9 / 10), Math.Min(em * height, screen.Height * 9 / 10));
            MinimumSize = new Size(em * minWidth, em * minHeight);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DEVICECHANGE = 0x219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
            base.WndProc(ref m);
            if (m.Msg == WM_DEVICECHANGE && page != null)
            {
                int ev = m.WParam.ToInt32();
                if (ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE) page.DevicesChanged();
            }
        }

        void ShowAbout()
        {
            bool rebuild;
            using (StandaloneAboutDialog dialog = about())
            {
                dialog.ShowDialog(this);
                rebuild = dialog.Rebuild;
            }
            if (rebuild && page.CanLeave()) { reopenAbout = true; Reopen = true; Close(); }
        }
    }

    // «О программе» of a standalone program, laid out like the one of comp-info (About in Ui.cs), with language and theme.
    public class StandaloneAboutDialog : Form
    {
        // Language or theme was changed: the main window must be built again.
        public bool Rebuild;

        public StandaloneAboutDialog(Image picture, string name, Color highlight, string subtitle, string[] sections, IniSettings settings)
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
            TableLayoutPanel t = About.Build(Font, em * 26, picture, name, highlight, subtitle, null, sections, out text);
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
                settings.Set("language", L.Codes[index]);
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
                settings.Set("theme", themes[index]);
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
            // An auto-sized form ignores its padding for the controls inside: the table is placed by hand.
            t.Location = new Point(em, em);
            t.Margin = new Padding(0, 0, em, em);
            Controls.Add(t);
            AcceptButton = close;
            CancelButton = close;
        }
    }

    // Start-up of a standalone program: theme and language, the screenshot mode, and the window built again
    // after a language or theme change.
    public static class StandaloneApp
    {
        public static int Run(string[] args, string name, IniSettings settings, Func<StandaloneForm> create)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e) { Crash(name, e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e) { Crash(name, e.ExceptionObject as Exception); };

            Theme.Choice = Option(args, "--theme", 1) ?? settings.Get("theme", "system");
            L.Choice = Option(args, "--language", 1) ?? settings.Get("language", "system");
            string screenshot = Option(args, "--screenshot", 1);
            if (screenshot != null)
            {
                Theme.ApplyChoice();
                L.ApplyChoice();
                StandaloneForm form = create();
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
                StandaloneForm form = create();
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

        public static string Option(string[] args, string name, int offset)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + offset < args.Length ? args[index + offset] : null;
        }

        static void Crash(string name, Exception error)
        {
            MessageBox.Show(
                L.T("Непредвиденная ошибка:") + "\n\n" + (error == null ? L.T("неизвестно") : error.Message),
                name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
