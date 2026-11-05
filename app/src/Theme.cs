using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CompInfo
{
    // Colours of the light and dark themes. They are read while controls are built,
    // so switching the theme rebuilds the main window.
    public static class Theme
    {
        public static bool Dark;
        public static Color Back, Surface, Sidebar, SidebarHover, SidebarSelected, Border, Text, Muted,
            Accent, AccentHover, AccentDown, AccentDisabled, AccentSoft, OnAccent,
            Good, GoodLight, Warn, WarnLight, Bad, BadLight, Info, Selection, HeaderBack;
        public static Color[] Rainbow;

        static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        // a moved towards b by part (0..1).
        public static Color Mix(Color a, Color b, double part)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * part), (int)(a.G + (b.G - a.G) * part), (int)(a.B + (b.B - a.B) * part));
        }

        // The colours of the web interface (cloud/public/app.css), so both look like one product.
        public static void Apply(bool dark)
        {
            Dark = dark;
            if (!dark)
            {
                Back = C(0xF5F6F8); Surface = C(0xFFFFFF); HeaderBack = C(0xF0F2F5);
                Text = C(0x1A1D21); Muted = C(0x5D6673); Border = C(0xDDE1E6);
                Accent = C(0x2563EB); OnAccent = C(0xFFFFFF); AccentSoft = C(0xE8EFFF);
                Good = C(0x16723A); GoodLight = C(0xE3F5E9); Warn = C(0x9A5B00); WarnLight = C(0xFDF1DC); Bad = C(0xB42318); BadLight = C(0xFDECEB);
                // Brand letters of the console header: red, yellow, green, cyan, blue, magenta.
                Rainbow = new[] { C(0xD92D20), C(0xB88700), C(0x15803D), C(0x0E8FA3), C(0x2563EB), C(0xB425C4) };
            }
            else
            {
                Back = C(0x111317); Surface = C(0x1A1D23); HeaderBack = C(0x22262D);
                Text = C(0xE6E8EB); Muted = C(0x9AA3AE); Border = C(0x2E333B);
                Accent = C(0x6C9BFF); OnAccent = C(0x0B1020); AccentSoft = C(0x1D2A45);
                Good = C(0x57C47F); GoodLight = C(0x173323); Warn = C(0xE8B04F); WarnLight = C(0x3A2C12); Bad = C(0xF2837A); BadLight = C(0x3D1C1A);
                Rainbow = new[] { C(0xFF6B6B), C(0xFFD43B), C(0x51CF66), C(0x3BC9DB), C(0x6C9BFF), C(0xE599F7) };
            }
            Sidebar = Surface;
            SidebarHover = HeaderBack;
            SidebarSelected = AccentSoft;
            Selection = AccentSoft;
            Info = Accent;
            // Web buttons: brightness(1.08) on hover.
            AccentHover = Mix(Accent, Color.White, 0.10);
            AccentDown = Mix(Accent, dark ? Color.White : Color.Black, 0.15);
            AccentDisabled = Mix(Accent, Surface, 0.55);
        }

        // Windows setting "Режим приложений по умолчанию".
        public static bool SystemIsDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception) { return false; }
        }

        // "system", "light" or "dark".
        public static string Choice = "system";

        public static void ApplyChoice()
        {
            Apply(Choice == "dark" || (Choice == "system" && SystemIsDark()));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        // Dark title bar (Windows 10 2004+ uses 20, older builds 19).
        public static void TitleBar(Form form)
        {
            form.HandleCreated += delegate
            {
                int on = Dark ? 1 : 0;
                try
                {
                    if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
                }
                catch (Exception) { }
            };
        }

        // Dark scroll bars and native parts of text boxes, combo boxes and grids.
        public static void Native(Control c)
        {
            EventHandler apply = delegate
            {
                try
                {
                    SetWindowTheme(c.Handle, Dark ? (c is ComboBox ? "DarkMode_CFD" : "DarkMode_Explorer") : "Explorer", null);
                    foreach (Control child in c.Controls) if (child is ScrollBar) SetWindowTheme(child.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
                }
                catch (Exception) { }
            };
            if (c.IsHandleCreated) apply(c, EventArgs.Empty);
            c.HandleCreated += apply;
        }
    }
}
