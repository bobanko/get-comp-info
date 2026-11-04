using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // «USB-накопители»: serial numbers of connected flash drives (get-usb-serial.ps1).
    public class UsbPage : Page
    {
        readonly ListView list = new ListView();
        readonly StatusLine status = new StatusLine();
        readonly Button copy;
        bool loading, again;

        public UsbPage()
        {
            Key = "usb";
            Title = "USB-накопители";
            Description = "Серийные номера подключённых флешек и USB-дисков. Подключите или отключите накопитель — список обновится сам.";
            Icon = Glyph.Usb;
            int em = Font.Height;

            Button refresh = Ui.Secondary("Обновить", Glyph.Refresh, Font);
            refresh.Click += delegate { Reload(); };
            copy = Ui.Primary("Копировать серийный номер", Glyph.Copy, Font);
            copy.Click += delegate { Copy(); };
            FlowLayoutPanel toolbar = Ui.Row();
            toolbar.Controls.Add(copy);
            toolbar.Controls.Add(refresh);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("Диск", em * 4);
            list.Columns.Add("Название", em * 16);
            list.Columns.Add("Объём", em * 6, HorizontalAlignment.Right);
            list.Columns.Add("Серийный номер", em * 18);
            list.SmallImageList = new ImageList();
            list.SmallImageList.ColorDepth = ColorDepth.Depth32Bit;
            list.SmallImageList.ImageSize = new Size(em + em / 2, em + em / 2);
            list.SmallImageList.Images.Add(Glyph.Image(Glyph.Usb, em + em / 2, Theme.Accent));
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            list.DoubleClick += delegate { Copy(); };
            list.KeyDown += delegate (object o, KeyEventArgs k) { if (k.Control && k.KeyCode == Keys.C) Copy(); };

            status.Dock = DockStyle.Bottom;
            status.Padding = new Padding(0, em / 2, 0, 0);

            Controls.Add(list);
            Controls.Add(toolbar);
            Controls.Add(status);
            UpdateButtons();
        }

        public override void Opened() { Reload(); }

        public override void DevicesChanged()
        {
            // Windows needs a moment before the new disk appears in WMI.
            Timer t = new Timer();
            t.Interval = 1500;
            t.Tick += delegate { t.Dispose(); Reload(); };
            t.Start();
        }

        UsbDisk Selected
        {
            get { return list.SelectedItems.Count > 0 ? (UsbDisk)list.SelectedItems[0].Tag : null; }
        }

        void UpdateButtons()
        {
            UsbDisk d = Selected;
            copy.Enabled = d != null && d.Serial.Length > 0;
        }

        async void Reload()
        {
            if (loading) { again = true; return; }
            loading = true;
            try
            {
                status.Set(Glyph.More, "Поиск USB-накопителей…", Theme.Muted);
                List<UsbDisk> disks = await Task.Run(delegate { return UsbDisk.List(); });
                list.BeginUpdate();
                list.Items.Clear();
                foreach (UsbDisk d in disks)
                {
                    ListViewItem item = new ListViewItem("Диск " + d.Number, 0);
                    item.UseItemStyleForSubItems = false;
                    item.SubItems.Add(d.Name);
                    item.SubItems.Add(string.Format("{0:N1} ГБ", d.Size / 1073741824.0));
                    ListViewItem.ListViewSubItem serial = item.SubItems.Add(d.Serial.Length > 0 ? d.Serial : "устройство не сообщает номер");
                    serial.ForeColor = d.Serial.Length > 0 ? Theme.Text : Theme.Warn;
                    if (d.Serial.Length > 0) serial.Font = new Font(Font, FontStyle.Bold);
                    item.Tag = d;
                    list.Items.Add(item);
                }
                if (list.Items.Count > 0) list.Items[0].Selected = true;
                list.EndUpdate();
                Ui.FitColumns(list);
                if (disks.Count == 0) status.Set(Glyph.Info, "USB-накопители не найдены. Подключите флешку.", Theme.Warn);
                else status.Set(Glyph.Info, "Двойной щелчок по строке или Ctrl+C копирует серийный номер.", Theme.Muted);
            }
            catch (Exception e)
            {
                status.Set(Glyph.Warning, "Не удалось получить список USB-накопителей: " + e.Message, Theme.Bad);
            }
            finally
            {
                loading = false;
                UpdateButtons();
                if (again) { again = false; Reload(); }
            }
        }

        void Copy()
        {
            UsbDisk d = Selected;
            if (d == null) return;
            if (d.Serial.Length == 0) { status.Set(Glyph.Warning, "У выбранного накопителя нет серийного номера для копирования.", Theme.Warn); return; }
            try
            {
                Clipboard.SetText(d.Serial);
                status.Set(Glyph.Check, "Серийный номер скопирован: " + d.Serial, Theme.Good);
            }
            catch (Exception e) { status.Set(Glyph.Warning, "Не удалось скопировать: " + e.Message, Theme.Bad); }
        }
    }

    // «Быстрый ping»: own address, gateway, DNS and domain or internet, stopping at the first failure (quick-ping.ps1).
    public class PingPage : Page
    {
        readonly ListView list = new ListView();
        readonly StatusLine adapter = new StatusLine();
        readonly StatusLine summary = new StatusLine();
        readonly Button run;
        bool running;

        const int Waiting = 0, Running = 1, Ok = 2, Failed = 3, Skipped = 4;

        public PingPage()
        {
            Key = "ping";
            Title = "Быстрый ping";
            Description = "Проверка связи по порядку: свой IP, шлюз, DNS, затем домен (в сети корпуса) или интернет. Один запрос на узел, ожидание до 2 секунд, остановка при первом сбое.";
            Icon = Glyph.Ping;
            int em = Font.Height;

            run = Ui.Primary("Проверить ещё раз", Glyph.Refresh, Font);
            run.Click += delegate { Run(); };
            FlowLayoutPanel toolbar = Ui.Row();
            toolbar.Controls.Add(run);
            toolbar.Dock = DockStyle.Top;
            toolbar.Padding = new Padding(0, 0, 0, em / 2);

            adapter.Dock = DockStyle.Top;
            adapter.Padding = new Padding(0, 0, 0, em / 2);

            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("Узел", em * 10);
            list.Columns.Add("Адрес", em * 12);
            list.Columns.Add("Результат", em * 24);
            ImageList images = new ImageList();
            images.ColorDepth = ColorDepth.Depth32Bit;
            int size = em + em / 2;
            images.ImageSize = new Size(size, size);
            images.Images.Add(Glyph.Image(Glyph.Circle, size, Theme.Border));
            images.Images.Add(Glyph.Image(Glyph.More, size, Theme.Info));
            images.Images.Add(Glyph.Image(Glyph.Check, size, Theme.Good));
            images.Images.Add(Glyph.Image(Glyph.Cross, size, Theme.Bad));
            images.Images.Add(Glyph.Image(Glyph.Dash, size, Theme.Muted));
            list.SmallImageList = images;
            list.Resize += delegate { if (list.Items.Count > 0) Ui.FitColumns(list); };

            summary.Dock = DockStyle.Bottom;
            summary.Padding = new Padding(0, em / 2, 0, 0);

            Controls.Add(list);
            Controls.Add(adapter);
            Controls.Add(toolbar);
            Controls.Add(summary);
        }

        public override void Opened() { Run(); }

        static void SetRow(ListViewItem item, int state, string text)
        {
            item.ImageIndex = state;
            item.SubItems[2].Text = text;
            item.SubItems[2].ForeColor = state == Ok ? Theme.Good : state == Failed ? Theme.Bad : Theme.Muted;
            Ui.FitColumns(item.ListView);
        }

        async void Run()
        {
            if (running) return;
            running = true;
            run.Enabled = false;
            list.Items.Clear();
            summary.Clear();
            adapter.Set(Glyph.More, "Определение активного адаптера и основного маршрута…", Theme.Muted);
            try
            {
                NetworkProfiles profiles = NetworkProfiles.Read(AppPaths.File("network-profiles.json"));
                QuickPing.Plan plan = await Task.Run(delegate { return QuickPing.Prepare(profiles); });
                adapter.Set(Glyph.Network, "Адаптер: " + plan.AdapterName + " (индекс " + plan.AdapterIndex + ")", Theme.Text);
                foreach (QuickPing.Target t in plan.Targets)
                {
                    ListViewItem item = new ListViewItem(t.Label, Waiting);
                    item.UseItemStyleForSubItems = false;
                    item.SubItems.Add(t.Address ?? "не настроен");
                    item.SubItems.Add("");
                    list.Items.Add(item);
                }
                for (int i = 0; i < plan.Targets.Count; i++)
                {
                    QuickPing.Target t = plan.Targets[i];
                    ListViewItem item = list.Items[i];
                    SetRow(item, Running, "отправка запроса, ожидание ответа…");
                    string details = null;
                    string error = await Task.Run(delegate { string d; string e = QuickPing.Ping(t.Address, out d); details = d; return e; });
                    if (error == null) { SetRow(item, Ok, "ответил: " + details); continue; }
                    SetRow(item, Failed, error);
                    for (int j = i + 1; j < plan.Targets.Count; j++) SetRow(list.Items[j], Skipped, "не проверялся");
                    summary.Set(Glyph.Warning, "Проверка остановлена на узле «" + t.Label + "». Следующие узлы не проверялись.", Theme.Warn);
                    return;
                }
                summary.Set(Glyph.Check, "Все узлы ответили.", Theme.Good);
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

    // Section that still runs in the console version: opens its script in a PowerShell window.
    public class ConsolePage : Page
    {
        readonly string script;
        readonly StatusLine status = new StatusLine();

        public ConsolePage(string key, string title, string description, string icon, string script, bool needsAdmin)
        {
            Key = key;
            Title = title;
            Description = description;
            Icon = icon;
            NeedsAdmin = needsAdmin;
            this.script = script;
            int em = Font.Height;

            Label text = Ui.Paragraph("Этот раздел ещё не перенесён в новое окно. Он откроется в прежней консольной версии программы: управление там стрелками, Enter и Esc." +
                (needsAdmin ? "\r\n\r\nДля изменений нужны права администратора: Windows спросит разрешение (окно UAC)." : ""), Font, Theme.Text);
            text.Padding = new Padding(0, 0, 0, em);
            Button open = Ui.Primary("Открыть в консоли", Glyph.Open, Font);
            open.Click += delegate { Launch(); };
            FlowLayoutPanel row = Ui.Row();
            row.Controls.Add(open);
            row.Dock = DockStyle.Top;
            status.Dock = DockStyle.Top;
            status.Padding = new Padding(0, em, 0, 0);
            Controls.Add(status);
            Controls.Add(row);
            Controls.Add(text);
        }

        void Launch()
        {
            string path = AppPaths.File(script);
            if (!File.Exists(path)) { status.Set(Glyph.Warning, "Не найден сценарий " + script + " рядом с программой.", Theme.Bad); return; }
            // Keep the window open at the end, as the console menu did (42 = closed with Esc).
            string command = "& { & '" + path.Replace("'", "''") + "'; if ($LASTEXITCODE -ne 42) { Write-Host ''; " +
                "Write-Host 'Нажмите любую клавишу, чтобы закрыть окно.' -ForegroundColor DarkGray; [void][Console]::ReadKey($true) } }";
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"");
                info.UseShellExecute = false;
                info.WorkingDirectory = AppPaths.Root;
                Process.Start(info);
                status.Set(Glyph.Info, "Открыто отдельное окно. Когда закончите, закройте его — эта программа продолжит работать.", Theme.Muted);
            }
            catch (Exception e) { status.Set(Glyph.Warning, "Не удалось открыть консоль: " + e.Message, Theme.Bad); }
        }
    }
}
