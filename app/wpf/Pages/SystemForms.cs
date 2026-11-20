using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace CompInfo.Wpf
{
    // A two-column form (FieldTable of SystemPages.cs on WinForms): grey labels on the left, fields on the right,
    // every row of the same height; a row can be hidden with its label. A click on a label puts the cursor in its field.
    public class FormGrid : Grid
    {
        readonly Dictionary<UIElement, TextBlock> labels = new Dictionary<UIElement, TextBlock>();

        public FormGrid()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition());
        }

        public void Add(string label, FrameworkElement field)
        {
            int row = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 40 });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 18, 4) };
            l.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            l.MouseLeftButtonDown += delegate { field.Focus(); };
            field.VerticalAlignment = VerticalAlignment.Center;
            if (field.HorizontalAlignment == HorizontalAlignment.Stretch && !(field is TextBlock)) field.HorizontalAlignment = HorizontalAlignment.Left;
            field.Margin = new Thickness(0, 4, 0, 4);
            SetRow(l, row);
            SetRow(field, row);
            SetColumn(field, 1);
            Children.Add(l);
            Children.Add(field);
            labels[field] = l;
        }

        public void Show(UIElement field, bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            field.Visibility = v;
            TextBlock l;
            if (labels.TryGetValue(field, out l)) l.Visibility = v;
            // a hidden row takes no room
            int row = GetRow(field);
            if (row < RowDefinitions.Count) RowDefinitions[row].MinHeight = visible ? 40 : 0;
        }
    }

    public static class AdminGlyph
    {
        // A button of a system change (AdminPrimary of Ui.cs): the shield — Windows will ask for administrator rights;
        // the shield with a check — this copy already has them.
        public static void Label(Button b, string text)
        {
            bool admin = PowerShell.IsAdministrator();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var shield = new Grid { Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            shield.Children.Add(Glyph.Block(Glyph.Shield, 15, null));
            if (admin)
            {
                var check = Glyph.Block(Glyph.Check, 8, null);
                check.HorizontalAlignment = HorizontalAlignment.Center;
                check.FontWeight = FontWeights.Bold;
                shield.Children.Add(check);
            }
            row.Children.Add(shield);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            b.Content = row;
            System.Windows.Automation.AutomationProperties.SetName(b, text);
        }
    }

    // An item of a drop-down list with its value.
    public class Choice
    {
        public string Text;
        public object Value;
        public override string ToString() { return Text; }
    }
}
