using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CompInfo.Wpf
{
    // Icons of the Segoe Fluent Icons font (Segoe MDL2 Assets on older Windows), the same as Glyph in Ui.cs.
    // All icons come from the font: no drawn paths.
    public static class Glyph
    {
        public const string Usb = "", Refresh = "", Copy = "", Check = "", Cross = "",
            Warning = "", Info = "", Open = "", Person = "", Globe = "",
            Pin = "", Pinned = "", Sun = "", Theme = "", Moon = "";

        public const string Shield = "", Play = "", Power = "", Circle = "", More = "", Folder = "",
            Delete = "", Lock = "", DockBottom = "", DockRight = "", Update = "";

        // A glyph of the icon font in a colour of the palette (a key of WpfTheme).
        public static TextBlock Block(string glyph, double size, string brushKey)
        {
            var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            if (brushKey != null) t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return t;
        }

        // A button with an icon and the text after it; the text is also its name for screen readers (and UI tests).
        public static void Label(Button b, string glyph, string text)
        {
            b.Content = WithText(glyph, text);
            System.Windows.Automation.AutomationProperties.SetName(b, text);
        }

        // The content of a button: an icon and the text after it.
        public static StackPanel WithText(string glyph, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Block(glyph, 14, null);
            icon.Margin = new Thickness(0, 1, 8, 0);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }
    }

    public static class Brand
    {
        // The name of a program with its coloured part in square brackets: «you[SBee]», «[SUS]pector».
        public static TextBlock Title(string name, string highlightKey, double size)
        {
            var t = new TextBlock { FontSize = size, FontWeight = FontWeights.SemiBold };
            foreach (Match m in Regex.Matches(name, @"\[([^\]]*)\]|[^\[]+"))
            {
                var run = new Run(m.Groups[1].Success ? m.Groups[1].Value : m.Value);
                if (m.Groups[1].Success) run.SetResourceReference(TextElement.ForegroundProperty, highlightKey);
                t.Inlines.Add(run);
            }
            return t;
        }

        public static string Plain(string name) { return (name ?? "").Replace("[", "").Replace("]", ""); }

        static readonly Regex ShortWord = new Regex(@"(?<![\w’'-])(\w{1,3}) (?=\S)");
        static readonly Regex InnerHyphen = new Regex(@"(?<=\w)-(?=\w)");
        static readonly Regex ShortLastWord = new Regex(@" (\S{1,10})$");

        // Typography of a paragraph (as AboutWindow.Keep of SupraWeed): a short word (и, в, з, на, для, USB…) and the dash
        // stay on the line of their neighbour (no-break space), a hyphenated word (Ctrl+C, USB-диск) is not broken at the
        // hyphen, and the last line is never one short word.
        public static string Keep(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            const string nbsp = " ";
            string s = text.Replace(" —", nbsp + "—");
            s = ShortWord.Replace(s, "$1" + nbsp);
            s = ShortWord.Replace(s, "$1" + nbsp);   // a second pass for short words in a row («и в»)
            s = InnerHyphen.Replace(s, "‑");
            return ShortLastWord.Replace(s, nbsp + "$1");
        }
    }
}
