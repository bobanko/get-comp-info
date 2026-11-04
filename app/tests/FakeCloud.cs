// A tiny in-process copy of the cloud API (/api/client) for the synchronization checks:
// the same statuses and fields as cloud/src, without a network or Cloudflare.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace CompInfo
{
    public class FakeCloud : IDisposable
    {
        public class Device
        {
            public string Id;
            public int Revision;
            public long Seq;
            public bool Deleted;
            public JsonObject Data;
            public string UpdatedBy = "web@example";
        }

        readonly TcpListener listener;
        readonly Thread thread;
        public readonly Dictionary<string, Device> Devices = new Dictionary<string, Device>(StringComparer.Ordinal);
        long seq;
        public int Puts;
        public string ApiUrl;
        public const string Key = "test-key";

        public FakeCloud()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            ApiUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/api/client";
            thread = new Thread(Serve);
            thread.IsBackground = true;
            thread.Start();
        }

        // Change made "in the web interface".
        public Device Put(string id, JsonObject data, bool deleted)
        {
            lock (Devices)
            {
                Device d;
                if (!Devices.TryGetValue(id, out d)) { d = new Device(); d.Id = id; Devices[id] = d; }
                d.Revision++;
                d.Seq = ++seq;
                d.Deleted = deleted;
                if (data != null) d.Data = data;
                return d;
            }
        }

        void Serve()
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch (Exception) { return; }
                using (client)
                {
                    try { Handle(client.GetStream()); } catch (Exception) { }
                }
            }
        }

        static string ReadLine(Stream s)
        {
            StringBuilder sb = new StringBuilder();
            int b;
            while ((b = s.ReadByte()) >= 0)
            {
                if (b == '\n') break;
                if (b != '\r') sb.Append((char)b);
            }
            return sb.ToString();
        }

        void Handle(NetworkStream s)
        {
            string[] request = ReadLine(s).Split(' ');
            int length = 0;
            string auth = null;
            string line;
            while ((line = ReadLine(s)).Length > 0)
            {
                int colon = line.IndexOf(':');
                string name = line.Substring(0, colon).Trim().ToLowerInvariant(), value = line.Substring(colon + 1).Trim();
                if (name == "content-length") length = int.Parse(value);
                if (name == "authorization") auth = value;
            }
            byte[] body = new byte[length];
            for (int read = 0; read < length;) read += s.Read(body, read, length - read);
            int status;
            object answer;
            if (auth != "Bearer " + Key) { status = 401; answer = Error("Ключ не подошёл."); }
            else answer = Route(request[0], request[1], Encoding.UTF8.GetString(body), out status);
            byte[] bytes = Encoding.UTF8.GetBytes(answer == null ? "" : Json.Compact(answer));
            string head = "HTTP/1.1 " + status + " X\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n";
            byte[] h = Encoding.ASCII.GetBytes(head);
            s.Write(h, 0, h.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        static JsonObject Error(string message)
        {
            JsonObject e = new JsonObject();
            e["message"] = message;
            JsonObject o = new JsonObject();
            o["error"] = e;
            return o;
        }

        JsonObject DeviceJson(Device d, bool withId)
        {
            JsonObject o = new JsonObject();
            if (withId) o["id"] = d.Id;
            o["revision"] = d.Revision;
            o["deleted"] = d.Deleted;
            o["data"] = d.Data;
            o["updatedBy"] = d.UpdatedBy;
            o["updatedAt"] = "2026-11-04T10:00:00Z";
            return o;
        }

        object Route(string method, string target, string body, out int status)
        {
            status = 200;
            string path = target.Split('?')[0];
            string query = target.Contains("?") ? target.Substring(target.IndexOf('?') + 1) : "";
            if (path == "/api/client/me") { JsonObject me = new JsonObject(); me["name"] = "Тест"; me["email"] = "t@example"; me["role"] = "harvester"; return me; }
            if (path == "/api/client/settings/network" || path == "/api/client/program/latest") { status = 404; return Error("Нет."); }
            lock (Devices)
            {
                if (path == "/api/client/devices" && method == "GET")
                {
                    Dictionary<string, string> q = new Dictionary<string, string>();
                    foreach (string p in query.Split('&')) { string[] kv = p.Split('='); q[kv[0]] = kv.Length > 1 ? kv[1] : ""; }
                    long since = long.Parse(q["since"]);
                    int limit = int.Parse(q["limit"]);
                    List<Device> changed = new List<Device>();
                    foreach (Device d in Devices.Values) if (d.Seq > since) changed.Add(d);
                    changed.Sort(delegate (Device a, Device b) { return a.Seq.CompareTo(b.Seq); });
                    List<object> page = new List<object>();
                    long cursor = since;
                    for (int i = 0; i < changed.Count && i < limit; i++) { page.Add(DeviceJson(changed[i], true)); cursor = changed[i].Seq; }
                    JsonObject o = new JsonObject();
                    o["devices"] = page;
                    o["cursor"] = cursor;
                    o["hasMore"] = changed.Count > limit;
                    return o;
                }
                if (path.StartsWith("/api/client/devices/") && method == "PUT")
                {
                    Puts++;
                    string id = Uri.UnescapeDataString(path.Substring("/api/client/devices/".Length));
                    JsonObject request = Json.ParseObject(body);
                    JsonObject data = (JsonObject)request["data"];
                    Device d;
                    Devices.TryGetValue(id, out d);
                    JsonObject result = new JsonObject();
                    if (d == null)
                    {
                        d = Put(id, data, false);
                        d.UpdatedBy = "t@example";
                        result["changed"] = true;
                    }
                    else
                    {
                        bool restore = request["restore"] is bool && (bool)request["restore"];
                        int baseRevision = request["baseRevision"] == null ? -1 : Convert.ToInt32(request["baseRevision"]);
                        if (d.Deleted && !restore) { status = 410; JsonObject g = new JsonObject(); g["current"] = DeviceJson(d, false); return g; }
                        if (!d.Deleted && baseRevision != d.Revision) { status = 409; JsonObject c = new JsonObject(); c["current"] = DeviceJson(d, false); return c; }
                        bool same = !d.Deleted && Json.Canonical(d.Data) == Json.Canonical(data);
                        if (!same) { Put(id, data, false); d.UpdatedBy = "t@example"; }
                        result["changed"] = !same;
                    }
                    result["device"] = DeviceJson(d, true);
                    return result;
                }
            }
            status = 404;
            return Error("Нет такого пути: " + path);
        }

        public void Dispose() { listener.Stop(); }
    }

    // Answers of the user in the synchronization checks.
    public class ScriptedQuestions : ISyncQuestions
    {
        public string ConflictAnswer = "merged";
        public List<string> CloudFields = new List<string>();
        public int DeletedAnswer = 2;
        public int Conflicts, Deletions;

        public void Progress(string text) { }

        public string ResolveConflict(string title, JsonObject local, JsonObject cloud, out JsonObject result)
        {
            Conflicts++;
            result = ConflictAnswer == "cloud" ? cloud : ConflictAnswer == "local" ? local : RecordDiff.Merge(local, cloud, CloudFields);
            return ConflictAnswer;
        }

        public int DeletedInCloud(string title, string details) { Deletions++; return DeletedAnswer; }
    }
}
