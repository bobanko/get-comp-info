// Checks of the program core, without touching the computer: run by tools\build.BAT.
// tests.exe --check-data <program folder>: compares record hashes with cloud-state.json of a real copy.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CompInfo
{
    static class Tests
    {
        static int failures;
        static string here;

        static void Check(bool ok, string message)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + message);
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            here = Path.GetDirectoryName(typeof(Tests).Assembly.Location);
            if (args.Length == 2 && args[0] == "--check-data") return CheckData(args[1]);
            if (args.Length == 1 && args[0] == "--security-smoke") return SecuritySmoke();
            if (args.Length == 1 && args[0] == "--recycle-smoke") return RecycleSmoke();
            string fixtures = args.Length > 0 ? args[0] : Path.Combine(here, @"..\app\tests");
            Run("JSON hashes match PowerShell", delegate { HashesMatchPowerShell(fixtures); });
            Run("JSON round trip", JsonRoundTrip);
            Run("record ids and file names", RecordIds);
            Run("computer identity", IdentityMatching);
            Run("CSV export matches PowerShell", delegate { CsvMatchesPowerShell(fixtures); });
            Run("network profiles", Profiles);
            Run("USB serial numbers", UsbSerials);
            Run("collected record", CollectedRecord);
            Run("cloud state", CloudStateFile);
            Run("computer names match PowerShell", ComputerNames);
            Run("security document scan", SecurityDocumentScan);
            Run("record differences and merge", Differences);
            Run("program files and versions", ProgramFiles);
            Run("program update install", InstallRelease);
            Run("synchronization with a fake cloud", Synchronization);
            Run("translations cover the interface", delegate { Translations(Path.Combine(fixtures, @"..\src")); });
            Run("settings file", SettingsFile);
            Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        static void Run(string name, Action test)
        {
            int before = failures;
            try { test(); }
            catch (Exception e) { failures++; Console.WriteLine("FAIL: " + name + ": " + e); }
            Console.WriteLine((failures == before ? "ok    " : "FAILED ") + name);
        }

        // Runs the read-only security review against the current computer. This is not part of the
        // regular build because the document scan can take a while; it is useful before a release.
        static int SecuritySmoke()
        {
            foreach (SecurityCheck check in SecurityAudit.Run(delegate (string title) { Console.WriteLine("checking " + title); }))
                Console.WriteLine(check.Level + "\t" + check.Title + "\t" + check.Summary +
                    (check.Level == SecurityLevel.Error ? "\t" + check.Details : ""));
            return 0;
        }

        static int RecycleSmoke()
        {
            List<RecycleFile> files = RecycleBin.Files();
            Console.WriteLine("Recycle Bin items available through Windows Shell: " + files.Count);
            return 0;
        }

        static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "comp-info-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void WriteRecord(string dir, string name, string json)
        {
            File.WriteAllText(Path.Combine(dir, name), json, new UTF8Encoding(true));
        }

        // Expected values were produced by Get-RecordHash / Get-CanonicalJson of the PowerShell version.
        static void HashesMatchPowerShell(string fixtures)
        {
            IList records = (IList)Json.Parse(File.ReadAllText(Path.Combine(fixtures, "hash-records.json"), Encoding.UTF8));
            string[] expected = File.ReadAllLines(Path.Combine(fixtures, "hash-expected.txt"), Encoding.UTF8);
            Check(records.Count == expected.Length, "fixture count");
            for (int i = 0; i < records.Count && i < expected.Length; i++)
            {
                string hash = expected[i].Substring(0, 64);
                string canonical = expected[i].Substring(66);
                Check(Json.Canonical(records[i]) == canonical, "canonical JSON of record " + i + ":\n  " + Json.Canonical(records[i]) + "\n  " + canonical);
                Check(Json.Hash(records[i]) == hash, "hash of record " + i);
            }
        }

        static void JsonRoundTrip()
        {
            JsonObject o = Json.ParseObject("{\"b\":1,\"a\":[true,null,\"т<е>кст\",1.50,{\"x\":{}}],\"c\":\"\"}");
            string pretty = Json.Pretty(o);
            JsonObject back = Json.ParseObject(pretty);
            Check(Json.Hash(o) == Json.Hash(back), "hash changes after a write");
            Check(back.Names[0] == "b" && back.Names[1] == "a", "field order is lost");
            Check(Json.Compact(back) == "{\"b\":1,\"a\":[true,null,\"т\\u003cе\\u003eкст\",1.50,{\"x\":{}}],\"c\":\"\"}", "compact form: " + Json.Compact(back));
            back["a"] = null;
            back.Remove("b");
            back["d"] = "new";
            Check(string.Join(",", back.Names) == "a,c,d", "set/remove order");
            bool refused = false;
            try { Json.ParseObject("{\"a\":1,\"A\":2}"); } catch (FormatException) { refused = true; }
            Check(refused, "fields differing only by case must be refused, as PowerShell does");
        }

        static void RecordIds()
        {
            JsonObject withId = Json.ParseObject("{\"computerId\":\"ID-1\"}");
            Check(Records.CloudId(withId, "other.json") == "ID-1", "computerId wins");
            Check(Records.CloudId(Json.ParseObject("{\"computerId\":\" \"}"), "O.E.M..json") == "O.E.M.", "legacy id from file name");
            Check(Records.SafeFileName("a/b:c*d.") == "a_b_c_d..json", "safe file name: " + Records.SafeFileName("a/b:c*d."));
        }

        static void IdentityMatching()
        {
            string dir = TempDir();
            try
            {
                const string uuid = "03341FE8-9B79-4CC9-8CBD-A82BDD18C277";
                const string win = "11111111-2222-3333-4444-555555555555";
                Identity.Result fresh = Identity.Find(uuid.ToLowerInvariant(), "SER1", win, dir);
                Check(fresh.Id == uuid && fresh.Source == "smbiosUuid" && fresh.Path == null, "new computer by UUID");
                Check(Identity.Find(null, "To be filled by O.E.M.", win, dir).Id == "WINDOWS-" + win, "placeholder serial falls back to Windows ID");
                Check(Identity.Find("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", " SER2 ", win, dir).Id == "SER2", "invalid UUID falls back to serial");

                WriteRecord(dir, "OLD-HOST.json", "{\"computerId\":\"OLD-HOST\",\"computerIdSource\":\"hostname\",\"hostname\":\"pc1\",\"smbiosUuid\":\"" + uuid + "\"}");
                Identity.Result old = Identity.Find(uuid, "SER1", win, dir);
                Check(old.Id == "OLD-HOST" && old.Source == "legacyId" && old.Path.EndsWith("OLD-HOST.json"), "old cloud key is kept");

                // Same serial but another valid UUID: a different computer.
                Identity.Result other = Identity.Find("A3341FE8-9B79-4CC9-8CBD-A82BDD18C277", "SER1", "99999999-2222-3333-4444-555555555555", dir);
                Check(other.Path == null, "serial must not match a record with another UUID");

                WriteRecord(dir, "dup.json", "{\"computerId\":\"DUP\",\"windowsMachineGuid\":\"" + win + "\"}");
                bool refused = false;
                try { Identity.Find(uuid, "SER1", win, dir); } catch (InvalidDataException) { refused = true; }
                Check(refused, "two records of one computer must stop the collection");

                string legacyDir = TempDir();
                WriteRecord(legacyDir, "PC-7.json", "{\"hostname\":\"PC-7\",\"serial\":\"Default string\"}");
                List<Identity.Match> legacy = Identity.FindUnboundLegacy(legacyDir, "pc-7");
                Check(legacy.Count == 1 && legacy[0].Id == "PC-7", "unbound legacy record by host name");
                Directory.Delete(legacyDir, true);
            }
            finally { Directory.Delete(dir, true); }
        }

        // Expected file was written by export-comp-info.ps1 from the same fixture records.
        static void CsvMatchesPowerShell(string fixtures)
        {
            string output = Path.Combine(TempDir(), "out.csv");
            int n = CsvExport.Write(Path.Combine(fixtures, "export-data"), output, new CloudState(), ';');
            Check(n == 3, "exported count");
            byte[] actual = File.ReadAllBytes(output);
            byte[] expected = File.ReadAllBytes(Path.Combine(fixtures, "export-expected.csv"));
            Check(Encoding.UTF8.GetString(actual) == Encoding.UTF8.GetString(expected), "CSV differs:\n" + Encoding.UTF8.GetString(actual) + "\nexpected:\n" + Encoding.UTF8.GetString(expected));
            Check(actual.Length > 3 && actual[0] == 0xEF, "CSV must start with a UTF-8 BOM for Excel");
            bool refused = false;
            try { CsvExport.Write(Path.Combine(fixtures, "export-data"), output, new CloudState(), ';'); } catch (IOException) { refused = true; }
            Check(refused, "an existing export must not be overwritten");
            Directory.Delete(Path.GetDirectoryName(output), true);
        }

        static void Profiles()
        {
            Check(NetworkProfiles.MaskFromPrefix(25) == "255.255.255.128" && NetworkProfiles.MaskFromPrefix(0) == "0.0.0.0", "subnet mask");
            string dir = TempDir();
            string path = Path.Combine(dir, "network-profiles.json");
            File.WriteAllText(path, "{\"key\":\"network\",\"value\":{\"firstOctet\":10,\"segments\":[111,112],\"domainCheckHost\":\"dc.example\"," +
                "\"locations\":[{\"name\":\"Штаб\",\"thirdOctet\":171,\"prefixLength\":25,\"gatewayOctet\":129,\"firstHostOctet\":130,\"lastHostOctet\":254,\"dnsServers\":[\"10.121.1.2\"]}," +
                "{\"name\":\"Склад\",\"thirdOctet\":5,\"prefixLength\":24,\"gatewayOctet\":1,\"firstHostOctet\":2,\"lastHostOctet\":200,\"dnsServers\":[],\"segments\":[113]}]}}", Encoding.UTF8);
            NetworkProfiles p = NetworkProfiles.Read(path);
            Check(!p.Missing && p.Locations.Count == 2 && p.DomainCheckHost == "dc.example" && p.DomainName == "dc.example", "profiles read, domain falls back to the check host");
            Check(p.IsOfficeAddress("10.112.171.200") && !p.IsOfficeAddress("10.112.171.100") && !p.IsOfficeAddress("10.113.171.200"), "office address by common segments");
            Check(p.IsOfficeAddress("10.113.5.20") && !p.IsOfficeAddress("10.111.5.20"), "office address by own segments");
            Check(NetworkProfiles.Read(Path.Combine(dir, "none.json")).Missing && NetworkProfiles.Read(Path.Combine(dir, "none.json")).DomainName == null, "missing profiles");
            File.WriteAllText(path, "{\"value\":{\"domainCheckHost\":\"10.0.0.1\",\"domainName\":\" Corp.Example \",\"locations\":[{\"name\":\"A\",\"thirdOctet\":1}]}}", Encoding.UTF8);
            Check(NetworkProfiles.Read(path).DomainName == "corp.example", "domain from the «Домен» field");
            File.WriteAllText(path, "{\"value\":{\"domainCheckHost\":\"10.0.0.1\",\"locations\":[{\"name\":\"A\",\"thirdOctet\":1}]}}", Encoding.UTF8);
            Check(NetworkProfiles.Read(path).DomainName == null, "an IP address is not a domain");
            Directory.Delete(dir, true);
        }

        static void UsbSerials()
        {
            Check(UsbDisk.SerialFromDeviceId(@"USBSTOR\DISK&VEN_GENERAL&PROD_USB_DISK&REV_5.00\7&2A5A8D4F&0") == "", "generated id is not a serial");
            Check(UsbDisk.SerialFromDeviceId(@"USBSTOR\DISK&VEN_KINGSTON&PROD_DT&REV_PMAP\408D5C1EB2F1E12039CF0A27&0") == "408D5C1EB2F1E12039CF0A27", "serial with LUN");
            Check(UsbDisk.SerialFromDeviceId(@"SCSI\DISK&VEN_NVME\5&1") == "", "not a USB disk");
        }

        static void CollectedRecord()
        {
            Collection c = new Collection();
            c.Snapshot = new ComputerSnapshot();
            c.Snapshot.Hostname = "IF-042-PC"; c.Snapshot.Serial = "S1"; c.Snapshot.Manufacturer = "LENOVO"; c.Snapshot.Model = "82YU";
            c.Snapshot.EsetInstalled = true; c.Snapshot.Domain = "WORKGROUP"; c.Snapshot.Username = "user"; c.Snapshot.Windows = "Windows 11";
            c.Identity = new Identity.Result();
            c.Identity.Id = "S1"; c.Identity.Source = "serial"; c.Identity.InstallationId = "11111111-2222-3333-4444-555555555555";
            JsonObject record = c.Build("note", "2026-11-04T10:00:00.0000000+02:00");
            Check(string.Join(",", record.Names) == "computerId,computerIdSource,hostname,ip,mac,serial,smbiosUuid,windowsMachineGuid,partOfDomain,domain," +
                "manufacturer,model,username,comment,windows,esetInstalled,collectedAt,createdAt", "field order of the console version");
            Check(record["ip"] == null && record.Has("ip"), "missing IP is saved as null");
            Check(c.HasChanges("note"), "new record has changes");
            c.Previous = record;
            Check(!c.HasChanges("note"), "same data: nothing to save");
            Check(c.HasChanges("other"), "comment change is a change");
            c.Previous = Json.ParseObject("{\"collectedAt\":\"2020-01-01T00:00:00\"}");
            Check((string)c.Build("", "now")["createdAt"] == "2020-01-01T00:00:00", "legacy createdAt from collectedAt");
        }

        static void CloudStateFile()
        {
            string dir = TempDir();
            CloudState s = new CloudState();
            s.ApiUrl = "https://example/api/client";
            s.Cursor = 12345678901;
            CloudState.Entry e = new CloudState.Entry();
            e.File = "a.json"; e.Revision = 3; e.Hash = "ABC";
            s.Records["ID-a"] = e;
            s.Save(dir);
            CloudState back = CloudState.Read(dir, s.ApiUrl);
            Check(back.Cursor == s.Cursor && back.Records["ID-a"].Revision == 3 && back.Records["ID-a"].Hash == "ABC", "state round trip");
            Check(CloudState.Read(dir, "https://other/api").Records.Count == 0, "state of another cloud is ignored");
            RecordFile r = new RecordFile();
            r.Id = "ID-a"; r.Hash = "ABC";
            Check(back.StatusOf(r) == CloudState.Synced, "synced status");
            r.Hash = "DEF";
            Check(back.StatusOf(r) == CloudState.Changed, "changed status");
            r.Id = "id-a";
            Check(back.StatusOf(r) == CloudState.Missing, "ids are case-sensitive");
            Directory.Delete(dir, true);
        }

        // Expected values were printed by computer-management.ps1 (ConvertTo-ComputerDesignation and friends).
        static void ComputerNames()
        {
            string[][] designations = { new[] { "Иванов", "IVANOV" }, new[] { "отдел 5", "OTDEL-5" }, new[] { "Щукин-Їжак", "SHCHUKIN-YIZHAK" },
                new[] { "ivanov", "IVANOV" }, new[] { "Ёлка Ґ", "ELKA-G" }, new[] { "Объём Шъ", "OBEM-SH" }, new[] { "Цех №3", "TSEKH-№3" } };
            foreach (string[] d in designations) Check(ComputerNaming.Designation(d[0]) == d[1], "designation of " + d[0] + ": " + ComputerNaming.Designation(d[0]));
            ComputerPolicy p = new ComputerPolicy();
            Check(ComputerNaming.Suggested("IF-042-IVANOV", "FR", p, null, null) == "FR-042-IVANOV", "suggested: keep designation");
            Check(ComputerNaming.Suggested("DESKTOP-ABC", "IF", p, "Петров", null) == "IF-042-PETROV", "suggested: from designation");
            Check(ComputerNaming.Suggested("DESKTOP-ABC", "IF", p, null, "10.111.171.130") == "IF-042-171-130", "suggested: from IP");
            Check(ComputerNaming.Suggested("DESKTOP-ABC", "FR", p, null, null) == "FR-042-PC", "suggested: fallback");
            Check(ComputerNaming.Suggested("XX-042-VERYLONGNAMEHERE", "IF", p, null, null) == "IF-042-VERYLONG", "suggested: cut to 15");
            Check(ComputerNaming.Suggested("DESKTOP", "IF", p, "---", null) == "IF-042-PC", "suggested: only hyphens");
            const string bad = "Нужно имя IF-042-...: латиница, цифры и дефис; окончание без дефиса по краям.";
            Check(ComputerNaming.Issue("IF-042-IVANOV", "IF", p) == null, "valid name");
            foreach (string n in new[] { "IF-042-", "IF-042--A", "IF-042-A-", "IF-042-ivanov", "IF-043-A", "IF-042-A_B" })
                Check(ComputerNaming.Issue(n, "IF", p) == bad, "invalid name " + n);
            Check(ComputerNaming.Issue("FR-042-IVANOV-PETR", "IF", p) == "Полное имя должно содержать не больше 15 символов.", "too long");
            foreach (string name in new[] { "IF-042-PC", "ABC-123-WORK-1", "AA-000-A" })
                Check(ComputerNaming.MatchesPolicy(name), "valid generic hostname policy: " + name);
            foreach (string name in new[] { "A-123-PC", "ABCD-123-PC", "AA-12-PC", "AA-123-", "AA-123-A-", "aa-123-PC", "AA-123-A_B", "ABC-123-TOO-LONG" })
                Check(!ComputerNaming.MatchesPolicy(name), "invalid generic hostname policy: " + name);
        }

        static void SecurityDocumentScan()
        {
            List<SecurityCheck> plan = SecurityAudit.Plan();
            Check(plan.Count == 9 && plan[0].Key == "hostname" && plan[6].Key == "administrators" && plan[7].Key == "passwords" && plan[8].Key == "documents",
                "fast security checks precede the document scan");
            Check(SecurityAudit.AdministratorLevel(1) == SecurityLevel.Good && SecurityAudit.AdministratorLevel(2) == SecurityLevel.Good &&
                SecurityAudit.AdministratorLevel(3) == SecurityLevel.Warning && SecurityAudit.AdministratorLevel(4) == SecurityLevel.Warning &&
                SecurityAudit.AdministratorLevel(5) == SecurityLevel.Bad, "administrator account thresholds");
            string dir = TempDir();
            try
            {
                string named = Path.Combine(dir, "ТФД список.doc");
                File.WriteAllText(named, "fixture", Encoding.UTF8);
                string documents = Path.Combine(dir, "Documents");
                string desktop = Path.Combine(dir, "Desktop");
                string telegram = Path.Combine(dir, "Downloads", "Telegram Desktop");
                string whatsapp = Path.Combine(dir, "WhatsApp");
                string claude = Path.Combine(dir, "AppData", "Roaming", "Claude", "Cache");
                Directory.CreateDirectory(documents);
                Directory.CreateDirectory(desktop);
                Directory.CreateDirectory(telegram);
                Directory.CreateDirectory(whatsapp);
                Directory.CreateDirectory(claude);
                string excel = Path.Combine(documents, "відомість.xlsx");
                string powerpoint = Path.Combine(dir, "доповідь.pptx");
                File.WriteAllText(excel, "fixture", Encoding.UTF8);
                File.WriteAllText(powerpoint, "fixture", Encoding.UTF8);
                string telegramFile = Path.Combine(telegram, "ДСК telegram.doc");
                string whatsappFile = Path.Combine(whatsapp, "МОСІ whatsapp.doc");
                string claudeFile = Path.Combine(claude, "ДСК Claude cache.doc");
                File.WriteAllText(telegramFile, "fixture", Encoding.UTF8);
                File.WriteAllText(whatsappFile, "fixture", Encoding.UTF8);
                File.WriteAllText(claudeFile, "fixture", Encoding.UTF8);
                string content = Path.Combine(desktop, "ДСК ordinary.docx");
                using (ZipArchive zip = ZipFile.Open(content, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry entry = zip.CreateEntry("word/document.xml");
                    using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                        writer.Write("<w:document><w:t>телефонний</w:t><w:t> довідник ДСК дск ДСК-ЕП дск-еп для службового користування МОСІ таємно особливої важливості</w:t></w:document>");
                }
                List<SecurityProgress> progress = new List<SecurityProgress>();
                SecurityCheck check = SecurityAudit.CheckDocumentsAt(new[] { dir }, delegate (SecurityProgress value) { progress.Add(value); });
                SecurityProgress finalProgress = progress[progress.Count - 1];
                Check(progress.Count >= 3 && progress[0].Indeterminate && !finalProgress.Indeterminate,
                    "document scan reports discovery and inspection phases");
                Check(finalProgress.Total == 7 && finalProgress.Checked == 7 && finalProgress.Matches == 7,
                    "document scan progress reaches the exact total");
                bool word = false, excelType = false, powerpointType = false, counts = false, telegramTag = false, whatsappTag = false, claudeTag = false;
                bool onlyFiles = true;
                foreach (SecurityDetail item in check.Items)
                {
                    if (item.FilePath.Length == 0) onlyFiles = false;
                    if (item.FilePath == named && item.FileType == "WORD") word = true;
                    if (item.FilePath == excel && item.FileType == "EXCEL" && item.Note.Contains("#документы")) excelType = true;
                    if (item.FilePath == powerpoint && item.FileType == "PPT") powerpointType = true;
                    if (item.FilePath == content && item.Note == "#Desktop · 10" &&
                        item.ToolTip.Contains("ДСК — 3") && item.ToolTip.Contains("ДСК-ЕП — 2") &&
                        item.ToolTip.Contains("МОСІ — 1") && item.ToolTip.Contains("для службового користування — 1") &&
                        item.ToolTip.Contains("таємно — 1") && item.ToolTip.Contains("особливої важливості — 1") &&
                        item.ToolTip.Contains("\r\n")) counts = true;
                    if (item.FilePath == telegramFile && item.Note.Contains("#телеграм") && item.Note.Contains("#даунлоадс")) telegramTag = true;
                    if (item.FilePath == whatsappFile && item.Note.Contains("#WhatsApp")) whatsappTag = true;
                    if (item.FilePath == claudeFile && item.Note.Contains("#Claude")) claudeTag = true;
                }
                Check(check.Level == SecurityLevel.Bad && check.Details.Contains(named) && check.Details.Contains(content), "name and Open XML content matches");
                Check(check.Summary.Contains("Проверено: 7") && check.Summary.Contains("помечено как ДСК: 7"), "document totals in the main result");
                Check(word && excelType && powerpointType, "Office file types in structured security details");
                Check(counts, "total occurrence count with a line-by-line tooltip");
                Check(telegramTag && whatsappTag && claudeTag, "messenger, Claude and downloads tags");
                Check(onlyFiles, "document detail table contains files only");
                File.Delete(named);
                File.Delete(excel);
                File.Delete(powerpoint);
                File.Delete(telegramFile);
                File.Delete(whatsappFile);
                File.Delete(claudeFile);
                File.Delete(content);
                check = SecurityAudit.CheckDocumentsAt(new[] { dir });
                Check(check.Level == SecurityLevel.Good && check.Summary.Contains("Проверено: 0") && check.Items.Count == 0, "empty document scan");
            }
            finally { Directory.Delete(dir, true); }
        }

        static void Differences()
        {
            JsonObject local = Json.ParseObject("{\"a\":1,\"b\":\"x\",\"c\":null,\"only\":true}");
            JsonObject cloud = Json.ParseObject("{\"a\":1,\"b\":\"y\",\"c\":null,\"new\":[1]}");
            Check(string.Join(",", RecordDiff.Differences(local, cloud)) == "b,new,only", "differences: " + string.Join(",", RecordDiff.Differences(local, cloud)));
            JsonObject merged = RecordDiff.Merge(local, cloud, new[] { "b", "only" });
            Check(Json.Compact(merged) == "{\"a\":1,\"c\":null,\"b\":\"y\"}", "merge: " + Json.Compact(merged));
            Check(Json.Compact(local) == "{\"a\":1,\"b\":\"x\",\"c\":null,\"only\":true}", "merge must not change the local record");
            Check(RecordDiff.ValueText(local, "missing") == "(поле отсутствует)" && RecordDiff.ValueText(local, "c") == "(null)", "value texts");
        }

        static void ProgramFiles()
        {
            foreach (string ok in new[] { "cloud-config.json", "comp-info.exe", "COMP-INFO.EXE", "a.ps1", "menu.BAT", "x.cmd" })
                Check(ProgramUpdate.IsProgramFile(ok), "program file " + ok);
            foreach (string no in new[] { "data.json", "other.exe", "sub/a.ps1", "..\\a.ps1", "cloud-key.txt", "Cloud-Config.json" })
                Check(!ProgramUpdate.IsProgramFile(no), "not a program file " + no);
            Check(ProgramUpdate.IsNewer("2026.11.05.1", "2026.11.04.12") && !ProgramUpdate.IsNewer("2026.11.04.1", "2026.11.04.1") && !ProgramUpdate.IsNewer("x", "1.0"), "versions");
        }

        static string MakeRelease(string dir, string version, Dictionary<string, string> files, string extra)
        {
            string content = Path.Combine(dir, "content-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(content);
            List<object> list = new List<object>();
            foreach (KeyValuePair<string, string> f in files)
            {
                string path = Path.Combine(content, f.Key);
                File.WriteAllText(path, f.Value, new UTF8Encoding(false));
                JsonObject entry = new JsonObject();
                entry["path"] = f.Key; entry["sha256"] = ProgramUpdate.Sha256(path); entry["size"] = new FileInfo(path).Length;
                list.Add(entry);
            }
            if (extra != null) File.WriteAllText(Path.Combine(content, extra), "x");
            JsonObject manifest = new JsonObject();
            manifest["version"] = version; manifest["files"] = list;
            File.WriteAllText(Path.Combine(content, "manifest.json"), Json.Compact(manifest));
            string zip = Path.Combine(dir, "release-" + Guid.NewGuid().ToString("N") + ".zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(content, zip);
            return zip;
        }

        static void InstallRelease()
        {
            string dir = TempDir();
            try
            {
                string root = Path.Combine(dir, "copy");
                Directory.CreateDirectory(Path.Combine(root, "data"));
                File.WriteAllText(Path.Combine(root, "old.ps1"), "old");
                File.WriteAllText(Path.Combine(root, "keep.ps1"), "v1");
                File.WriteAllText(Path.Combine(root, "comp-info.exe"), "exe v1");
                File.WriteAllText(Path.Combine(root, "data", "r.json"), "{}");
                Dictionary<string, string> files = new Dictionary<string, string> { { "keep.ps1", "v2" }, { "new.BAT", "@echo off" }, { "cloud-config.json", "{}" } };

                bool refused = false;
                try { ProgramUpdate.Install(root, MakeRelease(dir, "2.0", files, "evil.ps1"), "2.0", "1.0"); } catch (InvalidDataException) { refused = true; }
                Check(refused && File.ReadAllText(Path.Combine(root, "keep.ps1")) == "v1", "an archive with an extra file must change nothing");
                refused = false;
                try { ProgramUpdate.Install(root, MakeRelease(dir, "2.0", files, null), "3.0", "1.0"); } catch (InvalidDataException) { refused = true; }
                Check(refused, "wrong version must be refused");

                ProgramUpdate.Result r = ProgramUpdate.Install(root, MakeRelease(dir, "2.0", files, null), "2.0", "1.0");
                Check(File.ReadAllText(Path.Combine(root, "keep.ps1")) == "v2" && File.Exists(Path.Combine(root, "new.BAT")), "files installed");
                Check(!File.Exists(Path.Combine(root, "old.ps1")) && r.Removed.Contains("old.ps1"), "old program file removed");
                Check(File.Exists(Path.Combine(root, "comp-info.exe")), "the window program is kept by a release without it");
                Check(File.Exists(Path.Combine(root, "data", "r.json")), "data untouched");
                Check(File.ReadAllText(Path.Combine(r.Backup, "keep.ps1")) == "v1", "backup of the previous version");
            }
            finally { Directory.Delete(dir, true); }
        }

        static JsonObject Rec(string id, string host, string comment)
        {
            JsonObject r = new JsonObject();
            r["computerId"] = id; r["hostname"] = host; r["comment"] = comment;
            return r;
        }

        static void Synchronization()
        {
            string root = TempDir();
            using (FakeCloud cloud = new FakeCloud())
            {
                try
                {
                    string data = Path.Combine(root, "data");
                    Directory.CreateDirectory(data);
                    Files.WriteJson(Path.Combine(root, "cloud-config.json"), Json.ParseObject("{\"apiUrl\":\"" + cloud.ApiUrl + "\"}"));
                    Check(CloudConfig.ReadApiUrl(root) == cloud.ApiUrl, "local test cloud address accepted");
                    CloudClient client = new CloudClient(cloud.ApiUrl, FakeCloud.Key);
                    string error;
                    Check(client.Me(out error) != null, "key accepted");
                    Check(new CloudClient(cloud.ApiUrl, "wrong").Me(out error) == null && error == "Ключ не подошёл.", "wrong key: " + error);

                    JsonObject a = Rec("A", "pc-a", "первый");
                    a["addedToExternalDatabase"] = true;
                    Files.WriteJson(Path.Combine(data, "A.json"), a);
                    Files.WriteJson(Path.Combine(data, "O.E.M..json"), Rec(null, "pc-b", ""));
                    ScriptedQuestions ui = new ScriptedQuestions();
                    SyncCounts c = new CloudSync(root, client, ui).Run();
                    Check(c.PushedNew == 2 && c.Errors.Count == 0 && c.NetworkProfiles == "missing", "first sync pushes both: " + c.PushedNew + " " + string.Join("; ", c.Errors));
                    Check(cloud.Devices.ContainsKey("O.E.M."), "legacy id with dots reaches the cloud unchanged: " + string.Join(" | ", new List<string>(cloud.Devices.Keys)));
                    Check(!Files.ReadRecord(Path.Combine(data, "A.json")).Has("addedToExternalDatabase"), "deprecated field removed");
                    CloudState state = CloudState.ForDisplay(root);
                    Check(state.StatusOf(Records.Load(Path.Combine(data, "A.json"), null)) == CloudState.Synced, "record synced");

                    int puts = cloud.Puts;
                    c = new CloudSync(root, client, ui).Run();
                    Check(cloud.Puts == puts && c.PushedNew + c.PushedUpdated + c.ReceivedNew + c.ReceivedUpdated == 0, "nothing to do the second time");

                    // Changed on both sides: the user keeps the cloud comment and the local hostname.
                    cloud.Put("A", Rec("A", "pc-a", "из облака"), false);
                    Files.WriteJson(Path.Combine(data, "A.json"), Rec("A", "pc-a2", "первый"));
                    ui.CloudFields.Add("comment");
                    c = new CloudSync(root, client, ui).Run();
                    JsonObject saved = Files.ReadRecord(Path.Combine(data, "A.json"));
                    Check(ui.Conflicts == 1 && c.PushedUpdated == 1, "conflict resolved and sent");
                    Check(saved.Text("hostname") == "pc-a2" && saved.Text("comment") == "из облака", "merged record: " + Json.Compact(saved));
                    Check(Json.Canonical(cloud.Devices["A"].Data) == Json.Canonical(saved), "cloud has the merged record");

                    // New record in the cloud, the other one deleted there.
                    cloud.Put("C/1", Rec("C/1", "pc-c", ""), false);
                    cloud.Put("O.E.M.", null, true);
                    c = new CloudSync(root, client, ui).Run();
                    Check(c.ReceivedNew == 1 && File.Exists(Path.Combine(data, "C_1.json")), "new cloud record downloaded into a safe file name");
                    Check(c.Deleted == 1 && !File.Exists(Path.Combine(data, "O.E.M..json")), "record deleted in the cloud is deleted here");

                    // An old copy of a computer uploaded under its file name collides with the record in that file.
                    JsonObject old = Rec(null, "pc-old", "");
                    old["serial"] = "SER-X";
                    JsonObject current = Rec("UUID-X", "pc-new", "");
                    current["serial"] = "SER-X";
                    Files.WriteJson(Path.Combine(data, "SER-X.json"), current);
                    c = new CloudSync(root, client, ui).Run();
                    cloud.Put("SER-X", old, false);
                    c = new CloudSync(root, client, ui).Run();
                    Check(c.Errors.Count == 1 && c.Errors[0].Contains("запись UUID-X, pc-new") && c.Errors[0].Contains("дубликат"), "duplicate explained: " + string.Join("; ", c.Errors));
                    Check(Files.ReadRecord(Path.Combine(data, "SER-X.json")).Text("computerId") == "UUID-X", "the local record is kept");
                    File.Delete(Path.Combine(data, "SER-X.json"));
                    cloud.Put("SER-X", null, true);
                    cloud.Put("UUID-X", null, true);
                    c = new CloudSync(root, client, ui).Run();

                    // A file deleted by hand comes back: local deletion never deletes in the cloud.
                    File.Delete(Path.Combine(data, "C_1.json"));
                    c = new CloudSync(root, client, ui).Run();
                    Check(File.Exists(Path.Combine(data, "C_1.json")), "locally deleted record downloaded again");

                    // Deleted in the cloud but changed here: the user restores it.
                    cloud.Put("A", null, true);
                    Files.WriteJson(Path.Combine(data, "A.json"), Rec("A", "pc-a3", ""));
                    ui.DeletedAnswer = 0;
                    c = new CloudSync(root, client, ui).Run();
                    Check(ui.Deletions == 1 && !cloud.Devices["A"].Deleted && cloud.Devices["A"].Data.Text("hostname") == "pc-a3", "restored in the cloud");

                    // Two files with one id are skipped until the user keeps one.
                    Files.WriteJson(Path.Combine(data, "dup.json"), Rec("A", "copy", ""));
                    puts = cloud.Puts;
                    c = new CloudSync(root, client, ui).Run();
                    Check(cloud.Puts == puts && c.Errors.Exists(delegate (string e) { return e.StartsWith("Несколько файлов с идентификатором A"); }), "duplicates are reported, not sent");
                }
                finally { Directory.Delete(root, true); }
            }
        }
        static void SettingsFile()
        {
            string dir = TempDir();
            string saved = AppPaths.Root;
            AppPaths.Root = dir;
            try
            {
                File.WriteAllText(Path.Combine(dir, "app-settings.json"), "{\"theme\":\"dark\"}");
                Check(Settings.Get("theme", "system") == "dark", "theme taken over from app-settings.json");
                Settings.Set("language", "uk");
                Settings.Set("domainUser", "CORP\\admin");
                string ini = File.ReadAllText(Path.Combine(dir, "comp-info.ini"));
                Check(ini.Contains("theme=dark") && ini.Contains("language=uk") && ini.Contains("domainUser=CORP\\admin") && ini.Contains("[settings]"), "ini written: " + ini);
                File.WriteAllText(Path.Combine(dir, "comp-info.ini"), "; hand-edited\r\n[settings]\r\n Theme = light \r\nlanguage=en\r\n");
                Check(Settings.Get("theme", "system") == "light" && Settings.Get("language", "") == "en" && Settings.Get("missing", "x") == "x", "hand-edited ini read");
                Settings.Set("domainUser", "");
                Check(!File.ReadAllText(Path.Combine(dir, "comp-info.ini")).Contains("domainUser="), "empty values are dropped");
                L.Choice = "system"; L.ApplyChoice();
                Check(Array.IndexOf(L.Codes, L.Code) >= 0, "system language mapped to a supported one");
                L.Choice = "en"; L.ApplyChoice();
                Check(L.Code == "en", "chosen language");
            }
            finally { AppPaths.Root = saved; L.Code = "ru"; L.Choice = "system"; Directory.Delete(dir, true); }
        }

        // Every L.T / L.F text of the program has Ukrainian and English versions with the same edges and placeholders.
        static void Translations(string sources)
        {
            Dictionary<string, string[]> table = new Dictionary<string, string[]>(StringComparer.Ordinal);
            for (int i = 0; i + 2 < Strings.All.Length; i += 3)
            {
                Check(!table.ContainsKey(Strings.All[i]), "translated twice: " + Strings.All[i]);
                table[Strings.All[i]] = new[] { Strings.All[i + 1], Strings.All[i + 2] };
            }
            Check(Strings.All.Length % 3 == 0, "Strings.All must hold triples");
            System.Text.RegularExpressions.Regex call = new System.Text.RegularExpressions.Regex("L\\.[TF]\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            System.Text.RegularExpressions.Regex holes = new System.Text.RegularExpressions.Regex("\\{\\d+[^}]*\\}");
            int used = 0;
            foreach (string file in Directory.GetFiles(sources, "*.cs"))
            {
                foreach (System.Text.RegularExpressions.Match m in call.Matches(File.ReadAllText(file, Encoding.UTF8)))
                {
                    string key = System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value);
                    used++;
                    string[] v;
                    if (!table.TryGetValue(key, out v)) { Check(false, Path.GetFileName(file) + ": no translation for «" + key + "»"); continue; }
                    foreach (string t in v)
                    {
                        Check(t.Length > 0, "empty translation of «" + key + "»");
                        Check(t.Length - t.TrimStart().Length == key.Length - key.TrimStart().Length && t.Length - t.TrimEnd().Length == key.Length - key.TrimEnd().Length,
                            "spaces at the edges differ: «" + key + "» / «" + t + "»");
                        Check(holes.Matches(t).Count == holes.Matches(key).Count, "placeholders differ: «" + key + "» / «" + t + "»");
                    }
                }
            }
            Check(used > 300, "too few interface texts found: " + used);
            L.Code = "en";
            Check(L.T("Отмена") == "Cancel" && L.F("{0:N1} ГБ", 2.0).EndsWith("GB") && L.T("нет такой строки") == "нет такой строки", "lookup");
            L.Code = "ru";
        }

        // Real copy: every record the console version marked as synced must still be synced here.
        static int CheckData(string root)
        {
            CloudState state = CloudState.ForDisplay(root);
            List<string> warnings = new List<string>();
            List<RecordFile> records = Records.ReadAll(Path.Combine(root, "data"), state, warnings);
            int synced = 0, changed = 0, missing = 0;
            foreach (RecordFile r in records)
            {
                if (r.CloudStatus == CloudState.Synced) synced++;
                else if (r.CloudStatus == CloudState.Changed) { changed++; Console.WriteLine("changed: " + r.FileName); }
                else missing++;
            }
            foreach (string w in warnings) Console.WriteLine("warning: " + w);
            Console.WriteLine("records " + records.Count + ", synced " + synced + ", changed " + changed + ", not in cloud " + missing);
            return 0;
        }
    }
}
