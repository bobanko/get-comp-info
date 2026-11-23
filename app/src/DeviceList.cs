using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CompInfo
{
    // «База устройств» without window code (DatabaseView of appwpfPages and the tests): the records of this copy,
    // repeated values across the whole base by the keys of the web interface (logic.js), the naming policy,
    // the filters and the sort order of the table.
    public class DeviceList
    {
        public List<RecordFile> All = new List<RecordFile>();
        public CloudState Cloud = new CloudState();
        public readonly List<string> Warnings = new List<string>();
        Dictionary<string, int> ipCounts = new Dictionary<string, int>(), hostCounts = new Dictionary<string, int>(), serialCounts = new Dictionary<string, int>();

        // Columns of the table: 0 name, 1 IP, 2 serial, 3 employee or user, 4 collected, 5 cloud, 6 ESET, 7 inventory, 8 comment.
        public static string[] Columns { get { return new[] { L.T("Имя компьютера"), L.T("IP-адрес"), L.T("Серийный номер"), L.T("Сотрудник / пользователь"), L.T("Собрано"), L.T("Облако"), "ESET", L.T("Инвентаризация"), L.T("Комментарий") }; } }
        public const int CommentColumn = 8;
        // ESET and the comment are not sortable, as in the console.
        public static bool Sortable(int column) { return column != 6 && column != CommentColumn; }

        // Filters of the web device list.
        public static string[] EsetFilters { get { return new[] { L.T("ESET: все"), L.T("ESET: установлен"), L.T("ESET: не установлен"), L.T("ESET: не проверено") }; } }
        public static string[] InventoryFilters { get { return new[] { L.T("Инвентаризация: все"), L.T("Инвентаризация: да"), L.T("Инвентаризация: нет") }; } }

        public void Load(string root)
        {
            Warnings.Clear();
            Cloud = CloudState.ForDisplay(root);
            All = Records.ReadAll(System.IO.Path.Combine(root, "data"), Cloud, Warnings);
            ipCounts = new Dictionary<string, int>();
            hostCounts = new Dictionary<string, int>();
            serialCounts = new Dictionary<string, int>();
            foreach (RecordFile r in All)
            {
                Count(ipCounts, IpKey(r.Data));
                Count(hostCounts, HostKey(r.Data));
                Count(serialCounts, SerialKey(r.Data));
            }
        }

        public string LastSync { get { return string.IsNullOrEmpty(Cloud.LastSyncAt) ? L.T("не было") : Fmt.Date(Cloud.LastSyncAt); } }

        // The texts of the repeats filter: how many values repeat per field.
        public string[] RepeatFilters
        {
            get
            {
                return new[] { L.T("Повторы: все"), L.T("Повторы IP: ") + Repeats(ipCounts), L.T("Повторы имён: ") + Repeats(hostCounts),
                    L.T("Повторы серийников: ") + Repeats(serialCounts) };
            }
        }

        static int Repeats(Dictionary<string, int> counts)
        {
            int n = 0;
            foreach (int c in counts.Values) if (c > 1) n++;
            return n;
        }

        static void Count(Dictionary<string, int> counts, string key)
        {
            if (key.Length == 0) return;
            int n;
            counts[key] = counts.TryGetValue(key, out n) ? n + 1 : 1;
        }

        static bool Repeated(Dictionary<string, int> counts, string key)
        {
            int n;
            return key.Length > 0 && counts.TryGetValue(key, out n) && n > 1;
        }

        static string IpKey(JsonObject d) { return (d.Text("ip") ?? "").Trim(); }
        static string HostKey(JsonObject d) { return (d.Text("hostname") ?? "").Trim().ToUpperInvariant(); }
        static string SerialKey(JsonObject d) { return Identity.UsableSerial(d.Text("serial")) ? d.Text("serial").Trim().ToUpperInvariant() : ""; }

        public bool RepeatedIp(JsonObject d) { return Repeated(ipCounts, IpKey(d)); }
        public bool RepeatedHost(JsonObject d) { return Repeated(hostCounts, HostKey(d)); }
        public bool RepeatedSerial(JsonObject d) { return Repeated(serialCounts, SerialKey(d)); }

        // Naming policy of the web interface: "AB-123-…"; DESKTOP-, LAPTOP-, WIN- are names Windows made up.
        public static string HostIssue(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, "^(DESKTOP|LAPTOP|WIN)-", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return L.T("Имя по умолчанию от Windows: переименуйте по правилам");
            return System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Z]{2}-\\d{3}-.", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? null : L.T("Имя не по правилам (ожидается вид АА-000-…)");
        }

        // Collected during the last 7 days.
        public static bool Recent(string value)
        {
            DateTime d;
            if (!DateTime.TryParse(value ?? "", out d)) return false;
            TimeSpan age = DateTime.Now - d;
            return age.TotalDays <= 7 && age.TotalMinutes >= -1;
        }

        public static long IpOrder(string value)
        {
            string[] octets = (value ?? "").Trim().Split('.');
            if (octets.Length != 4) return long.MaxValue;
            long n = 0;
            foreach (string o in octets)
            {
                int v;
                if (o.Length == 0 || o.Length > 3 || !int.TryParse(o, NumberStyles.None, CultureInfo.InvariantCulture, out v) || v > 255) return long.MaxValue;
                n = n * 256 + v;
            }
            return n;
        }

        // The inventory mark set on the website.
        // The person column: the employee set on the website, otherwise the Windows user.
        public static string Person(JsonObject d)
        {
            string employee = (d.Text("employee") ?? "").Trim();
            return employee.Length > 0 ? employee : d.Text("username") ?? "";
        }

        public static bool Inventoried(JsonObject d) { return d["inventoried"] is bool && (bool)d["inventoried"]; }

        // Every word of the search occurs somewhere in the record (any field), as typed or in the other alphabet.
        public static bool MatchesSearch(RecordFile r, List<string> words)
        {
            if (words.Count == 0) return true;
            StringBuilder text = new StringBuilder(r.Id);
            foreach (string name in r.Data.Names) text.Append('\n').Append(r.Data.Text(name));
            string haystack = text.ToString();
            string key = LooseSearch.LooseKey(haystack).Text;
            foreach (string word in words)
                if (!LooseSearch.Matches(haystack, word, key)) return false;
            return true;
        }

        // eset: 0 all, 1 installed, 2 not installed, 3 not checked; inventory: 0 all, 1 yes, 2 no;
        // repeats: 0 all, 1 IP, 2 names, 3 serial numbers.
        public bool Passes(RecordFile r, int eset, int inventory, int repeats)
        {
            object e = r.Data["esetInstalled"];
            switch (eset)
            {
                case 1: if (!(e is bool && (bool)e)) return false; break;
                case 2: if (!(e is bool && !(bool)e)) return false; break;
                case 3: if (e is bool) return false; break;
            }
            if (inventory == 1 && !Inventoried(r.Data)) return false;
            if (inventory == 2 && Inventoried(r.Data)) return false;
            switch (repeats)
            {
                case 1: if (!RepeatedIp(r.Data)) return false; break;
                case 2: if (!RepeatedHost(r.Data)) return false; break;
                case 3: if (!RepeatedSerial(r.Data)) return false; break;
            }
            return true;
        }

        // The records to show: filtered, searched and sorted (stable: equal rows keep the file order).
        public List<RecordFile> Shown(List<string> words, int eset, int inventory, int repeats, int sortColumn, bool descending)
        {
            List<KeyValuePair<int, RecordFile>> indexed = new List<KeyValuePair<int, RecordFile>>();
            foreach (RecordFile r in All)
                if (Passes(r, eset, inventory, repeats) && MatchesSearch(r, words)) indexed.Add(new KeyValuePair<int, RecordFile>(indexed.Count, r));
            StringComparer text = StringComparer.Create(CultureInfo.CurrentCulture, true);
            indexed.Sort(delegate (KeyValuePair<int, RecordFile> a, KeyValuePair<int, RecordFile> b)
            {
                int c = Compare(a.Value, b.Value, sortColumn, text);
                if (descending) c = -c;
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            List<RecordFile> shown = new List<RecordFile>();
            foreach (KeyValuePair<int, RecordFile> p in indexed) shown.Add(p.Value);
            return shown;
        }

        static int CloudOrder(string status)
        {
            return status == CloudState.Synced ? 0 : status == CloudState.Changed ? 1 : 2;
        }

        static DateTime DateOf(string value)
        {
            DateTime d;
            return DateTime.TryParse(value ?? "", out d) ? d : DateTime.MinValue;
        }

        static int Compare(RecordFile a, RecordFile b, int column, StringComparer text)
        {
            int c;
            switch (column)
            {
                case 1: c = IpOrder(a.Data.Text("ip")).CompareTo(IpOrder(b.Data.Text("ip"))); break;
                case 2: c = text.Compare(a.Data.Text("serial") ?? "", b.Data.Text("serial") ?? ""); break;
                case 3:
                    string ua = Person(a.Data), ub = Person(b.Data);
                    // Records without a user go last.
                    c = (ua.Trim().Length == 0).CompareTo(ub.Trim().Length == 0);
                    if (c == 0) c = text.Compare(ua, ub);
                    break;
                case 4: c = DateOf(a.Data.Text("collectedAt")).CompareTo(DateOf(b.Data.Text("collectedAt"))); break;
                case 5: c = CloudOrder(a.CloudStatus).CompareTo(CloudOrder(b.CloudStatus)); break;
                case 7: c = Inventoried(a.Data).CompareTo(Inventoried(b.Data)); break;
                default: c = 0; break;
            }
            if (c == 0) c = text.Compare(a.Data.Text("hostname") ?? "", b.Data.Text("hostname") ?? "");
            return c;
        }

        // The fields of the card of a computer, in this order; fields this version does not know follow them.
        public static string[][] CardFields { get { return new[]
        {
            new[] { "hostname", L.T("Имя компьютера") }, new[] { "employee", L.T("Сотрудник") }, new[] { "computerId", L.T("ID компьютера") }, new[] { "computerIdSource", L.T("Источник ID") },
            new[] { "serial", L.T("Серийный номер") }, new[] { "smbiosUuid", "UUID" }, new[] { "windowsMachineGuid", L.T("ID установки Windows") },
            new[] { "manufacturer", L.T("Производитель") }, new[] { "model", L.T("Модель") }, new[] { "windows", "Windows" },
            new[] { "partOfDomain", L.T("Член домена") }, new[] { "domain", L.T("Домен / группа") }, new[] { "ip", L.T("IP-адрес") }, new[] { "mac", L.T("MAC-адрес") },
            new[] { "username", L.T("Пользователь") }, new[] { "esetInstalled", "ESET" }, new[] { "inventoried", L.T("Инвентаризация") },
            new[] { "createdAt", L.T("Создана") }, new[] { "collectedAt", L.T("Собрана") },
        }; } }

        // A value of the card as the user reads it.
        public static string CardValue(JsonObject d, string name)
        {
            if (name == "esetInstalled") return ComputerSnapshot.EsetText(d[name]);
            if (name == "createdAt" || name == "collectedAt") return Fmt.Date(d.Text(name));
            if (name == "partOfDomain") { object v = d[name]; return v == null ? "" : (v is bool && (bool)v ? L.T("да") : L.T("нет")); }
            if (name == "inventoried") return d[name] is bool && (bool)d[name] ? L.T("да") : L.T("нет") + L.T(" (отметку ставят на сайте)");
            return d.Text(name) ?? "";
        }

        // Saves the comment of a record; returns the record read again.
        public static RecordFile SaveComment(RecordFile r, string comment, CloudState cloud)
        {
            JsonObject data = Files.ReadRecord(r.Path);
            data["comment"] = comment;
            Files.WriteJson(r.Path, data);
            return Records.Load(r.Path, cloud);
        }
    }
}
