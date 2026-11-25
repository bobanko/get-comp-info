using System;
using System.Collections.Generic;
using System.IO;

namespace CompInfo.Wpf
{
    // The utility's options read from flashstashbee.ini (IniSettings). Theme and language live there too,
    // but the shared code reads those (as in youSBee); here it is only about the copying itself.
    public class ArchiveOptions
    {
        public string BaseDir;        // where to put the backups
        public long MaxBytes;         // do not copy volumes using more than this; 0 = no limit
        public long MaxFileBytes;     // do not copy files larger than this; 0 = no limit
        public bool Stubs;            // leave a text placeholder instead of skipped large files
        public string[] Extensions;   // null/empty = any; otherwise a list of ".pdf"-style extensions in lower case
        public bool ExtensionsExclude; // true: Extensions lists what NOT to copy (everything else is copied)
        public bool Zip;              // pack the copy into a ZIP

        public static string DefaultBaseDir
        {
            get { return Path.Combine(Path.GetTempPath(), "FlashStashBee"); }
        }

        public static ArchiveOptions From(IniSettings s)
        {
            var o = new ArchiveOptions();
            o.BaseDir = s.Get("baseDir", DefaultBaseDir);
            if (string.IsNullOrWhiteSpace(o.BaseDir)) o.BaseDir = DefaultBaseDir;
            int gb, mb;
            bool gbOk = int.TryParse(s.Get("maxSizeGb", "64"), out gb) && gb > 0;
            bool mbOk = int.TryParse(s.Get("maxFileMb", "200"), out mb) && mb > 0;
            // Each limit has its own on/off checkbox; an older ini with "0" means "off".
            o.MaxBytes = SizeLimitOn(s, "maxSizeOn", gbOk) ? (long)gb * 1024 * 1024 * 1024 : 0;
            o.MaxFileBytes = SizeLimitOn(s, "maxFileOn", mbOk) ? (long)mb * 1024 * 1024 : 0;
            o.Stubs = s.Get("stubs", "on") == "on";
            o.Zip = s.Get("zip", "off") == "on";
            o.Extensions = ParseExtensions(s.Get("extensions", ""));
            o.ExtensionsExclude = s.Get("extensionsExclude", "off") == "on";
            return o;
        }

        // A size limit is active when its checkbox is on and the number is positive.
        public static bool SizeLimitOn(IniSettings s, string key, bool numberOk)
        {
            return numberOk && s.Get(key, numberOk ? "on" : "off") == "on";
        }

        // "pdf, .docx; jpg" -> { ".pdf", ".docx", ".jpg" }. Empty -> null (copy everything).
        public static string[] ParseExtensions(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var list = new List<string>();
            foreach (string part in raw.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string e = part.Trim().ToLowerInvariant();
                if (e.Length == 0) continue;
                if (e[0] != '.') e = "." + e;
                if (!list.Contains(e)) list.Add(e);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }
    }
}
