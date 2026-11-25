using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace CompInfo.Wpf
{
    // Light and dark theme of the WPF programs: the brushes of Styles.xaml are replaced at the application level,
    // so open windows repaint at once (DynamicResource), nothing is built again.
    // The palette follows the web interface (cloud/public/app.css), except the dark neutral surfaces and text:
    // those are plain neutral greys like Windows 11 / Visual Studio, with no blue tint (user's choice, 25.11.2026).
    public static class WpfTheme
    {
        // key, light, dark
        static readonly string[,] Palette = {
            { "WindowBack",  "#F5F6F8", "#202020" },
            { "Surface",     "#FFFFFF", "#2B2B2B" },
            { "HeaderBack",  "#F0F2F5", "#2B2B2B" },
            { "Text",        "#1A1D21", "#E4E4E4" },
            { "Muted",       "#5D6673", "#A0A0A0" },
            { "Border",      "#DDE1E6", "#3C3C3C" },
            { "Divider",     "#E6E9ED", "#343434" },
            // blue as on the web; hover = brightness 1.08, pressed a bit darker (lighter in the dark theme)
            { "Accent",      "#2563EB", "#6C9BFF" },
            { "AccentHover", "#3B73ED", "#7BA5FF" },
            { "AccentDown",  "#1F54C8", "#82AAFF" },
            { "AccentSoft",  "#E8EFFF", "#1D2A45" },
            { "OnAccent",    "#FFFFFF", "#0B1020" },
            // states: the colour of a mark or a text, the soft background, the frame of a panel on that background
            { "Good",        "#16723A", "#57C47F" },
            { "GoodBack",    "#E3F5E9", "#173323" },
            { "GoodLine",    "#A6CEB4", "#2A5F3F" },
            { "Warn",        "#9A5B00", "#E8B04F" },
            { "WarnBack",    "#FDF1DC", "#3A2C12" },
            { "WarnLine",    "#DFC59A", "#6E5424" },
            { "Bad",         "#B42318", "#F2837A" },
            { "BadBack",     "#FDECEB", "#3D1C1A" },
            { "BadLine",     "#E7B0AC", "#733B37" },
            // flash drives are orange, like the youSBee icon; «SBee» is honey yellow, as on chagear.top
            { "Flash",       "#E87E04", "#FFAA3C" },
            { "Honey",       "#B47A00", "#F5B82E" },
            // the toolbox: the letters of «CHAGEAR ТУЛБОКС» (the console header), the coloured word of its name
            { "Rainbow0",    "#D92D20", "#FF6B6B" },
            { "Rainbow1",    "#B88700", "#FFD43B" },
            { "Rainbow2",    "#15803D", "#51CF66" },
            { "Rainbow3",    "#0E8FA3", "#3BC9DB" },
            { "Rainbow4",    "#2563EB", "#6C9BFF" },
            { "Rainbow5",    "#B425C4", "#E599F7" },
            { "Toolbox",     "#3D6FB6", "#79A6E8" },
            // found words in a table, as <mark> of the website: the yellow of the brand letters over the row
            { "Mark",        "#E4D19E", "#716229" },
            // the shield of a copy already running with administrator rights
            { "AdminReady",  "#0B6E36", "#57C47F" },
            // violet of SUSpector; folders yellow-orange, as in Explorer
            { "Suspector",   "#6D3FD0", "#A585FF" },
            // violet of FlashStashBee (its flash-drive icon and the coloured word of its name)
            { "Violet",      "#7C3AED", "#A585FF" },
            { "Folder",      "#DE9200", "#FFC43D" },
            // location chips of found files (FileChips.ColorKey): Danger / Warn / Ok / Muted use the state colours
            { "ChipSupraWeedText", "#0E706A", "#5ED4C8" },
            { "ChipSupraWeedBack", "#D2F0EC", "#163A38" },
            { "ChipViberText",     "#6640B2", "#BEA0FF" },
            { "ChipViberBack",     "#EAE2FC", "#302648" },
            { "ChipClaudeText",    "#A64B31", "#E89A7D" },
            { "ChipClaudeBack",    "#F7E5DD", "#432A24" },
            // a subtle fill under the mouse / pressed (WinUI 3 SubtleFillColorSecondary / Tertiary)
            { "SubtleHover", "#0A000000", "#0FFFFFFF" },
            { "SubtleDown",  "#06000000", "#0AFFFFFF" },
            // text fields and buttons
            { "FieldBack",   "#FFFFFF", "#2D2D2D" },
            { "FieldHover",  "#F0F2F5", "#353535" },
            { "FieldDown",   "#E6E9ED", "#2A2A2A" },
            { "FieldFocus",  "#FFFFFF", "#1F1F1F" },
            { "FieldBottom", "#8A9099", "#8A8A8A" },
            // drop-down lists, tooltips, scroll bars
            { "FlyoutBack",  "#FFFFFF", "#2C2C2C" },
            { "ScrollThumb", "#A3AAB4", "#6B6B6B" },
            // InfoBar and Dialog (WinUI 3): background and icon circle per severity, the mark inside the circle, the frame
            { "BarInfoBack", "#F8F9FB", "#2B2B2B" },
            { "BarInfoIcon", "#2563EB", "#6C9BFF" },
            { "BarGoodBack", "#E3F5E9", "#173323" },
            { "BarGoodIcon", "#16723A", "#57C47F" },
            { "BarWarnBack", "#FDF1DC", "#3A2C12" },
            { "BarWarnIcon", "#9A5B00", "#E8B04F" },
            { "BarBadBack",  "#FDECEB", "#3D1C1A" },
            { "BarBadIcon",  "#B42318", "#F2837A" },
            { "BarIconMark", "#FFFFFF", "#E4000000" },
            { "BarBorder",   "#0F000000", "#19000000" },
        };

        public static bool Dark { get; private set; }

        // "system", "light" or "dark" (the ini of the program); "system" follows Windows while the program runs.
        public static string Choice { get; private set; } = "system";

        public static void Start(string choice)
        {
            Apply(choice);
            SystemEvents.UserPreferenceChanged += (s, a) =>
            {
                if (a.Category == UserPreferenceCategory.General && Choice == "system" && Application.Current != null)
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => Apply("system")));
            };
        }

        public static void Apply(string choice)
        {
            Choice = choice == "light" || choice == "dark" ? choice : "system";
            Dark = Choice == "dark" || (Choice == "system" && WindowsIsDark());
            var res = Application.Current.Resources;
            for (int i = 0; i < Palette.GetLength(0); i++)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Palette[i, Dark ? 2 : 1]));
                brush.Freeze();
                res[Palette[i, 0]] = brush;
            }
            foreach (Window w in Application.Current.Windows) DarkTitle(w);
        }

        // Windows setting «Режим приложений по умолчанию».
        static bool WindowsIsDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int i && i == 0;
            }
            catch (Exception) { return false; }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // The title bar is drawn by Windows: tell it the theme (Windows 10 2004+ uses 20, older builds 19).
        // Every window calls it once; Apply repeats it for the open windows.
        public static void DarkTitle(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) { w.SourceInitialized += (s, e) => DarkTitle(w); return; }
            int on = Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(h, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(h, 19, ref on, sizeof(int));
            }
            catch (Exception) { }
        }
    }
}
