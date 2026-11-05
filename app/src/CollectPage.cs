using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    // «Сбор информации»: reads this computer, compares with its saved record and saves it to data\.
    public class CollectPage : Page
    {
        readonly ProgressBar progress = new ProgressBar();
        readonly StatusLine state = new StatusLine();
        readonly DataGridView grid;
        readonly TextBox comment = Ui.Input(new TextBox());
        readonly Button save;
        readonly Button again;
        readonly StatusLine result = new StatusLine();
        readonly LinkLabel showFile = new LinkLabel();
        readonly TableLayoutPanel body = new TableLayoutPanel();
        Collection collection;
        bool started, busy;

        public CollectPage()
        {
            Key = "collect";
            Title = L.T("Сбор информации о компьютере");
            NavTitle = L.T("Сбор информации");
            Description = L.T("Имя, серийный номер, сеть и другие сведения об этом компьютере сохраняются в базу на флешке.");
            Icon = Glyph.Computer;
            int em = Font.Height;

            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 30;
            progress.Height = em / 3;
            progress.Margin = new Padding(0, 0, 0, em / 2);

            state.Margin = new Padding(0, 0, 0, em / 2);

            grid = Ui.Grid(Font);
            grid.Columns.Add("field", L.T("Поле"));
            grid.Columns.Add("current", L.T("Сейчас на компьютере"));
            grid.Columns.Add("saved", L.T("Сохранено в базе"));
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns[0].FillWeight = 24;
            grid.Columns[1].FillWeight = 38;
            grid.Columns[2].FillWeight = 38;
            grid.Columns[0].DefaultCellStyle.ForeColor = Theme.Muted;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Margin = new Padding(0, 0, 0, em / 2);

            Label commentLabel = Ui.Text(L.T("Комментарий (необязательно): кабинет, ответственный, особенности"), Font, Theme.Text);
            commentLabel.Margin = new Padding(0, 0, 0, em / 4);
            comment.MaxLength = 500;
            comment.Margin = new Padding(0, 0, 0, em / 2);
            comment.TextChanged += delegate { if (collection != null) ShowComparison(); };
            comment.KeyDown += delegate (object o, KeyEventArgs k)
            {
                if (k.KeyCode == Keys.Enter && save.Enabled) { k.SuppressKeyPress = true; Save(); }
            };

            save = Ui.Primary(L.T("Сохранить в базу"), Glyph.Save, Font);
            save.Click += delegate { Save(); };
            again = Ui.Secondary(L.T("Собрать заново"), Glyph.Refresh, Font);
            again.Click += delegate { Collect(); };
            Ui.Keys(save, "Ctrl+S");
            Ui.Keys(again, "F5");
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(save);
            actions.Controls.Add(again);
            actions.Margin = new Padding(0, 0, 0, em / 3);

            showFile.Text = L.T("Показать файл в папке");
            showFile.AutoSize = true;
            showFile.LinkColor = showFile.ActiveLinkColor = Theme.Info;
            showFile.Margin = new Padding(em + em / 3, 0, 0, 0);
            showFile.Visible = false;
            showFile.LinkClicked += delegate { if (collection != null) Ui.ShowInExplorer(collection.FilePath); };

            body.Dock = DockStyle.Fill;
            body.ColumnCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(progress, false);
            AddRow(state, false);
            AddRow(grid, true);
            AddRow(commentLabel, false);
            AddRow(comment, false);
            AddRow(actions, false);
            AddRow(result, false);
            AddRow(showFile, false);
            Controls.Add(body);
            SetBusy(false);
        }

        void AddRow(Control c, bool fill)
        {
            body.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            if (!(c is FlowLayoutPanel) && !(c is LinkLabel) && !(c is Label)) c.Dock = DockStyle.Fill;
            // Explicit cell: a hidden control must keep its row, or the rows below shift into the wrong styles.
            body.RowCount = body.RowStyles.Count;
            body.Controls.Add(c, 0, body.RowStyles.Count - 1);
        }

        public override void Opened()
        {
            if (!started) { started = true; Collect(); }
            else comment.Focus();
        }

        public override bool Shortcut(Keys keys)
        {
            if (keys == Keys.F5) { Collect(); return true; }
            if (keys == (Keys.Control | Keys.S)) { if (save.Enabled) Save(); return true; }
            return false;
        }

        void SetBusy(bool value)
        {
            busy = value;
            progress.Visible = value;
            again.Enabled = !value;
            comment.Enabled = !value && collection != null;
            save.Enabled = !value && collection != null && collection.HasChanges(comment.Text);
        }

        async void Collect()
        {
            if (busy) return;
            collection = null;
            grid.Rows.Clear();
            result.Clear();
            showFile.Visible = false;
            state.Set(Glyph.More, L.T("Собираем информацию о компьютере…"), Theme.Muted);
            SetBusy(true);
            string dataPath = AppPaths.Data;
            try
            {
                Collection c = await Task.Run(delegate { return Collection.Start(dataPath); });
                if (c.Legacy != null)
                {
                    string text = L.T("Найдена старая запись без UUID и серийного номера с таким же именем компьютера:\n\n") +
                        System.IO.Path.GetFileName(c.Legacy.Path) + "\n" +
                        L.T("Имя: ") + c.Legacy.Record.Text("hostname") + L.T("\nСобрана: ") + Fmt.Date(c.Legacy.Record.Text("collectedAt")) +
                        L.T("\nКомментарий: ") + (c.Legacy.Record.Text("comment") ?? "") +
                        L.T("\n\nЭто запись именно этого компьютера? Если да, она будет обновлена и привязана к этой установке Windows.");
                    if (Ui.Ask(FindForm(), L.T("Старая запись"), text, Glyph.Info, Theme.Info, L.T("Да, это этот компьютер"), L.T("Нет")) != 0)
                    {
                        state.Set(Glyph.Info, L.T("Сбор отменён: старую запись не привязали. Нажмите «Собрать заново», чтобы решить ещё раз."), Theme.Warn);
                        return;
                    }
                    c.BindLegacy(dataPath);
                }
                if (c.PreviousError != null)
                {
                    state.Set(Glyph.Warning, L.T("Не удалось прочитать сохранённую запись этого компьютера: ") + c.PreviousError, Theme.Bad);
                    return;
                }
                collection = c;
                comment.Text = c.PreviousComment;
                ShowComparison();
                comment.Focus();
                comment.SelectionStart = comment.Text.Length;
            }
            catch (Exception e)
            {
                state.Set(Glyph.Warning, L.T("Не удалось собрать информацию: ") + e.Message, Theme.Bad);
            }
            finally { SetBusy(false); }
        }

        void ShowComparison()
        {
            Collection c = collection;
            List<Collection.Field> fields = c.Compare(c.Build(comment.Text, ""));
            bool hasPrevious = c.Previous != null;
            grid.Columns[2].Visible = hasPrevious;
            grid.Columns[1].HeaderText = hasPrevious ? L.T("Сейчас на компьютере") : L.T("Значение");
            int changes = 0;
            if (grid.Rows.Count != fields.Count) { grid.Rows.Clear(); foreach (Collection.Field f in fields) grid.Rows.Add(); }
            for (int i = 0; i < fields.Count; i++)
            {
                Collection.Field f = fields[i];
                DataGridViewRow row = grid.Rows[i];
                row.Cells[0].Value = f.Label;
                row.Cells[1].Value = Shown(f.Current);
                row.Cells[2].Value = Shown(f.Previous);
                bool changed = hasPrevious && f.Changed;
                if (changed) changes++;
                Color back = changed ? Theme.WarnLight : Theme.Surface;
                row.DefaultCellStyle.BackColor = back;
                row.DefaultCellStyle.SelectionBackColor = back;
                row.Cells[1].Style.ForeColor = string.IsNullOrEmpty(f.Current) ? Theme.Muted : Theme.Text;
                row.Cells[2].Style.ForeColor = string.IsNullOrEmpty(f.Previous) ? Theme.Muted : Theme.Text;
            }
            grid.ClearSelection();

            if (!hasPrevious)
                state.Set(Glyph.Info, L.T("Этого компьютера ещё нет в базе — будет создана новая запись."), Theme.Info);
            else if (changes == 0)
                state.Set(Glyph.Check, L.T("Запись уже есть в базе и совпадает с текущими данными, сохранять нечего. Предыдущий сбор: ") +
                    Fmt.Date(c.Previous.Text("collectedAt")) + ".", Theme.Good);
            else
                state.Set(Glyph.Warning, L.T("Запись уже есть в базе. Изменения (") + changes + L.T(") выделены — сохраните, чтобы обновить запись. Предыдущий сбор: ") +
                    Fmt.Date(c.Previous.Text("collectedAt")) + ".", Theme.Warn);
            SetBusy(false);
        }

        static string Shown(string value)
        {
            return string.IsNullOrEmpty(value) ? L.T("(не указано)") : value;
        }

        void Save()
        {
            if (collection == null || busy) return;
            try
            {
                JsonObject saved = collection.Save(comment.Text);
                collection.Previous = saved;
                ShowComparison();
                result.Set(Glyph.Check, L.T("Сохранено ") + DateTime.Now.ToString("HH:mm") + ": " + System.IO.Path.GetFileName(collection.FilePath), Theme.Good);
                showFile.Visible = true;
            }
            catch (Exception e)
            {
                result.Set(Glyph.Warning, L.T("Не удалось сохранить: ") + e.Message, Theme.Bad);
            }
        }
    }
}
