using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CompInfo
{
    // Interface language. Russian texts in the code are the keys; Strings.cs holds their Ukrainian and English versions.
    // Data (records, CSV, cloud-state.json) is never translated, and messages written by the cloud stay as the cloud sent them.
    public static class L
    {
        public static readonly string[] Codes = { "uk", "en", "ru" };

        // "system", "uk", "en" or "ru" (comp-info.ini); Code is the language actually used.
        public static string Choice = "system";
        public static string Code = "ru";

        static Dictionary<string, string[]> table;

        public static string SystemCode()
        {
            string name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (name == "uk" || name == "ru") return name;
            return "en";
        }

        public static void ApplyChoice()
        {
            Code = Array.IndexOf(Codes, Choice) >= 0 ? Choice : SystemCode();
        }

        static Dictionary<string, string[]> Table
        {
            get
            {
                if (table == null)
                {
                    Dictionary<string, string[]> t = new Dictionary<string, string[]>(StringComparer.Ordinal);
                    string[] s = Strings.All;
                    for (int i = 0; i + 2 < s.Length; i += 3) t[s[i]] = new[] { s[i + 1], s[i + 2] };
                    table = t;
                }
                return table;
            }
        }

        public static bool Has(string ru) { return Table.ContainsKey(ru); }

        // Translation of a Russian text; the text itself when it is Russian or not translated.
        public static string T(string ru)
        {
            if (Code == "ru" || ru == null) return ru;
            string[] v;
            if (!Table.TryGetValue(ru, out v)) return ru;
            string s = Code == "uk" ? v[0] : v[1];
            return string.IsNullOrEmpty(s) ? ru : s;
        }

        // Translated format string: F("Записей: {0}", n).
        public static string F(string ru, params object[] args)
        {
            return string.Format(T(ru), args);
        }
    }

    // comp-info.ini next to the program: small preferences of this copy (theme, language).
    // A plain text file, so it can be edited by hand: lines "name=value" under "[settings]".
    public static class Settings
    {
        static string Path { get { return AppPaths.File("comp-info.ini"); } }

        static Dictionary<string, string> Read()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(Path))
                {
                    foreach (string raw in File.ReadAllLines(Path, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
                else
                {
                    // The first versions kept the theme in app-settings.json.
                    string old = AppPaths.File("app-settings.json");
                    if (File.Exists(old))
                    {
                        string theme = Files.ReadRecord(old).Text("theme");
                        if (!string.IsNullOrEmpty(theme)) values["theme"] = theme;
                    }
                }
            }
            catch (Exception) { }
            return values;
        }

        public static string Get(string name, string fallback)
        {
            string v;
            return Read().TryGetValue(name, out v) && v.Length > 0 ? v : fallback;
        }

        public static void Set(string name, string value)
        {
            try
            {
                Dictionary<string, string> values = Read();
                values[name] = value;
                StringBuilder sb = new StringBuilder();
                sb.Append("; CHAGEAR Toolbox settings of this copy.\r\n");
                sb.Append("; theme: system, light, dark. language: system, uk, en, ru.\r\n");
                sb.Append("; colorblind: on, off (a click on the coloured title switches it).\r\n");
                sb.Append("; domainUser, domainPassword: domain account remembered by the «Домен» section (the password is only masked).\r\n");
                sb.Append("[settings]\r\n");
                List<string> names = new List<string>(values.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string n in names) if (values[n].Length > 0) sb.Append(n).Append('=').Append(values[n]).Append("\r\n");
                File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }   // a read-only flash drive simply keeps the defaults
        }
    }
}
