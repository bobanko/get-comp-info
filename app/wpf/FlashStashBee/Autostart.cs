using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace CompInfo.Wpf
{
    // Start when Windows starts — through the HKCU\...\Run key (no administrator rights needed,
    // for the current user only).
    public static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "CHAGEAR FlashStashBee";

        static string ExePath
        {
            get
            {
                try { return Process.GetCurrentProcess().MainModule.FileName; }
                catch (Exception) { return null; }
            }
        }

        public static bool Enabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                        return key != null && key.GetValue(ValueName) != null;
                }
                catch (Exception) { return false; }
            }
        }

        public static void Set(bool on)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (on)
                    {
                        string exe = ExePath;
                        if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
