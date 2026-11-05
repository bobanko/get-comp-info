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
            Accent, AccentHover, AccentDown, AccentDisabled, OnAccent,
            Good, GoodLight, Warn, WarnLight, Bad, BadLight, Info, Selection, HeaderBack;
        public static Color[] Rainbow;

        static Color C(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static void Apply(bool dark)
        {
            Dark = dark;
            OnAccent = Color.White;
            if (!dark)
            {
                Back = Color.White; Surface = Color.White; Sidebar = C(0xF3F5F4); SidebarHover = C(0xE7EBE9); SidebarSelected = Color.White;
                Border = C(0xDDE1DF); Text = C(0x1F2937); Muted = C(0x6B7280);
                Accent = C(0x1E8C5A); AccentHover = C(0x24A067); AccentDown = C(0x177048); AccentDisabled = C(0xB8C9C0);
                Good = C(0x2E7D32); GoodLight = C(0xE8F5E9); Warn = C(0xB26A00); WarnLight = C(0xFFF4CC); Bad = C(0xC62828); BadLight = C(0xFDECEA);
                Info = C(0x1565C0); Selection = C(0xCCE4F7); HeaderBack = C(0xF3F5F4);
                // Letters of the brand, as in the console header (darker shades for a light background).
                Rainbow = new[] { C(0xD32F2F), C(0xE08E00), C(0x2E7D32), C(0x00838F), C(0x1565C0), C(0x8E24AA) };
            }
            else
            {
                Back = C(0x202124); Surface = C(0x2A2B2F); Sidebar = C(0x18191B); SidebarHover = C(0x26282B); SidebarSelected = C(0x2E3034);
                Border = C(0x3C3F44); Text = C(0xE6E7E9); Muted = C(0x9AA0A6);
                Accent = C(0x23905E); AccentHover = C(0x2BA56C); AccentDown = C(0x1C764D); AccentDisabled = C(0x2F4A3D);
                Good = C(0x5CC08A); GoodLight = C(0x1E3A2A); Warn = C(0xE8A93C); WarnLight = C(0x3C3220); Bad = C(0xF07167); BadLight = C(0x4A2626);
                Info = C(0x6AA9F0); Selection = C(0x264F78); HeaderBack = C(0x26282B);
                Rainbow = new[] { C(0xFF6B6B), C(0xFFC145), C(0x6BD38B), C(0x4FD1D9), C(0x6AA9F0), C(0xC77DFF) };
            }
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
