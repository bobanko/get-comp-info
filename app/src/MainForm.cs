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
        public bool NeedsInternet;

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

        // A key combination the main window did not use (F5, Ctrl+S, ...): true when the page handled it.
        public virtual bool Shortcut(Keys keys) { return false; }

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

        // Set when the window closes to be built again (theme or language change); the section to reopen.
        public string ReopenPage;

        public MainForm()
        {
            Text = L.T("CHAGEAR Тулбокс") + " " + AppVersion.Text + (PowerShell.IsAdministrator() ? " — " + L.T("администратор") : "");
            Ui.WindowIcon(this);
            Font = Ui.BaseFont;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Theme.TitleBar(this);
            StartPosition = FormStartPosition.CenterScreen;
            int em = Font.Height;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(em * 56, screen.Width * 9 / 10), Math.Min(em * 34, screen.Height * 9 / 10));
            MinimumSize = new Size(em * 44, em * 31);

            pages.Add(new CollectPage());
            pages.Add(new LanPage());
            pages.Add(new ComputerPage(false));
            pages.Add(new ComputerPage(true));
            pages.Add(new PingPage());
            pages.Add(new SecurityPage());
            pages.Add(new SearchPage());
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
            tip.SetToolTip(brand, L.T("Папка программы:") + " " + AppPaths.Root + "\n" +
                L.T(Theme.ColorBlind ? "Нажмите, чтобы выключить режим для дальтоников" : "Нажмите, чтобы включить режим для дальтоников"));
            brand.Click += delegate
            {
                Theme.ColorBlind = !Theme.ColorBlind;
                Settings.Set("colorblind", Theme.ColorBlind ? "on" : "off");
                Rebuild();
            };
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
                int number = pages.IndexOf(p) + 1;
                string shortcut = p is AboutPage ? "   (Ctrl+0)" : number <= 9 ? "   (Ctrl+" + number + ")" : "";
                tip.SetToolTip(b, (p.Title ?? "") + shortcut);
                buttons.Add(b);
                // «О программе» sits at the bottom, next to the theme switch.
                if (p is AboutPage) { about = b; b.Dock = DockStyle.Bottom; } else stack.Add(b);
            }
            // Docked controls stack from the last added, so add them in reverse.
            for (int i = stack.Count - 1; i >= 0; i--) menu.Controls.Add(stack[i]);

            // Bottom: theme (system in the middle) and language switches, then «О программе».
            string[] themes = { "light", "system", "dark" };
            Segmented theme = new Segmented(new[] { Glyph.Sun, Glyph.Theme, Glyph.Moon }, true,
                new[] { L.T("Светлая тема"), L.T("Как в Windows"), L.T("Тёмная тема") }, Array.IndexOf(themes, Theme.Choice) < 0 ? 1 : Array.IndexOf(themes, Theme.Choice));
            theme.Changed += delegate (int i) { Theme.Choice = themes[i]; Settings.Set("theme", themes[i]); Rebuild(); };
            Segmented language = new Segmented(new[] { "УКР", "ENG", "РУС" }, false,
                new[] { "Українська", "English", "Русский" }, Array.IndexOf(L.Codes, L.Code));
            language.Changed += delegate (int i) { L.Choice = L.Codes[i]; Settings.Set("language", L.Codes[i]); Rebuild(); };
            foreach (Segmented s in new[] { language, theme })
            {
                s.Dock = DockStyle.Bottom;
                s.Height = em * 3 / 2;
                Panel gap = new Panel();
                gap.Dock = DockStyle.Bottom;
                gap.Height = em / 3;
                gap.BackColor = Theme.Sidebar;
                menu.Controls.Add(gap);
                menu.Controls.Add(s);
            }
            menu.Controls.Add(about);
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
                if (ex.NativeErrorCode != 1223) Ui.Error(this, L.T("Не удалось запустить программу с правами администратора: ") + ex.Message);
                return;
            }
            ReopenPage = null;
            Close();
        }

        // Keyboard: Ctrl+1…9 sections, Ctrl+0 «О программе», Ctrl+Tab / Ctrl+PageDown next section,
        // Ctrl+Shift+Tab / Ctrl+PageUp previous; everything else (F5, Ctrl+S, Ctrl+F…) goes to the open section.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0, shift = (keyData & Keys.Shift) != 0, alt = (keyData & Keys.Alt) != 0;
            if (ctrl && !alt && !shift && key >= Keys.D0 && key <= Keys.D9)
            {
                int n = key - Keys.D0;
                Page p = n == 0 ? pages[pages.Count - 1] : n <= pages.Count - 1 ? pages[n - 1] : null;
                if (p != null) Open(p);
                return true;
            }
            if (ctrl && !alt && (key == Keys.Tab || key == Keys.PageDown || key == Keys.PageUp) && current != null)
            {
                int step = key == Keys.PageUp || (key == Keys.Tab && shift) ? -1 : 1;
                int i = (pages.IndexOf(current) + step + pages.Count) % pages.Count;
                Open(pages[i]);
                return true;
            }
            if (current != null && current.Shortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
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
                Cursor = Cursors.Hand;
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
                    foreach (char ch in "CHAGEAR " + L.T("ТУЛБОКС"))
                    {
                        if (ch == ' ')
                        {
                            // The gear of the console header, between the two words: here the program icon.
                            int side = em * 3 / 2;
                            x += em / 8;
                            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            Bitmap gear = Ui.AppImage;
                            if (Theme.ColorBlind)
                            {
                                // Black-and-white logo: the icon in shades of grey.
                                using (System.Drawing.Imaging.ImageAttributes grey = new System.Drawing.Imaging.ImageAttributes())
                                {
                                    float r = 0.299f, g = 0.587f, b = 0.114f;
                                    grey.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]
                                    {
                                        new[] { r, r, r, 0f, 0f }, new[] { g, g, g, 0f, 0f }, new[] { b, b, b, 0f, 0f },
                                        new[] { 0f, 0f, 0f, 1f, 0f }, new[] { 0f, 0f, 0f, 0f, 1f },
                                    }));
                                    e.Graphics.DrawImage(gear, new Rectangle(x, 0, side, side), 0, 0, gear.Width, gear.Height, GraphicsUnit.Pixel, grey);
                                }
                            }
                            else e.Graphics.DrawImage(gear, new Rectangle(x, 0, side, side));
                            x += side + em / 8;
                            continue;
                        }
                        string s = ch.ToString();
                        Size size = TextRenderer.MeasureText(e.Graphics, s, big, Size.Empty, flags);
                        TextRenderer.DrawText(e.Graphics, s, big, new Point(x, top), Theme.Rainbow[colorIndex % Theme.Rainbow.Length], flags);
                        colorIndex++;
                        x += size.Width;
                    }
                    string version = L.T("версия") + " " + AppVersion.Text + (PowerShell.IsAdministrator() ? " · " + L.T("админ") : "");
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
                int right = em / 3;
                if (Page != null && Page.NeedsAdmin) right += em * 3 / 2;
                if (Page != null && Page.NeedsInternet) right += em * 3 / 2;
                using (Font f = selected ? new Font(Font, FontStyle.Bold) : new Font(Font, FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics, Text, f, new Rectangle(em * 2, r.Top, Width - em * 2 - right, r.Height), selected ? Theme.Accent : Page == null ? Theme.Muted : Theme.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (Page != null && Page.NeedsAdmin)
                {
                    // A green shield confirms that this restarted copy already has elevation.
                    Rectangle shield = new Rectangle(Width - em * 3 / 2, r.Top, em, r.Height);
                    if (PowerShell.IsAdministrator()) CompInfo.Glyph.DrawAdminReady(e.Graphics, shield);
                    else CompInfo.Glyph.Draw(e.Graphics, CompInfo.Glyph.Shield, shield, Theme.Muted, em * 0.9f);
                }
                if (Page != null && Page.NeedsInternet)
                {
                    int offset = Page.NeedsAdmin ? em * 3 / 2 : 0;
                    CompInfo.Glyph.Draw(e.Graphics, CompInfo.Glyph.Globe, new Rectangle(Width - em * 3 / 2 - offset, r.Top, em, r.Height), Theme.Info, em * 0.65f);
                }
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, r);
            }
        }
    }
}
