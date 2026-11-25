using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CompInfo.Wpf
{
    // Tray icons drawn from code (System.Drawing): a violet outline flash drive copied from the standard Windows
    // removable-drive tray icon (a wide connector with two key holes on top of a square-cornered body) with a
    // status symbol laid straight over it — no badge circle. Each symbol first punches a transparent halo that
    // breaks the drive's outline around it, then is drawn in its colour:
    // three dots = waiting for a flash drive (grey), arrow down = a backup is being copied (green),
    // pause = paused (orange), "!" = error (red), a number = how many flash drives were processed (green).
    //
    // Everything is drawn pixel by pixel from whole-pixel rectangles (no paths, no antialiasing), at the real
    // tray-icon size (SM_CXSMICON, DPI-aware), so the shell does not rescale it and every edge stays crisp.
    public static class TrayIcons
    {
        // An HICON from Bitmap.GetHicon() must be freed by hand, otherwise GDI leaks.
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);
        const int SM_CXSMICON = 49;   // the small-icon (tray) size, scaled by the DPI

        public enum Badge { None, Wait, Down, Pause, Error, Count }

        static readonly Color Violet = Color.FromArgb(139, 92, 246);   // the outline

        // Status-symbol colours.
        public static readonly Color Wait    = Color.FromArgb(139, 146, 156); // waiting — grey dots
        public static readonly Color DownOn  = Color.FromArgb(131, 214, 54);  // copying — bright phase
        public static readonly Color DownOff = Color.FromArgb(78, 127, 34);   // copying — dim phase
        public static readonly Color Pause   = Color.FromArgb(245, 150, 30);  // paused — orange
        public static readonly Color Error   = Color.FromArgb(214, 60, 50);   // error — red
        public static readonly Color Done    = Color.FromArgb(131, 214, 54);  // done count — green

        static int TraySize
        {
            get { try { int s = GetSystemMetrics(SM_CXSMICON); return s >= 8 ? s : 16; } catch (Exception) { return 16; } }
        }

        // Violet outline flash drive + a status symbol, rendered at the real tray size.
        public static Icon Make(Badge badge, Color color, string text = null)
        {
            return ToIcon(RenderFrame(TraySize, badge, color, text));
        }

        // The same drive with no status symbol, at a given size: the windows' title-bar icon (TitleIcon).
        public static Icon MakeDrive(int size)
        {
            return ToIcon(RenderFrame(size, Badge.None, Color.Empty, null));
        }

        static Icon ToIcon(Bitmap frame)
        {
            using (var bmp = frame)
            {
                IntPtr handle = bmp.GetHicon();
                try { using (var tmp = Icon.FromHandle(handle)) return (Icon)tmp.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        // One icon bitmap at a given pixel size. Public so previews can render it.
        public static Bitmap RenderFrame(int size, Badge badge, Color color, string text)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                DrawDrive(g, size);
            }
            bool[,] mask = SymbolMask(size, badge, text);
            if (mask != null) Overlay(bmp, mask, color, Math.Max(1, R(size / 20.0)));
            return bmp;
        }

        static int R(double v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }

        // The nearest odd width not above v (at least 1), so a shape has a centre column.
        static int Odd(double v) { int n = (int)Math.Floor(v); return Math.Max(1, n % 2 == 1 ? n : n - 1); }

        // The outline flash drive, in whole pixels. Proportions from the Windows removable-drive icon at 20 px:
        // a 9 px connector (rows 0-5) with two 1 px key holes, sitting 1 px inside an 11 px body (rows 5-19);
        // the connector's bottom line is the body's top line.
        static void DrawDrive(Graphics g, int s)
        {
            int pw = Math.Max(1, s / 16);                 // stroke: 1 px up to 31 px, 2 px at 32 (200 %), ...
            int bw = R(s * 0.55);                         // body width
            int ch = R(s * 0.30);                         // connector height, shared line included
            int x0 = (s - bw) / 2;
            int cx = x0 + pw, cw = bw - 2 * pw;           // the connector is one stroke narrower on each side
            int top = ch - pw;                            // the body's top line
            using (var br = new SolidBrush(Violet))
            {
                Frame(g, br, cx, 0, cw, ch, pw);
                Frame(g, br, x0, top, bw, s - top, pw);
                int gap = Math.Max(1, R(s / 20.0)), hy = pw + gap;
                g.FillRectangle(br, cx + pw + gap, hy, pw, pw);
                g.FillRectangle(br, cx + cw - pw - gap - pw, hy, pw, pw);
            }
        }

        // A rectangle outline with square corners, pw pixels thick.
        static void Frame(Graphics g, Brush br, int x, int y, int w, int h, int pw)
        {
            g.FillRectangle(br, x, y, w, pw);
            g.FillRectangle(br, x, y + h - pw, w, pw);
            g.FillRectangle(br, x, y, pw, h);
            g.FillRectangle(br, x + w - pw, y, pw, h);
        }

        // The status symbol as a pixel mask [x, y] of the whole frame; null = no symbol.
        static bool[,] SymbolMask(int s, Badge badge, string text)
        {
            bool[,] sym;
            switch (badge)
            {
                case Badge.Wait:
                {
                    // three equal square dots in a row: waiting for a flash drive
                    int d = Math.Max(2, R(s * 0.13)), gap = Math.Max(1, R(s * 0.05));
                    sym = new bool[3 * d + 2 * gap, d];
                    for (int i = 0; i < 3; i++) Fill(sym, i * (d + gap), 0, d, d);
                    break;
                }
                case Badge.Down:
                {
                    // arrow down: a stem straight into a stepped head, one solid shape
                    int hw = Odd(s * 0.46), sw = Odd(s * 0.15), sh = Math.Max(2, R(s * 0.24)), rows = (hw + 1) / 2;
                    sym = new bool[hw, sh + rows];
                    Fill(sym, (hw - sw) / 2, 0, sw, sh);
                    for (int i = 0; i < rows; i++) Fill(sym, i, sh + i, hw - 2 * i, 1);
                    break;
                }
                case Badge.Pause:
                {
                    int w = Math.Max(2, R(s * 0.1)), h = R(s * 0.4);
                    sym = new bool[3 * w, h];
                    Fill(sym, 0, 0, w, h);
                    Fill(sym, 2 * w, 0, w, h);
                    break;
                }
                case Badge.Error:
                {
                    int w = Math.Max(2, R(s * 0.1)), bar = R(s * 0.3), gap = Math.Max(1, R(s * 0.08));
                    sym = new bool[w, bar + gap + w];
                    Fill(sym, 0, 0, w, bar);
                    Fill(sym, 0, bar + gap, w, w);
                    break;
                }
                case Badge.Count:
                    sym = TextMask(s, text ?? "");
                    break;
                default:
                    return null;
            }
            return sym == null ? null : Place(s, sym);
        }

        static void Fill(bool[,] m, int x, int y, int w, int h)
        {
            for (int i = Math.Max(0, x); i < Math.Min(m.GetLength(0), x + w); i++)
                for (int j = Math.Max(0, y); j < Math.Min(m.GetLength(1), y + h); j++)
                    m[i, j] = true;
        }

        // Puts a symbol into the bottom-right corner: centred in a box of 55 % of the frame, or flush against the
        // right and bottom edges when it is bigger than that box.
        static bool[,] Place(int s, bool[,] sym)
        {
            int w = sym.GetLength(0), h = sym.GetLength(1), box = R(s * 0.55);
            int x = Math.Max(0, Math.Min(s - box + (box - w) / 2, s - w));
            int y = Math.Max(0, Math.Min(s - box + (box - h) / 2, s - h));
            var m = new bool[s, s];
            for (int i = 0; i < w && x + i < s; i++)
                for (int j = 0; j < h && y + j < s; j++)
                    m[x + i, y + j] = sym[i, j];
            return m;
        }

        // The number, rendered with hinted one-bit glyphs and cropped to its ink.
        static bool[,] TextMask(int s, string text)
        {
            if (text.Length == 0) return null;
            float em = s * (text.Length >= 2 ? 0.62f : 0.78f);
            int n = s * 3;
            using (var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    g.DrawString(text, font, Brushes.Black, s / 2f, s / 2f);
                }
                int x0 = n, y0 = n, x1 = -1, y1 = -1;
                for (int x = 0; x < n; x++)
                    for (int y = 0; y < n; y++)
                        if (bmp.GetPixel(x, y).A > 128)
                        {
                            if (x < x0) x0 = x;
                            if (y < y0) y0 = y;
                            if (x > x1) x1 = x;
                            if (y > y1) y1 = y;
                        }
                if (x1 < 0) return null;
                var m = new bool[x1 - x0 + 1, y1 - y0 + 1];
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                        m[x - x0, y - y0] = bmp.GetPixel(x, y).A > 128;
                return m;
            }
        }

        // Lays a symbol over the drive: first clears every pixel within `halo` of the symbol, which breaks the
        // outline around it, then paints the symbol's pixels in its colour.
        static void Overlay(Bitmap bmp, bool[,] mask, Color color, int halo)
        {
            int s = bmp.Width;
            for (int x = 0; x < s; x++)
                for (int y = 0; y < s; y++)
                {
                    if (!mask[x, y]) continue;
                    for (int i = Math.Max(0, x - halo); i <= Math.Min(s - 1, x + halo); i++)
                        for (int j = Math.Max(0, y - halo); j <= Math.Min(s - 1, y + halo); j++)
                            if (!mask[i, j]) bmp.SetPixel(i, j, Color.Transparent);
                }
            for (int x = 0; x < s; x++)
                for (int y = 0; y < s; y++)
                    if (mask[x, y]) bmp.SetPixel(x, y, color);
        }
    }
}
