using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CompInfo.Wpf
{
    // The title-bar icon of every FlashStashBee window is the same pixel-drawn violet drive as the tray icon
    // (TrayIcons), rendered at the window's own small-icon size for its monitor's DPI so it is not rescaled.
    // Only the small icon (WM_SETICON ICON_SMALL) is replaced: the taskbar and Alt+Tab keep the program icon.
    public static class TitleIcon
    {
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        const int WM_SETICON = 0x0080, ICON_SMALL = 0, SM_CXSMICON = 49;

        // One icon per pixel size, kept alive for the program's lifetime (a window only borrows the HICON).
        static readonly Dictionary<int, System.Drawing.Icon> cache = new Dictionary<int, System.Drawing.Icon>();

        // Hooks every window the program opens from now on.
        public static void Install()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        }

        static void OnLoaded(object sender, RoutedEventArgs e)
        {
            var w = sender as Window;
            if (w == null || !ReferenceEquals(e.OriginalSource, w)) return;
            Apply(w);
            w.DpiChanged -= OnDpiChanged;
            w.DpiChanged += OnDpiChanged;
        }

        static void OnDpiChanged(object sender, DpiChangedEventArgs e) { Apply((Window)sender); }

        static void Apply(Window w)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                int size = SmallIconSize(hwnd);
                System.Drawing.Icon icon;
                if (!cache.TryGetValue(size, out icon)) cache[size] = icon = TrayIcons.MakeDrive(size);
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, icon.Handle);
            }
            catch (Exception) { }   // cosmetic: keep the default icon on any failure
        }

        static int SmallIconSize(IntPtr hwnd)
        {
            int s = 0;
            try { s = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForWindow(hwnd)); }
            catch (Exception) { s = GetSystemMetrics(SM_CXSMICON); }   // before Windows 10 1607
            return s >= 8 ? s : 16;
        }
    }
}
