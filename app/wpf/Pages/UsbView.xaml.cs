using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CompInfo.Wpf
{
    // «USB-накопители»: serial numbers of connected flash drives (UsbPage.cs on WinForms).
    // Drives removed since the last «Обновить» stay in the list, grey with a crossed icon, so several drives
    // can be plugged in one after another and their serial numbers compared. A newly connected drive lights up.
    public partial class UsbView : UserControl
    {
        readonly UsbList list = new UsbList();
        readonly ObservableCollection<UsbRow> rows = new ObservableCollection<UsbRow>();
        bool loading, again, againManual;

        public static readonly DependencyProperty CompactProperty = DependencyProperty.Register("Compact", typeof(bool), typeof(UsbView));
        public static readonly DependencyProperty CopyWidthProperty = DependencyProperty.Register("CopyWidth", typeof(double), typeof(UsbView));

        // A narrow window: «Копировать» becomes a copy icon.
        public bool Compact { get { return (bool)GetValue(CompactProperty); } set { SetValue(CompactProperty, value); } }

        // The width of «Копировать» / «Скопировано», so the button does not jump when its label changes.
        public double CopyWidth { get { return (double)GetValue(CopyWidthProperty); } set { SetValue(CopyWidthProperty, value); } }

        public UsbView()
        {
            InitializeComponent();
            Table.ItemsSource = rows;
            DiskHeader.Text = L.T("Диск");
            NameHeader.Text = L.T("Название");
            SizeHeader.Text = L.T("Объём");
            SerialHeader.Text = L.T("Серийный номер");
            EmptyText.Text = L.T("Подключите флешку");
            Glyph.Label(RefreshButton, Glyph.Refresh, L.T("Обновить"));
            RefreshButton.ToolTip = L.T("Обновить") + "   (F5)";
            SizeChanged += delegate { Compact = ActualWidth < 500; };
            Loaded += delegate { CopyWidth = MeasureCopy(); };
        }

        // youSBee shows its own «Обновить» in the window header, so the page row with it can be hidden.
        public bool ShowToolbar
        {
            get { return Toolbar.Visibility == Visibility.Visible; }
            set { Toolbar.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
        }

        public void Opened() { Reload(false); Table.Focus(); }

        // «Обновить»: the list of drives connected now; the removed ones go away.
        public void RefreshList() { Reload(true); }

        // Windows needs a moment before the new disk appears in WMI.
        public void DevicesChanged()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            t.Tick += delegate { t.Stop(); Reload(false); };
            t.Start();
        }

        async void Reload(bool manual)
        {
            if (loading) { again = true; againManual |= manual; return; }
            loading = true;
            try
            {
                List<UsbDisk> disks = await Task.Run(() => UsbDisk.List());
                List<UsbEntry> arrived = list.Merge(disks, manual);
                Render(arrived);
                if (list.Entries.Count == 0) Status.Clear();
                else if (list.AnyRemoved) Status.Note(Glyph.Info, L.T("Серые строки — отключённые флешки. «Обновить» убирает их из списка."));
                else Status.Note(Glyph.Info, L.T("Кнопка «Копировать», двойной щелчок или Ctrl+C копирует серийный номер."));
            }
            catch (Exception e)
            {
                Status.Show(Severity.Error, Glyph.Warning, L.T("Не удалось получить список USB-накопителей: ") + e.Message);
            }
            finally
            {
                loading = false;
                if (again) { bool m = againManual; again = againManual = false; Reload(m); }
            }
        }

        // The rows follow the entries of the list: the same row object stays for the same drive (its selection and
        // animations with it), new drives get new rows, forgotten ones go away.
        void Render(List<UsbEntry> arrived)
        {
            for (int i = rows.Count - 1; i >= 0; i--)
                if (!list.Entries.Contains(rows[i].Entry)) rows.RemoveAt(i);
            for (int i = 0; i < list.Entries.Count; i++)
            {
                UsbEntry e = list.Entries[i];
                int at = IndexOf(e);
                if (at < 0) rows.Insert(i, new UsbRow(e));
                else if (at != i) rows.Move(at, i);
                rows[i].Update();
                if (arrived.Contains(e)) rows[i].LightUp();
            }
            Empty.Visibility = rows.Count == 0 && list.Loaded ? Visibility.Visible : Visibility.Collapsed;
        }

        // Only for the screenshots (--demo): every kind of row, without plugging drives in and out.
        // A copied drive, a removed one (grey), one without a serial number, one just connected (green).
        public void Demo()
        {
            Func<int, string, string, string, string, UsbDisk> disk = (number, name, label, letter, serial) =>
                new UsbDisk { Number = number, Name = name, VolumeName = label, DriveLetters = letter, Serial = serial, Size = (long)(14.6 * 1073741824) * number };
            UsbDisk work = disk(1, "Kingston DataTraveler 3.0", "WORK", "E:", "408D5C1EB2F1E12039CF0A27"),
                old = disk(2, "SanDisk Ultra", "", "F:", "4C530001231108117394"),
                generic = disk(3, "Generic Flash Disk", "", "H:", ""),
                fresh = disk(4, "Transcend JetFlash", "BACKUP", "I:", "C3A9F21B");
            list.Merge(new List<UsbDisk> { work, old, generic }, false);
            Render(list.Merge(new List<UsbDisk> { work, generic, fresh }, false));
            rows[0].MarkCopied();
            Status.Show(Severity.Success, Glyph.Check, L.T("Серийный номер скопирован: ") + work.Serial);
        }

        int IndexOf(UsbEntry e)
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].Entry == e) return i;
            return -1;
        }

        void Copy(UsbRow row)
        {
            if (row == null || !row.HasSerial) return;
            string error;
            if (ClipText.Set(row.Entry.Disk.Serial, out error))
            {
                row.MarkCopied();
                Status.Show(Severity.Success, Glyph.Check, L.T("Серийный номер скопирован: ") + row.Entry.Disk.Serial);
            }
            else Status.Show(Severity.Error, Glyph.Warning, L.T("Не удалось скопировать: ") + error);
        }

        void Copy_Click(object sender, RoutedEventArgs e)
        {
            UsbRow row = ((FrameworkElement)sender).DataContext as UsbRow;
            Table.SelectedItem = row;
            Copy(row);
        }

        void Refresh_Click(object sender, RoutedEventArgs e) { RefreshList(); }

        void Table_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // a double click on a row, not on the scroll bar or the empty space under the rows
            var item = ItemsControl.ContainerFromElement(Table, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null) Copy(item.DataContext as UsbRow);
        }

        void Table_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Copy(Table.SelectedItem as UsbRow); }
        }

        // Icon, gap and the longer label, plus the padding of the button.
        double MeasureCopy()
        {
            double text = 0;
            var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            foreach (string label in new[] { L.T("Копировать"), L.T("Скопировано") })
            {
                var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize,
                    Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                text = Math.Max(text, ft.WidthIncludingTrailingWhitespace);
            }
            return Math.Ceiling(14 + 8 + text + 10 * 2 + 2);
        }
    }

    // One row of the table: the texts of a UsbEntry and its passing states (just connected, just copied).
    public class UsbRow : INotifyPropertyChanged
    {
        public readonly UsbEntry Entry;
        DispatcherTimer arrivedTimer, copiedTimer;

        public UsbRow(UsbEntry entry) { Entry = entry; }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool Connected { get { return Entry.Connected; } }
        public bool HasSerial { get { return Entry.HasSerial; } }
        public bool Arrived { get; private set; }
        public bool Copied { get; private set; }

        public string IconTip { get { return Connected ? L.T("Подключена") : L.T("Отключена — «Обновить» уберёт её из списка"); } }
        public string DiskText { get { return Connected ? UsbList.Letters(Entry.Disk) : L.T("отключена"); } }
        public string DiskTip { get { return L.T("Диск ") + Entry.Disk.Number + (Connected ? "" : " (" + UsbList.Letters(Entry.Disk) + ")"); } }
        public string NameText { get { return UsbList.Name(Entry.Disk); } }
        public string NameTip { get { return UsbList.FullName(Entry.Disk); } }
        public string SizeText { get { return UsbList.Size(Entry.Disk); } }
        public string SerialText { get { return HasSerial ? Entry.Disk.Serial : L.T("устройство не сообщает номер"); } }
        public string CopyText { get { return Copied ? L.T("Скопировано") : L.T("Копировать"); } }
        public string CopyTip { get { return L.T("Копировать") + "   (Ctrl+C)"; } }

        // All texts again (the drive may have changed its letter, or have been removed).
        public void Update() { Changed(""); }

        // Just connected: the row lights up for a few seconds (the animation of the row style).
        public void LightUp()
        {
            Arrived = false;
            Changed("Arrived");
            Arrived = true;
            Changed("Arrived");
            Restart(ref arrivedTimer, 3000, delegate { Arrived = false; Changed("Arrived"); });
        }

        // «Скопировано» with a check on the button for two seconds.
        public void MarkCopied()
        {
            Copied = true;
            Changed("Copied");
            Changed("CopyText");
            Restart(ref copiedTimer, 2000, delegate { Copied = false; Changed("Copied"); Changed("CopyText"); });
        }

        static void Restart(ref DispatcherTimer timer, int ms, Action done)
        {
            if (timer == null)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                t.Tick += delegate { t.Stop(); done(); };
                timer = t;
            }
            timer.Stop();
            timer.Start();
        }

        void Changed(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
}
