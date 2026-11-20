using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CompInfo.Wpf
{
    // A text with the words of a search marked in it (LooseSearch.MatchRanges: as typed or in the other alphabet).
    public class MarkedText
    {
        public readonly string Text;
        public readonly List<string> Words;

        public MarkedText(string text, List<string> words) { Text = text ?? ""; Words = words; }

        public override string ToString() { return Text; }
    }

    // Marked.Text on a TextBlock: the parts that match on the yellow «Mark» background, as <mark> on the website.
    public static class Marked
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(MarkedText), typeof(Marked),
            new PropertyMetadata(null, Changed));

        public static void SetText(TextBlock t, MarkedText value) { t.SetValue(TextProperty, value); }
        public static MarkedText GetText(TextBlock t) { return (MarkedText)t.GetValue(TextProperty); }

        static void Changed(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var block = o as TextBlock;
            var value = e.NewValue as MarkedText;
            if (block == null) return;
            block.Inlines.Clear();
            if (value == null) return;
            List<int[]> ranges = value.Words == null || value.Words.Count == 0 ? new List<int[]>() : LooseSearch.MatchRanges(value.Text, value.Words);
            int at = 0;
            foreach (int[] r in ranges)
            {
                if (r[0] > at) block.Inlines.Add(new Run(value.Text.Substring(at, r[0] - at)));
                var mark = new Run(value.Text.Substring(r[0], r[1] - r[0]));
                mark.SetResourceReference(TextElement.BackgroundProperty, "Mark");
                block.Inlines.Add(mark);
                at = r[1];
            }
            if (at < value.Text.Length) block.Inlines.Add(new Run(value.Text.Substring(at)));
        }
    }
}
