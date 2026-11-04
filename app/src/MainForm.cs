using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CompInfo
{
    // A section of the program, shown on the right of the menu.
    public class Page : UserControl
    {
        public string Key;
        public string Title;
        public string Description;
        public string Icon;
        public bool NeedsAdmin;

        public Page()
        {
            // Set before the page builds its controls: they take their sizes from this font.
            Font = new Font("Segoe UI", 10f);
            BackColor = Color.White;
            Dock = DockStyle.Fill;
            Margin = new Padding(0);
        }

        // Called every time the page is opened.
        public virtual void Opened() { }

        // A USB device was plugged in or removed.
        public virtual void DevicesChanged() { }
    }

    public class MainForm : Form
    {
        readonly List<Page> pages = new List<Page>();
        readonly List<NavButton> buttons = new List<NavButton>();
        readonly Panel host = new Panel();
        readonly Label title = new Label();
        readonly Label description = new Label();
        Page current;

        public MainForm()
        {
            Text = "CHAGEAR Тулбокс";
            try { base.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Font = new Font("Segoe UI", 10f);
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterScreen;
            int em = Font.Height;
            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(em * 70, screen.Width * 9 / 10), Math.Min(em * 40, screen.Height * 9 / 10));
            MinimumSize = new Size(em * 44, em * 28);

            pages.Add(new CollectPage());
            pages.Add(new ConsolePage("lan", "Настройка сети", "IP-адрес, маска подсети, шлюз и DNS проводного адаптера по профилям корпусов.", Glyph.Network,
                "setup-lan-adapter.ps1", true));
            pages.Add(new ConsolePage("name", "Имя компьютера", "Переименование по правилам IF / FR.", Glyph.Tag, "set-computer-name.ps1", true));
            pages.Add(new ConsolePage("domain", "Домен", "Ввод компьютера в домен и вывод из него.", Glyph.Domain, "manage-computer-domain.ps1", true));
            pages.Add(new PingPage());
            pages.Add(new DatabasePage());
            pages.Add(new UsbPage());
            pages.Add(new ConsolePage("sync", "Синхронизация с облаком", "Отправка изменений в облако и загрузка базы из облака.", Glyph.Sync, "sync-cloud.ps1", false));
            pages.Add(new ConsolePage("update", "Обновление программы", "Проверка и установка новой версии из облака.", Glyph.Update, "update-program.ps1", false));

            // Right side: page title, description and the page itself.
            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(em * 3 / 2, em, em * 3 / 2, em);
            right.BackColor = Color.White;
            host.Dock = DockStyle.Fill;
            host.BackColor = Color.White;
            description.Dock = DockStyle.Top;
            description.ForeColor = Theme.Muted;
            description.AutoSize = false;
            description.Padding = new Padding(0, 0, 0, em * 2 / 3);
            description.Resize += delegate { Ui.FitHeight(description); };
            description.TextChanged += delegate { Ui.FitHeight(description); };
            title.Dock = DockStyle.Top;
            title.Font = new Font("Segoe UI Semibold", 16f);
            title.ForeColor = Theme.Text;
            title.AutoSize = false;
            title.Height = title.Font.Height + em / 4;
            right.Controls.Add(host);
            right.Controls.Add(description);
            right.Controls.Add(title);
            Controls.Add(right);

            // Left side: brand, version and the sections.
            Panel menu = new Panel();
            menu.Dock = DockStyle.Left;
            menu.Width = em * 17;
            menu.BackColor = Theme.Sidebar;
            menu.Padding = new Padding(em / 2, em, em / 2, em);
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
            List<Control> stack = new List<Control>();
            stack.Add(brand);
            foreach (Page p in pages)
            {
                NavButton b = new NavButton(p);
                b.Dock = DockStyle.Top;
                b.Height = em * 2 + em / 3;
                Page target = p;
                b.Click += delegate { Open(target); };
                buttons.Add(b);
                stack.Add(b);
            }
            // Docked controls stack from the last added, so add them in reverse.
            for (int i = stack.Count - 1; i >= 0; i--) menu.Controls.Add(stack[i]);

            Label folder = new Label();
            folder.Dock = DockStyle.Bottom;
            folder.AutoSize = false;
            folder.Height = em * 2;
            folder.ForeColor = Theme.Muted;
            folder.Font = new Font(Font.FontFamily, 8.5f);
            folder.Text = "Папка программы:\r\n" + AppPaths.Root;
            folder.AutoEllipsis = true;
            menu.Controls.Add(folder);
            ToolTip tip = new ToolTip();
            tip.SetToolTip(folder, AppPaths.Root);
        }

        public Page Find(string key)
        {
            foreach (Page p in pages) if (p.Key == key) return p;
            return null;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (current == null) Open(pages[0]);
        }

        public void Open(Page page)
        {
            if (current != page)
            {
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
                int x = em / 2;
                using (Font big = new Font("Segoe UI Black", 12.5f))
                {
                    string brand = "CHAGEAR ТУЛБОКС";
                    int colorIndex = 0;
                    TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                    foreach (char ch in brand)
                    {
                        string s = ch.ToString();
                        if (ch == ' ')
                        {
                            // The gear of the console header, between the two words.
                            x += em / 4;
                            Glyph.Draw(e.Graphics, Glyph.Settings, new Rectangle(x, 0, em, big.Height), Theme.Muted, em * 0.85f);
                            x += em + em / 4;
                            continue;
                        }
                        Size size = TextRenderer.MeasureText(e.Graphics, s, big, Size.Empty, flags);
                        TextRenderer.DrawText(e.Graphics, s, big, new Point(x, 0), Theme.Rainbow[colorIndex % Theme.Rainbow.Length], flags);
                        colorIndex++;
                        x += size.Width;
                    }
                    TextRenderer.DrawText(e.Graphics, "версия " + AppVersion.Text, Font, new Point(em / 2, big.Height + em / 6), Theme.Muted, flags);
                }
            }
        }

        // Menu item: icon and title; the open section is marked with a green bar.
        class NavButton : Control
        {
            public readonly Page Page;
            bool selected, hover;

            public NavButton(Page page)
            {
                Page = page;
                Text = page.Title;
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
                Color back = selected ? Color.White : hover ? Theme.SidebarHover : Theme.Sidebar;
                e.Graphics.Clear(Theme.Sidebar);
                Rectangle r = new Rectangle(0, 2, Width, Height - 4);
                using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, r);
                if (selected)
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 0, r.Top + em / 3, 3, r.Height - em * 2 / 3);
                Color iconColor = selected ? Theme.Accent : Theme.Muted;
                Glyph.Draw(e.Graphics, Page.Icon, new Rectangle(em / 2, r.Top, em * 3 / 2, r.Height), iconColor, em * 0.95f);
                using (Font f = selected ? new Font(Font, FontStyle.Bold) : new Font(Font, FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics, Text, f, new Rectangle(em * 2 + em / 3, r.Top, Width - em * 3, r.Height), Theme.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (Page.NeedsAdmin)
                    Glyph.Draw(e.Graphics, Glyph.Shield, new Rectangle(Width - em * 3 / 2, r.Top, em, r.Height), Theme.Muted, em * 0.7f);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, r);
            }
        }
    }
}
