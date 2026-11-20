using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CompInfo.Wpf
{
    // Messages and questions in the look of the programs (WinUI 3 ContentDialog) instead of the MessageBox of Windows:
    // a round icon of the severity (the colours of InfoBar), the text, and the buttons on a footer, the main one blue.
    // Enter — the main button, Esc — the other one (or closing). Taken over from ..\SupraWeed\src\Wpf\Dialog.cs.
    public static class Dialog
    {
        // The window title when there is no owner: the name of the program.
        public static string AppTitle = "";

        // A message with one button.
        public static void Show(Window owner, string message, Severity severity = Severity.Error, string heading = null) =>
            new DialogWindow(owner, heading, message, severity, L.T("Закрыть"), null).ShowDialog();

        // A question: true for the main button ("yes"), false for the other one («Отмена») or closing.
        public static bool Ask(Window owner, string message, string yes, string no = null,
            Severity severity = Severity.Warning, string heading = null) =>
            new DialogWindow(owner, heading, message, severity, yes, no ?? L.T("Отмена")).ShowDialog() == true;
    }

    class DialogWindow : Window
    {
        public DialogWindow(Window owner, string heading, string message, Severity severity, string yes, string no)
        {
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Title = owner != null && owner.Title.Length > 0 ? owner.Title : Dialog.AppTitle;
            Width = 480;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = owner == null;
            UseLayoutRounding = true;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");

            var root = new DockPanel();
            Content = root;

            // footer: the buttons on the right, the main one blue
            var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 16, 24, 16) };
            footer.SetResourceReference(Border.BorderBrushProperty, "Divider");
            footer.SetResourceReference(Border.BackgroundProperty, "WindowBack");
            DockPanel.SetDock(footer, Dock.Bottom);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var main = new Button { Content = yes, IsDefault = true, MinWidth = 110 };
            main.SetResourceReference(StyleProperty, "AccentButton");
            main.Click += delegate { DialogResult = true; };
            if (no != null)
            {
                var other = new Button { Content = no, IsCancel = true, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
                other.Click += delegate { DialogResult = false; };   // IsCancel alone answers only Esc
                buttons.Children.Add(other);
            }
            else main.IsCancel = true;   // one button: Esc closes too
            buttons.Children.Add(main);
            footer.Child = buttons;
            root.Children.Add(footer);

            // the icon and the text
            var body = new Grid { Margin = new Thickness(24, 22, 24, 22) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            string key = severity == Severity.Success ? "Good" : severity == Severity.Warning ? "Warn" :
                         severity == Severity.Error ? "Bad" : "Info";
            var icon = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 16, 0) };
            var circle = Glyph("", 32);
            circle.SetResourceReference(TextBlock.ForegroundProperty, "Bar" + key + "Icon");
            var mark = Glyph(severity == Severity.Success ? "" : severity == Severity.Warning ? "" :
                             severity == Severity.Error ? "" : "", 32);
            mark.SetResourceReference(TextBlock.ForegroundProperty, "BarIconMark");
            icon.Children.Add(circle);
            icon.Children.Add(mark);
            body.Children.Add(icon);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(heading))
                text.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            // selectable, so a path or an error can be copied
            var body2 = new TextBox
            {
                Text = message, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, IsTabStop = false,
                MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            body2.SetResourceReference(StyleProperty, "BareTextBox");
            text.Children.Add(body2);
            Grid.SetColumn(text, 1);
            body.Children.Add(text);
            var bodyBack = new Border { Child = body };
            bodyBack.SetResourceReference(Border.BackgroundProperty, "Surface");
            root.Children.Add(bodyBack);

            WpfTheme.DarkTitle(this);
            Loaded += delegate { main.Focus(); };
        }

        static TextBlock Glyph(string glyph, double size)
        {
            var t = new TextBlock { Text = glyph, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            return t;
        }
    }
}
