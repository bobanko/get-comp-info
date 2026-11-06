// Builds app\app.ico (icon of comp-info.exe) from a picture, run by tools\make-icon.BAT:
// keeps its transparency (or removes a white background with soft edges), crops empty margins and writes an ICO with all sizes Windows uses.
// --cli adds a dark tile with a ">_" prompt in the lower-left corner.
// Usage: MakeIcon.exe <source.png> <out.ico> [preview.png] [--cli]
//        MakeIcon.exe <source.png> --face-png=<file>   (only the 256 px picture, no icon)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class MakeIcon
{
    static bool Cli;
    static int GearUpTo;        // --gear-small: sizes up to 32 px are a plain gear instead of the picture
    static string GearPng;      // --gear-png=<file>: the plain gear alone, 96 px (picture in the window header)
    static string FacePng;      // --face-png=<file>: the picture itself, 256 px (window «О программе»)
    static Bitmap Source;   // background removed, cropped to a square

    static int Main(string[] args)
    {
        List<string> pos = new List<string>();
        foreach (string a in args)
        {
            if (a == "--cli") Cli = true;
            else if (a == "--gear-small") GearUpTo = 32;
            else if (a.StartsWith("--gear-png=")) GearPng = a.Substring(11);
            else if (a.StartsWith("--face-png=")) FacePng = a.Substring(11);
            else pos.Add(a);
        }
        if (pos.Count < 2 && !(pos.Count == 1 && FacePng != null)) { Console.Error.WriteLine("MakeIcon.exe <source.png> <out.ico> [preview.png] [--cli]"); return 2; }

        // A picture that already has transparency is used as is; otherwise the white background is cut out.
        using (Bitmap raw = new Bitmap(pos[0]))
            Source = CropSquare(HasTransparency(raw) ? FromPixels(Pixels(raw), raw.Width, raw.Height) : RemoveBackground(raw));

        if (pos.Count == 1)
        {
            using (Bitmap f = Scale(Source, 256)) f.Save(FacePng, ImageFormat.Png);
            Console.WriteLine("Picture: " + FacePng);
            return 0;
        }

        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int s in sizes)
            using (Bitmap b = Render(s))
            using (MemoryStream ms = new MemoryStream())
            {
                b.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        WriteIco(pos[1], sizes, pngs);
        if (pos.Count > 2) SavePreview(pos[2], sizes);
        if (GearPng != null) using (Bitmap g = DrawGear(96)) g.Save(GearPng, ImageFormat.Png);
        if (FacePng != null) using (Bitmap f = Scale(Source, 256)) f.Save(FacePng, ImageFormat.Png);
        Console.WriteLine("Icon: " + pos[1]);
        return 0;
    }

    // ---------- background removal ----------

    static int[] Pixels(Bitmap b)
    {
        int[] px = new int[b.Width * b.Height];
        BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        b.UnlockBits(d);
        return px;
    }

    static Bitmap FromPixels(int[] px, int w, int h)
    {
        Bitmap b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, d.Scan0, px.Length);
        b.UnlockBits(d);
        return b;
    }

    static int R(int c) { return (c >> 16) & 255; }
    static int G(int c) { return (c >> 8) & 255; }
    static int B(int c) { return c & 255; }

    static bool HasTransparency(Bitmap b)
    {
        foreach (int c in Pixels(b)) if (((c >> 24) & 255) < 255) return true;
        return false;
    }

    static bool White(int c)
    {
        int r = R(c), g = G(c), b = B(c);
        int min = Math.Min(r, Math.Min(g, b)), max = Math.Max(r, Math.Max(g, b));
        return min >= 245 && max - min <= 10;
    }

    // White area connected to the border becomes transparent. Pixels next to it get alpha
    // estimated against the nearest solid colour of the picture, so there is no white halo.
    static Bitmap RemoveBackground(Bitmap raw)
    {
        int w = raw.Width, h = raw.Height;
        int[] px = Pixels(raw);
        bool[] bg = new bool[w * h];
        Queue<int> q = new Queue<int>();
        for (int x = 0; x < w; x++) { Seed(px, bg, q, x); Seed(px, bg, q, (h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Seed(px, bg, q, y * w); Seed(px, bg, q, y * w + w - 1); }
        while (q.Count > 0)
        {
            int i = q.Dequeue(), x = i % w, y = i / w;
            if (x > 0) Seed(px, bg, q, i - 1);
            if (x < w - 1) Seed(px, bg, q, i + 1);
            if (y > 0) Seed(px, bg, q, i - w);
            if (y < h - 1) Seed(px, bg, q, i + w);
        }

        // edge band: non-background pixels within 2 px of the background
        bool[] band = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (bg[i]) continue;
                for (int dy = -2; dy <= 2 && !band[i]; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < w && yy < h && bg[yy * w + xx]) { band[i] = true; break; }
                    }
            }

        int[] outPx = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, c = px[i];
                if (bg[i]) { outPx[i] = 0; continue; }
                if (!band[i]) { outPx[i] = c | unchecked((int)0xFF000000); continue; }

                // nearest solid colour (average of interior pixels within 4 px)
                long sr = 0, sg = 0, sb = 0; int n = 0;
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        int j = yy * w + xx;
                        if (bg[j] || band[j]) continue;
                        sr += R(px[j]); sg += G(px[j]); sb += B(px[j]); n++;
                    }
                double fr, fg, fb;
                if (n > 0) { fr = sr / (double)n; fg = sg / (double)n; fb = sb / (double)n; }
                else { fr = R(c); fg = G(c); fb = B(c); }
                // alpha = how far the pixel is from white, relative to the solid colour
                double vx = 255 - fr, vy = 255 - fg, vz = 255 - fb;
                double len2 = vx * vx + vy * vy + vz * vz;
                double a = len2 < 1 ? 1 : ((255 - R(c)) * vx + (255 - G(c)) * vy + (255 - B(c)) * vz) / len2;
                a = Math.Max(0, Math.Min(1, a));
                if (a < 0.04) { outPx[i] = 0; continue; }
                int A = (int)Math.Round(a * 255);
                outPx[i] = (A << 24) | ((int)Math.Round(fr) << 16) | ((int)Math.Round(fg) << 8) | (int)Math.Round(fb);
            }
        return FromPixels(outPx, w, h);
    }

    static void Seed(int[] px, bool[] bg, Queue<int> q, int i)
    {
        if (bg[i] || !White(px[i])) return;
        bg[i] = true;
        q.Enqueue(i);
    }

    // Square crop around the visible picture with a small margin.
    static Bitmap CropSquare(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        int[] px = Pixels(b);
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (((px[y * w + x] >> 24) & 255) > 24)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
        int side = Math.Max(x1 - x0 + 1, y1 - y0 + 1);
        side += side / 30;                     // ~3 % margin
        int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
        Bitmap c = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(c))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(b, new Rectangle(0, 0, side, side), new Rectangle(cx - side / 2, cy - side / 2, side, side), GraphicsUnit.Pixel);
        }
        b.Dispose();
        return c;
    }

    // ---------- rendering ----------

    static Bitmap Render(int size)
    {
        if (size <= GearUpTo) return DrawGear(size);
        Bitmap bmp = Scale(Source, size);
        if (Cli)
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(size / 256f, size / 256f);
                DrawPromptTile(g, size <= 32);
            }
        return bmp;
    }

    // High-quality downscale in halving steps (keeps small sizes sharp).
    // Plain grey gear in the style of the picture (dark outline, light top), drawn on a 256 grid:
    // at 16-32 px a face is unreadable, a gear stays recognisable.
    static Bitmap DrawGear(int size)
    {
        Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(size / 256f, size / 256f);
            const int teeth = 8;
            const float rootR = 86, tipR = 116, cx = 128, cy = 128;
            List<PointF> points = new List<PointF>();
            double step = 2 * Math.PI / teeth, baseHalf = step * 0.26, tipHalf = step * 0.18;
            for (int i = 0; i < teeth; i++)
            {
                double a = i * step - Math.PI / 2;
                points.Add(Polar(cx, cy, rootR, a - baseHalf));
                points.Add(Polar(cx, cy, tipR, a - tipHalf));
                points.Add(Polar(cx, cy, tipR, a + tipHalf));
                points.Add(Polar(cx, cy, rootR, a + baseHalf));
                for (double r = a + baseHalf + 0.05; r < a + step - baseHalf; r += 0.05) points.Add(Polar(cx, cy, rootR, r));
            }
            float outline = size <= 20 ? 14 : size <= 24 ? 12 : 10;
            using (GraphicsPath gear = new GraphicsPath())
            {
                gear.AddPolygon(points.ToArray());
                gear.AddEllipse(cx - 34, cy - 34, 68, 68);          // the hole in the middle
                gear.FillMode = FillMode.Alternate;
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(0, 8, 256, 240), Color.FromArgb(0xC4, 0xC4, 0xC8), Color.FromArgb(0x74, 0x74, 0x7A), 90f))
                    g.FillPath(fill, gear);
                using (Pen pen = new Pen(Color.FromArgb(0x1C, 0x1C, 0x1E), outline))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, gear);
                }
            }
        }
        return b;
    }

    static PointF Polar(float cx, float cy, float r, double angle)
    {
        return new PointF(cx + (float)(r * Math.Cos(angle)), cy + (float)(r * Math.Sin(angle)));
    }

    static Bitmap Scale(Bitmap src, int size)
    {
        Bitmap cur = src;
        while (cur.Width / 2 >= size * 2)
        {
            Bitmap next = Resize(cur, cur.Width / 2);
            if (cur != src) cur.Dispose();
            cur = next;
        }
        Bitmap result = Resize(cur, size);
        if (cur != src) cur.Dispose();
        return result;
    }

    static Bitmap Resize(Bitmap src, int size)
    {
        Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        using (ImageAttributes ia = new ImageAttributes())
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            ia.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        }
        return b;
    }

    // Dark rounded tile with a white ">_" in the lower-left corner (256 grid).
    static void DrawPromptTile(Graphics g, bool small)
    {
        RectangleF tile = small ? new RectangleF(0, 108, 148, 148) : new RectangleF(6, 146, 104, 104);
        float radius = small ? 28 : 20;
        for (int i = 1; i <= 6; i++)
            using (GraphicsPath sh = RoundRect(new RectangleF(tile.X + i * 0.8f, tile.Y + i * 1.6f, tile.Width, tile.Height), radius))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(18, 0, 30, 10)))
                g.FillPath(sb, sh);
        using (GraphicsPath p = RoundRect(tile, radius))
        using (LinearGradientBrush br = new LinearGradientBrush(tile, Color.FromArgb(0x14, 0x6A, 0x43), Color.FromArgb(0x08, 0x3C, 0x26), 70f))
        {
            g.FillPath(br, p);
            using (Pen rim = new Pen(Color.FromArgb(150, 0x3C, 0xAE, 0x72), small ? 5f : 2.5f)) g.DrawPath(rim, p);
        }
        RectangleF a = new RectangleF(tile.X + tile.Width * 0.24f, tile.Y + tile.Height * 0.28f, tile.Width * 0.56f, tile.Height * 0.44f);
        PointF[] chevron = { new PointF(a.X, a.Y), new PointF(a.X + a.Width * 0.38f, a.Y + a.Height * 0.5f), new PointF(a.X, a.Bottom) };
        using (Pen pen = new Pen(Color.White, tile.Width * 0.12f))
        {
            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, chevron);
            g.DrawLine(pen, a.X + a.Width * 0.54f, a.Bottom, a.Right, a.Bottom);
        }
    }

    static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---------- output ----------

    static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
    {
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] p in pngs) w.Write(p);
        }
    }

    // Preview: every size at 1:1 on light and dark backgrounds, plus a large copy.
    static void SavePreview(string path, int[] sizes)
    {
        using (Bitmap sheet = new Bitmap(900, 620))
        using (Graphics g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.White);
            g.FillRectangle(new SolidBrush(Color.FromArgb(32, 32, 32)), 0, 310, 900, 310);
            using (Bitmap big = Render(256)) { g.DrawImage(big, 20, 27); g.DrawImage(big, 20, 337); }
            int x = 300;
            foreach (int s in sizes)
            {
                if (s > 128) continue;
                using (Bitmap b = Render(s)) { g.DrawImage(b, x, 155 - s / 2, s, s); g.DrawImage(b, x, 465 - s / 2, s, s); }
                x += s + 24;
            }
            sheet.Save(path, ImageFormat.Png);
        }
    }
}
