using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CompInfo.Wpf
{
    // The tray icon and all of the utility's work: polls the drives, starts a copy (CopyJob) for a new flash drive,
    // and changes the icon (a violet flash drive + a status badge). The menu is a WPF ContextMenu styled like the
    // program (TrayMenu.xaml); the tray icon itself stays on the WinForms NotifyIcon, since WPF has no tray icon.
    public sealed class TrayController : IDisposable
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

        readonly IniSettings settings;
        readonly Forms.NotifyIcon notify = new Forms.NotifyIcon();
        DispatcherTimer poll, blink;
        Window menuOwner;   // an invisible window: it takes focus so the WPF menu closes on a click outside

        readonly Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, CopyJob> jobs = new Dictionary<string, CopyJob>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // blacklisted serials
        List<DriveRow> present = new List<DriveRow>();   // what is inserted right now

        ArchiveOptions opts;
        int processed;
        bool paused;
        bool blinkOn;
        DateTime lastOk = DateTime.MinValue, lastErr = DateTime.MinValue;
        string lastLabel = "";

        Drawing.Icon iconIdle, iconCopyOn, iconCopyOff, iconError, iconPaused, doneIcon;
        int doneCount = -1;

        SettingsWindow settingsWindow;

        // Letters and serials of USB disks (BusType=7); cached for a few seconds to avoid hitting WMI too often.
        Dictionary<string, string> usbInfoCache;
        DateTime usbInfoAt = DateTime.MinValue;

        public TrayController(IniSettings settings) { this.settings = settings; }

        public void Start()
        {
            opts = ArchiveOptions.From(settings);
            LoadExcluded();
            EnsureBaseDir();

            iconIdle = TrayIcons.Make(TrayIcons.Badge.Wait, TrayIcons.Wait);
            iconCopyOn = TrayIcons.Make(TrayIcons.Badge.Down, TrayIcons.DownOn);
            iconCopyOff = TrayIcons.Make(TrayIcons.Badge.Down, TrayIcons.DownOff);
            iconError = TrayIcons.Make(TrayIcons.Badge.Error, TrayIcons.Error);
            iconPaused = TrayIcons.Make(TrayIcons.Badge.Pause, TrayIcons.Pause);

            // An invisible owner window for the WPF menu (offscreen, transparent, no taskbar entry).
            menuOwner = new Window
            {
                Width = 1, Height = 1, Left = -32000, Top = -32000,
                ShowInTaskbar = false, ShowActivated = false,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = Brushes.Transparent
            };
            menuOwner.Show();
            menuOwner.Hide();

            notify.Icon = iconIdle;
            notify.Text = L.T("FlashStashBee — ожидание флешки");
            notify.Visible = true;
            notify.MouseUp += (s, e) => { if (e.Button == Forms.MouseButtons.Right) ShowMenu(); };
            notify.DoubleClick += delegate { ExitApp(); };   // double click on the tray icon closes the program

            // Volumes already present at startup are not copied — remember them as "seen".
            foreach (DriveRow d in Scan()) seen[d.Letter] = d.Signature;

            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            poll.Tick += delegate { try { Poll(); } catch (Exception) { } };
            poll.Start();

            blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            blink.Tick += delegate { if (!paused && jobs.Count > 0) { blinkOn = !blinkOn; UpdateIcon(); } };
            blink.Start();

            UpdateIcon();
        }

        // ---------- WPF menu ----------

        void ShowMenu()
        {
            // Only three items: the folder, "copy now" and the blacklist live in the settings window.
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            menu.Items.Add(Item(L.T("Настройки"), Glyph.Settings, delegate { OpenSettings(); }));
            menu.Items.Add(Item(paused ? L.T("Возобновить") : L.T("Пауза"), paused ? Glyph.Play : Glyph.Pause, delegate { TogglePause(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(L.T("Выход"), Glyph.Power, delegate { ExitApp(); }));

            // For the menu to close on a click outside, its owner window needs foreground focus.
            menuOwner.Show();
            SetForegroundWindow(new WindowInteropHelper(menuOwner).Handle);
            menu.Closed += delegate { menuOwner.Hide(); };
            menu.IsOpen = true;
        }

        static MenuItem Item(string text, string glyph, RoutedEventHandler onClick, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            if (glyph != null) mi.Icon = Glyph.Block(glyph, 15, "Muted");
            mi.Click += onClick;
            return mi;
        }

        void TogglePause()
        {
            paused = !paused;
            UpdateIcon();
        }

        void OpenSettings()
        {
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            settingsWindow = new SettingsWindow(settings);
            settingsWindow.SettingsChanged += delegate { opts = ArchiveOptions.From(settings); LoadExcluded(); EnsureBaseDir(); };
            settingsWindow.ReopenRequested += delegate { Application.Current.Dispatcher.BeginInvoke(new Action(OpenSettings)); };
            settingsWindow.Closed += delegate { settingsWindow = null; };
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        void ExitApp()
        {
            Dispose();
            if (Application.Current != null) Application.Current.Shutdown();
        }

        // ---------- blacklist ----------

        // The blacklist is edited in the settings window; here it is only read (again after every change).
        void LoadExcluded()
        {
            excluded.Clear();
            foreach (string s in (settings.Get("excludeSerials", "") ?? "").Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                excluded.Add(s.Trim());
            // Stop a running copy of a flash drive that has just been blacklisted.
            foreach (DriveRow d in present)
                if (d.Serial.Length > 0 && excluded.Contains(d.Serial) && jobs.ContainsKey(d.Letter))
                {
                    jobs[d.Letter].Cancel();
                    jobs.Remove(d.Letter);
                }
        }

        // ---------- polling and copying ----------

        struct DriveRow
        {
            public string Letter, Root, Label, Serial, Signature;
            public long Used;
        }

        List<DriveRow> Scan()
        {
            var list = new List<DriveRow>();
            Dictionary<string, string> usb = UsbInfo();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                bool ready;
                try { ready = d.IsReady; } catch (Exception) { ready = false; }
                if (!ready) continue;

                // Ordinary flash drives only (removable volumes); external HDD/SSD are not copied.
                if (d.DriveType != DriveType.Removable) continue;
                string letter = d.Name.Substring(0, 1);

                var row = new DriveRow();
                row.Letter = letter;
                row.Root = d.Name;
                string label = "";
                try { label = d.VolumeLabel ?? ""; } catch (Exception) { }
                row.Label = label;
                string serial;
                row.Serial = usb.TryGetValue(letter, out serial) ? (serial ?? "") : "";
                long total = 0, free = 0;
                try { total = d.TotalSize; free = d.TotalFreeSpace; } catch (Exception) { }
                row.Used = total - free;
                row.Signature = (row.Serial.Length > 0 ? "serial:" + row.Serial : "vol:" + label) + "|" + total;
                list.Add(row);
            }
            return list;
        }

        // Letter -> serial for USB disks (core, BusType=7). The serial may be empty.
        Dictionary<string, string> UsbInfo()
        {
            if (usbInfoCache != null && (DateTime.Now - usbInfoAt).TotalSeconds < 4) return usbInfoCache;
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
            usbInfoCache = map;
            usbInfoAt = DateTime.Now;
            return map;
        }

        void Poll()
        {
            // 1. Finished copies.
            foreach (string letter in new List<string>(jobs.Keys))
            {
                CopyJob job = jobs[letter];
                if (!job.Done) continue;
                if (job.Success) { processed++; lastOk = DateTime.Now; }
                else lastErr = DateTime.Now;
                lastLabel = job.Label;
                jobs.Remove(letter);
            }

            // 2. Current volumes; new (or swapped) ones are copied.
            present = Scan();
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DriveRow d in present)
            {
                current[d.Letter] = d.Signature;
                if (paused) continue;
                if (d.Serial.Length > 0 && excluded.Contains(d.Serial)) continue;   // blacklist
                bool isNew = !seen.ContainsKey(d.Letter) || seen[d.Letter] != d.Signature;
                if (!isNew || jobs.ContainsKey(d.Letter)) continue;

                if (opts.MaxBytes > 0 && d.Used > opts.MaxBytes)
                {
                    lastErr = DateTime.Now;
                    lastLabel = DisplayLabel(d) + " — " + L.T("слишком большой");
                }
                else StartCopy(d);
            }

            // Remember the inserted volumes; forget the removed ones.
            seen.Clear();
            foreach (KeyValuePair<string, string> kv in current) seen[kv.Key] = kv.Value;

            UpdateIcon();
        }

        void StartCopy(DriveRow d)
        {
            string dest = Path.Combine(opts.BaseDir, FolderName(d));
            var job = new CopyJob(d.Root, dest, DisplayLabel(d), opts);
            // React the moment the copy ends (not on the next poll tick), so the icon stops blinking at once.
            job.Finished += delegate
            {
                Application app = Application.Current;
                if (app != null) app.Dispatcher.BeginInvoke(new Action(delegate { try { Poll(); } catch (Exception) { } }));
            };
            jobs[d.Letter] = job;
            job.Start();
        }

        static string DisplayLabel(DriveRow d)
        {
            return string.IsNullOrWhiteSpace(d.Label) ? d.Letter + ":" : d.Label;
        }

        // This flash drive's folder: the same one across insertions (keyed by serial), so backups are not duplicated.
        // Without a serial — by the volume label and size.
        static string FolderName(DriveRow d)
        {
            string label = string.IsNullOrWhiteSpace(d.Label) ? "USB-" + d.Letter : d.Label;
            string tag = d.Serial.Length > 0 ? d.Serial : "vol";
            return Sanitize(label) + " [" + Sanitize(tag) + "]";
        }

        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim().Trim('.');
        }

        // ---------- icon ----------

        void UpdateIcon()
        {
            string text;
            if (paused)
            {
                notify.Icon = iconPaused;
                text = L.T("FlashStashBee: пауза");
            }
            else if (jobs.Count > 0)
            {
                notify.Icon = blinkOn ? iconCopyOn : iconCopyOff;
                CopyJob j = FirstJob();
                string one = j != null ? j.Label : "";
                int pct = j != null ? j.Percent : 0;
                if (j != null && j.Packing)
                    text = L.F("Упаковываю в ZIP: {0} — {1}%", one, pct);
                else
                    text = jobs.Count > 1 ? L.F("Копирую: {0} — {1}% (+{2})", one, pct, jobs.Count - 1)
                                          : L.F("Копирую: {0} — {1}%", one, pct);
            }
            else
            {
                DateTime now = DateTime.Now;
                if ((now - lastErr).TotalSeconds < 6) { notify.Icon = iconError; text = L.F("Ошибка: {0}", lastLabel); }
                else if ((now - lastOk).TotalSeconds < 4) { notify.Icon = DoneIcon(); text = L.F("Готово: {0}", lastLabel); }
                else { notify.Icon = iconIdle; text = L.T("FlashStashBee — ожидание флешки"); }
            }
            notify.Text = Clip(text);
        }

        Drawing.Icon DoneIcon()
        {
            if (doneCount != processed)
            {
                if (doneIcon != null) doneIcon.Dispose();
                doneIcon = TrayIcons.Make(TrayIcons.Badge.Count, TrayIcons.Done, processed > 9 ? "9+" : processed.ToString());
                doneCount = processed;
            }
            return doneIcon;
        }

        CopyJob FirstJob()
        {
            foreach (CopyJob j in jobs.Values) return j;
            return null;
        }

        // The NotifyIcon tooltip has a limited length — trim it with a margin.
        static string Clip(string s)
        {
            return s != null && s.Length > 62 ? s.Substring(0, 61) + "…" : s;
        }

        void EnsureBaseDir()
        {
            try { Directory.CreateDirectory(opts.BaseDir); } catch (Exception) { }
        }

        public void Dispose()
        {
            if (poll != null) poll.Stop();
            if (blink != null) blink.Stop();
            foreach (CopyJob j in jobs.Values) j.Cancel();
            if (notify != null) { notify.Visible = false; notify.Dispose(); }
            if (menuOwner != null) { try { menuOwner.Close(); } catch (Exception) { } }
            foreach (Drawing.Icon ic in new[] { iconIdle, iconCopyOn, iconCopyOff, iconError, iconPaused, doneIcon })
                if (ic != null) ic.Dispose();
        }
    }
}
