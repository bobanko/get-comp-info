using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // The card of a computer (RecordDialog of DatabasePage.cs on WinForms): every field (selectable, to copy),
    // the cloud status, the editable comment; «Предыдущая» / «Следующая» (also ←, →, PageUp, PageDown).
    public class RecordWindow : Window
    {
        readonly List<RecordFile> records;
        readonly CloudState cloud;
        readonly Grid fields = new Grid();
        readonly TextBox comment = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 72, MaxLength = 500,
            VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(10, 6, 10, 6), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly Button saveComment = new Button(), previous = new Button(), next = new Button();
        readonly StatusLine status = new StatusLine { Margin = new Thickness(0, 8, 0, 0) };
        public int Index { get; private set; }
        public bool Changed { get; private set; }

        public RecordWindow(List<RecordFile> records, int index, CloudState cloud)
        {
            this.records = records;
            this.cloud = cloud;
            Width = 700; Height = 640;
            MinWidth = 540; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");

            var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
            Content = root;

            var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            Glyph.Label(previous, Glyph.Back, L.T("Предыдущая"));
            Glyph.Label(next, Glyph.Forward, L.T("Следующая"));
            var close = new Button { Content = L.T("Закрыть"), MinWidth = 100, IsCancel = true };
            previous.Click += delegate { Step(-1); };
            next.Click += delegate { Step(1); };
            close.Click += delegate { Close(); };   // IsCancel alone answers only Esc
            foreach (Button b in new[] { previous, next, close }) { b.Margin = new Thickness(0, 0, 8, 0); nav.Children.Add(b); }
            DockPanel.SetDock(nav, Dock.Bottom);
            root.Children.Add(nav);
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);

            Glyph.Label(saveComment, Glyph.Save, L.T("Сохранить комментарий"));
            saveComment.SetResourceReference(StyleProperty, "AccentButton");
            saveComment.HorizontalAlignment = HorizontalAlignment.Left;
            saveComment.Margin = new Thickness(0, 8, 0, 0);
            saveComment.Click += delegate { SaveComment(); };
            DockPanel.SetDock(saveComment, Dock.Bottom);
            root.Children.Add(saveComment);
            comment.TextChanged += delegate { saveComment.IsEnabled = comment.Text != (Current.Data.Text("comment") ?? ""); };
            System.Windows.Automation.AutomationProperties.SetName(comment, L.T("Комментарий"));
            DockPanel.SetDock(comment, Dock.Bottom);
            root.Children.Add(comment);
            var commentLabel = new TextBlock { Text = L.T("Комментарий"), Margin = new Thickness(0, 12, 0, 4) };
            commentLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            commentLabel.MouseLeftButtonDown += delegate { comment.Focus(); };
            DockPanel.SetDock(commentLabel, Dock.Bottom);
            root.Children.Add(commentLabel);

            fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fields.ColumnDefinitions.Add(new ColumnDefinition());
            var frame = new Border { Child = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14, 8, 8, 8), Focusable = false } };
            frame.SetResourceReference(StyleProperty, "TableFrame");
            root.Children.Add(frame);

            WpfTheme.DarkTitle(this);
            PreviewKeyDown += (s, e) =>
            {
                if (comment.IsKeyboardFocused) return;
                if (e.Key == Key.Left || e.Key == Key.PageUp) { Step(-1); e.Handled = true; }
                else if (e.Key == Key.Right || e.Key == Key.PageDown) { Step(1); e.Handled = true; }
            };
            Closing += (s, e) =>
            {
                if (!saveComment.IsEnabled) return;
                if (!Dialog.Ask(this, L.T("Комментарий изменён, но не сохранён. Закрыть без сохранения?"), L.T("Закрыть"), L.T("Остаться"),
                    Severity.Warning, L.T("Карточка компьютера"))) e.Cancel = true;
            };
            Display(index);
        }

        RecordFile Current { get { return records[Index]; } }

        void Step(int step)
        {
            int target = Index + step;
            if (target < 0 || target >= records.Count) return;
            if (saveComment.IsEnabled && !Dialog.Ask(this, L.T("Комментарий изменён, но не сохранён. Перейти без сохранения?"), L.T("Перейти"), L.T("Остаться"),
                Severity.Warning, L.T("Карточка компьютера"))) return;
            Display(target);
        }

        void Display(int index)
        {
            Index = index;
            RecordFile r = Current;
            JsonObject d = r.Data;
            Title = L.T("Карточка компьютера — ") + (d.Text("hostname") ?? r.FileName) + " (" + (index + 1) + L.T(" из ") + records.Count + ")";
            fields.Children.Clear();
            fields.RowDefinitions.Clear();
            var known = new List<string>();
            foreach (string[] f in DeviceList.CardFields)
            {
                known.Add(f[0]);
                AddField(f[1], DeviceList.CardValue(d, f[0]));
            }
            known.Add("comment");
            // Fields this version does not know are shown too, so nothing is hidden.
            foreach (string name in d.Names)
                if (!known.Contains(name)) AddField(name, d.Text(name) ?? "");
            AddField(L.T("Облако"), L.T(r.CloudStatus) + L.T("   (последняя синхронизация копии: ") +
                (string.IsNullOrEmpty(cloud.LastSyncAt) ? L.T("не было") : Fmt.Date(cloud.LastSyncAt)) + ")");
            AddField(L.T("Файл"), r.FileName);
            comment.Text = d.Text("comment") ?? "";
            comment.CaretIndex = comment.Text.Length;
            saveComment.IsEnabled = false;
            previous.IsEnabled = index > 0;
            next.IsEnabled = index < records.Count - 1;
            status.Clear();
        }

        // A label and a read-only value that can be selected and copied.
        void AddField(string label, string value)
        {
            int row = fields.RowDefinitions.Count;
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Margin = new Thickness(0, 4, 18, 4), VerticalAlignment = VerticalAlignment.Top };
            l.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            Grid.SetRow(l, row);
            fields.Children.Add(l);
            var v = new TextBox { Text = value.Length == 0 ? "—" : value, IsReadOnly = true, IsTabStop = false, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Top };
            v.SetResourceReference(StyleProperty, "BareTextBox");
            if (value.Length == 0) v.SetResourceReference(ForegroundProperty, "Muted");
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            fields.Children.Add(v);
        }

        void SaveComment()
        {
            try
            {
                records[Index] = DeviceList.SaveComment(Current, comment.Text, cloud);
                Changed = true;
                Display(Index);
                status.Show(Severity.Success, Glyph.Check, L.T("Комментарий сохранён. При следующей синхронизации он попадёт в облако."));
            }
            catch (Exception e) { status.Show(Severity.Error, Glyph.Warning, L.T("Не удалось сохранить комментарий: ") + e.Message); }
        }
    }
}
