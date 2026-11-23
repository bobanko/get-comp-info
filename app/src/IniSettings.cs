using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CompInfo
{
    // Shared by the standalone programs (SUSpector, youSBee): no window code here.
    // UI preferences of a standalone program, kept next to the executable.
    public class IniSettings
    {
        readonly string fileName, header;
        readonly string[] keys;

        public IniSettings(string fileName, string header, params string[] keys)
        {
            this.fileName = fileName; this.header = header; this.keys = keys;
        }

        public string FileName { get { return fileName; } }

        string path;
        string Path { get { if (path == null) path = Locate(); return path; } }

        // Another file of the program kept where the ini is (SUSpector's copy of the cloud trigger words).
        public string Beside(string name)
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), name);
        }

        // On a flash drive the ini stays next to the exe, so the settings travel with it and nothing is left on
        // the computers it runs on. Anywhere else (desktop, Downloads) it goes to %APPDATA%\CHAGEAR, so no extra
        // file appears beside the program; an ini left there by an older version is moved over once.
        string Locate()
        {
            string beside = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            try
            {
                if (new DriveInfo(System.IO.Path.GetPathRoot(beside)).DriveType == DriveType.Removable) return beside;
            }
            catch (Exception) { }   // a network path has no drive letter: treat it as a fixed disk
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData)) return beside;
            string profile = System.IO.Path.Combine(System.IO.Path.Combine(appData, "CHAGEAR"), fileName);
            try
            {
                if (File.Exists(beside))
                {
                    if (File.Exists(profile)) File.Delete(beside);
                    else
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(profile));
                        File.Move(beside, profile);
                    }
                }
            }
            catch (Exception) { if (!File.Exists(profile)) return beside; }   // could not move: keep reading the old ini
            return profile;
        }

        Dictionary<string, string> Read()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(Path)) return values;
                foreach (string raw in File.ReadAllLines(Path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int equals = line.IndexOf('=');
                    if (equals > 0) values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                }
            }
            catch (Exception) { }
            return values;
        }

        public string Get(string name, string fallback)
        {
            string value;
            return Read().TryGetValue(name, out value) && value.Length > 0 ? value : fallback;
        }

        public void Set(string name, string value)
        {
            try
            {
                Dictionary<string, string> values = Read();
                values[name] = value;
                StringBuilder text = new StringBuilder("; " + header + "\r\n[settings]\r\n");
                foreach (string key in keys)
                    if (values.ContainsKey(key)) text.Append(key).Append('=').Append(values[key]).Append("\r\n");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, text.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }   // a read-only flash drive simply keeps the defaults
        }
    }
}
