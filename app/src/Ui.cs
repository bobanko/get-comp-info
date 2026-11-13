using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CompInfo
{
    // Windows icon font (Segoe MDL2 Assets, Windows 10 and 11): crisp monochrome icons in any colour and size.
    public static class Glyph
    {
        public const string Computer = "", Network = "", Tag = "", Domain = "", Ping = "",
            Database = "", Usb = "", Sync = "", Update = "", Refresh = "", Export = "",
            Copy = "", Save = "", Search = "", Check = "", Cross = "", Warning = "",
            Shield = "", Open = "", Back = "", Forward = "", Folder = "", Settings = "",
            Circle = "", Dash = "", More = "", Info = "", Cloud = "", Upload = "",
            Power = "", Lock = "", Person = "", Theme = "", Delete = "\uE74D", DockBottom = "\uE90E", DockRight = "\uE90D", Play = "", Globe = "\uE774";
        public const string Sun = "\uE706", Moon = "\uE708";

        static string family;

        public static string Family
        {
            get
            {
                if (family == null)
                {
                    family = "Segoe MDL2 Assets";
                    using (InstalledFontCollection fonts = new InstalledFontCollection())
                        foreach (FontFamily f in fonts.Families)
                            if (f.Name == "Segoe Fluent Icons") { family = f.Name; break; }
                }
                return family;
            }
        }

        public static Font Font(float pixels) { return new Font(Family, pixels, GraphicsUnit.Pixel); }

        public static Bitmap Image(string glyph, int size, Color color)
        {
            Bitmap b = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(b))
            using (Font f = Font(size * 0.86f))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                TextRenderer.DrawText(g, glyph, f, new Rectangle(0, 0, size, size), color, Color.Transparent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            return b;
        }

        public static void Draw(Graphics g, string glyph, Rectangle r, Color color, float pixels)
        {
            using (Font f = Font(pixels))
                TextRenderer.DrawText(g, glyph, f, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // A filled green shield with a white check: administrator access is already available.
        public static void DrawAdminReady(Graphics g, Rectangle r)
        {
            int side = Math.Max(6, Math.Min(r.Width, r.Height));
            using (Bitmap b = AdminReadyImage(side))
                g.DrawImage(b, r.Left + (r.Width - side) / 2, r.Top + (r.Height - side) / 2, side, side);
        }

        public static Bitmap AdminReadyImage(int size)
        {
            // Render the original Windows shield at high resolution and flood-fill the area it
            // encloses. This preserves exactly the same curved silhouette as the grey icon.
            int scale = 4, n = Math.Max(24, size * scale);
            Bitmap mask = new Bitmap(n, n);
            using (Graphics mg = Graphics.FromImage(mask))
            {
                mg.Clear(Color.Transparent);
                Draw(mg, Shield, new Rectangle(0, 0, n, n), Color.White, n * 0.92f);
            }

            bool[,] outside = new bool[n, n];
            Queue<Point> queue = new Queue<Point>();
            Action<int, int> add = delegate (int x, int y)
            {
                if (x < 0 || y < 0 || x >= n || y >= n || outside[x, y] || mask.GetPixel(x, y).A > 24) return;
                outside[x, y] = true;
                queue.Enqueue(new Point(x, y));
            };
            for (int i = 0; i < n; i++) { add(i, 0); add(i, n - 1); add(0, i); add(n - 1, i); }
            while (queue.Count > 0)
            {
                Point p = queue.Dequeue();
                add(p.X - 1, p.Y); add(p.X + 1, p.Y); add(p.X, p.Y - 1); add(p.X, p.Y + 1);
            }

            Color green = Color.FromArgb(0x0B, 0x6E, 0x36);
            Bitmap large = new Bitmap(n, n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (!outside[x, y]) large.SetPixel(x, y, green);
            mask.Dispose();

            using (Graphics lg = Graphics.FromImage(large))
            {
                lg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Draw(lg, Shield, new Rectangle(0, 0, n, n), Color.White, n * 0.92f);
                using (Pen check = new Pen(Color.White, Math.Max(4, n / 12)))
                {
                    check.StartCap = check.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    lg.DrawLines(check, new[] { new Point(n * 31 / 100, n * 53 / 100), new Point(n * 45 / 100, n * 67 / 100), new Point(n * 70 / 100, n * 39 / 100) });
                }
            }

            Bitmap result = new Bitmap(size, size);
            using (Graphics rg = Graphics.FromImage(result))
            {
                rg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                rg.DrawImage(large, new Rectangle(0, 0, size, size));
            }
            large.Dispose();
            return result;
        }
    }

    public static class Ui
    {
        public static readonly Font BaseFont = new Font("Segoe UI", 10f);

        // Technical values (IP, serial numbers, IDs), as the "mono" class of the web interface.
        public static readonly Font MonoFont = MakeMono();

        static Font MakeMono()
        {
            using (InstalledFontCollection fonts = new InstalledFontCollection())
                foreach (FontFamily f in fonts.Families)
                    if (f.Name == "Cascadia Mono") return new Font(f.Name, 9.5f);
            return new Font("Consolas", 10f);
        }

        static ToolTip tips;

        // Tooltip with the key combination of a button: Ui.Keys(save, "Ctrl+S").
        public static void Keys(Control c, string keys)
        {
            if (tips == null) tips = new ToolTip();
            tips.SetToolTip(c, c.Text.Trim() + "   (" + keys + ")");
        }

        public static Button Primary(string text, string glyph, Font font)
        {
            Button b = Plain(text, glyph, font, Theme.OnAccent);
            b.BackColor = Theme.Accent;
            b.ForeColor = Theme.OnAccent;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            b.FlatAppearance.MouseDownBackColor = Theme.AccentDown;
            b.EnabledChanged += delegate { b.BackColor = b.Enabled ? Theme.Accent : Theme.AccentDisabled; };
            return b;
        }

        public static Button Secondary(string text, string glyph, Font font)
        {
            Button b = Plain(text, glyph, font, Theme.Text);
            b.BackColor = Theme.Surface;
            b.ForeColor = Theme.Text;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Theme.SidebarHover;
            b.FlatAppearance.MouseDownBackColor = Theme.Border;
            return b;
        }

        // System-changing action: a shield until elevation is needed, then a confirmed-access badge.
        public static Button AdminPrimary(string text, Font font)
        {
            bool admin = PowerShell.IsAdministrator();
            Button b = Primary(text, admin ? null : Glyph.Shield, font);
            if (admin)
            {
                b.Image = Glyph.AdminReadyImage((int)(font.Height * 0.95f));
                b.TextImageRelation = TextImageRelation.ImageBeforeText;
                b.ImageAlign = ContentAlignment.MiddleLeft;
                b.Text = " " + text;
            }
            return b;
        }

        static Button Plain(string text, string glyph, Font font, Color iconColor)
        {
            int em = font.Height;
            Button b = new RoundButton();
            b.Font = font;
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(em / 2, em / 6, em / 2, em / 6);
            b.Margin = new Padding(0, 0, em / 2, 0);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            if (glyph != null)
            {
                b.Image = Glyph.Image(glyph, (int)(em * 0.95f), iconColor);
                b.TextImageRelation = TextImageRelation.ImageBeforeText;
                b.ImageAlign = ContentAlignment.MiddleLeft;
                b.Text = " " + text;
            }
            return b;
        }

        public static Label Text(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.AutoSize = true;
            l.Margin = new Padding(0);
            return l;
        }

        public static Label Heading(string text, Font font)
        {
            Label l = Text(text, new Font("Segoe UI Semibold", font.SizeInPoints + 1), Theme.Text);
            l.Margin = new Padding(0, font.Height / 2, 0, font.Height / 4);
            return l;
        }

        // Label that wraps inside its parent width.
        public static Label Paragraph(string text, Font font, Color color)
        {
            Label l = Text(text, font, color);
            l.AutoSize = false;
            l.Dock = DockStyle.Top;
            l.Resize += delegate { FitHeight(l); };
            l.TextChanged += delegate { FitHeight(l); };
            return l;
        }

        public static void FitHeight(Label l)
        {
            if (l.Width <= 0) return;
            Size s = TextRenderer.MeasureText(l.Text.Length == 0 ? " " : l.Text, l.Font, new Size(l.Width - l.Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak);
            int h = s.Height + l.Padding.Vertical;
            if (l.Height != h) l.Height = h;
        }

        public static FlowLayoutPanel Row()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.WrapContents = false;
            p.FlowDirection = FlowDirection.LeftToRight;
            p.Margin = new Padding(0);
            p.Padding = new Padding(0);
            return p;
        }

        public static TextBox Input(TextBox box)
        {
            box.BackColor = Theme.Surface;
            box.ForeColor = Theme.Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            Theme.Native(box);
            return box;
        }

        public static ComboBox Combo()
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Theme.Surface;
            c.ForeColor = Theme.Text;
            Theme.Native(c);
            return c;
        }

        // Read-only table in the colours of the theme.
        public static DataGridView Grid(Font font)
        {
            int em = font.Height;
            DataGridView grid = new DataGridView();
            grid.Font = font;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BackgroundColor = Theme.Surface;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Theme.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.HeaderBack;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.HeaderBack;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            // Web table header: 12px semibold, muted, on surface-2.
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            grid.ColumnHeadersHeight = em * 2;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowTemplate.Height = em + em / 2;
            grid.DefaultCellStyle.BackColor = Theme.Surface;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.Selection;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            DoubleBuffer(grid);
            Theme.Native(grid);
            return grid;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        // Grey hint inside an empty text box.
        public static void SetCue(TextBox box, string cue)
        {
            box.HandleCreated += delegate { SendMessage(box.Handle, 0x1501, (IntPtr)1, cue); };
            if (box.IsHandleCreated) SendMessage(box.Handle, 0x1501, (IntPtr)1, cue);
        }

        public static void ShowInExplorer(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch (Exception) { }
        }

        // Question in the colours of the program; returns the index of the pressed button, or -1 when closed.
        public static int Ask(IWin32Window owner, string title, string text, string glyph, Color glyphColor, params string[] buttons)
        {
            using (AskDialog d = new AskDialog(title, text, glyph, glyphColor, buttons))
            {
                d.ShowDialog(owner);
                return d.Answer;
            }
        }

        public static void Error(IWin32Window owner, string text)
        {
            Ask(owner, L.T("Ошибка"), text, Glyph.Warning, Theme.Bad, "OK");
        }

        public static void Info(IWin32Window owner, string title, string text)
        {
            Ask(owner, title, text, Glyph.Info, Theme.Info, "OK");
        }

        static Bitmap gearImage, faceImage, faceRuImage;

        // Plain gear (96 px) for the small picture in the window header, as in the small icon sizes.
        public static Bitmap AppImage { get { return gearImage ?? (gearImage = Resource("CompInfo.brand.png")); } }

        // The bearded gear itself (256 px) for «О программе».
        public static Bitmap FaceImage { get { return faceImage ?? (faceImage = Resource("CompInfo.face.png")); } }

        // The alternative picture of «О программе», shown only with the Russian interface.
        public static Bitmap FaceRuImage { get { return faceRuImage ?? (faceRuImage = Resource("CompInfo.face-ru.png")); } }

        // The picture of SUSpector (256 px, as its icon) for its «О программе»; only SUSpector.exe embeds it.
        public static Bitmap SuspectorImage { get { return suspectorImage ?? (suspectorImage = Resource("CompInfo.suspector.png")); } }
        static Bitmap suspectorImage;

        // The picture of youSBee for its «О программе»; only youSBee.exe embeds it.
        public static Bitmap YouSBeeImage { get { return youSBeeImage ?? (youSBeeImage = Resource("CompInfo.yousbee.png")); } }
        static Bitmap youSBeeImage;

        // Pictures embedded by tools\build.BAT (made by tools\make-icon.BAT).
        static Bitmap Resource(string name)
        {
            try { return new Bitmap(typeof(Ui).Assembly.GetManifestResourceStream(name)); }
            catch (Exception) { return new Bitmap(1, 1); }
        }

        public static string Date(string value)
        {
            DateTime d;
            if (DateTime.TryParse(value ?? "", out d)) return d.ToString("yyyy-MM-dd HH:mm");
            return "";
        }

        public static void DoubleBuffer(Control c)
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(c, true, null);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        static IntPtr ExeIcon(Size size)
        {
            IntPtr[] h = new IntPtr[1];
            int[] id = new int[1];
            try { return PrivateExtractIcons(Application.ExecutablePath, 0, size.Width, size.Height, h, id, 1, 0) == 1 ? h[0] : IntPtr.Zero; }
            catch (Exception) { return IntPtr.Zero; }
        }

        // Title bar and taskbar icons taken from the program file at the exact sizes Windows uses
        // (not through Explorer, whose icon cache may still hold an old picture): small sizes are the plain gear.
        public static void WindowIcon(Form f)
        {
            IntPtr big = ExeIcon(SystemInformation.IconSize);
            if (big != IntPtr.Zero) f.Icon = Icon.FromHandle(big);
            IntPtr small = ExeIcon(SystemInformation.SmallIconSize);
            MethodInvoker apply = delegate
            {
                if (f.IsDisposed) return;
                if (small != IntPtr.Zero) SendMessage(f.Handle, 0x80, IntPtr.Zero, small);   // WM_SETICON, ICON_SMALL
                if (big != IntPtr.Zero) SendMessage(f.Handle, 0x80, (IntPtr)1, big);         // ICON_BIG
            };
            // WinForms sets its own icons (the big one scaled down) while creating the window: ours go after it.
            f.HandleCreated += delegate { f.BeginInvoke(apply); };
            f.Shown += delegate { apply(); };
        }

        // Form in the colours of the theme.
        public static void Dialog(Form f)
        {
            f.Font = BaseFont;
            f.BackColor = Theme.Back;
            f.ForeColor = Theme.Text;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ShowInTaskbar = false;
            f.MinimizeBox = false;
            WindowIcon(f);
            Theme.TitleBar(f);
        }
    }

    // A program name with a coloured part, as on chagear.top: «[SUS]pector», «you[SBee]», «CHAGEAR [Тулбокс]».
    // The part in square brackets is drawn in the program's colour, the rest in the text colour.
    public class TitleText : Control
    {
        readonly List<string> parts = new List<string>();
        readonly List<bool> marked = new List<bool>();
        readonly Color highlight;
        readonly int inset;
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        public TitleText(string title, Font font, Color highlight)
        {
            this.highlight = highlight;
            Font = font;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            string rest = title ?? "";
            while (rest.Length > 0)
            {
                int open = rest.IndexOf('['), close = open < 0 ? -1 : rest.IndexOf(']', open);
                if (open < 0 || close < 0) { Add(rest, false); break; }
                Add(rest.Substring(0, open), false);
                Add(rest.Substring(open + 1, close - open - 1), true);
                rest = rest.Substring(close + 1);
            }
            Text = Plain(title);
            // The same left inset as the labels around it, so the name lines up with them.
            inset = (TextRenderer.MeasureText("a", Font).Width - TextRenderer.MeasureText("a", Font, Size.Empty, Flags).Width) / 2;
            Size = new Size(inset * 2 + StartOf(parts.Count), Font.Height + Font.Height / 6);
        }

        void Add(string part, bool isMarked)
        {
            if (part.Length == 0) return;
            parts.Add(part);
            marked.Add(isMarked);
        }

        // The name without the marks, for window titles.
        public static string Plain(string title) { return (title ?? "").Replace("[", "").Replace("]", ""); }

        // Where part i starts: the width of everything before it (trailing spaces included).
        int StartOf(int i)
        {
            string before = string.Concat(parts.GetRange(0, i).ToArray());
            if (before.Length == 0) return 0;
            // GDI drops trailing spaces when measuring; a closing character keeps them, then it is taken away.
            return TextRenderer.MeasureText(before + "|", Font, Size.Empty, Flags).Width - TextRenderer.MeasureText("|", Font, Size.Empty, Flags).Width;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            for (int i = 0; i < parts.Count; i++)
                TextRenderer.DrawText(e.Graphics, parts[i], Font, new Point(inset + StartOf(i), 0), marked[i] ? highlight : Theme.Text, Flags);
        }
    }

    // «О программе» of comp-info (AboutPage) and the standalone programs (StandaloneAboutDialog), as in SupraWeed:
    // the picture on the left; name, version, sections and how to reach the developer on the right.
    public static class About
    {
        // Address of the toolbox; SUSpector and youSBee have their own (suspector@, yousbee@chagear.top).
        public const string Email = "toolbox@chagear.top";
        public const string WebSite = "chagear.top";

        // Builds everything up to the developer's e-mail; the caller adds its own links to the returned column.
        public static TableLayoutPanel Build(Font font, int width, Image picture, string title, string subtitle, string intro, string[] sections, out FlowLayoutPanel text)
        {
            return Build(font, width, picture, title, Theme.Accent, Email, subtitle, intro, sections, out text);
        }

        // The title may mark a coloured part in square brackets (TitleText): «[SUS]pector».
        public static TableLayoutPanel Build(Font font, int width, Image picture, string title, Color highlight, string email,
            string subtitle, string intro, string[] sections, out FlowLayoutPanel text)
        {
            int em = font.Height;
            TableLayoutPanel t = new TableLayoutPanel();
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.Margin = Padding.Empty;

            int side = em * 7;
            PictureBox pic = new PictureBox();
            pic.Image = picture;
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.Size = new Size(side, side);
            pic.Margin = new Padding(0, 0, em, 0);
            t.Controls.Add(pic, 0, 0);

            text = new FlowLayoutPanel();
            text.FlowDirection = FlowDirection.TopDown;
            text.WrapContents = false;
            text.AutoSize = true;
            text.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            text.Margin = Padding.Empty;
            t.Controls.Add(text, 1, 0);

            Font titleFont = new Font("Segoe UI Semibold", 16f);
            if (title.IndexOf('[') >= 0)
            {
                TitleText name = new TitleText(title, titleFont, highlight);
                name.Margin = new Padding(0, 0, 0, em / 8);
                text.Controls.Add(name);
            }
            else text.Controls.Add(Line(title, titleFont, Theme.Text, width, 0));
            text.Controls.Add(Line(subtitle, font, Theme.Text, width, 0));
            text.Controls.Add(Line(L.T("Версия ") + AppVersion.Text + (PowerShell.IsAdministrator() ? L.T(" · запущена с правами администратора") : ""), font, Theme.Muted, width, em / 2));
            if (!string.IsNullOrEmpty(intro)) text.Controls.Add(Line(intro, font, Theme.Text, width, em / 2));

            Font bold = new Font(font, FontStyle.Bold);
            for (int i = 0; i + 1 < sections.Length; i += 2)
            {
                text.Controls.Add(Line(sections[i], bold, Theme.Text, width, 0));
                text.Controls.Add(Line(sections[i + 1], font, Theme.Text, width, em / 2));
            }

            text.Controls.Add(Line(L.T("Связь с разработчиком"), bold, Theme.Text, width, 0));
            Label copied = Line(L.T("← нажмите, чтобы скопировать"), font, Theme.Muted, width, 0);
            // Service computers have no mail client: a click copies the address instead of opening mailto.
            text.Controls.Add(LinkRow(font, Glyph.Person, email, copied, delegate
            {
                try { Clipboard.SetText(email); copied.Text = L.T("скопировано ✓"); } catch (Exception) { }
            }));
            return t;
        }

        public static Label Line(string s, Font font, Color color, int width, int bottom)
        {
            Label l = Ui.Text(s, font, color);
            l.MaximumSize = new Size(width, 0);
            l.Margin = new Padding(0, 0, 0, bottom);
            return l;
        }

        public static FlowLayoutPanel LinkRow(Font font, string glyph, string text, Label note, LinkLabelLinkClickedEventHandler click)
        {
            int em = font.Height;
            FlowLayoutPanel row = Ui.Row();
            PictureBox icon = new PictureBox();
            icon.Image = Glyph.Image(glyph, em, Theme.Muted);
            icon.SizeMode = PictureBoxSizeMode.AutoSize;
            icon.Margin = new Padding(0, 3, em / 4, 3);
            LinkLabel link = new LinkLabel();
            link.Text = text;
            link.Font = font;
            link.AutoSize = true;
            link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = Theme.Info;
            link.Margin = new Padding(0, 3, 0, 3);
            link.LinkClicked += click;
            row.Controls.Add(icon);
            row.Controls.Add(link);
            if (note != null) { note.Margin = new Padding(em / 2, 3, 0, 3); row.Controls.Add(note); }
            return row;
        }

        public static FlowLayoutPanel SiteRow(Font font, string glyph, string site, Label note)
        {
            return LinkRow(font, glyph, site, note, delegate { OpenSite(site); });
        }

        public static FlowLayoutPanel FolderRow(Font font, string folder)
        {
            return LinkRow(font, Glyph.Folder, L.T("Открыть папку программы"), null, delegate
            {
                try { Process.Start("explorer.exe", "\"" + folder + "\""); } catch (Exception) { }
            });
        }

        // Opens in the default browser; if that fails (no browser, locked down PC), the address is copied.
        public static void OpenSite(string site)
        {
            try { Process.Start(new ProcessStartInfo("https://" + site) { UseShellExecute = true }); }
            catch (Exception) { try { Clipboard.SetText("https://" + site); } catch (Exception) { } }
        }
    }

    // Message with an icon and buttons; replaces MessageBox, which ignores the dark theme.
    public class AskDialog : Form
    {
        public int Answer = -1;

        public AskDialog(string title, string text, string glyph, Color glyphColor, string[] buttons)
        {
            Ui.Dialog(this);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            int em = Font.Height;
            Padding = new Padding(em);

            PictureBox icon = new PictureBox();
            icon.Image = Glyph.Image(glyph, em * 2, glyphColor);
            icon.Size = new Size(em * 2, em * 2);
            icon.Margin = new Padding(0, 0, em, 0);
            Label message = Ui.Text(text, Font, Theme.Text);
            message.MaximumSize = new Size(em * 28, 0);
            message.Margin = new Padding(0, em / 4, 0, em);

            FlowLayoutPanel actions = Ui.Row();
            actions.FlowDirection = FlowDirection.RightToLeft;
            actions.Anchor = AnchorStyles.Right;
            for (int i = buttons.Length - 1; i >= 0; i--)
            {
                int index = i;
                Button b = i == 0 ? Ui.Primary(buttons[i], null, Font) : Ui.Secondary(buttons[i], null, Font);
                b.Margin = new Padding(em / 2, 0, 0, 0);
                b.MinimumSize = new Size(em * 5, 0);
                b.Click += delegate { Answer = index; Close(); };
                actions.Controls.Add(b);
                if (i == 0) AcceptButton = b;
            }

            TableLayoutPanel t = new TableLayoutPanel();
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = 2;
            t.Controls.Add(icon, 0, 0);
            t.Controls.Add(message, 1, 0);
            t.Controls.Add(actions, 0, 1);
            t.SetColumnSpan(actions, 2);
            t.Location = new Point(em, em);
            Controls.Add(t);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;
            KeyDown += delegate (object o, KeyEventArgs k) { if (k.KeyCode == Keys.Escape) Close(); };
        }
    }

    // Rounded rectangles of the web look (border-radius).
    public static class Shapes
    {
        public static System.Drawing.Drawing2D.GraphicsPath Round(Rectangle r, int radius)
        {
            System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Pill label ("badge" of the web interface): soft background, coloured text.
        public static void Badge(Graphics g, Rectangle cell, string text, Font font, Color fore, Color back, bool center)
        {
            if (string.IsNullOrEmpty(text)) return;
            int em = font.Height;
            Size size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int w = Math.Min(cell.Width - 4, size.Width + em * 2 / 3), h = Math.Min(cell.Height - 4, em + 2);
            int x = center ? cell.X + (cell.Width - w) / 2 : cell.X + em / 4, y = cell.Y + (cell.Height - h) / 2;
            Rectangle r = new Rectangle(x, y, w, h);
            System.Drawing.Drawing2D.SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Drawing2D.GraphicsPath path = Round(r, h / 2))
            using (SolidBrush b = new SolidBrush(back)) g.FillPath(b, path);
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, text, font, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // Kind of a badge cell: its colours follow the theme.
    public enum BadgeKind { None, Ok, Warn, Danger, Muted }

    public static class Badges
    {
        // Cells of these columns with a BadgeKind in Tag are drawn as pills (ESET, cloud status).
        public static void Enable(DataGridView grid, params int[] columns)
        {
            Font font = new Font("Segoe UI", 9f);
            grid.CellPainting += delegate (object o, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || Array.IndexOf(columns, e.ColumnIndex) < 0) return;
                DataGridViewCell cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (!(cell.Tag is BadgeKind) || (BadgeKind)cell.Tag == BadgeKind.None) return;
                e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                Color fore, back;
                Colors((BadgeKind)cell.Tag, out fore, out back);
                Shapes.Badge(e.Graphics, e.CellBounds, Convert.ToString(e.FormattedValue), font, fore, back, false);
                e.Handled = true;
            };
        }

        public static void Colors(BadgeKind kind, out Color fore, out Color back)
        {
            switch (kind)
            {
                case BadgeKind.Ok: fore = Theme.Good; back = Theme.GoodLight; break;
                case BadgeKind.Warn: fore = Theme.Warn; back = Theme.WarnLight; break;
                case BadgeKind.Danger: fore = Theme.Bad; back = Theme.BadLight; break;
                default: fore = Theme.Muted; back = Theme.HeaderBack; break;
            }
        }
    }

    // Flat button with rounded corners, like the buttons of the web interface.
    public class RoundButton : Button
    {
        bool hover, down;

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parent = Parent != null ? Parent.BackColor : Theme.Back;
            if (parent == Color.Transparent) parent = Theme.Back;
            g.Clear(parent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color back = !Enabled ? BackColor : down ? FlatAppearance.MouseDownBackColor : hover ? FlatAppearance.MouseOverBackColor : BackColor;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (System.Drawing.Drawing2D.GraphicsPath path = Shapes.Round(r, 6))
            {
                using (SolidBrush b = new SolidBrush(back)) g.FillPath(b, path);
                if (FlatAppearance.BorderSize > 0)
                    using (Pen pen = new Pen(FlatAppearance.BorderColor)) g.DrawPath(pen, path);
                // Keyboard focus: the accent outline of the web interface (focus-visible).
                if (Focused && ShowFocusCues)
                    using (Pen pen = new Pen(Theme.Accent, 2)) g.DrawPath(pen, path);
            }
            Color fore = Enabled ? ForeColor : Theme.Mix(ForeColor, back, 0.45);
            Size text = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPrefix);
            int iconW = Image != null ? Image.Width : 0;
            int x = (Width - iconW - text.Width) / 2;
            if (Image != null)
            {
                if (Enabled) g.DrawImage(Image, x, (Height - Image.Height) / 2, Image.Width, Image.Height);
                else ControlPaint.DrawImageDisabled(g, Image, x, (Height - Image.Height) / 2, back);
            }
            TextRenderer.DrawText(g, Text, Font, new Point(x + iconW, (Height - text.Height) / 2), fore, TextFormatFlags.NoPrefix);
        }
    }

    // A row of buttons where exactly one is chosen (theme, language), like the segmented toggles of the web.
    public class Segmented : Control
    {
        readonly string[] items, tips;
        readonly bool glyphs;
        int selected, hover = -1;
        readonly ToolTip tip = new ToolTip();
        public event Action<int> Changed;
        // Glyph size as a part of the font height.
        public float GlyphScale = 0.8f;

        public Segmented(string[] items, bool glyphs, string[] tips, int selected)
        {
            this.items = items;
            this.glyphs = glyphs;
            this.tips = tips;
            this.selected = selected;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        int At(int x) { return Math.Max(0, Math.Min(items.Length - 1, x * items.Length / Math.Max(1, Width))); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = At(e.X);
            if (i != hover) { hover = i; Invalidate(); tip.SetToolTip(this, tips[i]); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = At(e.X);
            if (i != selected) { selected = i; Invalidate(); if (Changed != null) Changed(i); }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Sidebar);
            int em = Font.Height;
            Rectangle all = new Rectangle(0, 0, Width - 1, Height - 1);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Drawing2D.GraphicsPath outline = Shapes.Round(all, 6))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, outline);
                g.SetClip(outline);
                for (int i = 0; i < items.Length; i++)
                {
                    int x0 = i * Width / items.Length, x1 = (i + 1) * Width / items.Length;
                    Rectangle r = new Rectangle(x0, 0, x1 - x0, Height);
                    Color back = i == selected ? Theme.AccentSoft : i == hover ? Theme.HeaderBack : Theme.Surface;
                    Color fore = i == selected ? Theme.Accent : Theme.Text;
                    using (SolidBrush b = new SolidBrush(back)) g.FillRectangle(b, r);
                    if (i > 0) using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, x0, 0, x0, Height);
                    if (glyphs) Glyph.Draw(g, items[i], r, fore, em * GlyphScale);
                    else
                        using (Font f = new Font(Font.FontFamily, 8.5f, i == selected ? FontStyle.Bold : FontStyle.Regular))
                            TextRenderer.DrawText(g, items[i], f, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                g.ResetClip();
                using (Pen p = new Pen(Theme.Border)) g.DrawPath(p, outline);
            }
        }
    }

    // Status message: muted text, or for success, warning and error a soft rounded panel
    // with an icon, like the notices of the web interface.
    public class StatusLine : Control
    {
        string glyph;
        Color color = Theme.Muted;

        public StatusLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = new Padding(0);
            Visible = false;
        }

        public void Set(string glyph, string text, Color color)
        {
            this.glyph = glyph;
            this.color = color;
            Text = text;
            Visible = !string.IsNullOrEmpty(text);
            FitHeight();
            Invalidate();
        }

        public void Clear() { Set(null, "", Theme.Muted); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); FitHeight(); }

        // The soft panel colour, or empty for plain text.
        Color Panel
        {
            get
            {
                if (color == Theme.Good) return Theme.GoodLight;
                if (color == Theme.Warn) return Theme.WarnLight;
                if (color == Theme.Bad) return Theme.BadLight;
                if (color == Theme.Info || color == Theme.Accent) return Theme.AccentSoft;
                return Color.Empty;
            }
        }

        int Inner { get { return Panel.IsEmpty ? 0 : Font.Height / 2; } }

        int TextLeft { get { return glyph == null ? 0 : Font.Height + Font.Height / 3; } }

        void FitHeight()
        {
            int inner = Inner;
            int width = Width - TextLeft - Padding.Horizontal - inner * 2;
            if (width <= 0) return;
            Size s = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
            int h = Math.Max(s.Height, Font.Height) + 2 + Padding.Vertical + inner * 2 - (inner > 0 ? Font.Height / 4 : 0);
            if (Height == h) return;
            Height = h;
            // The height changes while the parent lays out its children: a docked status line would keep the old
            // place (e.g. below the visible part of the page), so the parent lays them out again once this layout is over.
            Control parent = Parent;
            if (parent == null || Dock == DockStyle.None) return;
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (!parent.IsDisposed) parent.PerformLayout(); });
            else parent.PerformLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int em = Font.Height;
            int inner = Inner;
            Rectangle box = new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal - 1, Height - Padding.Vertical - 1);
            if (!Panel.IsEmpty)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (System.Drawing.Drawing2D.GraphicsPath path = Shapes.Round(box, 8))
                {
                    using (SolidBrush b = new SolidBrush(Panel)) e.Graphics.FillPath(b, path);
                    using (Pen p = new Pen(Theme.Mix(Panel, color, 0.3))) e.Graphics.DrawPath(p, path);
                }
            }
            int top = box.Top + (inner > 0 ? inner - em / 8 : 1);
            int left = box.Left + inner;
            if (glyph != null) Glyph.Draw(e.Graphics, glyph, new Rectangle(left, top, em, em), color, em * 0.8f);
            Color textColor = Panel.IsEmpty ? color : Theme.Text;
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left + TextLeft, top, box.Width - TextLeft - inner * 2, box.Height - (top - box.Top)), textColor,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
        }
    }
}
