using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    public enum Severity { Info, Success, Warning, Error }

    // The message bar of WinUI 3 (InfoBar): a card with a 4 px rounding and a thin frame, a round icon in the colour
    // of the severity, a bold title and the message under it. The text keeps the normal text colour —
    // only the background and the icon tell the severity. Colours: the Bar* keys of WpfTheme.
    public class InfoBar : Border
    {
        readonly TextBlock circle, mark, title, message;
        readonly ContentPresenter actions;

        // Buttons on the right of the bar (e.g. a StackPanel of them); null — none.
        public object Actions
        {
            get => actions.Content;
            set => actions.Content = value;
        }

        public InfoBar()
        {
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            SetResourceReference(BorderBrushProperty, "BarBorder");
            // WinUI 3: 16 px from the left, the text 14 px from the top and the bottom (inside the 1 px frame)
            Padding = new Thickness(15, 13, 15, 13);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            // the icon: a filled circle (F136) and the mark over it (i / check / ! / x), as in WinUI 3;
            // level with the title line, 14 px before the text
            var icon = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 14, 0) };
            circle = Glyph("");
            mark = Glyph("");
            mark.SetResourceReference(TextBlock.ForegroundProperty, "BarIconMark");
            icon.Children.Add(circle);
            icon.Children.Add(mark);
            grid.Children.Add(icon);

            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            text.Children.Add(title);
            text.Children.Add(message);
            grid.Children.Add(text);

            // buttons on the right (WinUI 3 ActionButton), optional
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            Child = grid;
            Show(Severity.Info, "", "");
        }

        static TextBlock Glyph(string text)
        {
            var t = new TextBlock { Text = text, FontSize = 16 };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            return t;
        }

        public Severity Severity { get; private set; }

        // An empty message leaves only the title.
        public void Show(Severity severity, string titleText, string messageText)
        {
            Severity = severity;
            string key = severity == Severity.Success ? "Good" : severity == Severity.Warning ? "Warn" :
                         severity == Severity.Error ? "Bad" : "Info";
            SetResourceReference(BackgroundProperty, "Bar" + key + "Back");
            circle.SetResourceReference(TextBlock.ForegroundProperty, "Bar" + key + "Icon");
            mark.Text = severity == Severity.Success ? "" : severity == Severity.Warning ? "" :
                        severity == Severity.Error ? "" : "";
            title.Text = titleText ?? "";
            message.Text = messageText ?? "";
            message.Visibility = string.IsNullOrEmpty(messageText) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
