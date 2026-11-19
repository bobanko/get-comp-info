using System;
using System.Collections.Generic;
using System.Text;

namespace CompInfo
{
    // Search that ignores the alphabet, the same as the web interface (cloud/public/js/search-logic.js):
    // "боб" finds "bob" and back, "мишко" finds "myshko" and "mishko", "мужичек" finds "mujichekk".
    //
    // Text and query are both turned into a loose Latin key and compared there:
    // 1. Cyrillic is transliterated by the official Ukrainian table (Cabinet of Ministers resolution No. 55 of
    //    27.01.2010, as on dmsu.gov.ua), with the Russian letters ы, э, ё, ъ added;
    // 2. spellings people mix up are folded into one: y/j/i → i, g/h → h, kh → h, ts → c, w → v,
    //    and doubled letters → one (digits stay as they are, so IP addresses do not change);
    // 3. a query also tries the other usual spellings of ж, х, щ (j, x, sh) and back.
    // Every key character remembers the part of the original text it came from, to highlight the match there.
    public static class LooseSearch
    {
        static readonly Dictionary<char, string> Cyrillic = new Dictionary<char, string>
        {
            { 'а', "a" }, { 'б', "b" }, { 'в', "v" }, { 'г', "h" }, { 'ґ', "g" }, { 'д', "d" }, { 'е', "e" }, { 'є', "ie" },
            { 'ж', "zh" }, { 'з', "z" }, { 'и', "y" }, { 'і', "i" }, { 'ї', "i" }, { 'й', "i" }, { 'к', "k" }, { 'л', "l" },
            { 'м', "m" }, { 'н', "n" }, { 'о', "o" }, { 'п', "p" }, { 'р', "r" }, { 'с', "s" }, { 'т', "t" }, { 'у', "u" },
            { 'ф', "f" }, { 'х', "kh" }, { 'ц', "ts" }, { 'ч', "ch" }, { 'ш', "sh" }, { 'щ', "shch" }, { 'ь', "" },
            { 'ю', "iu" }, { 'я', "ia" },
            // Russian letters that Ukrainian has not.
            { 'ы', "y" }, { 'э', "e" }, { 'ё', "e" }, { 'ъ', "" },
            // Apostrophes are dropped, as in the official table.
            { '\'', "" }, { '’', "" }, { 'ʼ', "" }, { '`', "" },
        };

        // Letters people write in more than one way: a query tries each spelling.
        static readonly Dictionary<char, string[]> Alternatives = new Dictionary<char, string[]>
        {
            { 'ж', new[] { "zh", "j" } }, { 'х', new[] { "kh", "x" } }, { 'щ', new[] { "shch", "sh" } },
            { 'j', new[] { "j", "zh" } }, { 'x', new[] { "x", "kh" } },
        };
        const int MaxVariants = 32;

        // Applied in this order: longer spellings first.
        static readonly string[][] Folds =
        {
            new[] { "shch", "sch" }, new[] { "kh", "h" }, new[] { "ts", "c" }, new[] { "g", "h" },
            new[] { "j", "i" }, new[] { "y", "i" }, new[] { "w", "v" },
        };

        struct Item
        {
            public char Ch;
            public int Start, End;
            public Item(char ch, int start, int end) { Ch = ch; Start = start; End = end; }
        }

        public class Key
        {
            public string Text;
            // Spans[i] = start and end (exclusive) of Text[i] in the original text.
            public int[] Starts, Ends;
        }

        public static Key LooseKey(string text)
        {
            string source = (text ?? "").ToLowerInvariant();
            List<Item> items = new List<Item>();
            for (int i = 0; i < source.Length; i++)
            {
                string latin;
                if (!Cyrillic.TryGetValue(source[i], out latin)) latin = source[i].ToString();
                foreach (char c in latin) items.Add(new Item(c, i, i + 1));
            }
            foreach (string[] f in Folds) items = Fold(items, f[0], f[1]);
            // Doubled letters: "kompp" = "komp", "дмитрий" (dmytrii) = "dmitri".
            List<Item> single = new List<Item>();
            foreach (Item item in items)
            {
                if (single.Count > 0 && single[single.Count - 1].Ch == item.Ch && item.Ch >= 'a' && item.Ch <= 'z')
                {
                    Item last = single[single.Count - 1];
                    last.End = item.End;
                    single[single.Count - 1] = last;
                }
                else single.Add(item);
            }
            Key key = new Key();
            StringBuilder sb = new StringBuilder(single.Count);
            key.Starts = new int[single.Count];
            key.Ends = new int[single.Count];
            for (int i = 0; i < single.Count; i++)
            {
                sb.Append(single[i].Ch);
                key.Starts[i] = single[i].Start;
                key.Ends[i] = single[i].End;
            }
            key.Text = sb.ToString();
            return key;
        }

