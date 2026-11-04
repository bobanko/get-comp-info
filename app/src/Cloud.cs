using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CompInfo
{
    // HTTP access to the cloud API with the personal key (cloud-client.ps1).
    public class CloudClient
    {
        public class Response
        {
            public int Status;
            public object Body;
            public JsonObject Object { get { return Body as JsonObject; } }

            public string ErrorMessage
            {
                get
                {
                    JsonObject error = Object == null ? null : Object["error"] as JsonObject;
                    string message = error == null ? null : error.Text("message");
                    return string.IsNullOrEmpty(message) ? "Облако ответило кодом " + Status + "." : message;
                }
            }
        }

        public readonly string ApiUrl;
        public string Key;
        public JsonObject User;      // owner of the key, after Me()

        public CloudClient(string apiUrl, string key)
        {
            ApiUrl = apiUrl;
            Key = key;
        }

        HttpWebRequest NewRequest(string method, string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            request.UserAgent = "comp-info/" + AppVersion.Text;
            request.Headers["Authorization"] = "Bearer " + Key;
            IWebProxy proxy = WebRequest.GetSystemWebProxy();
            proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
            request.Proxy = proxy;
            return request;
        }

        // Error statuses come back as responses; only an unreachable cloud throws.
        static HttpWebResponse Send(HttpWebRequest request, byte[] body)
        {
            try
            {
                if (body != null)
                {
                    request.ContentLength = body.Length;
                    using (Stream s = request.GetRequestStream()) s.Write(body, 0, body.Length);
                }
                return (HttpWebResponse)request.GetResponse();
            }
            catch (WebException e)
            {
                if (e.Response == null) throw new IOException("Облако недоступно (" + request.RequestUri + "): " + e.Message, e);
                return (HttpWebResponse)e.Response;
            }
        }

        static object ReadBody(HttpWebResponse response)
        {
            string text;
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) text = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return Json.Parse(text); } catch (Exception) { return null; }
        }

        public Response Request(string method, string url, object body)
        {
            HttpWebRequest request = NewRequest(method, url);
            request.Accept = "application/json";
            byte[] bytes = null;
            if (body != null)
            {
                bytes = Encoding.UTF8.GetBytes(Json.Compact(body));
                request.ContentType = "application/json; charset=utf-8";
            }
            using (HttpWebResponse response = Send(request, bytes))
            {
                Response r = new Response();
                r.Status = (int)response.StatusCode;
                r.Body = ReadBody(response);
                return r;
            }
        }

        public void Download(string url, string path)
        {
            using (HttpWebResponse response = Send(NewRequest("GET", url), null))
            {
                if ((int)response.StatusCode != 200)
                {
                    Response r = new Response();
                    r.Status = (int)response.StatusCode;
                    r.Body = ReadBody(response);
                    throw new IOException(r.ErrorMessage);
                }
                using (FileStream output = File.Create(path)) response.GetResponseStream().CopyTo(output);
            }
        }

        // GET /me: the owner of the key, or null when the key is not accepted (the reason goes to error).
        public JsonObject Me(out string error)
        {
            Response r = Request("GET", ApiUrl + "/me", null);
            error = null;
            if (r.Status == 200) return r.Object ?? new JsonObject();
            if (r.Status == 401) { error = r.ErrorMessage; return null; }
            throw new IOException(r.ErrorMessage);
        }

        public static string UserText(JsonObject me)
        {
            return me.Text("name") + " <" + me.Text("email") + ">, роль " + me.Text("role");
        }

        // ---------- personal key ----------

        public static string KeyFromFile(string root)
        {
            string path = Path.Combine(root, "cloud-key.txt");
            if (!File.Exists(path)) return null;
            foreach (string line in File.ReadAllLines(path))
                if (line.Trim().Length > 0) return line.Trim();
            return null;
        }

        public static void SaveKey(string root, string key)
        {
            string path = Path.Combine(root, "cloud-key.txt");
            Files.AssertNoLink(path);
            File.WriteAllText(path, key + Environment.NewLine, new UTF8Encoding(false));
        }

        // ---------- network profiles ----------

        // Saves the cloud network profiles to network-profiles.json. Returns updated | unchanged | missing.
        public string UpdateNetworkProfiles(string root)
        {
            Response r = Request("GET", ApiUrl + "/settings/network", null);
            if (r.Status == 404) return "missing";
            if (r.Status != 200) throw new IOException(r.ErrorMessage);
            string path = Path.Combine(root, "network-profiles.json");
            if (File.Exists(path))
            {
                try { if (Json.Hash(Files.ReadRecord(path)) == Json.Hash(r.Body)) return "unchanged"; }
                catch (Exception) { }
            }
            Files.WriteJson(path, r.Body);
            return "updated";
        }

        // Current release in the cloud, or null when nothing is published.
        public JsonObject LatestRelease()
        {
            Response r = Request("GET", ApiUrl + "/program/latest", null);
            if (r.Status == 200) return r.Object;
            if (r.Status == 404) return null;
            throw new IOException(r.ErrorMessage);
        }
    }

    // Comparison and merge of two versions of a record (sync-merge.ps1).
    public static class RecordDiff
    {
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "computerId", "Идентификатор компьютера" }, { "computerIdSource", "Источник идентификатора" },
            { "hostname", "Имя компьютера" }, { "ip", "IP-адрес" }, { "mac", "MAC-адрес" },
            { "serial", "Серийный номер" }, { "smbiosUuid", "UUID" }, { "manufacturer", "Производитель" },
            { "windowsMachineGuid", "ID установки Windows" }, { "partOfDomain", "Член домена" }, { "domain", "Домен / рабочая группа" },
            { "model", "Модель" }, { "username", "Пользователь" }, { "comment", "Комментарий" },
            { "windows", "Windows" }, { "esetInstalled", "ESET установлен" }, { "collectedAt", "Дата сбора" }, { "createdAt", "Дата создания" },
        };

        public static string Label(string name)
        {
            string label;
            return Labels.TryGetValue(name, out label) ? label : name;
        }

        public static string ValueText(JsonObject record, string name)
        {
            if (!record.Has(name)) return "(поле отсутствует)";
            object v = record[name];
            if (v == null) return "(null)";
            string s = v as string;
            if (s != null) return s.Length == 0 ? "(пустая строка)" : s;
            return Json.Compact(v);
        }

        // Names present in either version, in the order they appear.
        public static List<string> AllFields(JsonObject local, JsonObject incoming)
        {
            List<string> names = new List<string>(local.Names);
            foreach (string n in incoming.Names) if (!names.Contains(n)) names.Add(n);
            return names;
        }

        public static List<string> Differences(JsonObject local, JsonObject incoming)
        {
            List<string> names = AllFields(local, incoming);
            names.Sort(StringComparer.Create(CultureInfo.CurrentCulture, true));
            List<string> result = new List<string>();
            foreach (string n in names)
            {
                if (local.Has(n) != incoming.Has(n) || Json.Canonical(local[n]) != Json.Canonical(incoming[n])) result.Add(n);
            }
            return result;
        }

        // Local version with the given fields taken from the incoming one.
        public static JsonObject Merge(JsonObject local, JsonObject incoming, IEnumerable<string> incomingFields)
        {
            JsonObject result = local.Clone();
            foreach (string name in incomingFields)
            {
                result.Remove(name);
                if (incoming.Has(name)) result[name] = incoming[name] == null ? null : Json.Parse(Json.Compact(incoming[name]));
            }
            return result;
        }
    }

    // Decisions the synchronization needs from the user; called on the sync thread.
    public interface ISyncQuestions
    {
        void Progress(string text);
        // Conflict with the cloud version: returns cloud | local | merged | defer and the record to keep.
        string ResolveConflict(string title, JsonObject local, JsonObject cloud, out JsonObject result);
        // The record was deleted in the cloud but changed here: 0 restore, 1 delete locally, 2 defer.
        int DeletedInCloud(string title, string details);
    }

    public class SyncCounts
    {
        public int PushedNew, PushedUpdated, PushedSame, ReceivedNew, ReceivedUpdated, Deleted, Deferred;
        public List<string> Errors = new List<string>();
        public string NetworkProfiles;
        public string NewVersion;
    }

    // Full synchronization of one copy of the program (Invoke-CloudSync).
    public class CloudSync
    {
        public const int PageSize = 200;

        class Item
        {
            public string Id, Path, File, Hash;
            public JsonObject Record;
        }

        readonly string root, dataPath;
        readonly CloudClient client;
        readonly ISyncQuestions ui;
        readonly CloudState state;
        readonly Dictionary<string, Item> local = new Dictionary<string, Item>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> skipped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public readonly SyncCounts Counts = new SyncCounts();

        public CloudSync(string root, CloudClient client, ISyncQuestions ui)
        {
            this.root = root;
            this.client = client;
            this.ui = ui;
            dataPath = Path.Combine(root, "data");
            state = CloudState.Read(root, client.ApiUrl);
        }

        public static StringComparer NameOrder { get { return StringComparer.Create(CultureInfo.CurrentCulture, true); } }

        public SyncCounts Run()
        {
            ReadLocal();
            ui.Progress("Отправка изменений…");
            List<string> ids = new List<string>(local.Keys);
            ids.Sort(NameOrder);
            foreach (string id in ids)
            {
                Item item = local[id];
                CloudState.Entry entry;
                if (state.Records.TryGetValue(id, out entry) && entry.Hash == item.Hash) continue;
                SendRecord(item);
                state.Save(root);
            }

            // A record deleted locally comes back with a full download: local deletion never deletes in the cloud.
            bool missing = false;
            foreach (string id in state.Records.Keys)
                if (!local.ContainsKey(id) && !skipped.ContainsKey(id)) { missing = true; break; }
            ui.Progress("Загрузка базы из облака…");
            Receive(missing);

            state.LastSyncAt = DateTime.Now.ToString("o");
            state.Save(root);

            try { Counts.NetworkProfiles = client.UpdateNetworkProfiles(root); }
            catch (Exception e) { Counts.Errors.Add("Профили сети не загружены: " + e.Message); }

            // A newer program version is only a hint: failing to check it must not fail the sync.
            try
            {
                JsonObject release = client.LatestRelease();
                if (release != null && ProgramUpdate.IsNewer(release.Text("version"), AppVersion.Text)) Counts.NewVersion = release.Text("version");
            }
            catch (Exception) { }
            return Counts;
        }

        // Reads data\*.json and drops deprecated fields from the files. Records sharing one id are skipped
        // entirely until the user keeps only one of the files.
        void ReadLocal()
        {
            Directory.CreateDirectory(dataPath);
            string[] files = Directory.GetFiles(dataPath, "*.json");
            Array.Sort(files, NameOrder);
            foreach (string path in files)
            {
                string file = Path.GetFileName(path);
                JsonObject record;
                try
                {
                    record = Files.ReadRecord(path);
                    bool removed = false;
                    foreach (string name in Records.DeprecatedFields) removed |= record.Remove(name);
                    if (removed) Files.WriteJson(path, record);
                }
                catch (Exception e)
                {
                    Counts.Errors.Add(file + ": " + e.Message);
                    continue;
                }
                string id = Records.CloudId(record, file);
                if (local.ContainsKey(id) || skipped.ContainsKey(id))
                {
                    if (local.ContainsKey(id)) { skipped[id] = new List<string> { local[id].File }; local.Remove(id); }
                    skipped[id].Add(file);
                    continue;
                }
                Item item = new Item();
                item.Id = id; item.Path = path; item.File = file; item.Record = record; item.Hash = Json.Hash(record);
                local[id] = item;
            }
            foreach (KeyValuePair<string, List<string>> p in skipped)
                Counts.Errors.Add("Несколько файлов с идентификатором " + p.Key + ": " + string.Join(", ", p.Value) + ". Оставьте один и повторите синхронизацию.");
        }

        void SetLocal(string id, string path, JsonObject record, int revision)
        {
            Files.WriteJson(path, record);
            JsonObject saved = Files.ReadRecord(path);
            Item item = new Item();
            item.Id = id; item.Path = path; item.File = Path.GetFileName(path); item.Record = saved; item.Hash = Json.Hash(saved);
            local[id] = item;
            CloudState.Entry entry = new CloudState.Entry();
            entry.File = item.File; entry.Revision = revision; entry.Hash = item.Hash;
            state.Records[id] = entry;
        }

        void RemoveLocal(string id)
        {
            Item item;
            if (local.TryGetValue(id, out item) && File.Exists(item.Path)) File.Delete(item.Path);
            local.Remove(id);
            state.Records.Remove(id);
        }

        static string Title(Item item)
        {
            string hostname = item.Record.Text("hostname") ?? "";
            if (hostname.Length == 0) hostname = item.Id;
            return hostname + " — " + item.File;
        }

        // .NET Framework silently drops trailing dots from URL path segments ("O.E.M." -> "O.E.M"),
        // so dots are escaped too; the server decodes them back.
        string DeviceUrl(string id)
        {
            return client.ApiUrl + "/devices/" + Uri.EscapeDataString(id).Replace(".", "%2E");
        }

        static int Int(object v) { return v == null ? 0 : Convert.ToInt32(v); }

        // Uploads one local record, resolving conflicts and cloud deletions with the user.
        void SendRecord(Item item)
        {
            CloudState.Entry entry;
            object baseRevision = state.Records.TryGetValue(item.Id, out entry) ? (object)entry.Revision : null;
            JsonObject record = item.Record;
            bool restore = false;
            while (true)
            {
                JsonObject body = new JsonObject();
                body["baseRevision"] = baseRevision;
                body["data"] = record;
                body["restore"] = restore;
                CloudClient.Response r = client.Request("PUT", DeviceUrl(item.Id), body);
                JsonObject o = r.Object ?? new JsonObject();
                if (r.Status == 200)
                {
                    JsonObject device = (JsonObject)o["device"];
                    int revision = Int(device["revision"]);
                    SetLocal(item.Id, item.Path, (JsonObject)device["data"], revision);
                    if (!(o["changed"] is bool && (bool)o["changed"])) Counts.PushedSame++;
                    else if (revision == 1) Counts.PushedNew++;
                    else Counts.PushedUpdated++;
                    return;
                }
                if (r.Status == 409)
                {
                    JsonObject current = (JsonObject)o["current"];
                    string title = Title(item) + " | в облаке изменил(а) " + current.Text("updatedBy") + ", " + Fmt.Date(current.Text("updatedAt"));
                    JsonObject result;
                    string action = ui.ResolveConflict(title, record, (JsonObject)current["data"], out result);
                    if (action == "defer") { Counts.Deferred++; return; }
                    if (action == "cloud")
                    {
                        SetLocal(item.Id, item.Path, (JsonObject)current["data"], Int(current["revision"]));
                        Counts.ReceivedUpdated++;
                        return;
                    }
                    // Local or merged version: send it on top of the cloud revision just reviewed.
                    record = result;
                    baseRevision = Int(current["revision"]);
                    continue;
                }
                if (r.Status == 410)
                {
                    JsonObject current = o["current"] as JsonObject ?? new JsonObject();
                    int choice = ui.DeletedInCloud(Title(item), "Запись удалена в облаке: " + current.Text("updatedBy") + ", " + Fmt.Date(current.Text("updatedAt")) +
                        ".\nЛокальная копия изменена после последней синхронизации.");
                    if (choice == 0) { restore = true; continue; }
                    if (choice == 1) { RemoveLocal(item.Id); Counts.Deleted++; return; }
                    Counts.Deferred++;
                    return;
                }
                if (r.Status == 400 || r.Status == 413)
                {
                    Counts.Errors.Add(item.File + ": " + r.ErrorMessage);
                    return;
                }
                throw new IOException(item.File + ": " + r.ErrorMessage);
            }
        }

        // Downloads cloud changes since the saved cursor. Locally changed records that were not sent stay as they are.
        void Receive(bool full)
        {
            long since = full ? 0 : state.Cursor;
            bool more;
            do
            {
                CloudClient.Response r = client.Request("GET", client.ApiUrl + "/devices?since=" + since + "&limit=" + PageSize, null);
                if (r.Status != 200) throw new IOException(r.ErrorMessage);
                JsonObject o = r.Object;
                IList devices = o["devices"] as IList ?? new List<object>();
                foreach (object d in devices)
                {
                    JsonObject device = (JsonObject)d;
                    string id = device.Text("id");
                    if (skipped.ContainsKey(id)) continue;
                    Item item;
                    local.TryGetValue(id, out item);
                    CloudState.Entry entry;
                    state.Records.TryGetValue(id, out entry);
                    bool locallyChanged = item != null && (entry == null || entry.Hash != item.Hash);
                    if (device["deleted"] is bool && (bool)device["deleted"])
                    {
                        if (item == null) state.Records.Remove(id);
                        else if (!locallyChanged) { RemoveLocal(id); Counts.Deleted++; }
                        continue;
                    }
                    if (locallyChanged) continue;
                    JsonObject data = (JsonObject)device["data"];
                    string path;
                    if (item != null) path = item.Path;
                    else
                    {
                        string fileName = Records.SafeFileName(id);
                        path = Path.Combine(dataPath, fileName);
                        if (File.Exists(path))
                        {
                            // Usually an old copy of the same computer uploaded under its file name: say which records collide.
                            Item occupant = null;
                            foreach (Item other in local.Values) if (string.Equals(other.Path, path, StringComparison.OrdinalIgnoreCase)) occupant = other;
                            string cloudHost = data.Text("hostname") ?? "";
                            string mine = occupant == null ? "" : " (запись " + occupant.Id + ", " + (occupant.Record.Text("hostname") ?? "") + ")";
                            bool sameComputer = occupant != null && !string.IsNullOrEmpty(data.Text("serial")) &&
                                string.Equals(data.Text("serial"), occupant.Record.Text("serial"), StringComparison.OrdinalIgnoreCase);
                            Counts.Errors.Add(fileName + ": файл занят" + mine + ", запись " + id + (cloudHost.Length > 0 ? " (" + cloudHost + ")" : "") + " из облака не загружена." +
                                (sameComputer ? " У них один серийный номер — похоже на дубликат одного компьютера: удалите лишнюю запись в веб-интерфейсе." : ""));
                            continue;
                        }
                        // The saved file must be recognized as the same record on the next run.
                        if (Records.CloudId(data, fileName) != id)
                        {
                            Counts.Errors.Add("Запись " + id + " из облака не загружена: без computerId её нельзя сохранить в файл с тем же идентификатором.");
                            continue;
                        }
                    }
                    bool isNew = item == null;
                    if (isNew || Json.Hash(data) != item.Hash)
                    {
                        SetLocal(id, path, data, Int(device["revision"]));
                        if (isNew) Counts.ReceivedNew++; else Counts.ReceivedUpdated++;
                    }
                    else
                    {
                        CloudState.Entry e = new CloudState.Entry();
                        e.File = item.File; e.Revision = Int(device["revision"]); e.Hash = item.Hash;
                        state.Records[id] = e;
                    }
                }
                since = Convert.ToInt64(o["cursor"]);
                state.Cursor = since;
                state.Save(root);
                more = o["hasMore"] is bool && (bool)o["hasMore"];
            } while (more);
        }
    }

    // New versions of the program from the cloud (Invoke-ProgramUpdate / Install-ProgramRelease).
    public static class ProgramUpdate
    {
        public const string ExeName = "comp-info.exe";

        // Files that make up the program; must match isProgramFile in cloud/src/releases.ts.
        public static bool IsProgramFile(string name)
        {
            return name == "cloud-config.json" || string.Equals(name, ExeName, StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(name, "^[^\\\\/:*?\"<>|\\x00-\\x1f]+\\.(ps1|bat|cmd)$", RegexOptions.IgnoreCase);
        }

        public static List<string> ProgramFiles(string root)
        {
            List<string> result = new List<string>();
            foreach (string path in Directory.GetFiles(root))
                if (IsProgramFile(Path.GetFileName(path))) result.Add(path);
            return result;
        }

        public static Version Parse(string text)
        {
            Version v;
            return Version.TryParse(text ?? "", out v) ? v : null;
        }

        public static bool IsNewer(string candidate, string current)
        {
            Version a = Parse(candidate), b = Parse(current);
            return a != null && b != null && a > b;
        }

        public static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream s = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        public class Result
        {
            public int Installed;
            public List<string> Removed = new List<string>();
            public string Backup;
        }

        // Moves a file over target; the running program file is renamed to *.old first (Windows allows that).
        static void Place(string temporary, string target)
        {
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(Fmt.ExePath), StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                string old = target + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(target, old);
                File.Move(temporary, target);
                return;
            }
            Files.MoveIntoPlace(temporary, target, 10);
        }

        // Installs a downloaded, already checksum-verified archive. Nothing in root changes unless every file
        // matches manifest.json; on a failure while replacing files the previous version is restored.
        public static Result Install(string root, string zipPath, string expectedVersion, string currentVersion)
        {
            string updateRoot = Path.Combine(root, ".update");
            string extract = Path.Combine(updateRoot, "extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(updateRoot);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, extract);
                string manifestPath = Path.Combine(extract, "manifest.json");
                if (!File.Exists(manifestPath)) throw new InvalidDataException("В архиве нет manifest.json.");
                JsonObject manifest = Files.ReadRecord(manifestPath);
                if (manifest.Text("version") != expectedVersion) throw new InvalidDataException("Архив содержит версию " + manifest.Text("version") + ", ожидалась " + expectedVersion + ".");
                IList files = manifest["files"] as IList;
                if (files == null || files.Count == 0) throw new InvalidDataException("В manifest.json нет файлов.");
                List<string> names = new List<string>();
                foreach (object f in files)
                {
                    JsonObject file = (JsonObject)f;
                    string name = file.Text("path") ?? "";
                    string source = Path.Combine(extract, name);
                    if (!IsProgramFile(name)) throw new InvalidDataException("Недопустимый файл в архиве: " + name);
                    if (!File.Exists(source)) throw new InvalidDataException("В архиве нет файла " + name + ".");
                    if (new FileInfo(source).Length != Convert.ToInt64(file["size"]) || Sha256(source) != (file.Text("sha256") ?? "").ToLowerInvariant())
                        throw new InvalidDataException("Файл " + name + " в архиве не совпадает с manifest.json.");
                    names.Add(name);
                }
                foreach (string entry in Directory.GetFiles(extract, "*", SearchOption.AllDirectories))
                {
                    string relative = entry.Substring(extract.Length + 1);
                    if (relative != "manifest.json" && !names.Contains(relative)) throw new InvalidDataException("Лишний файл в архиве: " + relative);
                }

                string backup = Path.Combine(updateRoot, "backup-" + currentVersion + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(backup);
                foreach (string existing in ProgramFiles(root)) File.Copy(existing, Path.Combine(backup, Path.GetFileName(existing)));

                Result result = new Result();
                result.Backup = backup;
                try
                {
                    foreach (object f in files)
                    {
                        JsonObject file = (JsonObject)f;
                        string name = file.Text("path");
                        string target = Path.Combine(root, name);
                        string temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
                        File.Copy(Path.Combine(extract, name), temporary);
                        Place(temporary, target);
                        if (Sha256(target) != file.Text("sha256").ToLowerInvariant()) throw new IOException("Файл " + name + " записан с ошибкой.");
                    }
                    // The window program itself is never removed, even by a release without it.
                    foreach (string old in ProgramFiles(root))
                    {
                        string name = Path.GetFileName(old);
                        if (names.Contains(name) || string.Equals(name, ExeName, StringComparison.OrdinalIgnoreCase)) continue;
                        File.Delete(old);
                        result.Removed.Add(name);
                    }
                }
                catch (Exception e)
                {
                    try { RestoreBackup(root, backup, names); }
                    catch (Exception restoreError)
                    {
                        throw new IOException("Обновление не удалось (" + e.Message + "), и прежнюю версию восстановить не получилось: " + restoreError.Message + ". Файлы прежней версии лежат в " + backup);
                    }
                    throw new IOException("Обновление не удалось, прежняя версия восстановлена: " + e.Message);
                }
                result.Installed = files.Count;
                RemoveOldFiles(updateRoot, 2);
                return result;
            }
            finally
            {
                try { if (Directory.Exists(extract)) Directory.Delete(extract, true); } catch (Exception) { }
            }
        }

        // Puts the backed-up program files back and removes files the failed update added.
        static void RestoreBackup(string root, string backup, List<string> newNames)
        {
            List<string> backupNames = new List<string>();
            foreach (string f in Directory.GetFiles(backup)) backupNames.Add(Path.GetFileName(f));
            foreach (string name in newNames)
            {
                string path = Path.Combine(root, name);
                if (!backupNames.Contains(name) && File.Exists(path)) File.Delete(path);
            }
            foreach (string file in Directory.GetFiles(backup))
            {
                string target = Path.Combine(root, Path.GetFileName(file));
                // A file the update never managed to replace is already the old one.
                if (File.Exists(target) && Sha256(target) == Sha256(file)) continue;
                string temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tmp");
                File.Copy(file, temporary);
                Place(temporary, target);
            }
        }

        // Keeps the most recent backups and downloaded archives in .update.
        static void RemoveOldFiles(string updateRoot, int keep)
        {
            List<DirectoryInfo> backups = new List<DirectoryInfo>(new DirectoryInfo(updateRoot).GetDirectories("backup-*"));
            backups.Sort(delegate (DirectoryInfo a, DirectoryInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
            for (int i = keep; i < backups.Count; i++) { try { backups[i].Delete(true); } catch (Exception) { } }
            List<FileInfo> zips = new List<FileInfo>(new DirectoryInfo(updateRoot).GetFiles("*.zip"));
            zips.Sort(delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
            for (int i = keep; i < zips.Count; i++) { try { zips[i].Delete(); } catch (Exception) { } }
        }

        // The previous program file left by an update, removed on the next start.
        public static void RemoveLeftovers()
        {
            try
            {
                string old = Fmt.ExePath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch (Exception) { }
        }
    }
}
