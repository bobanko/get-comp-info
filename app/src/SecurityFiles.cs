using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CompInfo
{
    // Where a found document lies, for the view «По пользователям» of SUSpector: whose profile (or which disk) and which
    // place in it: the bin, a messenger or Claude, or the first folder under the profile (Документы, Рабочий стол…).
    public class DocumentPlace
    {
        // Owner: "user" (a profile folder), "drive" (outside the profiles), "unknown" (a bin file without its folder).
        public string OwnerKind, Owner, OwnerPath;
        // Folder: "bin", "app" (Claude, Telegram, WhatsApp, Viber, SupraWeed), "folder", "root" (straight in the owner's folder).
        public string FolderKind, Folder;
        // The path under the place: under the folder for "folder", under the owner for the others.
        public string Rest;

        public string OwnerKey { get { return OwnerKind + ":" + (OwnerPath ?? "").ToLowerInvariant(); } }
        public string FolderKey { get { return OwnerKey + "|" + FolderKind + ":" + (Folder ?? "").ToLowerInvariant(); } }

        // The profile folders of this computer (ProfileList, without the system ones) and the folder that holds them.
        public static List<string> Profiles(out List<string> roots)
        {
            List<string> profiles = new List<string>();
            roots = new List<string>();
            try
            {
                using (RegistryKey list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                    if (list != null)
                    {
                        string root = Convert.ToString(list.GetValue("ProfilesDirectory"));
                        if (!string.IsNullOrEmpty(root)) roots.Add(Environment.ExpandEnvironmentVariables(root));
                        foreach (string sid in list.GetSubKeyNames())
                            using (RegistryKey key = list.OpenSubKey(sid))
                            {
                                string path = key == null ? null : Convert.ToString(key.GetValue("ProfileImagePath"));
                                if (string.IsNullOrEmpty(path)) continue;
                                path = Environment.ExpandEnvironmentVariables(path);
                                if (path.IndexOf(@"\Windows\", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                                profiles.Add(path);
                            }
                    }
            }
            catch (Exception) { }
            string mine = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (mine.Length > 0)
            {
                profiles.Add(mine);
                string parent = Path.GetDirectoryName(mine);
                if (!string.IsNullOrEmpty(parent)) roots.Add(parent);
            }
            return profiles;
        }

        // profiles: the profile folders; roots: the folders of profiles (C:\Users), where a folder of a removed profile
        // also counts as a user.
        public static DocumentPlace Of(string path, bool recycle, IList<string> profiles, IList<string> roots)
        {
            DocumentPlace place = new DocumentPlace();
            string p = (path ?? "").Replace('/', '\\');
            string owner = null;
            foreach (string profile in profiles)
            {
                string folder = profile.TrimEnd('\\');
                if (Under(p, folder) && (owner == null || folder.Length > owner.Length)) owner = folder;
            }
            if (owner == null)
                foreach (string root in roots)
                {
                    string folder = root.TrimEnd('\\');
                    if (!Under(p, folder)) continue;
                    int end = p.IndexOf('\\', folder.Length + 1);
                    // a file straight in C:\Users is no one's
                    if (end > 0) { owner = p.Substring(0, end); break; }
                }
            if (owner != null)
            {
                place.OwnerKind = "user";
                place.OwnerPath = owner;
                place.Owner = Path.GetFileName(owner);
            }
            else if (p.Length >= 3 && p[1] == ':' && p[2] == '\\')
            {
                place.OwnerKind = "drive";
                place.OwnerPath = p.Substring(0, 3).ToUpperInvariant();
                place.Owner = L.F("Диск {0}", place.OwnerPath.Substring(0, 2));
            }
            else
            {
                place.OwnerKind = "unknown";
                place.OwnerPath = "";
                place.Owner = L.T("Место неизвестно");
            }

            string rest = place.OwnerPath.Length == 0 ? p.TrimStart('\\') : p.Substring(place.OwnerPath.TrimEnd('\\').Length).TrimStart('\\');
            place.Rest = rest;
            List<string> tags = SecurityAudit.LocationTags(p, recycle);
            if (recycle) { place.FolderKind = "bin"; place.Folder = L.T("Корзина"); return place; }
            foreach (string app in new[] { "#Claude", L.T("#телеграм"), "#WhatsApp", "#Viber", "#SupraWeed" })
                if (tags.Contains(app))
                {
                    place.FolderKind = "app";
                    place.Folder = app == L.T("#телеграм") ? "Telegram" : app.TrimStart('#');
                    return place;
                }
            int slash = rest.IndexOf('\\');
            if (slash < 0)
            {
                place.FolderKind = "root";
                place.Folder = place.OwnerKind == "user" ? L.T("В папке пользователя") : place.OwnerKind == "drive" ? L.T("В корне диска") : "";
                return place;
            }
            string first = rest.Substring(0, slash);
            place.FolderKind = "folder";
            place.Folder = KnownFolder(first);
            place.Rest = rest.Substring(slash + 1);
            return place;
        }

        static string KnownFolder(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "desktop": case "рабочий стол": case "робочий стіл": return L.T("Рабочий стол");
                case "documents": case "документы": case "документи": return L.T("Документы");
                case "downloads": case "загрузки": case "завантаження": return L.T("Загрузки");
                default: return name;
            }
        }

        static bool Under(string path, string folder)
        {
            return folder.Length > 0 && path.Length > folder.Length + 1 && path[folder.Length] == '\\' &&
                path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
    }

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

        // The place of a found file for the view by users: its shown path is the original one also for a file in the bin.
        public static DocumentPlace Place(SecurityDetail item, IList<string> profiles, IList<string> roots)
        {
            return DocumentPlace.Of(item.Text, TagsOf(item).Contains(L.T("#корзина").TrimStart('#')), profiles, roots);
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
