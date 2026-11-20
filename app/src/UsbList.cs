using System;
using System.Collections.Generic;

namespace CompInfo
{
    // A drive seen since the last «Обновить»: the key is its serial number (or disk number and name without one).
    // The two times belong to the window: the highlight of a new drive and «Скопировано» on its button.
    public class UsbEntry
    {
        public string Key;
        public UsbDisk Disk;
        public bool Connected;
        public DateTime HighlightUntil, CopiedUntil;

        public bool HasSerial { get { return Disk.Serial.Length > 0; } }
    }

    // The list of «USB-накопители» (UsbView of youSBee), without any window code.
    // Drives removed since the last «Обновить» stay in it as not connected, so several drives can be plugged in
    // one after another and their serial numbers compared.
    public class UsbList
    {
        public readonly List<UsbEntry> Entries = new List<UsbEntry>();
        // The first list has been read: drives that show up later have just been connected.
        public bool Loaded;

        public static string KeyOf(UsbDisk d)
        {
            return d.Serial.Length > 0 ? "serial:" + d.Serial : "disk:" + d.Number + "|" + d.Name;
        }

        // Takes the drives connected now. manual («Обновить») forgets the removed ones.
        // Returns the drives that have just been connected while the program is open (not the ones already there
        // at the start or after «Обновить»): the window lights them up.
        public List<UsbEntry> Merge(List<UsbDisk> disks, bool manual)
        {
            if (manual) Entries.Clear();
            // Drives connected before this update; the others that show up now have just arrived.
            HashSet<string> before = new HashSet<string>();
            foreach (UsbEntry e in Entries) { if (e.Connected) before.Add(e.Key); e.Connected = false; }
            List<UsbEntry> arrived = new List<UsbEntry>();
            foreach (UsbDisk d in disks)
            {
                string key = KeyOf(d);
                UsbEntry entry = Entries.Find(delegate (UsbEntry e) { return e.Key == key; });
                if (entry == null) { entry = new UsbEntry(); entry.Key = key; Entries.Add(entry); }
                entry.Disk = d;
                entry.Connected = true;
                if (!before.Contains(key) && Loaded && !manual) arrived.Add(entry);
            }
            Loaded = true;
            return arrived;
        }

        public bool AnyRemoved
        {
            get { return Entries.Exists(delegate (UsbEntry e) { return !e.Connected; }); }
        }

        // The name shown in the table: the volume label, otherwise the device name.
        public static string Name(UsbDisk d)
        {
            return d.VolumeName.Length > 0 ? d.VolumeName : d.Name;
        }

        // Its tooltip: the label and the device name together, when both are known.
        public static string FullName(UsbDisk d)
        {
            return d.VolumeName.Length > 0 && d.Name.Length > 0 ? d.VolumeName + " — " + d.Name : Name(d);
        }

        public static string Letters(UsbDisk d)
        {
            return d.DriveLetters.Length > 0 ? d.DriveLetters : "—";
        }

        public static string Size(UsbDisk d)
        {
            return string.Format(L.T("{0:N1} ГБ"), d.Size / 1073741824.0);
        }
    }
}
