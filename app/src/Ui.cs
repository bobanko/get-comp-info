using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CompInfo
{
    public static class Theme
    {
        public static readonly Color Accent = Color.FromArgb(0x1E, 0x8C, 0x5A);
        public static readonly Color AccentHover = Color.FromArgb(0x24, 0xA0, 0x67);
        public static readonly Color AccentDown = Color.FromArgb(0x17, 0x70, 0x48);
        public static readonly Color AccentLight = Color.FromArgb(0xE6, 0xF4, 0xEC);
        public static readonly Color Sidebar = Color.FromArgb(0xF3, 0xF5, 0xF4);
        public static readonly Color SidebarHover = Color.FromArgb(0xE7, 0xEB, 0xE9);
        public static readonly Color Border = Color.FromArgb(0xDD, 0xE1, 0xDF);
        public static readonly Color Text = Color.FromArgb(0x1F, 0x29, 0x37);
        public static readonly Color Muted = Color.FromArgb(0x6B, 0x72, 0x80);
        public static readonly Color Good = Color.FromArgb(0x2E, 0x7D, 0x32);
        public static readonly Color GoodLight = Color.FromArgb(0xE8, 0xF5, 0xE9);
        public static readonly Color Warn = Color.FromArgb(0xB2, 0x6A, 0x00);
        public static readonly Color WarnLight = Color.FromArgb(0xFF, 0xF4, 0xCC);
        public static readonly Color Bad = Color.FromArgb(0xC6, 0x28, 0x28);
        public static readonly Color BadLight = Color.FromArgb(0xFD, 0xEC, 0xEA);
        public static readonly Color Info = Color.FromArgb(0x15, 0x65, 0xC0);
        // Letters of the brand, as in the console header (darker shades for a light background).
        public static readonly Color[] Rainbow =
        {
            Color.FromArgb(0xD3, 0x2F, 0x2F), Color.FromArgb(0xE0, 0x8E, 0x00), Color.FromArgb(0x2E, 0x7D, 0x32),
            Color.FromArgb(0x00, 0x83, 0x8F), Color.FromArgb(0x15, 0x65, 0xC0), Color.FromArgb(0x8E, 0x24, 0xAA)
        };
    }

    // Windows icon font (Segoe MDL2 Assets, Windows 10 and 11): crisp monochrome icons in any colour and size.
    public static class Glyph
    {
        public const string Computer = "", Network = "", Tag = "", Domain = "", Ping = "",
            Database = "", Usb = "", Sync = "", Update = "", Refresh = "", Export = "",
            Copy = "", Save = "", Search = "", Check = "", Cross = "", Warning = "",
            Shield = "", Open = "", Back = "", Forward = "", Folder = "", Settings = "",
            Circle = "", Dash = "", More = "", Info = "";

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

        public static Font Font(float pixels)
        {
            return new Font(Family, pixels, GraphicsUnit.Pixel);
        }

        public static Bitmap Image(string glyph, int size, Color color)
        {
            Bitmap b = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(b))
            using (Font f = Font(size * 0.86f))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                TextRenderer.DrawText(g, glyph, f, new Rectangle(0, 0, size, size), color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            return b;
        }

        public static void Draw(Graphics g, string glyph, Rectangle r, Color color, float pixels)
        {
            using (Font f = Font(pixels))
                TextRenderer.DrawText(g, glyph, f, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public static class Ui
    {
        public static int Em(Control c) { return c.Font.Height; }

        public static Button Primary(string text, string glyph, Font font)
        {
            Button b = Plain(text, glyph, font, Color.White);
            b.BackColor = Theme.Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Theme.AccentHover;
            b.FlatAppearance.MouseDownBackColor = Theme.AccentDown;
            b.EnabledChanged += delegate { b.BackColor = b.Enabled ? Theme.Accent : Color.FromArgb(0xB8, 0xC9, 0xC0); };
            return b;
        }

        public static Button Secondary(string text, string glyph, Font font)
        {
            Button b = Plain(text, glyph, font, Theme.Text);
            b.BackColor = Color.White;
            b.ForeColor = Theme.Text;
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Theme.SidebarHover;
            b.FlatAppearance.MouseDownBackColor = Theme.Border;
            return b;
        }

        static Button Plain(string text, string glyph, Font font, Color iconColor)
        {
            int em = font.Height;
            Button b = new Button();
            b.Font = font;
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(em / 2, em / 5, em / 2, em / 5);
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

        public static void Error(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static string Date(string value)
        {
            DateTime d;
            if (DateTime.TryParse(value ?? "", out d)) return d.ToString("yyyy-MM-dd HH:mm");
            return "";
        }

        // Every column as wide as its widest value or its header; the last one takes the rest.
        static bool fitting;

        public static void FitColumns(ListView list)
        {
            // Changing widths can show or hide the scroll bar, which resizes the list and calls this again.
            if (fitting || list.Columns.Count == 0) return;
            fitting = true;
            try { FitColumnsOnce(list); }
            finally { fitting = false; }
        }

        static void FitColumnsOnce(ListView list)
        {
            int total = 0;
            for (int i = 0; i < list.Columns.Count; i++)
            {
                list.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
                int content = list.Columns[i].Width;
                int header = TextRenderer.MeasureText(list.Columns[i].Text, list.Font).Width + list.Font.Height;
                list.Columns[i].Width = Math.Max(content + list.Font.Height / 2, header);
                total += list.Columns[i].Width;
            }
            int last = list.Columns.Count - 1;
            // A little less than the full width, so the horizontal scroll bar never appears.
            int rest = list.ClientSize.Width - (total - list.Columns[last].Width) - 4;
            if (rest > list.Columns[last].Width) list.Columns[last].Width = rest;
        }

        public static void DoubleBuffer(Control c)
        {
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(c, true, null);
        }
    }

    // Small coloured status line: an icon and a text, e.g. "✓ Сохранено".
    public class StatusLine : Control
    {
        string glyph;
        Color color = Theme.Muted;

        public StatusLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = new Padding(0);
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

        int TextLeft { get { return glyph == null ? 0 : Font.Height + Font.Height / 3; } }

        void FitHeight()
        {
            if (Width <= TextLeft) return;
            Size s = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(Width - TextLeft, int.MaxValue), TextFormatFlags.WordBreak);
            int h = Math.Max(s.Height, Font.Height) + 2;
            if (Height != h) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int em = Font.Height;
            if (glyph != null) Glyph.Draw(e.Graphics, glyph, new Rectangle(0, 1, em, em), color, em * 0.8f);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(TextLeft, 1, Width - TextLeft, Height), color == Theme.Muted ? Theme.Muted : color,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
        }
    }
}
