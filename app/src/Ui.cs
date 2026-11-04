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
            Power = "", Lock = "", Person = "", Theme = "", Play = "";

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
    }

    public static class Ui
    {
        public static readonly Font BaseFont = new Font("Segoe UI", 10f);

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

        static Button Plain(string text, string glyph, Font font, Color iconColor)
        {
            int em = font.Height;
            Button b = new Button();
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
            Ask(owner, "Ошибка", text, Glyph.Warning, Theme.Bad, "OK");
        }

        public static void Info(IWin32Window owner, string title, string text)
        {
            Ask(owner, title, text, Glyph.Info, Theme.Info, "OK");
        }

        static Bitmap gearImage, faceImage;

        // Plain gear (96 px) for the small picture in the window header, as in the small icon sizes.
        public static Bitmap AppImage { get { return gearImage ?? (gearImage = Resource("CompInfo.brand.png")); } }

        // The bearded gear itself (256 px) for «О программе».
        public static Bitmap FaceImage { get { return faceImage ?? (faceImage = Resource("CompInfo.face.png")); } }

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

        int TextLeft { get { return glyph == null ? 0 : Font.Height + Font.Height / 3; } }

        void FitHeight()
        {
            if (Width <= TextLeft) return;
            Size s = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(Width - TextLeft - Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak);
            int h = Math.Max(s.Height, Font.Height) + 2 + Padding.Vertical;
            if (Height != h) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            int em = Font.Height;
            int top = Padding.Top + 1;
            if (glyph != null) Glyph.Draw(e.Graphics, glyph, new Rectangle(Padding.Left, top, em, em), color, em * 0.8f);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left + TextLeft, top, Width - TextLeft - Padding.Horizontal, Height - top), color,
                TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
        }
    }
}
