using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
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
