using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CompInfo
{
    // Folders of this copy of the program: everything sits next to comp-info.exe (flash drive or PC).
    public static class AppPaths
    {
        static string root;

        public static string Root
        {
            get
            {
                if (root == null)
                {
                    // COMPINFO_ROOT lets tests and development builds point at another copy.
                    string forced = Environment.GetEnvironmentVariable("COMPINFO_ROOT");
                    root = !string.IsNullOrEmpty(forced) ? Path.GetFullPath(forced) : AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                }
                return root;
            }
            set { root = value; }
        }

        public static string Data { get { return Path.Combine(Root, "data"); } }
        public static string Exports { get { return Path.Combine(Root, "exports"); } }
        public static string File(string name) { return Path.Combine(Root, name); }
    }

    // Safe file operations shared by every part of the program (sync-merge.ps1).
    public static class Files
    {
        static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);

        // Links could redirect writes outside the program folder.
        public static void AssertNoLink(string path)
        {
            string item = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(item))
            {
                if (System.IO.File.Exists(item) || Directory.Exists(item))
                {
                    if ((System.IO.File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Ссылки на файлы и папки не поддерживаются: " + item);
                }
                item = Path.GetDirectoryName(item);
            }
        }

        public static JsonObject ReadRecord(string path)
        {
            AssertNoLink(path);
            JsonObject o;
            try { o = Json.ParseObject(System.IO.File.ReadAllText(path, Encoding.UTF8)); }
            catch (FormatException e) { throw new FormatException(e.Message + " (" + path + ")"); }
            catch (ArgumentException e) { throw new FormatException("Некорректный JSON: " + e.Message + " (" + path + ")"); }
            catch (InvalidOperationException e) { throw new FormatException("Некорректный JSON: " + e.Message + " (" + path + ")"); }
            return o;
        }

        // Writes JSON through a temporary file, so a failure never leaves a half-written record.
        public static void WriteJson(string path, object value)
        {
            AssertNoLink(path);
            string parent = Path.GetDirectoryName(path);
            Directory.CreateDirectory(parent);
            string temporary = Path.Combine(parent, Guid.NewGuid().ToString("N") + ".tmp");
            System.IO.File.WriteAllText(temporary, Json.Pretty(value) + "\r\n", Utf8Bom);
            MoveIntoPlace(temporary, path, 10);
        }

        // Replaces path with the already written temporary file (same folder) and removes the temporary file.
        public static void MoveIntoPlace(string temporary, string path, int attempts)
        {
            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        if (System.IO.File.Exists(path)) System.IO.File.Replace(temporary, path, null);
                        else System.IO.File.Move(temporary, path);
                        return;
                    }
                    catch (IOException)
                    {
                        // Antivirus and search indexers briefly open a fresh file without allowing deletion.
                        // Wait a little; as a last resort overwrite the file in place, which they still allow.
                        if (attempt >= attempts) { System.IO.File.Copy(temporary, path, true); return; }
                        Thread.Sleep(50 * attempt);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        if (attempt >= attempts) { System.IO.File.Copy(temporary, path, true); return; }
                        Thread.Sleep(50 * attempt);
                    }
                }
            }
            finally
            {
                try { if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary); } catch (Exception) { }
            }
        }
    }

    // One data\*.json file.
    public class RecordFile
    {
        public string Path;
        public string FileName;
        public JsonObject Data;
        public string Id;
        public string Hash;
        public string CloudStatus;
    }

    public static class Records
    {
        // The old manual "added to external database" mark is replaced by the computed cloud status.
        public static readonly string[] DeprecatedFields = { "addedToExternalDatabase", "addedToExternalDatabaseAt" };

        // computerId, or the file name for legacy records without it.
        public static string CloudId(JsonObject record, string fileName)
        {
            string id = record["computerId"] as string;
            if (id != null && id.Trim().Length > 0) return id;
            return System.IO.Path.GetFileNameWithoutExtension(fileName);
        }

        // Only characters Windows forbids are replaced: the name must give back exactly the same id.
        public static string SafeFileName(string id)
        {
            return Regex.Replace(id, "[<>:\"/\\\\|?*\\x00-\\x1f]", "_") + ".json";
        }

        public static List<RecordFile> ReadAll(string dataPath, CloudState state, List<string> warnings)
        {
            List<RecordFile> result = new List<RecordFile>();
            if (!Directory.Exists(dataPath))
            {
                if (warnings != null) warnings.Add("Папка с данными не найдена: " + dataPath);
                return result;
            }
            string[] files = Directory.GetFiles(dataPath, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                try { result.Add(Load(path, state)); }
                catch (Exception e)
                {
                    if (warnings != null) warnings.Add(System.IO.Path.GetFileName(path) + ": " + e.Message);
                }
            }
            return result;
        }

        public static RecordFile Load(string path, CloudState state)
        {
            RecordFile r = new RecordFile();
            r.Path = path;
            r.FileName = System.IO.Path.GetFileName(path);
            r.Data = Files.ReadRecord(path);
            r.Id = CloudId(r.Data, r.FileName);
            r.Hash = Json.Hash(r.Data);
            r.CloudStatus = state != null ? state.StatusOf(r) : CloudState.Missing;
            return r;
        }
    }

    // cloud-config.json: address of the cloud API.
    public static class CloudConfig
    {
        public static string ReadApiUrl(string root)
        {
            string path = System.IO.Path.Combine(root, "cloud-config.json");
            if (!System.IO.File.Exists(path)) throw new FileNotFoundException("Не найден файл настроек облака: " + path);
            string url = (Files.ReadRecord(path).Text("apiUrl") ?? "").Trim().TrimEnd('/');
            // Plain http is allowed only for a local development server.
            if (!Regex.IsMatch(url, "^(https://|http://(localhost|127\\.0\\.0\\.1)(:\\d+)?/)"))
                throw new InvalidDataException("В cloud-config.json нужен адрес API, начинающийся с https://");
            return url;
        }
    }

    // cloud-state.json: what this copy last exchanged with the cloud (cloud-client.ps1).
    public class CloudState
    {
        public const string Synced = "В облаке";
        public const string Changed = "Изменено";
        public const string Missing = "Нет в облаке";

        public class Entry
        {
            public string File;
            public int Revision;
            public string Hash;
        }

        public string ApiUrl = "";
        public long Cursor;
        public string LastSyncAt;
        // Record ids are case-sensitive in the cloud.
        public readonly Dictionary<string, Entry> Records = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public static CloudState Read(string root, string apiUrl)
        {
            CloudState state = new CloudState();
            state.ApiUrl = apiUrl;
            string path = System.IO.Path.Combine(root, "cloud-state.json");
            if (!System.IO.File.Exists(path)) return state;
            JsonObject saved = Files.ReadRecord(path);
            // State of another cloud is meaningless here: start over, local data stays untouched.
            if ((saved.Text("apiUrl") ?? "") != apiUrl) return state;
            object cursor = saved["cursor"];
            state.Cursor = cursor == null ? 0 : System.Convert.ToInt64(cursor);
            state.LastSyncAt = saved.Text("lastSyncAt");
            JsonObject records = saved["records"] as JsonObject;
            if (records != null)
            {
                foreach (string id in records.Names)
                {
                    JsonObject e = records[id] as JsonObject;
                    if (e == null) continue;
                    Entry entry = new Entry();
                    entry.File = e.Text("file") ?? "";
                    entry.Revision = e["revision"] == null ? 0 : System.Convert.ToInt32(e["revision"]);
                    entry.Hash = e.Text("hash") ?? "";
                    state.Records[id] = entry;
                }
            }
            return state;
        }

        // State for the viewer and the export; empty when the cloud is not configured.
        public static CloudState ForDisplay(string root)
        {
            try { return Read(root, CloudConfig.ReadApiUrl(root)); }
            catch (Exception) { return new CloudState(); }
        }

        public void Save(string root)
        {
            JsonObject records = new JsonObject();
            List<string> ids = new List<string>(Records.Keys);
            ids.Sort(StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, true));
            foreach (string id in ids)
            {
                Entry e = Records[id];
                JsonObject o = new JsonObject();
                o["file"] = e.File;
                o["revision"] = e.Revision;
                o["hash"] = e.Hash;
                records[id] = o;
            }
            JsonObject document = new JsonObject();
            document["apiUrl"] = ApiUrl;
            document["cursor"] = Cursor;
            document["lastSyncAt"] = LastSyncAt;
            document["records"] = records;
            Files.WriteJson(System.IO.Path.Combine(root, "cloud-state.json"), document);
        }

        public string StatusOf(RecordFile r)
        {
            Entry e;
            if (!Records.TryGetValue(r.Id, out e)) return Missing;
            return e.Hash == r.Hash ? Synced : Changed;
        }
    }
}
