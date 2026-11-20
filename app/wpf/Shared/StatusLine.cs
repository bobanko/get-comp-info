using System.Windows;
using System.Windows.Controls;

namespace CompInfo.Wpf
{
    // The line under a page (StatusLine of Ui.cs): a hint in the muted colour, or a result on a soft panel
    // of its colour (copied — green, an error — red). Empty text hides it.
    public class StatusLine : Border
    {
        readonly TextBlock icon, text;
        int links;

        public StatusLine()
        {
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            icon = Glyph.Block("", 14, null);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 10, 0);
            text = new TextBlock { TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1);
            row.Children.Add(icon);
            row.Children.Add(text);
            Child = row;
            Visibility = Visibility.Collapsed;
        }

        // A hint: muted, no panel.
        public void Note(string glyph, string message)
        {
            Fill(null, glyph, message, "Muted", "Muted");
        }

        // A result: Success, Warning or Error on its soft panel (Info — on the soft blue).
        public void Show(Severity severity, string glyph, string message)
        {
            string key = severity == Severity.Success ? "Good" : severity == Severity.Warning ? "Warn" :
                         severity == Severity.Error ? "Bad" : "Accent";
            Fill(key, glyph, message, key, "Text");
        }

        public void Clear() { Visibility = Visibility.Collapsed; }

        // A link under the message, after the ones before it (the web cards of records the program cannot fix itself).
        public void AddLink(string label, System.Action click)
        {
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(label));
            link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Accent");
            link.Click += delegate { click(); };
            text.Inlines.Add(links++ == 0 ? (System.Windows.Documents.Inline)new System.Windows.Documents.LineBreak() : new System.Windows.Documents.Run("  ·  "));
            text.Inlines.Add(link);
        }

        public string Text { get { return text.Text; } }

        void Fill(string panel, string glyph, string message, string iconKey, string textKey)
        {
            if (panel == null)
            {
                Background = null;
                BorderBrush = null;
                Padding = new Thickness(0);
            }
            else
            {
                SetResourceReference(BackgroundProperty, panel == "Accent" ? "AccentSoft" : panel + "Back");
                SetResourceReference(BorderBrushProperty, panel == "Accent" ? "Accent" : panel + "Line");
                Padding = new Thickness(10, 6, 10, 7);
            }
            icon.Text = glyph ?? "";
            icon.Visibility = string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
            icon.SetResourceReference(TextBlock.ForegroundProperty, iconKey);
            text.SetResourceReference(TextBlock.ForegroundProperty, textKey);
            text.Text = message ?? "";
            links = 0;
            Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
