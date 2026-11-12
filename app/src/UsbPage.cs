using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // Used by comp-info.exe and by the standalone youSBee.exe (YouSBee.cs).
    // «USB-накопители»: serial numbers of connected flash drives (get-usb-serial.ps1).
    public class UsbPage : Page
    {
        readonly DataGridView grid;
        readonly StatusLine status = new StatusLine();
        bool loading, again;

        public UsbPage()
        {
            Key = "usb";
            Title = L.T("USB-накопители");
            Description = L.T("Серийные номера подключённых флешек и USB-дисков. Список обновляется сам, когда накопитель подключают или отключают.");
            Icon = Glyph.Usb;
            int em = Font.Height;

            Button refresh = Ui.Secondary(L.T("Обновить"), Glyph.Refresh, Font);
            refresh.Click += delegate { Reload(); };
            Ui.Keys(refresh, "F5");
            FlowLayoutPanel toolbar = Ui.Row();
            toolbar.Controls.Add(refresh);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);

            grid = Ui.Grid(Font);
            grid.Dock = DockStyle.Fill;
            grid.Columns.Add("disk", L.T("Диск"));
            grid.Columns.Add("name", L.T("Название"));
            grid.Columns.Add("size", L.T("Объём"));
            grid.Columns.Add("serial", L.T("Серийный номер"));
            DataGridViewButtonColumn copy = new DataGridViewButtonColumn();
            copy.HeaderText = "";
            copy.FlatStyle = FlatStyle.Flat;
            copy.DefaultCellStyle.BackColor = Theme.Accent;
            copy.DefaultCellStyle.ForeColor = Theme.OnAccent;
            copy.DefaultCellStyle.SelectionBackColor = Theme.AccentHover;
            copy.DefaultCellStyle.SelectionForeColor = Theme.OnAccent;
            copy.DefaultCellStyle.Padding = new Padding(em / 4, 3, em / 4, 3);
            grid.Columns.Add(copy);
            grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            // Volume labels are short (FAT32 up to 11, exFAT up to 15 characters); a longer device name is cut
            // with an ellipsis and shown in full in the tooltip. The serial column takes the free width.
            grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.Columns[1].Width = em * 6;
            grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[3].MinimumWidth = em * 7;
            grid.Columns[3].DefaultCellStyle.Font = new Font(Ui.MonoFont, FontStyle.Bold);
            grid.Columns[4].Width = em * 6;
            grid.RowTemplate.Height = em * 2;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.CellContentClick += delegate (object o, DataGridViewCellEventArgs e) { if (e.ColumnIndex == 4 && e.RowIndex >= 0) Copy(e.RowIndex); };
            grid.CellDoubleClick += delegate (object o, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex != 4) Copy(e.RowIndex); };
            grid.KeyDown += delegate (object o, KeyEventArgs k) { if (k.Control && k.KeyCode == Keys.C && grid.CurrentRow != null) { k.Handled = true; Copy(grid.CurrentRow.Index); } };

            status.Dock = DockStyle.Bottom;
            status.Padding = new Padding(0, em / 2, 0, 0);

            Controls.Add(grid);
            Controls.Add(toolbar);
            Controls.Add(status);
        }

        public override void Opened() { Reload(); grid.Focus(); }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { Reload(); return true; }
            return false;
        }

        public override void DevicesChanged()
        {
            // Windows needs a moment before the new disk appears in WMI.
            Timer t = new Timer();
            t.Interval = 1500;
            t.Tick += delegate { t.Dispose(); Reload(); };
            t.Start();
        }

        async void Reload()
        {
            if (loading) { again = true; return; }
            loading = true;
            try
            {
                List<UsbDisk> disks = await Task.Run(delegate { return UsbDisk.List(); });
                grid.Rows.Clear();
                foreach (UsbDisk d in disks)
                {
                    bool has = d.Serial.Length > 0;
                    string diskName = L.T("Диск ") + d.Number + (d.DriveLetters.Length > 0 ? " (" + d.DriveLetters + ")" : "");
                    string name = d.VolumeName.Length > 0 ? d.VolumeName : d.Name;
                    int i = grid.Rows.Add(diskName, name, string.Format(L.T("{0:N1} ГБ"), d.Size / 1073741824.0),
                        has ? d.Serial : L.T("устройство не сообщает номер"), has ? L.T("Копировать") : "");
                    DataGridViewRow row = grid.Rows[i];
                    row.Cells[1].ToolTipText = d.VolumeName.Length > 0 && d.Name.Length > 0 ? d.VolumeName + " — " + d.Name : name;
                    row.Tag = d;
                    if (!has)
                    {
                        row.Cells[3].Style.ForeColor = Theme.Warn;
                        row.Cells[3].Style.Font = Font;
                        // No serial: the button cell looks like an empty cell.
                        row.Cells[4].Style.BackColor = row.Cells[4].Style.SelectionBackColor = Theme.Surface;
                    }
                }
                if (disks.Count == 0) status.Set(Glyph.Info, L.T("USB-накопители не найдены. Подключите флешку."), Theme.Warn);
                else status.Set(Glyph.Info, L.T("Кнопка «Копировать», двойной щелчок или Ctrl+C копирует серийный номер."), Theme.Muted);
            }
            catch (Exception e)
            {
                status.Set(Glyph.Warning, L.T("Не удалось получить список USB-накопителей: ") + e.Message, Theme.Bad);
            }
            finally
            {
                loading = false;
                if (again) { again = false; Reload(); }
            }
        }

        void Copy(int index)
        {
            UsbDisk d = grid.Rows[index].Tag as UsbDisk;
            if (d == null || d.Serial.Length == 0) return;
            try
            {
                Clipboard.SetText(d.Serial);
                status.Set(Glyph.Check, L.T("Серийный номер скопирован: ") + d.Serial, Theme.Good);
            }
            catch (Exception e) { status.Set(Glyph.Warning, L.T("Не удалось скопировать: ") + e.Message, Theme.Bad); }
        }
    }
}
