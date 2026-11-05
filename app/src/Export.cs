using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CompInfo
{
    // CSV of all local records, in the format of the console export (export-comp-info.ps1 / ConvertTo-Csv).
    public static class CsvExport
    {
        public static string DefaultPath()
        {
            return Path.Combine(AppPaths.Exports, "devices_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture) + ".csv");
        }

        // Returns the number of exported devices. Reads everything first: an invalid record never produces a partial file.
        public static int Write(string dataPath, string outputPath, CloudState state, char delimiter)
        {
            if (!Directory.Exists(dataPath)) throw new DirectoryNotFoundException(L.T("Папка с данными не найдена: ") + dataPath);
            string[] files = Directory.GetFiles(dataPath, "*.json");
            if (files.Length == 0) throw new FileNotFoundException(L.T("В папке нет JSON-файлов: ") + dataPath);
            Array.Sort(files, StringComparer.Create(CultureInfo.CurrentCulture, true));

            List<JsonObject> records = new List<JsonObject>();
            foreach (string file in files)
            {
                RecordFile r;
                try { r = Records.Load(file, state); }
                catch (Exception e) { throw new InvalidDataException(Path.GetFileName(file) + ": " + e.Message); }
                JsonObject row = r.Data.Clone();
                foreach (string name in Records.DeprecatedFields) row.Remove(name);
                if (!row.Has("esetInstalled")) row["esetInstalled"] = null;
                row["cloudStatus"] = r.CloudStatus;
                records.Add(row);
            }

            // Union of fields keeps optional properties of later files.
            List<string> columns = new List<string>();
            foreach (JsonObject r in records)
                foreach (string name in r.Names)
                    if (!columns.Contains(name)) columns.Add(name);

            StringComparer text = StringComparer.Create(CultureInfo.CurrentCulture, true);
            List<JsonObject> sorted = new List<JsonObject>(records);
            StableSort(sorted, delegate (JsonObject a, JsonObject b)
            {
                int c = CompareText(text, a.Text("hostname"), b.Text("hostname"));
                return c != 0 ? c : CompareText(text, a.Text("serial"), b.Text("serial"));
            });

            StringBuilder csv = new StringBuilder();
            AppendLine(csv, columns, delimiter);
            foreach (JsonObject r in sorted)
            {
                List<string> cells = new List<string>();
                foreach (string name in columns) cells.Add(Cell(r[name]));
                AppendRow(csv, cells, delimiter);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            // CreateNew protects an existing export, including when two launches overlap.
            using (FileStream stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(true)))
                writer.Write(csv.ToString());
            return records.Count;
        }

        static int CompareText(StringComparer comparer, string a, string b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            return comparer.Compare(a, b);
        }

        static void StableSort(List<JsonObject> list, Comparison<JsonObject> compare)
        {
            List<KeyValuePair<int, JsonObject>> indexed = new List<KeyValuePair<int, JsonObject>>();
            for (int i = 0; i < list.Count; i++) indexed.Add(new KeyValuePair<int, JsonObject>(i, list[i]));
            indexed.Sort(delegate (KeyValuePair<int, JsonObject> a, KeyValuePair<int, JsonObject> b)
            {
                int c = compare(a.Value, b.Value);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            for (int i = 0; i < list.Count; i++) list[i] = indexed[i].Value;
        }

        // null stays an empty cell; nested values become compact JSON, like the console export.
        static string Cell(object value)
        {
            if (value == null) return null;
            if (value is JsonObject || (value is IList && !(value is string))) return Json.Compact(value);
            if (value is bool) return (bool)value ? "True" : "False";
            return Convert.ToString(value, CultureInfo.CurrentCulture);
        }

        static void AppendLine(StringBuilder csv, List<string> header, char delimiter)
        {
            AppendRow(csv, header, delimiter);
        }

        static void AppendRow(StringBuilder csv, List<string> cells, char delimiter)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) csv.Append(delimiter);
                if (cells[i] != null) csv.Append('"').Append(cells[i].Replace("\"", "\"\"")).Append('"');
            }
            csv.Append("\r\n");
        }
    }
}
