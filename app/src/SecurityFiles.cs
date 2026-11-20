using System;
using System.Collections.Generic;

namespace CompInfo
{
    // Location chips (#даунлоадс #корзина …) in front of a note: «#Desktop #документы · 3».
    // This part has no window code; the WinForms drawing is in FileChips.cs, the WPF one in app\wpf.
    public partial class FileChips
    {
        public readonly List<string> Labels = new List<string>();
        public string Text = "";

        public static FileChips Parse(string value)
        {
            string rest = (value ?? "").Trim();
            FileChips chips = new FileChips();
            while (rest.StartsWith("#", StringComparison.Ordinal))
            {
                int space = rest.IndexOf(' ');
                string token = space < 0 ? rest : rest.Substring(0, space);
                chips.Labels.Add(token.Substring(1));
                rest = space < 0 ? "" : rest.Substring(space + 1).TrimStart();
            }
            if (chips.Labels.Count == 0) return null;
            if (rest.StartsWith("·", StringComparison.Ordinal)) rest = rest.Substring(1).TrimStart();
            chips.Text = rest;
            return chips;
        }

        // The colour of a chip: "Danger", "Warn", "Ok", "Muted", or a colour of its own: "SupraWeed", "Viber", "Claude".
        public static string ColorKey(string label)
        {
            if (label == "SupraWeed" || label == "Viber" || label == "Claude") return label;
            if (label == L.T("#корзина").TrimStart('#')) return "Danger";
            if (label == L.T("#даунлоадс").TrimStart('#')) return "Warn";
            if (label == "Desktop") return "Muted";
            return "Ok";
        }
    }

    // The found documents of the ДСК check (SecurityView of app\wpf\Pages, in SUSpector): tags, the files that are never
    // deleted, deleting all files with a tag, and the summary of the check after deletions.
    public static class SecurityFiles
    {
        // Location tags whose documents can be deleted all at once.
        public static string[] BulkTags
        {
            get { return new[] { L.T("#корзина"), L.T("#даунлоадс"), L.T("#телеграм"), "#WhatsApp", "#Viber", "#Claude" }; }
        }

        public static bool HasFiles(SecurityCheck check)
        {
            if (check != null) foreach (SecurityDetail item in check.Items) if (item.FilePath.Length > 0) return true;
            return false;
        }

        public static List<string> TagsOf(SecurityDetail item)
        {
            FileChips chips = FileChips.Parse(item.Note);
            return chips == null ? new List<string>() : chips.Labels;
        }

        // Files in a SupraWeed folder are the program's own templates: they hold trigger words, not ДСК content.
        public static bool Protected(SecurityDetail item)
        {
            return TagsOf(item).Contains("SupraWeed");
        }

        // Found files with the tag that may be deleted all at once (SupraWeed files never are); skipped counts the SupraWeed ones.
        public static List<SecurityDetail> Tagged(SecurityCheck check, string tag, out int skipped)
        {
            List<SecurityDetail> result = new List<SecurityDetail>();
            skipped = 0;
            if (check == null) return result;
            string label = tag.TrimStart('#');
            foreach (SecurityDetail item in check.Items)
            {
                if (item.FilePath.Length == 0) continue;
                List<string> tags = TagsOf(item);
                if (!tags.Contains(label)) continue;
                if (tags.Contains("SupraWeed")) skipped++;
                else result.Add(item);
            }
            return result;
        }

        // removed files are already out of check.Items: the summary counts them; when none is left,
        // a failed check becomes a warning asking to run it again.
        public static void Removed(SecurityCheck check, int removed)
        {
            if (check.ScanSummary == null) check.ScanSummary = check.Summary;
            check.DeletedFiles += removed;
            if (!HasFiles(check) && check.Level == SecurityLevel.Bad)
            {
                check.Level = SecurityLevel.Warning;
                check.Summary = L.F("Найденные документы удалены ({0}). Запустите проверку снова, чтобы убедиться.", check.DeletedFiles);
            }
            else check.Summary = check.ScanSummary + L.F("; удалено: {0}", check.DeletedFiles);
        }
    }
}
