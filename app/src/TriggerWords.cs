using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CompInfo
{
    // A trigger word of the ДСК document check and its importance: TriggerWords.High, Medium or Low.
    public class TriggerWord
    {
        public readonly string Text;
        public readonly int Level;

        public TriggerWord(string text, int level) { Text = text; Level = level; }
    }

    // The trigger words of the ДСК document check. The text format is the same in app\trigger-words.txt (built into
    // SUSpector) and in the list the admin keeps in the cloud (web page «Слова SUSpector», cloud\src\trigger-words.ts):
    // one word or phrase per line; [высокий], [средний], [низкий] (also Ukrainian and English names) start a level;
    // # starts a comment. Words before the first level are medium; repeats (any case) count once, the first one wins.
    // A space in a phrase matches any spaces or none: «т. в. о.» finds «т.в.о.» too.
    // A line with * or ? and no spaces is a mask of file names (*.pfx — VPN certificates): such files are found by the name
    // alone, whatever is in them, and a mask is not searched for in the text.
    public class TriggerWords
    {
        public const int High = 3, Medium = 2, Low = 1;

        public readonly TriggerWord[] Words;
        readonly Regex pattern, masks;
        // group number in pattern (masks) → index in Words
        readonly int[] groups, maskGroups;

        public TriggerWords(IList<TriggerWord> words)
        {
            Words = new List<TriggerWord>(words).ToArray();
            // The longest word first: where words overlap (ДСК in ДСК-ЕП, штат in штатка) the regex alternation
            // takes the first one that matches, so only the longest is counted.
            List<int> order = new List<int>();
            List<string> maskParts = new List<string>();
            List<int> maskIndex = new List<int> { -1 };
            for (int i = 0; i < Words.Length; i++)
                if (IsMask(Words[i].Text)) { maskParts.Add("(" + MaskPattern(Words[i].Text) + ")"); maskIndex.Add(i); }
                else order.Add(i);
            maskGroups = maskIndex.ToArray();
            masks = maskParts.Count == 0 ? null
                : new Regex("^(?:" + string.Join("|", maskParts.ToArray()) + ")$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            order.Sort(delegate (int a, int b)
            {
                int byLength = Compact(Words[b].Text).Length.CompareTo(Compact(Words[a].Text).Length);
                return byLength != 0 ? byLength : a.CompareTo(b);
            });
            List<string> parts = new List<string>();
            groups = new int[order.Count + 1];
            for (int n = 0; n < order.Count; n++)
            {
                parts.Add("(" + Pattern(Words[order[n]].Text) + ")");
                groups[n + 1] = order[n];
            }
            pattern = parts.Count == 0 ? null
                : new Regex(string.Join("|", parts.ToArray()), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }

        // *.pfx, ключ?.txt: a word with * or ? and no spaces.
        public static bool IsMask(string word)
        {
            return word.IndexOfAny(new[] { '*', '?' }) >= 0 && !Regex.IsMatch(word, @"\s");
        }

        static string MaskPattern(string mask)
        {
            return Regex.Escape(mask).Replace(@"\*", ".*").Replace(@"\?", ".");
        }

        public bool HasMasks { get { return masks != null; } }

        // The masks the file name matches (1 each), in the order of Words.
        public int[] MatchName(string fileName)
        {
            int[] counts = new int[Words.Length];
            if (masks == null || string.IsNullOrEmpty(fileName)) return counts;
            // the masks may overlap (*.pfx and cert*): every matching one counts
            for (int g = 1; g < maskGroups.Length; g++)
                if (Regex.IsMatch(fileName, "^" + MaskPattern(Words[maskGroups[g]].Text) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    counts[maskGroups[g]] = 1;
            return counts;
        }

        // Whether a file is found by its name alone (a mask matches it): the scan takes it whatever its type.
        public bool NameMatches(string fileName)
        {
            return masks != null && !string.IsNullOrEmpty(fileName) && masks.IsMatch(fileName);
        }

        static string Compact(string text)
        {
            return Regex.Replace(text, @"\s+", "");
        }

        static string Pattern(string word)
        {
            string[] pieces = word.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pieces.Length; i++) pieces[i] = Regex.Escape(pieces[i]);
            return string.Join(@"\s*", pieces);
        }

        public static TriggerWords Parse(string text)
        {
            List<TriggerWord> words = new List<TriggerWord>();
            HashSet<string> seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            int level = Medium;
            foreach (string raw in (text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    // an unknown level keeps the current one: the web page does not save such a list
                    int named = LevelOf(line.Substring(1, line.Length - 2));
                    if (named > 0) level = named;
                    continue;
                }
                string word = Regex.Replace(line, @"\s+", " ");
                if (seen.Add(word)) words.Add(new TriggerWord(word, level));
            }
            return new TriggerWords(words);
        }

        // «высокий» → High; 0 for a name that is no level. The same names as cloud\src\trigger-words.ts.
        public static int LevelOf(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "высокий": case "високий": case "high": return High;
                case "средний": case "середній": case "medium": return Medium;
                case "низкий": case "низький": case "low": return Low;
                default: return 0;
            }
        }

        public static string LevelName(int level)
        {
            return level >= High ? L.T("высокая") : level == Medium ? L.T("средняя") : L.T("низкая");
        }

        // Occurrences of every word in the text, in the order of Words.
        public int[] Count(string text)
        {
            int[] counts = new int[Words.Length];
            if (pattern == null || string.IsNullOrEmpty(text)) return counts;
            for (Match m = pattern.Match(text); m.Success; m = m.NextMatch())
                for (int g = 1; g < groups.Length; g++)
                    if (m.Groups[g].Success) { counts[groups[g]]++; break; }
            return counts;
        }
    }
}
