using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CompInfo
{
    // Static IPv4 or DHCP on a wired adapter (setup-lan-adapter.ps1).
    public static class LanSetup
    {
        // Wired hardware adapters: the usual "Ethernet" first, disabled ones last.
        public static List<Adapter> WiredAdapters()
        {
            List<Adapter> result = new List<Adapter>();
            foreach (Adapter a in Adapter.List()) if (a.Hardware && a.Wired) result.Add(a);
            result.Sort(delegate (Adapter x, Adapter y)
            {
                int c = Priority(x).CompareTo(Priority(y));
                if (c == 0) c = (x.Status == "Disabled").CompareTo(y.Status == "Disabled");
                if (c == 0) c = string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
                return c;
            });
            return result;
        }

        static int Priority(Adapter a)
        {
            if (string.Equals(a.Name, "Ethernet", StringComparison.OrdinalIgnoreCase)) return 0;
            return 1;
        }

        public static string StatusText(Adapter a, out bool good, out bool bad)
        {
            good = bad = false;
            switch (a.Status)
            {
                case "Disabled": bad = true; return "отключён — будет включён";
                case "Up": good = true; return "включён";
                case "Disconnected": return "включён, нет подключения";
                default: return a.Status;
            }
        }

        // Same commands as Invoke-NetworkConfiguration of the console version.
        public static string Script(int index, bool dhcp, string ip, int prefixLength, string gateway, IList<string> dns)
        {
            StringBuilder s = new StringBuilder();
            s.Append("$index = " + index + "\n");
            s.Append("try { $adapter = Get-NetAdapter -InterfaceIndex $index -ErrorAction Stop } catch { throw \"Не удалось получить адаптер с индексом ${index}: $($_.Exception.Message)\" }\n");
            s.Append("if ($adapter.Status -eq 'Disabled') {\n" +
                     "  try { $adapter | Enable-NetAdapter -Confirm:$false -ErrorAction Stop } catch { throw \"Не удалось включить адаптер '$($adapter.Name)': $($_.Exception.Message)\" }\n}\n");
            if (dhcp)
            {
                s.Append("try {\n" +
                    "  foreach ($store in @('PersistentStore', 'ActiveStore')) {\n" +
                    "    Get-NetRoute -PolicyStore $store -ErrorAction Stop | Where-Object { $_.InterfaceIndex -eq $index -and $_.AddressFamily -eq 'IPv4' -and $_.DestinationPrefix -eq '0.0.0.0/0' -and $_.Protocol -eq 'NetMgmt' } | Remove-NetRoute -Confirm:$false -ErrorAction Stop\n" +
                    "    Get-NetIPAddress -PolicyStore $store -ErrorAction Stop | Where-Object { $_.InterfaceIndex -eq $index -and $_.AddressFamily -eq 'IPv4' -and $_.PrefixOrigin -eq 'Manual' } | Remove-NetIPAddress -Confirm:$false -ErrorAction Stop\n" +
                    "  }\n" +
                    "  Set-NetIPInterface -InterfaceIndex $index -AddressFamily IPv4 -Dhcp Enabled -ErrorAction Stop\n" +
                    "  Set-DnsClientServerAddress -InterfaceIndex $index -ResetServerAddresses -ErrorAction Stop\n" +
                    "} catch { throw \"Не удалось полностью включить автоматическую настройку адаптера '$($adapter.Name)': $($_.Exception.Message)\" }\n");
                return s.ToString();
            }
            List<string> quoted = new List<string>();
            foreach (string d in dns) quoted.Add(PowerShell.Quote(d));
            s.Append("$ip = " + PowerShell.Quote(ip) + "; $prefix = " + prefixLength + "; $gateway = " + PowerShell.Quote(gateway) + "; $dns = @(" + string.Join(", ", quoted) + ")\n");
            s.Append("try { Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false -ErrorAction Stop }\n" +
                     "catch { throw \"Не удалось удалить прежний IPv4-адрес адаптера '$($adapter.Name)': $($_.Exception.Message)\" }\n");
            s.Append("try { Get-NetRoute -InterfaceIndex $index -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Remove-NetRoute -Confirm:$false -ErrorAction Stop }\n" +
                     "catch { throw \"Не удалось удалить прежний маршрут по умолчанию адаптера '$($adapter.Name)': $($_.Exception.Message)\" }\n");
            s.Append("try { New-NetIPAddress -InterfaceIndex $index -IPAddress $ip -PrefixLength $prefix -DefaultGateway $gateway -ErrorAction Stop | Out-Null }\n" +
                     "catch { throw \"Не удалось назначить адрес $ip/$prefix и шлюз $gateway адаптеру '$($adapter.Name)': $($_.Exception.Message)\" }\n");
            s.Append("try { Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $dns -ErrorAction Stop }\n" +
                     "catch { throw \"IP-адрес назначен, но не удалось установить DNS $($dns -join ', '): $($_.Exception.Message)\" }\n");
            return s.ToString();
        }

        public static void Apply(int index, bool dhcp, string ip, int prefixLength, string gateway, IList<string> dns)
        {
            PowerShell.Run(Script(index, dhcp, ip, prefixLength, gateway, dns), null, !PowerShell.IsAdministrator());
        }

        // Address, gateway and DNS of an adapter as Windows reports them now.
        public static List<string[]> Current(int index)
        {
            List<string[]> lines = new List<string[]>();
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Net, "SELECT IPAddress, PrefixLength FROM MSFT_NetIPAddress WHERE AddressFamily=2 AND InterfaceIndex=" + index))
                lines.Add(new[] { "IPv4", Wmi.Str(row, "IPAddress") + " (маска " + NetworkProfiles.MaskFromPrefix((int)Wmi.Num(row, "PrefixLength")) + ")" });
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Net, "SELECT NextHop FROM MSFT_NetRoute WHERE AddressFamily=2 AND DestinationPrefix='0.0.0.0/0' AND InterfaceIndex=" + index))
                lines.Add(new[] { "Шлюз", Wmi.Str(row, "NextHop") });
            int n = 1;
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Net, "SELECT ServerAddresses FROM MSFT_DNSClientServerAddress WHERE AddressFamily=2 AND InterfaceIndex=" + index))
                foreach (string s in Wmi.Strings(row, "ServerAddresses")) lines.Add(new[] { "DNS " + n++, s });
            return lines;
        }
    }

    // Naming rules of the unit (computer-policy.ps1).
    public class ComputerPolicy
    {
        public string UnitCode = "042";
        public string DomainName = "gu.gvardia.mvs";
        public string WorkgroupName = "WORKGROUP";
        public string InternetPrefix = "IF";
        public string DomainPrefix = "FR";

        public static readonly ComputerPolicy Current = new ComputerPolicy();
    }

    // Computer name and domain membership (computer-management.ps1).
    public static class ComputerNaming
    {
        const string Letters = "АБВГҐДЕЁЄЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ";
        static readonly string[] Latin = { "A", "B", "V", "G", "G", "D", "E", "E", "YE", "ZH", "Z", "I", "I", "YI", "Y", "K", "L", "M", "N", "O", "P", "R", "S", "T", "U", "F", "KH", "TS", "CH", "SH", "SHCH", "", "Y", "", "E", "YU", "YA" };

        // Cyrillic to Latin, spaces to hyphens: "отдел 5" -> "OTDEL-5".
        public static string Designation(string text)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in (text ?? "").Trim().ToUpperInvariant())
            {
                int i = Letters.IndexOf(ch);
                if (i >= 0) sb.Append(Latin[i]);
                else if (char.IsWhiteSpace(ch)) sb.Append('-');
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        // Null when the name is valid, otherwise the reason.
        public static string Issue(string name, string prefix, ComputerPolicy p)
        {
            if (prefix != p.InternetPrefix && prefix != p.DomainPrefix) return "Неизвестный тип сети.";
            if (name.Length > 15) return "Полное имя должно содержать не больше 15 символов.";
            string start = Regex.Escape(prefix + "-" + p.UnitCode + "-");
            if (!Regex.IsMatch(name, "^" + start + "[A-Z0-9](?:[A-Z0-9-]*[A-Z0-9])?$"))
                return "Нужно имя " + prefix + "-" + p.UnitCode + "-...: латиница, цифры и дефис; окончание без дефиса по краям.";
            return null;
        }

        public static string Suggested(string currentName, string prefix, ComputerPolicy p, string designation, string ip)
        {
            string suffix = "";
            Match m = Regex.Match(currentName ?? "", "^[A-Z]{2,3}-\\d{3}-(.+)$", RegexOptions.IgnoreCase);
            if (m.Success) suffix = Designation(m.Groups[1].Value);
            if (suffix.Length == 0 && !string.IsNullOrEmpty(designation)) suffix = Designation(designation);
            m = Regex.Match(ip ?? "", "^\\d{1,3}\\.\\d{1,3}\\.(\\d{1,3})\\.(\\d{1,3})$");
            if (suffix.Length == 0 && m.Success) suffix = m.Groups[1].Value + "-" + m.Groups[2].Value;
            if (suffix.Length == 0) suffix = "PC";
            suffix = Regex.Replace(suffix, "[^A-Z0-9-]", "", RegexOptions.IgnoreCase).Trim('-');
            if (suffix.Length == 0) suffix = "PC";
            string start = prefix + "-" + p.UnitCode + "-";
            suffix = suffix.Substring(0, Math.Min(suffix.Length, 15 - start.Length)).TrimEnd('-');
            return start + suffix;
        }
    }

    public class ComputerState
    {
        public string Name, Domain, Boot, PendingName;
        public bool PartOfDomain, PendingDomain;
        public int DomainRole, ProductType;
        public Dictionary<string, string> Receipt;   // expected result of an unfinished change, or null

        public const string ReceiptKey = @"SYSTEM\CurrentControlSet\Control\ComputerInventoryCollector\PendingChange";

        public static ComputerState Read()
        {
            ComputerState s = new ComputerState();
            Dictionary<string, object> cs = Wmi.First(Wmi.Cimv2, "SELECT Name, Domain, PartOfDomain, DomainRole FROM Win32_ComputerSystem");
            Dictionary<string, object> os = Wmi.First(Wmi.Cimv2, "SELECT ProductType, LastBootUpTime FROM Win32_OperatingSystem");
            s.Name = Wmi.Str(cs, "Name") ?? "";
            s.Domain = Wmi.Str(cs, "Domain") ?? "";
            s.PartOfDomain = Wmi.Bool(cs, "PartOfDomain");
            s.DomainRole = (int)Wmi.Num(cs, "DomainRole");
            s.ProductType = (int)Wmi.Num(os, "ProductType");
            s.Boot = ManagementDateTimeConverter.ToDateTime(Wmi.Str(os, "LastBootUpTime")).ToUniversalTime().ToString("o");
            using (RegistryKey lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            {
                string active = Value(lm, @"SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName", "ComputerName");
                string configured = Value(lm, @"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName", "ComputerName");
                s.PendingName = string.Equals(active, configured, StringComparison.OrdinalIgnoreCase) ? null : configured;
                using (RegistryKey netlogon = lm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Netlogon"))
                    s.PendingDomain = netlogon != null && (netlogon.GetValue("JoinDomain") != null || netlogon.GetValue("AvoidSpnSet") != null);
                using (RegistryKey receipt = lm.OpenSubKey(ReceiptKey))
                {
                    if (receipt != null)
                    {
                        s.Receipt = new Dictionary<string, string>();
                        foreach (string n in receipt.GetValueNames()) s.Receipt[n] = Convert.ToString(receipt.GetValue(n));
                    }
                }
            }
            return s;
        }

        static string Value(RegistryKey root, string path, string name)
        {
            using (RegistryKey k = root.OpenSubKey(path)) return k == null ? null : Convert.ToString(k.GetValue(name));
        }

        public string ReceiptValue(string name)
        {
            string v;
            return Receipt != null && Receipt.TryGetValue(name, out v) ? v : "";
        }

        public bool ReceiptMember { get { int v; return int.TryParse(ReceiptValue("PartOfDomain"), out v) && v != 0; } }

        public bool HasUnfinishedChange { get { return PendingName != null || PendingDomain || Receipt != null; } }

        // The computer restarted after the change and is in the expected state.
        public bool ReceiptMatches
        {
            get
            {
                return Receipt != null && string.Equals(Name, ReceiptValue("Name"), StringComparison.OrdinalIgnoreCase) && PartOfDomain == ReceiptMember &&
                    string.Equals(Domain, ReceiptValue("Domain"), StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool RebootedSinceReceipt { get { return Receipt != null && ReceiptValue("Boot") != Boot; } }
    }

    public static class ComputerChange
    {
        public static void WriteReceipt(ComputerState state, string operation, string name, string domain, bool member)
        {
            using (RegistryKey lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            using (RegistryKey k = lm.CreateSubKey(ComputerState.ReceiptKey))
            {
                k.SetValue("Boot", state.Boot, RegistryValueKind.String);
                k.SetValue("Operation", operation, RegistryValueKind.String);
                k.SetValue("Name", name, RegistryValueKind.String);
                k.SetValue("Domain", domain, RegistryValueKind.String);
                k.SetValue("PartOfDomain", member ? "1" : "0", RegistryValueKind.String);
            }
        }

        public static void ClearReceipt()
        {
            using (RegistryKey lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
                lm.DeleteSubKeyTree(ComputerState.ReceiptKey, false);
        }

        public static void AssertAllowed(ComputerState s)
        {
            if (s.DomainRole >= 4) throw new InvalidOperationException("Изменения на контроллере домена не поддерживаются.");
            if (s.HasUnfinishedChange) throw new InvalidOperationException("Есть незавершённое изменение. Сначала перезагрузите компьютер и проверьте результат.");
        }

        // DNS knows the domain controllers and one of them answers. Returns their names.
        public static string TestDomain(string domain)
        {
            string script =
                "$srv = @(Resolve-DnsName -Name " + PowerShell.Quote("_ldap._tcp.dc._msdcs." + domain) + " -Type SRV -DnsOnly -ErrorAction Stop | Where-Object { $_.NameTarget })\n" +
                "if ($srv.Count -eq 0) { throw 'DNS не вернул контроллеры домена. Проверьте DNS LAN-адаптера.' }\n" +
                "$details = & \"$env:SystemRoot\\System32\\nltest.exe\" " + PowerShell.Quote("/dsgetdc:" + domain) + " '/force' 2>&1\n" +
                "if ($LASTEXITCODE -ne 0) { throw \"Контроллер домена недоступен. Проверьте LAN, DNS и время Windows. $($details -join ' ')\" }\n" +
                "$srv.NameTarget -join ', '";
            return PowerShell.Run(script, null, false);
        }

        public static bool TestSecureChannel()
        {
            return PowerShell.Run("Test-ComputerSecureChannel -ErrorAction Stop", null, false).Trim() == "True";
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LogonUser(string user, string domain, string password, int type, int provider, out IntPtr token);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        // Enabled local user that is directly in the Administrators group and can log on with this password.
        public static string CheckLocalAdministrator(string userName, string password, string computerName)
        {
            string[] parts = (userName ?? "").Split('\\');
            string name = parts[parts.Length - 1].Trim();
            if ((parts.Length > 1 && (parts.Length != 2 || !(parts[0] == "." || string.Equals(parts[0], computerName, StringComparison.OrdinalIgnoreCase)))) ||
                name.Contains("@") || name.Length == 0)
                throw new InvalidOperationException("Укажите локальную учётную запись: .\\имя или ИМЯ-ПК\\имя.");
            using (PrincipalContext machine = new PrincipalContext(ContextType.Machine))
            using (UserPrincipal user = UserPrincipal.FindByIdentity(machine, IdentityType.SamAccountName, name))
            using (GroupPrincipal admins = GroupPrincipal.FindByIdentity(machine, IdentityType.Sid, "S-1-5-32-544"))
            {
                bool ok = user != null && user.Enabled == true && !user.IsAccountLockedOut() && admins != null;
                if (ok)
                {
                    ok = false;
                    foreach (Principal member in admins.GetMembers(false))
                        using (member) if (member.Sid == user.Sid) ok = true;
                }
                if (!ok) throw new InvalidOperationException("Нужен включённый локальный пользователь, непосредственно входящий в группу администраторов.");
            }
            IntPtr token;
            if (!LogonUser(name, computerName, password, 2, 0, out token))
                throw new InvalidOperationException("Локальный вход не подтверждён (код " + Marshal.GetLastWin32Error() + "). Проверьте пароль и право локального входа.");
            CloseHandle(token);
            return name;
        }

        public class Request
        {
            public string Operation;      // Rename, Join, Leave
            public string Name;
            public string DomainUser, DomainPassword;
            public string LocalUser, LocalPassword;
        }

        // Invoke-ComputerManagementChange: checks everything again, writes the receipt, then runs the Windows command.
        // Returns false when the computer already has this name.
        public static bool Apply(Request r, ComputerPolicy p)
        {
            if (!PowerShell.IsAdministrator()) throw new InvalidOperationException("Нужны права локального администратора.");
            ComputerState state = ComputerState.Read();
            AssertAllowed(state);
            string prefix = null;
            if (r.Operation != "Leave")
            {
                prefix = r.Name.StartsWith(p.DomainPrefix + "-") ? p.DomainPrefix : p.InternetPrefix;
                string issue = ComputerNaming.Issue(r.Name, prefix, p);
                if (issue != null) throw new InvalidOperationException(issue);
            }
            if (state.PartOfDomain && !string.Equals(state.Domain, p.DomainName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Компьютер состоит в другом домене. Автоматический перенос не поддерживается.");
            if (r.Operation == "Rename" && state.PartOfDomain && prefix != p.DomainPrefix)
                throw new InvalidOperationException("Сначала выведите компьютер из домена, затем назначайте имя интернет-сети.");
            if (r.Operation == "Rename" && string.Equals(state.Name, r.Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (r.Operation == "Join")
            {
                if (state.PartOfDomain) throw new InvalidOperationException("Компьютер уже состоит в домене.");
                if (prefix != p.DomainPrefix) throw new InvalidOperationException("Для домена требуется префикс доменной сети.");
                if (state.ProductType == 1)
                {
                    string edition;
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) edition = Convert.ToString(k.GetValue("EditionID"));
                    if (!Regex.IsMatch(edition ?? "", "^(Professional|Enterprise|Education|IoTEnterprise|Ultimate)", RegexOptions.IgnoreCase))
                        throw new InvalidOperationException("Эта редакция Windows не поддерживается для ввода в домен.");
                }
            }
            if (r.Operation == "Leave" && !state.PartOfDomain) throw new InvalidOperationException("Компьютер уже находится в рабочей группе.");
            if (r.Operation == "Leave") CheckLocalAdministrator(r.LocalUser, r.LocalPassword, state.Name);
            bool needsDomain = r.Operation != "Rename" || state.PartOfDomain;
            if (needsDomain)
            {
                if (string.IsNullOrEmpty(r.DomainUser)) throw new InvalidOperationException("Нужны доменные учётные данные.");
                TestDomain(p.DomainName);
            }
            string targetName = r.Operation == "Leave" ? state.Name : r.Name;
            string targetDomain = r.Operation == "Join" ? p.DomainName : r.Operation == "Leave" ? p.WorkgroupName : state.Domain;
            bool targetMember = r.Operation == "Join" || (r.Operation != "Leave" && state.PartOfDomain);
            // Written BEFORE the system command: a partial failure must not permit a second change.
            WriteReceipt(state, r.Operation, targetName, targetDomain, targetMember);

            StringBuilder s = new StringBuilder();
            if (needsDomain)
                s.Append("$user = Read-Secret\n$password = ConvertTo-SecureString (Read-Secret) -AsPlainText -Force\n$credential = New-Object System.Management.Automation.PSCredential($user, $password)\n");
            if (r.Operation == "Join")
                s.Append("$result = Add-Computer -DomainName " + PowerShell.Quote(p.DomainName) + " -Credential $credential" +
                    (string.Equals(state.Name, r.Name, StringComparison.OrdinalIgnoreCase) ? "" : " -NewName " + PowerShell.Quote(r.Name)) + " -Force -PassThru -ErrorAction Stop\n");
            else if (r.Operation == "Leave")
                s.Append("$result = Remove-Computer -UnjoinDomainCredential $credential -WorkgroupName " + PowerShell.Quote(p.WorkgroupName) + " -Force -PassThru -ErrorAction Stop\n");
            else
                s.Append("$result = Rename-Computer -NewName " + PowerShell.Quote(r.Name) + (state.PartOfDomain ? " -DomainCredential $credential" : "") + " -Force -PassThru -ErrorAction Stop\n");
            s.Append("if (-not $result.HasSucceeded) { throw 'Windows не подтвердила изменение. Перезагрузите компьютер и проверьте состояние.' }\n");
            PowerShell.Run(s.ToString(), needsDomain ? new[] { r.DomainUser, r.DomainPassword } : null, false);
            return true;
        }

        // The record of this computer can be found without the old name (Assert-InventoryReadyForRename).
        public static void AssertInventoryReady(string dataPath)
        {
            Dictionary<string, object> product = Wmi.First(Wmi.Cimv2, "SELECT UUID FROM Win32_ComputerSystemProduct");
            Dictionary<string, object> bios = Wmi.First(Wmi.Cimv2, "SELECT SerialNumber FROM Win32_BIOS");
            Identity.Result identity = Identity.Find(Wmi.Str(product, "UUID"), (Wmi.Str(bios, "SerialNumber") ?? "").Trim(), Identity.WindowsInstallationId(), dataPath);
            if (identity.Path == null && Identity.FindUnboundLegacy(dataPath, Environment.MachineName).Count > 0)
                throw new InvalidOperationException("Есть старая запись без устойчивого ID. Сначала выполните «Сбор информации» и подтвердите её привязку, затем изменяйте имя.");
        }

        public static void Restart()
        {
            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/r /t 0");
            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            System.Diagnostics.Process.Start(info);
        }
    }
}
