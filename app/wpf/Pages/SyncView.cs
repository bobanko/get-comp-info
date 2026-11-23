using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // «Синхронизация с облаком» (SyncPage of CloudPages.cs on WinForms): the personal key, then CloudSync of Cloud.cs
    // in the background; its questions (a conflict, a record deleted in the cloud) come to the window as dialogs.
    public class SyncView : DockPanel, ISection, SyncStarter
    {
        // «Доступна новая версия»: the window opens «Обновление».
        public Action UpdateRequested;

        readonly TextBlock server = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        readonly Button start = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        readonly ProgressBar progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Hidden, Margin = new Thickness(0, 10, 0, 0) };
        readonly StatusLine step = new StatusLine { Margin = new Thickness(0, 10, 0, 10) };
        readonly Grid counts = new Grid { Visibility = Visibility.Collapsed };
        readonly Border countsFrame = new Border { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        readonly Button newVersion = new Button { Visibility = Visibility.Collapsed, Margin = new Thickness(-11, 8, 0, 0) };
        readonly TextBox errors = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        bool running;

        public SyncView()
        {
            server.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            SetDock(server, Dock.Top);
            Children.Add(server);
            Glyph.Label(start, Glyph.Sync, L.T("Синхронизировать"));
            start.ToolTip = L.T("Синхронизировать") + "   (F5)";
            start.SetResourceReference(StyleProperty, "AccentButton");
            start.Click += delegate { Run(); };
            SetDock(start, Dock.Top);
            Children.Add(start);
            progress.SetResourceReference(StyleProperty, "ThinProgress");
            SetDock(progress, Dock.Top);
            Children.Add(progress);
            SetDock(step, Dock.Top);
            Children.Add(step);
            countsFrame.SetResourceReference(StyleProperty, "TableFrame");
            countsFrame.Child = counts;
            counts.ColumnDefinitions.Add(new ColumnDefinition());
            counts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            counts.Visibility = Visibility.Visible;
            SetDock(countsFrame, Dock.Top);
            Children.Add(countsFrame);
            newVersion.SetResourceReference(StyleProperty, "Link");
            newVersion.Click += delegate { if (UpdateRequested != null) UpdateRequested(); };
            SetDock(newVersion, Dock.Top);
            Children.Add(newVersion);
            errors.SetResourceReference(Control.ForegroundProperty, "Bad");
            Children.Add(errors);
        }

        public bool Shortcut(KeyEventArgs e)
        {
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None) { Run(); return true; }
            return false;
        }

        public void Opened()
        {
            if (running) return;
            start.Focus();
            try
            {
                CloudState state = CloudState.ForDisplay(AppPaths.Root);
                server.Text = L.T("Облако: ") + CloudConfig.ReadApiUrl(AppPaths.Root) + L.T("\r\nПоследняя синхронизация этой копии: ") +
                    (string.IsNullOrEmpty(state.LastSyncAt) ? L.T("не было") : Fmt.Date(state.LastSyncAt));
            }
            catch (Exception e) { server.Text = e.Message; }
        }

        public bool CanLeave()
        {
            if (running) Dialog.Show(Window.GetWindow(this), L.T("Дождитесь окончания синхронизации."), Severity.Info, L.T("Синхронизация"));
            return !running;
        }

        public void DevicesChanged() { }

        // Started from another section (the cloud status of «Сбор информации»).
        public void Start() { Run(); }

        async void Run()
        {
            if (running) return;
            running = true;
            start.IsEnabled = false;
            countsFrame.Visibility = errors.Visibility = newVersion.Visibility = Visibility.Collapsed;
            step.Note(Glyph.More, L.T("Подключение к облаку…"));
            progress.Visibility = Visibility.Visible;
            try
            {
                Window owner = Window.GetWindow(this);
                CloudClient client = await CloudConnect.Connect(owner);
                if (client == null) { step.Show(Severity.Warning, Glyph.Info, L.T("Синхронизация отменена: ключ не введён.")); return; }
                step.Note(Glyph.Person, L.T("Пользователь: ") + CloudClient.UserText(client.User));
                var sync = new CloudSync(AppPaths.Root, client, new SyncQuestions(owner, text => step.Note(Glyph.More, text)));
                SyncCounts c = await Task.Run(() => sync.Run());
                ShowCounts(c);
                if (c.Errors.Count == 0) step.Show(Severity.Success, Glyph.Check, L.T("Синхронизация завершена."));
                else step.Show(Severity.Warning, Glyph.Warning, L.T("Синхронизация завершена с ошибками (") + c.Errors.Count + ").");
            }
            catch (Exception e)
            {
                step.Show(Severity.Error, Glyph.Warning, L.T("Синхронизация не завершена: ") + e.Message +
                    L.T("\r\nУже обработанные записи сохранены. После устранения причины запустите синхронизацию ещё раз."));
            }
            finally
            {
                progress.Visibility = Visibility.Hidden;
                running = false;
                start.IsEnabled = true;
                Opened();
            }
        }

        void ShowCounts(SyncCounts c)
        {
            counts.Children.Clear();
            counts.RowDefinitions.Clear();
            AddCount(L.T("Отправлено новых"), c.PushedNew, false);
            AddCount(L.T("Отправлено изменённых"), c.PushedUpdated, false);
            AddCount(L.T("Совпало с облаком"), c.PushedSame, false);
            AddCount(L.T("Получено новых из облака"), c.ReceivedNew, false);
            AddCount(L.T("Обновлено из облака"), c.ReceivedUpdated, false);
            AddCount(L.T("Удалено по облаку"), c.Deleted, false);
            AddCount(L.T("Конфликтов отложено"), c.Deferred, true);
            if (c.Merged > 0) AddCount(L.T("Лишних файлов одной записи убрано"), c.Merged, false);
            if (c.Bound > 0) AddCount(L.T("Продолжено записей из облака"), c.Bound, false);
            string profiles = c.NetworkProfiles == "updated" ? L.T("обновлены") : c.NetworkProfiles == "unchanged" ? L.T("без изменений") :
                c.NetworkProfiles == "missing" ? L.T("в облаке не заданы") : L.T("не загружены");
            AddLine(L.T("Профили сети"), profiles, "Text", c.NetworkProfiles == "updated" ? "Accent" : "Muted");
            string people = c.People == "updated" ? L.T("обновлён") : c.People == "unchanged" ? L.T("без изменений") :
                c.People == "missing" ? L.T("в облаке нет") : L.T("не загружен");
            AddLine(L.T("Список людей"), people, "Text", c.People == "updated" ? "Accent" : "Muted");
            countsFrame.Visibility = Visibility.Visible;
            if (c.Errors.Count > 0)
            {
                errors.Text = L.T("Ошибки:\r\n") + string.Join("\r\n", c.Errors);
                errors.Visibility = Visibility.Visible;
            }
            if (c.NewVersion != null)
            {
                Glyph.Label(newVersion, Glyph.Update, L.T("Доступна новая версия программы ") + c.NewVersion + L.T(": раздел «Обновление» (нажмите здесь)."));
                newVersion.Visibility = Visibility.Visible;
            }
        }

        void AddCount(string name, int value, bool warnWhenAny)
        {
            AddLine(name, value.ToString(CultureInfo.CurrentCulture), value == 0 ? "Muted" : "Text", value == 0 ? "Muted" : warnWhenAny ? "Warn" : "Accent");
        }

        void AddLine(string name, string value, string nameKey, string valueKey)
        {
            int row = counts.RowDefinitions.Count;
            counts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { BorderThickness = new Thickness(0, row == 0 ? 0 : 1, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "Divider");
            Grid.SetRow(line, row);
            Grid.SetColumnSpan(line, 2);
            counts.Children.Add(line);
            var n = new TextBlock { Text = name, Margin = new Thickness(14, 7, 10, 7) };
            n.SetResourceReference(TextBlock.ForegroundProperty, nameKey);
            Grid.SetRow(n, row);
            counts.Children.Add(n);
            var v = new TextBlock { Text = value, Margin = new Thickness(0, 7, 14, 7), HorizontalAlignment = HorizontalAlignment.Right, FontWeight = FontWeights.SemiBold };
            v.SetResourceReference(TextBlock.ForegroundProperty, valueKey);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            counts.Children.Add(v);
        }
    }

    // The questions of the synchronization, asked from its background thread in the window (SyncDialogs on WinForms).
    public class SyncQuestions : ISyncQuestions
    {
        readonly Window owner;
        readonly Action<string> progress;

        public SyncQuestions(Window owner, Action<string> progress)
        {
            this.owner = owner;
            this.progress = progress;
        }

        public void Progress(string text) { owner.Dispatcher.BeginInvoke(new Action(() => progress(text))); }

        public string ResolveConflict(string title, JsonObject local, JsonObject cloud, bool cloudNewer, out JsonObject result)
        {
            JsonObject chosen = null;
            string action = owner.Dispatcher.Invoke(() =>
            {
                var w = new ConflictWindow(title, local, cloud, cloudNewer) { Owner = owner };
                w.ShowDialog();
                chosen = w.Result;
                return w.Action;
            });
            result = chosen;
            return action;
        }

        // 0 continue the cloud record (the main button), 1 a separate record, 2 defer
        public int SameComputer(string title, string details)
        {
            return owner.Dispatcher.Invoke(() =>
            {
                int a = Dialog.Choose(owner, L.T("Этот компьютер уже есть в облаке"), title + "\n\n" + details, Severity.Warning,
                    L.T("Продолжить запись из облака"), L.T("Отдельная запись"), L.T("Отложить"));
                return a < 0 ? 2 : a;
            });
        }

        // 0 restore in the cloud, 1 delete the local record, 2 defer. The newer side is the main button (Enter).
        public int DeletedInCloud(string title, string details, bool deletionNewer)
        {
            return owner.Dispatcher.Invoke(() =>
            {
                string restore = L.T("Восстановить в облаке"), delete = L.T("Удалить локальную запись");
                int a = Dialog.Choose(owner, L.T("Запись удалена в облаке"), title + "\n\n" + details, Severity.Warning,
                    deletionNewer ? delete : restore, deletionNewer ? restore : delete, L.T("Отложить"));
                if (a < 0 || a == 2) return 2;
                return deletionNewer ? 1 - a : a;
            });
        }
    }

    // A conflict between the local record and the cloud version (ConflictDialog on WinForms): whole versions or field by
    // field. A click on a value keeps it; of two different dates the newer one is green.
    public class ConflictWindow : Window
    {
        readonly JsonObject local, cloud;
        readonly List<string> fields, different;
        readonly HashSet<string> fromCloud = new HashSet<string>();
        readonly Grid table = new Grid();
        public string Action = "defer";
        public JsonObject Result;

        readonly bool cloudNewer;

        // cloudNewer: the cloud version is newer (Recency), so its values are chosen at the start.
        public ConflictWindow(string title, JsonObject local, JsonObject cloud, bool cloudNewer)
        {
            this.local = local;
            this.cloud = cloud;
            this.cloudNewer = cloudNewer;
            fields = RecordDiff.AllFields(local, cloud);
            different = RecordDiff.Differences(local, cloud);
            if (cloudNewer) foreach (string name in different) fromCloud.Add(name);
            Title = L.T("Конфликт с облаком");
            Width = 820; Height = 560;
            MinWidth = 600; MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");

            var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
            Content = root;
            var head = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);
            var hint = new TextBlock { Text = Brand.Keep(L.T("Запись изменили и здесь, и в облаке. Различия выделены. Щёлкните значение, которое нужно оставить, ") +
                L.T("или выберите версию целиком. Для разных дат более новая — зелёная.") + " " +
                (cloudNewer ? L.T("Сначала выбрана версия из облака: она новее.") : L.T("Сначала выбрана локальная версия: она новее."))),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            DockPanel.SetDock(hint, Dock.Top);
            root.Children.Add(hint);

            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            AddAction(actions, Glyph.Check, L.T("Сохранить выбранное"), true, delegate { Finish("merged", RecordDiff.Merge(local, cloud, fromCloud)); });
            AddAction(actions, Glyph.Cloud, L.T("Всё из облака"), false, delegate { Finish("cloud", cloud); });
            AddAction(actions, Glyph.Upload, L.T("Всё локальное"), false, delegate { Finish("local", local); });
            AddAction(actions, null, L.T("Отложить"), false, delegate { Finish("defer", null); });
            DockPanel.SetDock(actions, Dock.Bottom);
            root.Children.Add(actions);

            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24, GridUnitType.Star) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
            var frame = new Border { Child = new ScrollViewer { Content = table, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false } };
            frame.SetResourceReference(StyleProperty, "TableFrame");
            root.Children.Add(frame);
            Repaint();
            WpfTheme.DarkTitle(this);
        }

        void AddAction(Panel panel, string glyph, string text, bool main, Action click)
        {
            var b = new Button { Margin = new Thickness(0, 0, 8, 0) };
            if (glyph != null) Glyph.Label(b, glyph, text); else b.Content = text;
            if (main) { b.SetResourceReference(StyleProperty, "AccentButton"); b.IsDefault = true; }
            b.Click += delegate { click(); };
            panel.Children.Add(b);
        }

        void Finish(string action, JsonObject result)
        {
            Action = action;
            Result = result;
            Close();
        }

        static DateTimeOffset? DateOf(JsonObject r, string name)
        {
            DateTimeOffset d;
            if (DateTimeOffset.TryParse(r.Text(name) ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d)) return d;
            return null;
        }

        // The table again: the header, then every field; the kept value of a difference on the soft green.
        void Repaint()
        {
            table.Children.Clear();
            table.RowDefinitions.Clear();
            string newer = L.T(" · новее");
            AddRow(new[] { L.T("Поле"), L.T("Локально (эта копия)") + (cloudNewer ? "" : newer), L.T("В облаке") + (cloudNewer ? newer : "") }, null, true);
            foreach (string name in fields) AddRow(new[] { RecordDiff.Label(name), RecordDiff.ValueText(local, name), RecordDiff.ValueText(cloud, name) }, name, false);
        }

        void AddRow(string[] cells, string name, bool header)
        {
            int row = table.RowDefinitions.Count;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool diff = name != null && different.Contains(name);
            bool cloudWins = name != null && fromCloud.Contains(name);
            DateTimeOffset? a = null, b = null;
            if (diff && name.EndsWith("At")) { a = DateOf(local, name); b = DateOf(cloud, name); }
            for (int c = 0; c < 3; c++)
            {
                var cell = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(c == 0 ? 14 : 10, 7, 10, 7), Background = Brushes.Transparent };
                cell.SetResourceReference(Border.BorderBrushProperty, "Divider");
                var text = new TextBlock { Text = cells[c], TextWrapping = TextWrapping.Wrap };
                cell.Child = text;
                if (header)
                {
                    cell.SetResourceReference(Border.BackgroundProperty, "HeaderBack");
                    text.FontWeight = FontWeights.SemiBold;
                    text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                }
                else if (!diff) text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                else
                {
                    bool keep = c == (cloudWins ? 2 : 1), drop = c == (cloudWins ? 1 : 2);
                    if (c == 0) cell.SetResourceReference(Border.BackgroundProperty, "WarnBack");
                    if (keep) { cell.SetResourceReference(Border.BackgroundProperty, "GoodBack"); cell.ToolTip = L.T("Будет сохранено"); }
                    if (drop) { text.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); cell.ToolTip = L.T("Щёлкните, чтобы оставить это значение"); }
                    if (a.HasValue && b.HasValue && a.Value != b.Value && c > 0)
                    {
                        bool newer = c == 1 ? a.Value > b.Value : b.Value > a.Value;
                        text.SetResourceReference(TextBlock.ForegroundProperty, newer ? "Good" : "Bad");
                    }
                    if (c > 0)
                    {
                        cell.Cursor = Cursors.Hand;
                        int column = c;
                        string field = name;
                        cell.MouseLeftButtonUp += delegate
                        {
                            if (column == 2) fromCloud.Add(field); else fromCloud.Remove(field);
                            Repaint();
                        };
                    }
                }
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, c);
                table.Children.Add(cell);
            }
        }
    }
}
