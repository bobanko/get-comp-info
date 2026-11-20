using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace CompInfo.Wpf
{
    // «Сбор информации» (CollectPage.cs on WinForms): reads this computer and saves its record to data\ by itself:
    // right after the program starts, whenever the data differ from the record, and after the comment is edited.
    // The page shows the computer, where its record is (the database next to the program, the cloud) and what this
    // collection changed. The reading and saving are Collection of Collector.cs.
    public class CollectView : DockPanel, ISection
    {
        // Off for screenshots of the interface: they must not write to the database.
        public static bool AutoSave = true;

        // «Синхронизировать»: the window opens the synchronization and starts it.
        public Action SyncRequested;

        // Order of the values; the last ones are technical identifiers, shown in grey.
        static readonly string[] ListedFields = { "hostname", "ip", "mac", "username", "partOfDomain", "domain", "windows", "computer", "serial", "computerId", "windowsMachineGuid" };
        static readonly string[] TechnicalFields = { "computerId", "windowsMachineGuid" };

        readonly ProgressBar progress = new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
        readonly StatusLine state = new StatusLine { Margin = new Thickness(0, 0, 0, 10) };
        readonly StackPanel header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        readonly TextBlock hostname = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold };
        readonly TextBlock summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        readonly Border eset = new Border { Margin = new Thickness(0, 0, 6, 0) }, inventory = new Border();
        readonly TextBlock localIcon = Glyph.Block(Glyph.Database, 17, "Muted"), cloudIcon = Glyph.Block(Glyph.Cloud, 17, "Muted");
        readonly TextBlock localText = new TextBlock { TextWrapping = TextWrapping.Wrap }, cloudText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly Button showFile = new Button { Visibility = Visibility.Collapsed }, sync = new Button { Visibility = Visibility.Collapsed };
        readonly Grid values = new Grid();
        readonly Border valuesFrame = new Border { Margin = new Thickness(0, 0, 0, 10) };
        readonly TextBox comment = new TextBox { MaxLength = 500 };
        readonly Button again = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        // The comment is saved a moment after typing stops, and at once when the page or the window closes.
        readonly DispatcherTimer commentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        Collection collection;
        // The record as it was before this collection saved anything: what changed is shown against it.
        JsonObject before;
        bool started, busy, filling;

        public CollectView()
        {
            SetDock(progress, Dock.Top);
            Children.Add(progress);
            SetDock(state, Dock.Top);
            Children.Add(state);

            // Header: the name, the model line and the marks of the web interface.
            summary.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            var pills = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (Border pill in new[] { eset, inventory })
            {
                pill.SetResourceReference(StyleProperty, "Pill");
                pill.Child = new TextBlock();
                pills.Children.Add(pill);
            }
            inventory.ToolTip = L.T("Отметку ставят на сайте; программа её только показывает и сохраняет при новом сборе.");
            header.Children.Add(hostname);
            header.Children.Add(summary);
            header.Children.Add(pills);
            SetDock(header, Dock.Top);
            Children.Add(header);

            // Where the record is: the database next to the program, then the cloud.
            var storage = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            foreach (GridLength w in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
                storage.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            storage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            storage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(storage, localIcon, 0, 0);
            Place(storage, Strong(AppPaths.OnRemovableDrive ? L.T("База на флешке") : L.T("База программы")), 0, 1);
            Place(storage, localText, 0, 2);
            Place(storage, showFile, 0, 3);
            Place(storage, cloudIcon, 1, 0);
            Place(storage, Strong(L.T("Облако")), 1, 1);
            Place(storage, cloudText, 1, 2);
            Place(storage, sync, 1, 3);
            if (AppPaths.OnRemovableDrive) localIcon.Text = Glyph.Usb;
            Glyph.Label(showFile, Glyph.Folder, L.T("Показать файл в папке"));
            showFile.SetResourceReference(StyleProperty, "Link");
            showFile.Click += delegate
            {
                if (collection == null) return;
                try { Process.Start("explorer.exe", "/select,\"" + collection.FilePath + "\""); } catch (Exception) { }
            };
            Glyph.Label(sync, Glyph.Sync, L.T("Синхронизировать"));
            sync.Margin = new Thickness(0, 4, 0, 4);
            sync.Click += delegate { FlushComment(); if (SyncRequested != null) SyncRequested(); };
            SetDock(storage, Dock.Top);
            Children.Add(storage);

            // the comment and «Собрать заново» at the bottom; the values fill the rest
            Glyph.Label(again, Glyph.Refresh, L.T("Собрать заново"));
            again.ToolTip = L.T("Собрать заново") + "   (F5)";
            again.Click += delegate { Collect(); };
            SetDock(again, Dock.Bottom);
            Children.Add(again);
            System.Windows.Automation.AutomationProperties.SetName(comment, L.T("Комментарий"));
            SetDock(comment, Dock.Bottom);
            Children.Add(comment);
            var commentLabel = new TextBlock { Text = L.T("Комментарий (необязательно): кабинет, ответственный, особенности. Сохраняется сам."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            commentLabel.MouseLeftButtonDown += delegate { comment.Focus(); };
            SetDock(commentLabel, Dock.Bottom);
            Children.Add(commentLabel);
            commentTimer.Tick += delegate { commentTimer.Stop(); SaveChanges(); };
            comment.TextChanged += delegate
            {
                if (collection == null || filling) return;
                if (collection.HasChanges(comment.Text)) SetLocal(Glyph.More, L.T("комментарий сейчас сохранится…"), "Muted");
                commentTimer.Stop();
                commentTimer.Start();
            };
            comment.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; FlushComment(); } };
            comment.LostKeyboardFocus += delegate { FlushComment(); };

            // All values in one table; what this collection changed is marked and shows the previous value.
            valuesFrame.SetResourceReference(StyleProperty, "TableFrame");
            valuesFrame.Child = new ScrollViewer { Content = values, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            Children.Add(valuesFrame);
            ShowRecord();
            SetBusy(false);
        }

        static TextBlock Strong(string text) { return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold }; }

        static void Place(Grid grid, FrameworkElement e, int row, int column)
        {
            if (e is TextBlock) e.Margin = new Thickness(0, 4, column == 0 ? 8 : 16, 4);
            e.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(e, row);
            Grid.SetColumn(e, column);
            grid.Children.Add(e);
        }

        // Every opening reads the computer again: whatever changed since (name, address, domain) is saved at once.
        public void Opened()
        {
            started = true;
            if (!busy) Collect();
            else comment.Focus();
        }

        // Called when the program starts on another section: the computer is read and saved in the background.
        public void StartInBackground()
        {
            if (started) return;
            started = true;
            Collect();
        }

        public bool CanLeave()
        {
            FlushComment();
            return true;
        }

        public bool Shortcut(KeyEventArgs e)
        {
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None) { Collect(); return true; }
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { FlushComment(); return true; }
            return false;
        }

        public void DevicesChanged() { }

        void SetBusy(bool value)
        {
            busy = value;
            progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            again.IsEnabled = !value;
            comment.IsEnabled = !value && collection != null;
        }

        void FlushComment()
        {
            if (!commentTimer.IsEnabled) return;
            commentTimer.Stop();
            SaveChanges();
        }

        async void Collect()
        {
            if (busy) return;
            FlushComment();
            collection = null;
            before = null;
            ShowRecord();
            state.Note(Glyph.More, L.T("Собираем информацию о компьютере…"));
            SetBusy(true);
            string dataPath = AppPaths.Data;
            try
            {
                Collection c = await Task.Run(() => Collection.Start(dataPath));
                if (c.Legacy != null && !IsVisible)
                {
                    // Read in the background while another section is open: ask when the user comes here.
                    started = false;
                    state.Show(Severity.Warning, Glyph.Info, L.T("Найдена старая запись с таким же именем компьютера: откройте этот раздел, чтобы решить, чья она."));
                    return;
                }
                if (c.Legacy != null)
                {
                    string text = L.T("Найдена старая запись без UUID и серийного номера с таким же именем компьютера:\n\n") +
                        System.IO.Path.GetFileName(c.Legacy.Path) + "\n" +
                        L.T("Имя: ") + c.Legacy.Record.Text("hostname") + L.T("\nСобрана: ") + Fmt.Date(c.Legacy.Record.Text("collectedAt")) +
                        L.T("\nКомментарий: ") + (c.Legacy.Record.Text("comment") ?? "") +
                        L.T("\n\nЭто запись именно этого компьютера? Если да, она будет обновлена и привязана к этой установке Windows.");
                    if (!Dialog.Ask(Window.GetWindow(this), text, L.T("Да, это этот компьютер"), L.T("Нет"), Severity.Info, L.T("Старая запись")))
                    {
                        state.Show(Severity.Warning, Glyph.Info, L.T("Сбор отменён: старую запись не привязали. Нажмите «Собрать заново», чтобы решить ещё раз."));
                        return;
                    }
                    c.BindLegacy(dataPath);
                }
                if (c.PreviousError != null)
                {
                    state.Show(Severity.Error, Glyph.Warning, L.T("Не удалось прочитать сохранённую запись этого компьютера: ") + c.PreviousError);
                    return;
                }
                collection = c;
                before = c.Previous;
                state.Clear();
                filling = true;
                comment.Text = c.PreviousComment;
                filling = false;
                ShowRecord();
                busy = false;
                SaveChanges();
                if (IsVisible)
                {
                    comment.IsEnabled = true;
                    comment.Focus();
                    comment.CaretIndex = comment.Text.Length;
                }
            }
            catch (Exception e)
            {
                state.Show(Severity.Error, Glyph.Warning, L.T("Не удалось собрать информацию: ") + e.Message);
            }
            finally { SetBusy(false); }
        }

        // The header, the values and both storage lines for the collection (empty while nothing is read).
        void ShowRecord()
        {
            Collection c = collection;
            header.Visibility = valuesFrame.Visibility = c == null ? Visibility.Hidden : Visibility.Visible;
            if (c == null)
            {
                values.Children.Clear();
                SetLocal(null, "", "Muted");
                SetCloud(null, "", "Muted", false);
                showFile.Visibility = Visibility.Collapsed;
                return;
            }
            JsonObject current = c.Build(comment.Text, "");
            hostname.Text = Shown(Collection.Show(current, "hostname"));
            var parts = new List<string>();
            string computer = Collection.Show(current, "computer");
            if (!string.IsNullOrEmpty(computer)) parts.Add(computer);
            if (!string.IsNullOrEmpty(current.Text("serial"))) parts.Add("S/N " + current.Text("serial"));
            if (!string.IsNullOrEmpty(current.Text("domain"))) parts.Add(current.Text("domain"));
            summary.Text = string.Join("  ·  ", parts);
            object esetValue = current["esetInstalled"];
            ((TextBlock)eset.Child).Text = "ESET: " + ComputerSnapshot.EsetText(esetValue).ToLowerInvariant();
            eset.Tag = esetValue is bool ? ((bool)esetValue ? "Good" : "Warn") : "Muted";
            bool inventoried = current["inventoried"] is bool && (bool)current["inventoried"];
            ((TextBlock)inventory.Child).Text = L.T("Инвентаризация: ") + (inventoried ? L.T("да") : L.T("нет"));
            inventory.Tag = inventoried ? "Good" : "Muted";

            // Values against the record before this collection.
            var fields = new Dictionary<string, Collection.Field>();
            foreach (Collection.Field f in c.Compare(current))
            {
                f.Previous = before == null ? null : Collection.Show(before, f.Name);
                fields[f.Name] = f;
            }
            var listed = new List<Collection.Field>();
            foreach (string name in ListedFields) if (fields.ContainsKey(name)) listed.Add(fields[name]);
            bool anyChange = false;
            foreach (Collection.Field f in listed) if (before != null && f.Changed) anyChange = true;
            values.Children.Clear();
            values.RowDefinitions.Clear();
            values.ColumnDefinitions.Clear();
            values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24, GridUnitType.Star) });
            values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46, GridUnitType.Star) });
            values.ColumnDefinitions.Add(new ColumnDefinition { Width = anyChange ? new GridLength(30, GridUnitType.Star) : new GridLength(0) });
            for (int i = 0; i < listed.Count; i++)
            {
                Collection.Field f = listed[i];
                bool changed = before != null && f.Changed;
                bool technical = Array.IndexOf(TechnicalFields, f.Name) >= 0;
                values.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var line = new Border { BorderThickness = new Thickness(0, 0, 0, i < listed.Count - 1 ? 1 : 0) };
                line.SetResourceReference(Border.BorderBrushProperty, "Divider");
                if (changed) line.SetResourceReference(Border.BackgroundProperty, "WarnBack");
                Grid.SetRow(line, i);
                Grid.SetColumnSpan(line, 3);
                values.Children.Add(line);
                AddCell(f.Label, i, 0, "Muted");
                AddCell(Shown(f.Current), i, 1, string.IsNullOrEmpty(f.Current) || technical ? "Muted" : null);
                if (changed) AddCell(L.T("было: ") + Shown(f.Previous), i, 2, "Muted");
            }
            ShowStorage();
        }

        void AddCell(string text, int row, int column, string brushKey)
        {
            // selectable, so a value can be copied
            var t = new TextBox { Text = text, IsReadOnly = true, IsTabStop = false, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(column == 0 ? 14 : 0, 7, 10, 7), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(StyleProperty, "BareTextBox");
            if (brushKey != null) t.SetResourceReference(Control.ForegroundProperty, brushKey);
            Grid.SetRow(t, row);
            Grid.SetColumn(t, column);
            values.Children.Add(t);
        }

        // The line of the database next to the program: when the record was saved.
        void ShowStorage()
        {
            Collection c = collection;
            if (c == null) return;
            JsonObject saved = c.Previous;
            if (saved == null)
                SetLocal(Glyph.Info, AutoSave ? L.T("записи ещё нет, сейчас будет создана") : L.T("записи этого компьютера ещё нет"), "Warn");
            else if (c.HasChanges(comment.Text))
                SetLocal(Glyph.More, L.T("есть изменения, сейчас сохранятся"), "Warn");
            else
                SetLocal(Glyph.Check, L.T("сохранено ") + Fmt.Date(saved.Text("collectedAt")), "Good");
            showFile.Visibility = saved != null ? Visibility.Visible : Visibility.Collapsed;
            ShowCloud();
        }

        // The line of the cloud: whether the saved record is the one the cloud has.
        void ShowCloud()
        {
            Collection c = collection;
            if (c == null || c.Previous == null) { SetCloud(Glyph.Cloud, L.T("записи ещё нет в облаке"), "Muted", false); return; }
            try { CloudConfig.ReadApiUrl(AppPaths.Root); }
            catch (Exception) { SetCloud(Glyph.Cloud, L.T("облако не настроено в этой копии программы"), "Muted", false); return; }
            CloudState cloud = CloudState.ForDisplay(AppPaths.Root);
            string status;
            try { status = cloud.StatusOf(Records.Load(c.FilePath, cloud)); }
            catch (Exception e) { SetCloud(Glyph.Warning, e.Message, "Bad", false); return; }
            string last = L.T(" · последняя синхронизация: ") + (string.IsNullOrEmpty(cloud.LastSyncAt) ? L.T("не было") : Fmt.Date(cloud.LastSyncAt));
            if (status == CloudState.Synced) SetCloud(Glyph.Check, L.T("в облаке та же запись") + last, "Good", false);
            else if (status == CloudState.Changed) SetCloud(Glyph.Upload, L.T("есть изменения, ещё не отправленные в облако") + last, "Warn", true);
            else SetCloud(Glyph.Upload, L.T("записи ещё нет в облаке") + last, "Warn", true);
        }

        // glyph null: nothing read yet, the icon stays grey
        void SetLocal(string glyph, string text, string key)
        {
            localIcon.SetResourceReference(TextBlock.ForegroundProperty, glyph == null ? "Muted" : key);
            localText.Text = text;
            localText.SetResourceReference(TextBlock.ForegroundProperty, key);
        }

        void SetCloud(string glyph, string text, string key, bool canSync)
        {
            cloudIcon.SetResourceReference(TextBlock.ForegroundProperty, glyph == null ? "Muted" : key);
            cloudText.Text = text;
            cloudText.SetResourceReference(TextBlock.ForegroundProperty, key);
            sync.Visibility = canSync ? Visibility.Visible : Visibility.Collapsed;
            // without «Синхронизировать» the line has the room up to the right edge
            Grid.SetColumnSpan(cloudText, canSync ? 1 : 2);
        }

        static string Shown(string value)
        {
            return string.IsNullOrEmpty(value) ? L.T("(не указано)") : value;
        }

        // Saves the record when it differs from the saved one.
        void SaveChanges()
        {
            if (collection == null || busy || !AutoSave || !collection.HasChanges(comment.Text)) { ShowStorage(); return; }
            bool isNew = collection.Previous == null;
            try
            {
                JsonObject saved = collection.Save(comment.Text);
                collection.Previous = saved;
                ShowRecord();
                SetLocal(Glyph.Check, (isNew ? L.T("новая запись сохранена в ") : L.T("сохранено в ")) + DateTime.Now.ToString("HH:mm"), "Good");
            }
            catch (Exception e)
            {
                SetLocal(Glyph.Warning, L.T("не удалось сохранить: ") + e.Message, "Bad");
            }
        }
    }
}
