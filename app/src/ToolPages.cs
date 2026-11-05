using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
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
            grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            grid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            grid.Columns[3].DefaultCellStyle.Font = new Font(Ui.MonoFont, FontStyle.Bold);
            grid.Columns[4].Width = em * 7;
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
                    int i = grid.Rows.Add(L.T("Диск ") + d.Number, d.Name, string.Format(L.T("{0:N1} ГБ"), d.Size / 1073741824.0),
                        has ? d.Serial : L.T("устройство не сообщает номер"), has ? L.T("Копировать") : "");
                    DataGridViewRow row = grid.Rows[i];
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

    // «Быстрый ping»: own address, gateway, DNS and domain or internet, stopping at the first failure (quick-ping.ps1).
    public class PingPage : Page
    {
        readonly DataGridView grid;
        readonly StatusLine adapter = new StatusLine();
        readonly StatusLine summary = new StatusLine();
        readonly Button run;
        readonly Bitmap[] icons;
        bool running;

        const int Waiting = 0, Running = 1, Ok = 2, Failed = 3, Skipped = 4;

        public PingPage()
        {
            Key = "ping";
            Title = L.T("Быстрый ping");
            Description = L.T("Свой IP, шлюз, DNS, затем домен (в сети корпуса) или интернет. Один запрос на узел, до 2 секунд, остановка при первом сбое.");
            Icon = Glyph.Ping;
            int em = Font.Height;

            run = Ui.Primary(L.T("Проверить ещё раз"), Glyph.Refresh, Font);
            run.Click += delegate { Run(); };
            Ui.Keys(run, "F5");
            FlowLayoutPanel toolbar = Ui.Row();
            toolbar.Controls.Add(run);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);

            adapter.Dock = DockStyle.Top;
            adapter.Padding = new Padding(0, 0, 0, em / 2);

            int size = em;
            icons = new[]
            {
                Glyph.Image(Glyph.Circle, size, Theme.Border), Glyph.Image(Glyph.More, size, Theme.Info), Glyph.Image(Glyph.Check, size, Theme.Good),
                Glyph.Image(Glyph.Cross, size, Theme.Bad), Glyph.Image(Glyph.Dash, size, Theme.Muted)
            };
            grid = Ui.Grid(Font);
            grid.Dock = DockStyle.Fill;
            DataGridViewImageColumn state = new DataGridViewImageColumn();
            state.HeaderText = "";
            state.Width = em * 2;
            state.DefaultCellStyle.NullValue = null;
            grid.Columns.Add(state);
            grid.Columns.Add("target", L.T("Узел"));
            grid.Columns.Add("address", L.T("Адрес"));
            grid.Columns.Add("result", L.T("Результат"));
            grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
            grid.Columns[2].DefaultCellStyle.Font = Ui.MonoFont;
            grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            summary.Dock = DockStyle.Bottom;
            summary.Padding = new Padding(0, em / 2, 0, 0);

            Controls.Add(grid);
            Controls.Add(adapter);
            Controls.Add(toolbar);
            Controls.Add(summary);
        }

        public override void Opened() { Run(); }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { Run(); return true; }
            return false;
        }

        void SetRow(int index, int state, string text)
        {
            DataGridViewRow row = grid.Rows[index];
            row.Cells[0].Value = icons[state];
            row.Cells[3].Value = text;
            row.Cells[3].Style.ForeColor = state == Ok ? Theme.Good : state == Failed ? Theme.Bad : Theme.Muted;
        }

        async void Run()
        {
            if (running) return;
            running = true;
            run.Enabled = false;
            grid.Rows.Clear();
            summary.Clear();
            adapter.Set(Glyph.More, L.T("Определение активного адаптера и основного маршрута…"), Theme.Muted);
            try
            {
                NetworkProfiles profiles = NetworkProfiles.Read(AppPaths.File("network-profiles.json"));
                QuickPing.Plan plan = await Task.Run(delegate { return QuickPing.Prepare(profiles); });
                adapter.Set(Glyph.Network, L.T("Адаптер: ") + plan.AdapterName + L.T(" (индекс ") + plan.AdapterIndex + ")", Theme.Text);
                foreach (QuickPing.Target t in plan.Targets) grid.Rows.Add(icons[Waiting], t.Label, t.Address ?? L.T("не настроен"), "");
                grid.ClearSelection();
                for (int i = 0; i < plan.Targets.Count; i++)
                {
                    QuickPing.Target t = plan.Targets[i];
                    SetRow(i, Running, L.T("отправка запроса, ожидание ответа…"));
                    string details = null;
                    string error = await Task.Run(delegate { string d; string e = QuickPing.Ping(t.Address, out d); details = d; return e; });
                    if (error == null) { SetRow(i, Ok, L.T("ответил: ") + details); continue; }
                    SetRow(i, Failed, error);
                    for (int j = i + 1; j < plan.Targets.Count; j++) SetRow(j, Skipped, L.T("не проверялся"));
                    summary.Set(Glyph.Warning, L.T("Проверка остановлена на узле «") + t.Label + L.T("». Следующие узлы не проверялись."), Theme.Warn);
                    return;
                }
                summary.Set(Glyph.Check, L.T("Все узлы ответили."), Theme.Good);
            }
            catch (Exception e)
            {
                adapter.Set(Glyph.Warning, e.Message, Theme.Bad);
            }
            finally
            {
                running = false;
                run.Enabled = true;
            }
        }
    }
}
