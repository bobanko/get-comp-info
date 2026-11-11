using System;
using System.Collections.Generic;
using System.Management;
using System.Text;

namespace CompInfo
{
    // Thin WMI wrapper: rows as property dictionaries, so callers do not touch ManagementObject.
    public static class Wmi
    {
        public const string Cimv2 = @"root\cimv2";
        public const string Net = @"root\StandardCimv2";
        public const string Storage = @"root\Microsoft\Windows\Storage";

        public static List<Dictionary<string, object>> Query(string scope, string wql)
        {
            List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql)))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementBaseObject item in items)
                {
                    using (item)
                    {
                        Dictionary<string, object> row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (PropertyData p in item.Properties) row[p.Name] = p.Value;
                        rows.Add(row);
                    }
                }
            }
            return rows;
        }

        public static Dictionary<string, object> First(string scope, string wql)
        {
            List<Dictionary<string, object>> rows = Query(scope, wql);
            if (rows.Count == 0) throw new InvalidOperationException(L.T("WMI не вернул данные: ") + wql);
            return rows[0];
        }

        public static string Str(Dictionary<string, object> row, string name)
        {
            object v;
            return row.TryGetValue(name, out v) && v != null ? Convert.ToString(v) : null;
        }

        public static long Num(Dictionary<string, object> row, string name)
        {
            object v;
            return row.TryGetValue(name, out v) && v != null ? Convert.ToInt64(v) : 0;
        }

        public static bool Bool(Dictionary<string, object> row, string name)
        {
            object v;
            return row.TryGetValue(name, out v) && v is bool && (bool)v;
        }

        public static string[] Strings(Dictionary<string, object> row, string name)
        {
            object v;
            return row.TryGetValue(name, out v) && v is string[] ? (string[])v : new string[0];
        }
    }

    // Network adapter as Get-NetAdapter shows it.
    public class Adapter
    {
        public int Index;
        public string Name;
        public string Description;
        public string Status;        // Up, Disabled, Disconnected, ...
        public string Mac;           // 00-11-22-33-44-55
        public bool Hardware;
        public bool Wired;           // NdisMedium 0 = 802.3

        public static List<Adapter> List()
        {
            List<Adapter> result = new List<Adapter>();
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Net, "SELECT * FROM MSFT_NetAdapter"))
            {
                if (Wmi.Bool(row, "Hidden")) continue;   // Get-NetAdapter hides them too
                Adapter a = new Adapter();
                a.Index = (int)Wmi.Num(row, "InterfaceIndex");
                a.Name = Wmi.Str(row, "Name") ?? "";
                a.Description = Wmi.Str(row, "InterfaceDescription") ?? "";
                a.Status = StatusOf(row);
                string[] addresses = Wmi.Strings(row, "NetworkAddresses");
                a.Mac = addresses.Length > 0 ? FormatMac(addresses[0]) : "";
                a.Hardware = Wmi.Bool(row, "HardwareInterface");
                a.Wired = Wmi.Num(row, "NdisMedium") == 0;
                result.Add(a);
            }
            return result;
        }

        static string StatusOf(Dictionary<string, object> row)
        {
            switch (Wmi.Num(row, "InterfaceOperationalStatus"))
            {
                case 1: return "Up";
                case 3: return "Testing";
                case 4: return "Unknown";
                case 5: return "Dormant";
                case 6: return "Not Present";
                case 7: return "Lower Layer Down";
                case 2:
                    if (Wmi.Num(row, "InterfaceAdminStatus") == 2) return "Disabled";
                    List<string> parts = new List<string>();
                    if (Wmi.Bool(row, "OperationalStatusDownDefaultPortNotAuthenticated")) parts.Add("Not Authenticated");
                    if (Wmi.Bool(row, "OperationalStatusDownMediaDisconnected")) parts.Add("Disconnected");
                    if (Wmi.Bool(row, "OperationalStatusDownLowPowerState")) parts.Add("Low Power State");
                    return string.Join(", ", parts);
                default: return "";
            }
        }

        static string FormatMac(string raw)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                if (i > 0 && i % 2 == 0) sb.Append('-');
                sb.Append(raw[i]);
            }
            return sb.ToString();
        }
    }

    // Information collected from this computer (get-comp-info.ps1).
    public class ComputerSnapshot
    {
        public string Hostname, Serial, SmbiosUuid, Manufacturer, Model, Windows, Domain, Ip, Mac, Username, InstallationId;
        public bool PartOfDomain;
        public bool? EsetInstalled;

        public static ComputerSnapshot Collect()
        {
            ComputerSnapshot s = new ComputerSnapshot();
            Dictionary<string, object> cs = Wmi.First(Wmi.Cimv2, "SELECT Name, Manufacturer, Model, PartOfDomain, Domain FROM Win32_ComputerSystem");
            Dictionary<string, object> bios = Wmi.First(Wmi.Cimv2, "SELECT SerialNumber FROM Win32_BIOS");
            Dictionary<string, object> product = Wmi.First(Wmi.Cimv2, "SELECT UUID FROM Win32_ComputerSystemProduct");
            Dictionary<string, object> os = Wmi.First(Wmi.Cimv2, "SELECT Caption FROM Win32_OperatingSystem");
            s.Hostname = Wmi.Str(cs, "Name") ?? "";
            s.Serial = (Wmi.Str(bios, "SerialNumber") ?? "").Trim();
            s.SmbiosUuid = Identity.NormalizeUuid(Wmi.Str(product, "UUID"));
            s.Manufacturer = (Wmi.Str(cs, "Manufacturer") ?? "").Trim();
            s.Model = (Wmi.Str(cs, "Model") ?? "").Trim();
            s.PartOfDomain = Wmi.Bool(cs, "PartOfDomain");
            s.Domain = Wmi.Str(cs, "Domain") ?? "";
            s.Windows = Wmi.Str(os, "Caption");
            s.EsetInstalled = CheckEset();
            s.InstallationId = Identity.WindowsInstallationId();
            s.Ip = MainIpAddress();
            s.Mac = null;
            foreach (Adapter a in Adapter.List())
            {
                if (a.Status == "Up" && a.Mac.Length > 0) { s.Mac = a.Mac; break; }
            }
            s.Username = Environment.UserName;
            return s;
        }

        // Installed antivirus service, running or not; the ESET Management Agent alone does not count.
        public static bool CheckEset()
        {
            try { return Wmi.Query(Wmi.Cimv2, "SELECT Name FROM Win32_Service WHERE Name='ekrn'").Count > 0; }
            catch (Exception e) { throw new InvalidOperationException(L.T("Проверка ESET не удалась: ") + e.Message, e); }
        }

        public static string EsetText(object value)
        {
            if (value == null) return L.T("Не проверено");
            if (value is bool && (bool)value) return L.T("Установлен");
            return L.T("Не установлен");
        }

        // First IPv4 address that is not link-local and not on a loopback, virtual or VPN interface.
        // A working (Preferred) address wins over one on a disconnected adapter.
        public static string MainIpAddress()
        {
            string fallback = null;
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Net, "SELECT IPAddress, InterfaceAlias, AddressState FROM MSFT_NetIPAddress WHERE AddressFamily=2"))
            {
                string ip = Wmi.Str(row, "IPAddress") ?? "";
                string alias = Wmi.Str(row, "InterfaceAlias") ?? "";
                if (ip.Length == 0 || ip.StartsWith("169.") || ip.StartsWith("127.") ||
                    System.Text.RegularExpressions.Regex.IsMatch(alias, "Loopback|vEthernet|VPN")) continue;
                if (Wmi.Num(row, "AddressState") == 4) return ip;
                if (fallback == null) fallback = ip;
            }
            return fallback;
        }
    }

    // How the viewer recognises the record of the computer it runs on.
    public class CurrentComputer
    {
        public string Uuid = "", Serial = "", Hostname = "";

        public static CurrentComputer Read()
        {
            CurrentComputer c = new CurrentComputer();
            try { c.Uuid = Identity.NormalizeUuid(Wmi.Str(Wmi.First(Wmi.Cimv2, "SELECT UUID FROM Win32_ComputerSystemProduct"), "UUID")) ?? ""; } catch (Exception) { }
            try
            {
                string serial = Wmi.Str(Wmi.First(Wmi.Cimv2, "SELECT SerialNumber FROM Win32_BIOS"), "SerialNumber");
                c.Serial = Identity.UsableSerial(serial) ? serial.Trim() : "";
            }
            catch (Exception) { }
            c.Hostname = (Environment.MachineName ?? "").Trim();
            return c;
        }

        public bool Matches(JsonObject data)
        {
            string uuid = Identity.NormalizeUuid(data["smbiosUuid"]) ?? "";
            if (uuid.Length == 0 && data.Text("computerIdSource") == "smbiosUuid") uuid = Identity.NormalizeUuid(data["computerId"]) ?? "";
            if (uuid.Length > 0 && Uuid.Length > 0) return uuid == Uuid;

            string serial = Identity.UsableSerial(data.Text("serial")) ? data.Text("serial").Trim() : "";
            if (serial.Length == 0 && data.Text("computerIdSource") == "serial" && Identity.UsableSerial(data.Text("computerId"))) serial = data.Text("computerId").Trim();
            if (serial.Length > 0 && Serial.Length > 0) return string.Equals(serial, Serial, StringComparison.OrdinalIgnoreCase);

            string hostname = (data.Text("hostname") ?? "").Trim();
            return hostname.Length > 0 && string.Equals(hostname, Hostname, StringComparison.OrdinalIgnoreCase);
        }
    }

    // USB disk with the serial number the device reports (get-usb-serial.ps1).
    public class UsbDisk
    {
        public int Number;
        public string Name;
        public long Size;
        public string Serial;
        public string VolumeName;
        public string DriveLetters;

        public static List<UsbDisk> List()
        {
            Dictionary<long, string> deviceIds = new Dictionary<long, string>();
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Cimv2, "SELECT Index, PNPDeviceID FROM Win32_DiskDrive"))
                deviceIds[Wmi.Num(row, "Index")] = Wmi.Str(row, "PNPDeviceID") ?? "";
            Dictionary<string, string> volumeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Storage, "SELECT DriveLetter, FileSystemLabel FROM MSFT_Volume"))
            {
                string letter = Wmi.Str(row, "DriveLetter");
                if (!string.IsNullOrEmpty(letter)) volumeLabels[letter] = Wmi.Str(row, "FileSystemLabel") ?? "";
            }
            Dictionary<long, List<string>> driveLetters = new Dictionary<long, List<string>>();
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Storage, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
            {
                string letter = Wmi.Str(row, "DriveLetter");
                if (string.IsNullOrEmpty(letter)) continue;
                long number = Wmi.Num(row, "DiskNumber");
                List<string> letters;
                if (!driveLetters.TryGetValue(number, out letters)) { letters = new List<string>(); driveLetters[number] = letters; }
                letters.Add(letter);
            }
            List<UsbDisk> result = new List<UsbDisk>();
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Storage, "SELECT Number, FriendlyName, Size, BusType FROM MSFT_Disk WHERE BusType=7"))
            {
                UsbDisk d = new UsbDisk();
                d.Number = (int)Wmi.Num(row, "Number");
                d.Name = Wmi.Str(row, "FriendlyName") ?? "";
                d.Size = Wmi.Num(row, "Size");
                string id;
                d.Serial = SerialFromDeviceId(deviceIds.TryGetValue(d.Number, out id) ? id : "");
                List<string> letters;
                if (driveLetters.TryGetValue(d.Number, out letters))
                {
                    d.DriveLetters = string.Join(", ", letters.ConvertAll(delegate (string letter) { return letter + ":"; }).ToArray());
                    d.VolumeName = string.Join(", ", letters.ConvertAll(delegate (string letter) { return volumeLabels.ContainsKey(letter) ? volumeLabels[letter].Trim() : ""; }).FindAll(delegate (string label) { return label.Length > 0; }).ToArray());
                }
                else { d.DriveLetters = ""; d.VolumeName = ""; }
                result.Add(d);
            }
            result.Sort(delegate (UsbDisk a, UsbDisk b) { return a.Number.CompareTo(b.Number); });
            return result;
        }

        // USBSTOR's instance holds the USB serial followed by the LUN suffix.
        // Windows-generated instance IDs contain '&' and are not device serials.
        public static string SerialFromDeviceId(string pnpDeviceId)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(pnpDeviceId ?? "", @"^USBSTOR\\[^\\]+\\([^\\]+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            string serial = System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"&\d+$", "");
            return serial.Contains("&") ? "" : serial;
        }
    }
}
