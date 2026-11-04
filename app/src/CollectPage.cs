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
        readonly DataGridView grid = new DataGridView();
        readonly TextBox comment = new TextBox();
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
            Title = "Сбор информации о компьютере";
            Description = "Программа считывает имя, серийный номер, сеть и другие сведения об этом компьютере и сохраняет их в базу на флешке.";
            Icon = Glyph.Computer;
            int em = Font.Height;

            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 30;
            progress.Height = em / 2;
            progress.Dock = DockStyle.Top;
            progress.Margin = new Padding(0, 0, 0, em / 2);

            state.Dock = DockStyle.Top;
            state.Margin = new Padding(0, 0, 0, em / 2);

            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Theme.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Sidebar;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Sidebar;
            grid.ColumnHeadersHeight = em * 2;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowTemplate.Height = em + em / 2;
            grid.DefaultCellStyle.SelectionBackColor = grid.DefaultCellStyle.BackColor;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add("field", "Поле");
            grid.Columns.Add("current", "Сейчас на компьютере");
            grid.Columns.Add("saved", "Сохранено в базе");
            grid.Columns[0].FillWeight = 22;
            grid.Columns[1].FillWeight = 39;
            grid.Columns[2].FillWeight = 39;
            grid.Columns[0].DefaultCellStyle.ForeColor = Theme.Muted;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Margin = new Padding(0, 0, 0, em);

            Label commentLabel = Ui.Text("Комментарий (необязательно): кабинет, ответственный, особенности", Font, Theme.Text);
            commentLabel.Margin = new Padding(0, 0, 0, em / 4);
            comment.Dock = DockStyle.Top;
            comment.MaxLength = 500;
            comment.Margin = new Padding(0, 0, 0, em);
            comment.TextChanged += delegate { if (collection != null) ShowComparison(); };
            comment.KeyDown += delegate (object o, KeyEventArgs k)
            {
                if (k.KeyCode == Keys.Enter && save.Enabled) { k.SuppressKeyPress = true; Save(); }
            };

            save = Ui.Primary("Сохранить в базу", Glyph.Save, Font);
            save.Click += delegate { Save(); };
            again = Ui.Secondary("Собрать заново", Glyph.Refresh, Font);
            again.Click += delegate { Collect(); };
            FlowLayoutPanel actions = Ui.Row();
            actions.Controls.Add(save);
            actions.Controls.Add(again);
            actions.Margin = new Padding(0, 0, 0, em / 2);

            result.Dock = DockStyle.Top;
            showFile.Text = "Показать файл в папке";
            showFile.AutoSize = true;
            showFile.LinkColor = Theme.Info;
            showFile.Margin = new Padding(em + em / 3, 0, 0, 0);
            showFile.Visible = false;
            showFile.LinkClicked += delegate { if (collection != null) Ui.ShowInExplorer(collection.FilePath); };

            body.Dock = DockStyle.Fill;
            body.ColumnCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(progress, SizeType.AutoSize);
            AddRow(state, SizeType.AutoSize);
            AddRow(grid, SizeType.Percent);
            AddRow(commentLabel, SizeType.AutoSize);
            AddRow(comment, SizeType.AutoSize);
            AddRow(actions, SizeType.AutoSize);
            AddRow(result, SizeType.AutoSize);
            AddRow(showFile, SizeType.AutoSize);
            Controls.Add(body);
            SetBusy(false);
        }

        void AddRow(Control c, SizeType size)
        {
            body.RowStyles.Add(size == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            if (c.Dock == DockStyle.Top) c.Dock = DockStyle.Fill;
            // Explicit cell: a hidden control must keep its row, or the rows below shift into the wrong styles.
            body.RowCount = body.RowStyles.Count;
            body.Controls.Add(c, 0, body.RowStyles.Count - 1);
        }

        public override void Opened()
        {
            if (!started) { started = true; Collect(); }
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
            state.Set(Glyph.More, "Собираем информацию о компьютере…", Theme.Muted);
            SetBusy(true);
            string dataPath = AppPaths.Data;
            try
            {
                Collection c = await Task.Run(delegate { return Collection.Start(dataPath); });
                if (c.Legacy != null)
                {
                    string text = "Найдена старая запись без UUID и серийного номера с таким же именем компьютера:\n\n" +
                        System.IO.Path.GetFileName(c.Legacy.Path) + "\n" +
                        "Имя: " + c.Legacy.Record.Text("hostname") + "\nСобрана: " + Ui.Date(c.Legacy.Record.Text("collectedAt")) +
                        "\nКомментарий: " + (c.Legacy.Record.Text("comment") ?? "") +
                        "\n\nЭто запись именно этого компьютера? Если да, она будет обновлена и привязана к этой установке Windows.";
                    if (MessageBox.Show(FindForm(), text, "Старая запись", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        state.Set(Glyph.Info, "Сбор отменён: старую запись не привязали. Нажмите «Собрать заново», чтобы решить ещё раз.", Theme.Warn);
                        return;
                    }
                    c.BindLegacy(dataPath);
                }
                if (c.PreviousError != null)
                {
                    state.Set(Glyph.Warning, "Не удалось прочитать сохранённую запись этого компьютера: " + c.PreviousError, Theme.Bad);
                    return;
                }
                collection = c;
                comment.Text = c.PreviousComment;
                ShowComparison();
            }
            catch (Exception e)
            {
                state.Set(Glyph.Warning, "Не удалось собрать информацию: " + e.Message, Theme.Bad);
            }
            finally { SetBusy(false); }
        }

        void ShowComparison()
        {
            Collection c = collection;
            JsonObject current = c.Build(comment.Text, "");
            List<Collection.Field> fields = c.Compare(current);
            bool hasPrevious = c.Previous != null;
            grid.Columns[2].Visible = hasPrevious;
            grid.Columns[1].HeaderText = hasPrevious ? "Сейчас на компьютере" : "Значение";
            int changes = 0;
            grid.SuspendLayout();
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
                Color back = changed ? Theme.WarnLight : Color.White;
                row.DefaultCellStyle.BackColor = back;
                row.DefaultCellStyle.SelectionBackColor = back;
                row.Cells[1].Style.ForeColor = string.IsNullOrEmpty(f.Current) ? Theme.Muted : Theme.Text;
                row.Cells[2].Style.ForeColor = string.IsNullOrEmpty(f.Previous) ? Theme.Muted : Theme.Text;
            }
            grid.ResumeLayout();
            grid.ClearSelection();

            if (!hasPrevious)
                state.Set(Glyph.Info, "Этого компьютера ещё нет в базе — будет создана новая запись.", Theme.Info);
            else if (changes == 0)
                state.Set(Glyph.Check, "Запись этого компьютера уже есть в базе и совпадает с текущими данными. Сохранять нечего. Предыдущий сбор: " +
                    Ui.Date(c.Previous.Text("collectedAt")) + ".", Theme.Good);
            else
                state.Set(Glyph.Warning, "Запись этого компьютера уже есть в базе. Изменения (" + changes + ") выделены жёлтым — сохраните, чтобы обновить запись. Предыдущий сбор: " +
                    Ui.Date(c.Previous.Text("collectedAt")) + ".", Theme.Warn);
            SetBusy(false);
        }

        static string Shown(string value)
        {
            return string.IsNullOrEmpty(value) ? "(не указано)" : value;
        }

        void Save()
        {
            if (collection == null || busy) return;
            try
            {
                JsonObject saved = collection.Save(comment.Text);
                collection.Previous = saved;
                ShowComparison();
                result.Set(Glyph.Check, "Сохранено " + DateTime.Now.ToString("HH:mm") + ": " + collection.FilePath, Theme.Good);
                showFile.Visible = true;
            }
            catch (Exception e)
            {
                result.Set(Glyph.Warning, "Не удалось сохранить: " + e.Message, Theme.Bad);
            }
        }
    }
}
