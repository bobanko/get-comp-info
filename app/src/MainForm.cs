using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CompInfo
{
    // A section of the program, shown on the right of the menu.
    public class Page : UserControl
    {
        public string Key;
        public string Title;
        public string NavTitle;
        public string Description;
        public string Icon;
        public bool NeedsAdmin;

        public Page()
        {
            // Set before the page builds its controls: they take their sizes from this font.
            Font = Ui.BaseFont;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
        }

        public MainForm Main { get { return FindForm() as MainForm; } }

        // Called every time the page is opened.
        public virtual void Opened() { }

        // A USB device was plugged in or removed.
        public virtual void DevicesChanged() { }

        // The window is about to close or rebuild: false keeps it open (e.g. an operation is running).
        public virtual bool CanLeave() { return true; }
    }

    public class MainForm : Form
    {
        readonly List<Page> pages = new List<Page>();
        readonly List<NavButton> buttons = new List<NavButton>();
        readonly Panel host = new Panel();
        readonly Label title = new Label();
        readonly Label description = new Label();
        Page current;

        // Set when the window closes to be built again (theme change); the section to reopen.
        public string ReopenPage;

        public MainForm()
        {
            Text = "CHAGEAR Тулбокс" + (PowerShell.IsAdministrator() ? " — администратор" : "");
            Ui.WindowIcon(this);
            Font = Ui.BaseFont;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Theme.TitleBar(this);
            StartPosition = FormStartPosition.CenterScreen;
            int em = Font.Height;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(em * 56, screen.Width * 9 / 10), Math.Min(em * 34, screen.Height * 9 / 10));
            MinimumSize = new Size(em * 44, em * 28);

            pages.Add(new CollectPage());
            pages.Add(new LanPage());
            pages.Add(new ComputerPage(false));
            pages.Add(new ComputerPage(true));
            pages.Add(new PingPage());
            pages.Add(new DatabasePage());
            pages.Add(new UsbPage());
            pages.Add(new SyncPage());
            pages.Add(new UpdatePage());
            pages.Add(new AboutPage());

            // Right side: page title, description and the page itself.
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(em, em * 3 / 4, em, em * 3 / 4);
            right.BackColor = Theme.Back;
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Back;
            description.Dock = DockStyle.Top;
            description.ForeColor = Theme.Muted;
            description.AutoSize = false;
            description.Padding = new Padding(0, 0, 0, em / 2);
            description.Resize += delegate { Ui.FitHeight(description); };
            description.TextChanged += delegate { Ui.FitHeight(description); };
            title.Dock = DockStyle.Top;
            title.Font = new Font("Segoe UI Semibold", 15f);
            title.ForeColor = Theme.Text;
            title.AutoSize = false;
            title.Height = title.Font.Height + em / 6;
            right.Controls.Add(host);
            right.Controls.Add(description);
            right.Controls.Add(title);
            Controls.Add(right);

            // Left side: brand, version and the sections.
            Panel menu = new Panel();
            menu.Dock = DockStyle.Left;
            menu.Width = em * 13;
            menu.BackColor = Theme.Sidebar;
            menu.Padding = new Padding(em / 3, em * 2 / 3, em / 3, em / 2);
            Controls.Add(menu);
            Panel line = new Panel();
            line.Dock = DockStyle.Left;
            line.Width = 1;
            line.BackColor = Theme.Border;
            Controls.Add(line);
            line.BringToFront();
            right.BringToFront();

            Brand brand = new Brand();
            brand.Dock = DockStyle.Top;
            brand.Height = em * 3;
            ToolTip tip = new ToolTip();
            tip.SetToolTip(brand, "Папка программы: " + AppPaths.Root);
            List<Control> stack = new List<Control>();
            stack.Add(brand);
            NavButton about = null;
            foreach (Page p in pages)
            {
                NavButton b = new NavButton(p);
                b.Dock = DockStyle.Top;
                b.Height = em * 2;
                Page target = p;
                b.Click += delegate { Open(target); };
                buttons.Add(b);
                // «О программе» sits at the bottom, next to the theme switch.
                if (p is AboutPage) { about = b; b.Dock = DockStyle.Bottom; } else stack.Add(b);
            }
            // Docked controls stack from the last added, so add them in reverse.
            for (int i = stack.Count - 1; i >= 0; i--) menu.Controls.Add(stack[i]);

            NavButton theme = new NavButton(null);
            theme.Text = ThemeTitle();
            theme.Glyph = Glyph.Theme;
            theme.Dock = DockStyle.Bottom;
            theme.Height = em * 2;
            theme.Click += delegate { NextTheme(); };
            tip.SetToolTip(theme, "Сменить оформление: как в Windows, светлое, тёмное");
            menu.Controls.Add(theme);
            menu.Controls.Add(about);
        }

        static string ThemeTitle()
        {
            return Theme.Choice == "dark" ? "Тема: тёмная" : Theme.Choice == "light" ? "Тема: светлая" : "Тема: как в Windows";
        }

        void NextTheme()
        {
            Theme.Choice = Theme.Choice == "system" ? "light" : Theme.Choice == "light" ? "dark" : "system";
            Settings.Set("theme", Theme.Choice);
            Rebuild();
        }

        // Builds the window again (colours are read while controls are built).
        public void Rebuild()
        {
            if (current != null && !current.CanLeave()) return;
            ReopenPage = current != null ? current.Key : null;
            Close();
        }

        public Page Find(string key)
        {
            foreach (Page p in pages) if (p.Key == key) return p;
            return null;
        }

        public string StartPage;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (current == null) Open(Find(StartPage ?? "") ?? pages[0]);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (current != null && !current.CanLeave()) { e.Cancel = true; ReopenPage = null; }
            base.OnFormClosing(e);
        }

        public void Open(Page page)
        {
            if (current != page)
            {
                if (current != null && !current.CanLeave()) return;
                host.SuspendLayout();
                host.Controls.Clear();
                host.Controls.Add(page);
                host.ResumeLayout();
                current = page;
                title.Text = page.Title;
                description.Text = page.Description;
                foreach (NavButton b in buttons) b.Selected = b.Page == page;
            }
            page.Opened();
        }

        public void Open(string key)
        {
            Page p = Find(key);
            if (p != null) Open(p);
        }

        // Starts the program again with administrator rights on the given section; this copy closes.
        public void RestartAsAdministrator(string page)
        {
            ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, "--page " + page);
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WorkingDirectory = AppPaths.Root;
            try { Process.Start(info); }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) Ui.Error(this, "Не удалось запустить программу с правами администратора: " + ex.Message);
                return;
            }
            ReopenPage = null;
            Close();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_DEVICECHANGE = 0x219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
            base.WndProc(ref m);
            if (m.Msg == WM_DEVICECHANGE && current != null)
            {
                int ev = m.WParam.ToInt32();
                if (ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE) current.DevicesChanged();
            }
        }

        // "CHAGEAR ⚙ ТУЛБОКС" in rainbow letters, like the console header, and the version.
        class Brand : Control
        {
            public Brand()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Theme.Sidebar);
                int em = Font.Height;
                int x = em / 3;
                using (Font big = new Font("Segoe UI Black", 11f))
                {
                    int colorIndex = 0;
                    int top = (em * 3 / 2 - big.Height) / 2;
                    TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                    foreach (char ch in "CHAGEAR ТУЛБОКС")
                    {
                        if (ch == ' ')
                        {
                            // The gear of the console header, between the two words: here the program icon.
                            int side = em * 3 / 2;
                            x += em / 8;
                            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            e.Graphics.DrawImage(Ui.AppImage, new Rectangle(x, 0, side, side));
                            x += side + em / 8;
                            continue;
                        }
                        string s = ch.ToString();
                        Size size = TextRenderer.MeasureText(e.Graphics, s, big, Size.Empty, flags);
                        TextRenderer.DrawText(e.Graphics, s, big, new Point(x, top), Theme.Rainbow[colorIndex % Theme.Rainbow.Length], flags);
                        colorIndex++;
                        x += size.Width;
                    }
                    string version = "версия " + AppVersion.Text + (PowerShell.IsAdministrator() ? " · админ" : "");
                    TextRenderer.DrawText(e.Graphics, version, Font, new Point(em / 3, em * 3 / 2 + em / 8), Theme.Muted, flags);
                }
            }
        }

        // Menu item: icon and title; the open section is marked with a green bar.
        class NavButton : Control
        {
            public readonly Page Page;
            public string Glyph;
            bool selected, hover;

            public NavButton(Page page)
            {
                Page = page;
                if (page != null) { Text = page.NavTitle ?? page.Title; Glyph = page.Icon; }
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
            }

            public bool Selected
            {
                get { return selected; }
                set { selected = value; Invalidate(); }
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
                base.OnKeyDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                int em = Font.Height;
                Color back = selected ? Theme.SidebarSelected : hover ? Theme.SidebarHover : Theme.Sidebar;
                e.Graphics.Clear(Theme.Sidebar);
                Rectangle r = new Rectangle(0, 1, Width, Height - 2);
                using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, r);
                if (selected)
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 0, r.Top + em / 3, 3, r.Height - em * 2 / 3);
                Color iconColor = selected ? Theme.Accent : Theme.Muted;
                CompInfo.Glyph.Draw(e.Graphics, Glyph, new Rectangle(em / 3, r.Top, em * 3 / 2, r.Height), iconColor, em * 0.9f);
                int right = Page != null && Page.NeedsAdmin ? em * 3 / 2 : em / 3;
                using (Font f = selected ? new Font(Font, FontStyle.Bold) : new Font(Font, FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics, Text, f, new Rectangle(em * 2, r.Top, Width - em * 2 - right, r.Height), Page == null ? Theme.Muted : Theme.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (Page != null && Page.NeedsAdmin)
                    CompInfo.Glyph.Draw(e.Graphics, CompInfo.Glyph.Shield, new Rectangle(Width - em * 3 / 2, r.Top, em, r.Height), Theme.Muted, em * 0.65f);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, r);
            }
        }
    }
}
