using System;
using System.Collections.Generic;
using System.IO;

namespace CompInfo.Wpf
{
    // A flash drive inserted right now: its letter, volume label and USB serial (may be empty).
    public class FlashDrive
    {
        public string Letter, Label, Serial;

        public string Display
        {
            get { return string.IsNullOrWhiteSpace(Label) ? Letter + ":" : Label + " (" + Letter + ":)"; }
        }
    }

    // The flash drives inserted right now — for the blacklist in the settings window.
    public static class FlashDrives
    {
        // Letter -> USB serial for USB disks (core, BusType=7).
        public static Dictionary<string, string> UsbSerials()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (UsbDisk disk in UsbDisk.List())
                    foreach (string tok in (disk.DriveLetters ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = tok.Trim();
                        if (t.Length >= 1 && char.IsLetter(t[0])) map[t.Substring(0, 1)] = disk.Serial ?? "";
                    }
            }
            catch (Exception) { }
            return map;
        }

        // Ordinary flash drives (removable volumes) that are ready now.
        public static List<FlashDrive> Present()
        {
            var list = new List<FlashDrive>();
            Dictionary<string, string> usb = UsbSerials();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                bool ready;
                try { ready = d.IsReady; } catch (Exception) { ready = false; }
                if (!ready || d.DriveType != DriveType.Removable) continue;
                string letter = d.Name.Substring(0, 1);
                string label = "";
                try { label = d.VolumeLabel ?? ""; } catch (Exception) { }
                string serial;
                list.Add(new FlashDrive { Letter = letter, Label = label, Serial = usb.TryGetValue(letter, out serial) ? (serial ?? "") : "" });
            }
            return list;
        }
    }
}