        static List<Item> Fold(List<Item> items, string from, string to)
        {
            List<Item> result = new List<Item>(items.Count);
            for (int i = 0; i < items.Count; )
            {
                bool same = i + from.Length <= items.Count;
                for (int k = 0; same && k < from.Length; k++) same = items[i + k].Ch == from[k];
                if (!same) { result.Add(items[i]); i++; continue; }
                int start = items[i].Start, end = items[i + from.Length - 1].End;
                // The replacement covers the whole original part: its first character carries the span.
                for (int k = 0; k < to.Length; k++) result.Add(new Item(to[k], k == 0 ? start : end, end));
                i += from.Length;
            }
            return result;
        }

        static readonly Dictionary<string, List<string>> keysCache = new Dictionary<string, List<string>>();

        // Loose keys of one query word: one per spelling variant, without empty and repeated ones.
        public static List<string> WordKeys(string word)
        {
            word = (word ?? "").ToLowerInvariant();
            List<string> cached;
            if (keysCache.TryGetValue(word, out cached)) return cached;
            List<string> variants = new List<string> { "" };
            foreach (char ch in word)
            {
                string latin;
                if (!Cyrillic.TryGetValue(ch, out latin)) latin = ch.ToString();
                string[] options;
                if (!Alternatives.TryGetValue(ch, out options)) options = new[] { latin };
                List<string> next = new List<string>();
                foreach (string variant in variants)
                    foreach (string option in options)
                        if (next.Count < MaxVariants || option == options[0]) next.Add(variant + option);
                variants = next.Count > MaxVariants ? next.GetRange(0, MaxVariants) : next;
            }
            List<string> keys = new List<string>();
            foreach (string variant in variants)
            {
                string k = LooseKey(variant).Text;
                if (k.Length > 0 && !keys.Contains(k)) keys.Add(k);
            }
            if (keysCache.Count > 500) keysCache.Clear();
            keysCache[word] = keys;
            return keys;
        }

        // Words of a search query, in lower case.
        public static List<string> Words(string query)
        {
            List<string> words = new List<string>();
            foreach (string w in (query ?? "").ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                words.Add(w);
            return words;
        }

        // The word occurs in the text as typed or in another alphabet / spelling; key: LooseKey(text).Text, if known.
        public static bool Matches(string text, string word, string key)
        {
            if ((text ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (key == null) key = LooseKey(text).Text;
            foreach (string loose in WordKeys(word))
                if (key.Contains(loose)) return true;
            return false;
        }

        // Parts of the text to highlight for these words: sorted, merged [start, end) pairs.
        public static List<int[]> MatchRanges(string text, IList<string> words)
        {
            List<int[]> ranges = new List<int[]>();
            if (string.IsNullOrEmpty(text) || words == null || words.Count == 0) return ranges;
            string lower = text.ToLowerInvariant();
            Key key = LooseKey(text);
            foreach (string word in words)
            {
                if (word.Length == 0) continue;
                for (int at = lower.IndexOf(word, StringComparison.Ordinal); at >= 0; at = lower.IndexOf(word, at + 1, StringComparison.Ordinal))
                    ranges.Add(new[] { at, at + word.Length });
                foreach (string loose in WordKeys(word))
                    for (int at = key.Text.IndexOf(loose, StringComparison.Ordinal); at >= 0; at = key.Text.IndexOf(loose, at + 1, StringComparison.Ordinal))
                        ranges.Add(new[] { key.Starts[at], key.Ends[at + loose.Length - 1] });
            }
            ranges.Sort(delegate (int[] a, int[] b) { int c = a[0].CompareTo(b[0]); return c != 0 ? c : a[1].CompareTo(b[1]); });
            List<int[]> merged = new List<int[]>();
            foreach (int[] r in ranges)
            {
                if (merged.Count > 0 && r[0] <= merged[merged.Count - 1][1])
                    merged[merged.Count - 1][1] = Math.Max(merged[merged.Count - 1][1], r[1]);
                else merged.Add(new[] { r[0], r[1] });
            }
            return merged;
        }
    }
}
