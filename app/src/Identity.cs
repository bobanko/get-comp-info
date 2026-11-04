using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CompInfo
{
    // Which record in data\ belongs to this computer (computer-identity.ps1).
    public static class Identity
    {
        static readonly string[] SerialPlaceholders = { "To be filled by O.E.M.", "Default string", "System Serial Number", "None", "Unknown", "0" };

        public static bool UsableSerial(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string v = value.Trim();
            foreach (string p in SerialPlaceholders) if (string.Equals(v, p, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // Upper-case UUID, or null for an empty, zero or FFFFFFFF-… value.
        public static string NormalizeUuid(object value)
        {
            Guid g;
            if (value == null || !Guid.TryParse(Convert.ToString(value).Trim(), out g)) return null;
            if (g == Guid.Empty || g.ToString() == "ffffffff-ffff-ffff-ffff-ffffffffffff") return null;
            return g.ToString().ToUpperInvariant();
        }

        // MachineGuid of the Windows installation; the explicit registry view gives 32- and 64-bit the same value.
        public static string WindowsInstallationId()
        {
            RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
            string id;
            using (RegistryKey hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (RegistryKey key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                if (key == null) throw new InvalidOperationException("Не удалось прочитать идентификатор установки Windows.");
                id = NormalizeUuid(key.GetValue("MachineGuid"));
            }
            if (id == null) throw new InvalidOperationException("Идентификатор установки Windows отсутствует. Сбор остановлен: имя компьютера как ID не используется.");
            return id;
        }

        public class Match
        {
            public string Id;
            public string Path;
            public JsonObject Record;
        }

        public class Result
        {
            public string Id;
            public string Source;
            public string Path;        // existing record of this computer, or null
            public string InstallationId;
        }

        static bool Same(object a, string b)
        {
            return a != null && b != null && string.Equals(Convert.ToString(a), b, StringComparison.OrdinalIgnoreCase);
        }

        public static Result Find(string uuidValue, string serial, string installationIdValue, string dataPath)
        {
            string uuid = NormalizeUuid(uuidValue);
            string installationId = NormalizeUuid(installationIdValue);
            string source = uuid != null ? "smbiosUuid" : UsableSerial(serial) ? "serial" : "windowsMachineGuid";
            string id;
            if (source == "smbiosUuid") id = uuid;
            else if (source == "serial") id = serial.Trim();
            else
            {
                if (installationId == null) throw new InvalidOperationException("Нет устойчивого идентификатора компьютера.");
                id = "WINDOWS-" + installationId;
            }
            string newId = id;

            List<Match> matches = new List<Match>();
            if (Directory.Exists(dataPath))
            {
                foreach (string file in Directory.GetFiles(dataPath, "*.json"))
                {
                    JsonObject record;
                    try { record = Files.ReadRecord(file); }
                    catch (Exception e) { throw new InvalidDataException("Не удалось проверить запись " + System.IO.Path.GetFileName(file) + ": " + e.Message); }
                    string recordUuid = NormalizeUuid(record["smbiosUuid"]);
                    bool sameInstallation = installationId != null && NormalizeUuid(record["windowsMachineGuid"]) == installationId &&
                        (uuid == null || recordUuid == null || uuid == recordUuid);
                    bool sameUuid = uuid != null && recordUuid == uuid;
                    // Do not match by serial when a record has a different valid UUID.
                    bool sameSerial = UsableSerial(serial) && Same(record["serial"], serial) && (uuid == null || recordUuid == null);
                    string recordId = record.Text("computerId");
                    if (string.IsNullOrEmpty(recordId)) recordId = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (sameInstallation || sameUuid || sameSerial || string.Equals(recordId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        Match m = new Match();
                        m.Id = recordId; m.Path = file; m.Record = record;
                        matches.Add(m);
                    }
                }
            }
            if (matches.Count > 1) throw new InvalidDataException("Найдено несколько записей этого компьютера. Сначала устраните дубликаты в базе.");
            Result result = new Result();
            result.InstallationId = installationId;
            if (matches.Count == 1)
            {
                id = matches[0].Id;
                result.Path = matches[0].Path;
                // Preserve the immutable cloud key, even if it originally came from a hostname.
                string oldSource = matches[0].Record.Text("computerIdSource");
                if (oldSource == "hostname" || oldSource == "legacyId" || id != newId) source = "legacyId";
            }
            result.Id = id;
            result.Source = source;
            return result;
        }

        // Old records without UUID, serial and Windows ID can only be recognised by the computer name.
        public static List<Match> FindUnboundLegacy(string dataPath, string hostname)
        {
            List<Match> result = new List<Match>();
            if (!Directory.Exists(dataPath)) return result;
            foreach (string file in Directory.GetFiles(dataPath, "*.json"))
            {
                JsonObject record = Files.ReadRecord(file);
                if (Same(record["hostname"], hostname) && string.IsNullOrEmpty(record.Text("windowsMachineGuid")) &&
                    NormalizeUuid(record["smbiosUuid"]) == null && !UsableSerial(record.Text("serial")))
                {
                    Match m = new Match();
                    m.Path = file; m.Record = record;
                    m.Id = string.IsNullOrEmpty(record.Text("computerId")) ? System.IO.Path.GetFileNameWithoutExtension(file) : record.Text("computerId");
                    result.Add(m);
                }
            }
            return result;
        }
    }
}
