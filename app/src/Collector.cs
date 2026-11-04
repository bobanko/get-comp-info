using System;
using System.Collections.Generic;
using System.IO;

namespace CompInfo
{
    // Building and saving the record of this computer (get-comp-info.ps1).
    public class Collection
    {
        public ComputerSnapshot Snapshot;
        public Identity.Result Identity;
        public string FilePath;
        public JsonObject Previous;       // saved record of this computer, or null
        public string PreviousError;     // the saved record exists but cannot be read
        public Identity.Match Legacy;    // old record that may belong to this computer, to confirm with the user

        public class Field
        {
            public string Label;
            public string Previous;
            public string Current;
            public bool Changed { get { return Previous != Current; } }
        }

        public static Collection Start(string dataPath)
        {
            Collection c = new Collection();
            c.Snapshot = ComputerSnapshot.Collect();
            c.Identity = CompInfo.Identity.Find(c.Snapshot.SmbiosUuid, c.Snapshot.Serial, c.Snapshot.InstallationId, dataPath);
            if (c.Identity.Path == null)
            {
                List<Identity.Match> legacy = CompInfo.Identity.FindUnboundLegacy(dataPath, c.Snapshot.Hostname);
                if (legacy.Count > 1) throw new InvalidDataException("Несколько старых записей с таким именем. Сначала устраните дубликаты.");
                if (legacy.Count == 1) c.Legacy = legacy[0];
            }
            c.ResolvePath(dataPath);
            return c;
        }

        // The user confirmed that the old record belongs to this computer.
        public void BindLegacy(string dataPath)
        {
            Identity.Id = Legacy.Id;
            Identity.Source = "legacyId";
            Identity.Path = Legacy.Path;
            Legacy = null;
            ResolvePath(dataPath);
        }

        void ResolvePath(string dataPath)
        {
            string safe = System.Text.RegularExpressions.Regex.Replace(Identity.Id, "[<>:\"/\\\\|?*]", "_").Trim().TrimEnd('.');
            FilePath = Identity.Path ?? Path.Combine(dataPath, safe + ".json");
            Previous = null;
            PreviousError = null;
            if (File.Exists(FilePath))
            {
                try { Previous = Files.ReadRecord(FilePath); }
                catch (Exception e) { PreviousError = e.Message; }
            }
        }

        public string PreviousComment
        {
            get { return Previous == null ? "" : (Previous.Text("comment") ?? ""); }
        }

        // The record exactly as it will be saved, in the field order of the console version.
        public JsonObject Build(string comment, string savedAt)
        {
            ComputerSnapshot s = Snapshot;
            JsonObject r = new JsonObject();
            r["computerId"] = Identity.Id;
            r["computerIdSource"] = Identity.Source;
            r["hostname"] = s.Hostname;
            r["ip"] = s.Ip;
            r["mac"] = s.Mac;
            r["serial"] = s.Serial;
            r["smbiosUuid"] = s.SmbiosUuid;
            r["windowsMachineGuid"] = Identity.InstallationId;
            r["partOfDomain"] = s.PartOfDomain;
            r["domain"] = s.Domain;
            r["manufacturer"] = s.Manufacturer;
            r["model"] = s.Model;
            r["username"] = s.Username;
            r["comment"] = comment;
            r["windows"] = s.Windows;
            r["esetInstalled"] = s.EsetInstalled.HasValue ? (object)s.EsetInstalled.Value : null;
            r["collectedAt"] = savedAt;
            r["createdAt"] = savedAt;
            if (Previous != null)
            {
                if (!string.IsNullOrWhiteSpace(Previous.Text("createdAt"))) r["createdAt"] = Previous["createdAt"];
                // Legacy records only have the date of their last collection.
                else if (!string.IsNullOrWhiteSpace(Previous.Text("collectedAt"))) r["createdAt"] = Previous["collectedAt"];
            }
            return r;
        }

        public static string Show(JsonObject r, string name)
        {
            if (r == null) return null;
            if (name == "computer")
            {
                List<string> parts = new List<string>();
                foreach (string n in new string[] { "manufacturer", "model" })
                    if (!string.IsNullOrWhiteSpace(r.Text(n))) parts.Add(r.Text(n));
                return parts.Count > 0 ? string.Join(" ", parts) : "";
            }
            if (name == "esetInstalled") return ComputerSnapshot.EsetText(r[name]);
            if (name == "partOfDomain") { object v = r[name]; return v == null ? "" : (v is bool && (bool)v ? "да" : "нет"); }
            return r.Text(name) ?? "";
        }

        static readonly string[][] FieldNames =
        {
            new[] { "computerId", "ID компьютера" },
            new[] { "hostname", "Имя компьютера" },
            new[] { "windowsMachineGuid", "ID установки Windows" },
            new[] { "partOfDomain", "Член домена" },
            new[] { "domain", "Домен / группа" },
            new[] { "ip", "IP-адрес" },
            new[] { "mac", "MAC-адрес" },
            new[] { "serial", "Серийный номер" },
            new[] { "computer", "Компьютер" },
            new[] { "username", "Пользователь" },
            new[] { "comment", "Комментарий" },
            new[] { "windows", "Windows" },
            new[] { "esetInstalled", "ESET" },
        };

        public List<Field> Compare(JsonObject current)
        {
            List<Field> fields = new List<Field>();
            foreach (string[] f in FieldNames)
            {
                Field field = new Field();
                field.Label = f[1];
                field.Current = Show(current, f[0]);
                field.Previous = Previous == null ? null : Show(Previous, f[0]);
                fields.Add(field);
            }
            return fields;
        }

        public bool HasChanges(string comment)
        {
            if (Previous == null) return true;
            foreach (Field f in Compare(Build(comment, "")))
                if (f.Changed) return true;
            return false;
        }

        public JsonObject Save(string comment)
        {
            // The actual save time, after the user has confirmed.
            JsonObject record = Build(comment, DateTime.Now.ToString("o"));
            Files.WriteJson(FilePath, record);
            return record;
        }
    }
}
