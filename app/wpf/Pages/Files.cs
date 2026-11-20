using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CompInfo.Wpf
{
    // Small file icons of Windows (SHGetFileInfo) by extension, as in Explorer; the file itself need not exist.
    public static class ShellIcons
    {
        const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string displayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string typeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyIcon(IntPtr icon);

        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static ImageSource Small(string path)
        {
            string key = Path.GetExtension(path) ?? "";
            ImageSource icon;
            if (cache.TryGetValue(key, out icon)) return icon;
            var info = new SHFILEINFO();
            try
            {
                IntPtr result = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
                if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    icon = bitmap;
                }
            }
            catch (Exception) { }
            finally { if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon); }
            cache[key] = icon;
            return icon;
        }
    }

    // Location chips of found files («#даунлоадс #корзина»): small pills in the colour of FileChips.ColorKey.
    public static class Chips
    {
        public static StackPanel Row(IEnumerable<string> labels)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (string label in labels) row.Children.Add(Chip(label));
            return row;
        }

        public static Border Chip(string label)
        {
            string key = FileChips.ColorKey(label);
            string text = key == "Danger" ? "Bad" : key == "Ok" ? "Good" : key;
            string back = key == "Danger" ? "BadBack" : key == "Warn" ? "WarnBack" : key == "Ok" ? "GoodBack" : key == "Muted" ? "HeaderBack" : "Chip" + key + "Back";
            if (key == "SupraWeed" || key == "Viber" || key == "Claude") text = "Chip" + key + "Text";
            var t = new TextBlock { Text = label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, text);
            var chip = new Border { Child = t, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 0, 4, 0) };
            chip.SetResourceReference(Border.BackgroundProperty, back);
            return chip;
        }
    }
}
