// Checks of the program core, without touching the computer: run by tools\build.BAT.
// tests.exe --check-data <program folder>: compares record hashes with cloud-state.json of a real copy.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            Check(!p.Missing && p.Locations.Count == 2 && p.DomainCheckHost == "dc.example", "profiles read");
            Check(p.IsOfficeAddress("10.112.171.200") && !p.IsOfficeAddress("10.112.171.100") && !p.IsOfficeAddress("10.113.171.200"), "office address by common segments");
            Check(p.IsOfficeAddress("10.113.5.20") && !p.IsOfficeAddress("10.111.5.20"), "office address by own segments");
            Check(NetworkProfiles.Read(Path.Combine(dir, "none.json")).Missing, "missing profiles");
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
