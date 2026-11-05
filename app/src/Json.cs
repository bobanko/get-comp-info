using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CompInfo
{
    // JSON object that keeps the order of its fields, like PowerShell's [pscustomobject].
    public class JsonObject
    {
        readonly List<string> names = new List<string>();
        readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);

        public IList<string> Names { get { return names.AsReadOnly(); } }
        public int Count { get { return names.Count; } }
        public bool Has(string name) { return values.ContainsKey(name); }

        public object this[string name]
        {
            get { object v; return values.TryGetValue(name, out v) ? v : null; }
            set { if (!values.ContainsKey(name)) names.Add(name); values[name] = value; }
        }

        public bool Remove(string name)
        {
            if (!values.Remove(name)) return false;
            names.Remove(name);
            return true;
        }

        // Value as text: null for a missing field or null, otherwise the JSON scalar as PowerShell prints it.
        public string Text(string name)
        {
            object v = this[name];
            if (v == null) return null;
            if (v is string) return (string)v;
            if (v is bool) return (bool)v ? "True" : "False";
            if (v is JsonObject || v is IList) return Json.Compact(v);
            return Convert.ToString(v, CultureInfo.CurrentCulture);
        }

        public JsonObject Clone() { return (JsonObject)Json.Parse(Json.Compact(this)); }
    }

    // Reading and writing JSON exactly as Windows PowerShell 5.1 does (ConvertFrom-Json / ConvertTo-Json):
    // both use JavaScriptSerializer, so numbers, escapes and record hashes match the console version.
    public static class Json
    {
        static JavaScriptSerializer NewSerializer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 512;
            return s;
        }

        // Objects become JsonObject, arrays List<object>, numbers int/long/decimal/double.
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException(L.T("Пустой JSON."));
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            return Convert(NewSerializer().DeserializeObject(text));
        }

        public static JsonObject ParseObject(string text)
        {
            JsonObject o = Parse(text) as JsonObject;
            if (o == null) throw new FormatException(L.T("Ожидался JSON-объект."));
            return o;
        }

        static object Convert(object v)
        {
            IDictionary<string, object> map = v as IDictionary<string, object>;
            if (map != null)
            {
                JsonObject o = new JsonObject();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, object> p in map)
                {
                    // PowerShell refuses such JSON too: its property names ignore case.
                    if (!seen.Add(p.Key)) throw new FormatException(L.T("В JSON повторяются поля, различающиеся только регистром: ") + p.Key);
                    o[p.Key] = Convert(p.Value);
                }
                return o;
            }
            object[] array = v as object[];
            if (array != null)
            {
                List<object> list = new List<object>(array.Length);
                foreach (object item in array) list.Add(Convert(item));
                return list;
            }
            return v;
        }

        // One scalar as JavaScriptSerializer writes it (strings escaped like PowerShell: < for <, etc.).
        public static string Scalar(object v)
        {
            if (v == null) return "null";
            return NewSerializer().Serialize(v);
        }

        public static string Compact(object v)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, v, -1, 0);
            return sb.ToString();
        }

        public static string Pretty(object v)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, v, 2, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int indent, int level)
        {
            JsonObject o = v as JsonObject;
            if (o != null)
            {
                if (o.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                for (int i = 0; i < o.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, indent, level + 1);
                    sb.Append(Scalar(o.Names[i])).Append(indent < 0 ? ":" : ": ");
                    Write(sb, o[o.Names[i]], indent, level + 1);
                }
                NewLine(sb, indent, level);
                sb.Append('}');
                return;
            }
            IList list = v as IList;
            if (list != null && !(v is string))
            {
                if (list.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, indent, level + 1);
                    Write(sb, list[i], indent, level + 1);
                }
                NewLine(sb, indent, level);
                sb.Append(']');
                return;
            }
            sb.Append(Scalar(v));
        }

        static void NewLine(StringBuilder sb, int indent, int level)
        {
            if (indent < 0) return;
            sb.Append("\r\n").Append(' ', indent * level);
        }

        // Get-CanonicalJson of sync-merge.ps1: fields sorted like Sort-Object -CaseSensitive (current culture).
        public static string Canonical(object v)
        {
            StringBuilder sb = new StringBuilder();
            WriteCanonical(sb, v, StringComparer.Create(CultureInfo.CurrentCulture, false));
            return sb.ToString();
        }

        static void WriteCanonical(StringBuilder sb, object v, StringComparer order)
        {
            JsonObject o = v as JsonObject;
            if (o != null)
            {
                List<string> names = new List<string>(o.Names);
                names.Sort(order);
                sb.Append('{');
                for (int i = 0; i < names.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Scalar(names[i])).Append(':');
                    WriteCanonical(sb, o[names[i]], order);
                }
                sb.Append('}');
                return;
            }
            IList list = v as IList;
            if (list != null && !(v is string))
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteCanonical(sb, list[i], order);
                }
                sb.Append(']');
                return;
            }
            sb.Append(Scalar(v));
        }

        // Get-RecordHash: SHA-256 of the canonical JSON, upper-case hex.
        public static string Hash(object v)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(v)));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }
    }
}
