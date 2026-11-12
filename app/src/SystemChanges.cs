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
                case "Disabled": bad = true; return L.T("отключён — будет включён");
                case "Up": good = true; return L.T("включён");
                case "Disconnected": return L.T("включён, нет подключения");
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
            // netsh, not Set-NetIPInterface + New-NetIPAddress: on an adapter without a cable
            // Set-NetIPInterface switches DHCP off only until reboot, and New-NetIPAddress then refuses with
            // "Inconsistent parameters PolicyStore PersistentStore and Dhcp Enabled". netsh switches DHCP off,
            // replaces previous addresses and gateways and stores the result permanently.
            s.Append("$netshOutput = netsh interface ipv4 set address \"name=$index\" source=static \"address=$ip/$prefix\" \"gateway=$gateway\" gwmetric=256\n" +
                     "if ($LASTEXITCODE -ne 0) { throw \"Не удалось назначить адрес $ip/$prefix и шлюз $gateway адаптеру '$($adapter.Name)': $((@($netshOutput) -join ' ').Trim())\" }\n");
            s.Append("try { Set-DnsClientServerAddress -InterfaceIndex $index -ServerAddresses $dns -ErrorAction Stop }\n" +
                     "catch { throw \"IP-адрес назначен, но не удалось установить DNS $($dns -join ', '): $($_.Exception.Message)\" }\n");
            return s.ToString();
        }

        public static void Apply(int index, bool dhcp, string ip, int prefixLength, string gateway, IList<string> dns)
        {
            PowerShell.Run(Script(index, dhcp, ip, prefixLength, gateway, dns), null, !PowerShell.IsAdministrator());
        }

        // Configured IPv4 settings of an adapter.
        public class State
        {
            public bool Dhcp;
            public string Ip = "", Mask = "", Gateway = "";
            public List<string> Dns = new List<string>();
            public bool DnsManual;
        }

        // Read from the TCP/IP settings in the registry rather than the live addresses: a static address set
        // on an adapter without a cable is shown too, as is DHCP without a lease yet.
        public static State Read(string guid)
        {
            State s = new State();
            if (string.IsNullOrEmpty(guid)) return null;
            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + guid))
            {
                if (key == null) return null;
                s.Dhcp = Convert.ToInt32(key.GetValue("EnableDHCP", 0)) != 0;
                s.Ip = FirstAddress(key, s.Dhcp ? "DhcpIPAddress" : "IPAddress");
                s.Mask = FirstAddress(key, s.Dhcp ? "DhcpSubnetMask" : "SubnetMask");
                s.Gateway = FirstAddress(key, s.Dhcp ? "DhcpDefaultGateway" : "DefaultGateway");
                s.Dns = Addresses(key, "NameServer");
                s.DnsManual = s.Dns.Count > 0;
                if (!s.DnsManual && s.Dhcp) s.Dns = Addresses(key, "DhcpNameServer");
            }
            return s;
        }

        // REG_SZ or REG_MULTI_SZ, entries separated by commas or spaces; 0.0.0.0 means "not set".
        static List<string> Addresses(RegistryKey key, string name)
        {
            List<string> result = new List<string>();
            object value = key.GetValue(name);
            string[] parts = value is string[] ? (string[])value : new[] { Convert.ToString(value ?? "") };
            foreach (string part in parts)
                foreach (string address in part.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    if (address != "0.0.0.0" && !result.Contains(address)) result.Add(address);
            return result;
        }

        static string FirstAddress(RegistryKey key, string name)
        {
            List<string> all = Addresses(key, name);
            return all.Count > 0 ? all[0] : "";
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

        public static string NoDomain { get { return L.T("Домен не задан: синхронизируйтесь с облаком, чтобы загрузить профили сети (в веб-интерфейсе — страница «Сеть», поле «Домен»)."); } }

        public static ComputerPolicy Load()
        {
            return new ComputerPolicy();
        }
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
            if (prefix != p.InternetPrefix && prefix != p.DomainPrefix) return L.T("Неизвестный тип сети.");
            if (name.Length > 15) return L.T("Полное имя должно содержать не больше 15 символов.");
            string start = Regex.Escape(prefix + "-" + p.UnitCode + "-");
            if (!Regex.IsMatch(name, "^" + start + "[A-Z0-9](?:[A-Z0-9-]*[A-Z0-9])?$"))
                return L.T("Нужно имя ") + prefix + "-" + p.UnitCode + L.T("-...: латиница, цифры и дефис; окончание без дефиса по краям.");
            return null;
        }

        // Generic inventory policy used by the security review: AA-XXX-... or AAA-XXX-....
        public static bool MatchesPolicy(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length <= 15 &&
                Regex.IsMatch(name, "^[A-Z]{2,3}-[0-9]{3}-[A-Z0-9](?:[A-Z0-9-]*[A-Z0-9])?$");
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
            if (s.DomainRole >= 4) throw new InvalidOperationException(L.T("Изменения на контроллере домена не поддерживаются."));
            if (s.HasUnfinishedChange) throw new InvalidOperationException(L.T("Есть незавершённое изменение. Сначала перезагрузите компьютер и проверьте результат."));
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
                throw new InvalidOperationException(L.T("Укажите локальную учётную запись: .\\имя или ИМЯ-ПК\\имя."));
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
                if (!ok) throw new InvalidOperationException(L.T("Нужен включённый локальный пользователь, непосредственно входящий в группу администраторов."));
            }
            IntPtr token;
            if (!LogonUser(name, computerName, password, 2, 0, out token))
                throw new InvalidOperationException(L.T("Локальный вход не подтверждён (код ") + Marshal.GetLastWin32Error() + L.T("). Проверьте пароль и право локального входа."));
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
            if (p.DomainName == null && (r.Operation != "Rename" || ComputerState.Read().PartOfDomain)) throw new InvalidOperationException(ComputerPolicy.NoDomain);
            if (!PowerShell.IsAdministrator()) throw new InvalidOperationException(L.T("Нужны права локального администратора."));
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
                throw new InvalidOperationException(L.T("Компьютер состоит в другом домене. Автоматический перенос не поддерживается."));
            if (r.Operation == "Rename" && state.PartOfDomain && prefix != p.DomainPrefix)
                throw new InvalidOperationException(L.T("Сначала выведите компьютер из домена, затем назначайте имя интернет-сети."));
            if (r.Operation == "Rename" && string.Equals(state.Name, r.Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (r.Operation == "Join")
            {
                if (state.PartOfDomain) throw new InvalidOperationException(L.T("Компьютер уже состоит в домене."));
                if (prefix != p.DomainPrefix) throw new InvalidOperationException(L.T("Для домена требуется префикс доменной сети."));
                if (state.ProductType == 1)
                {
                    string edition;
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) edition = Convert.ToString(k.GetValue("EditionID"));
                    if (!Regex.IsMatch(edition ?? "", "^(Professional|Enterprise|Education|IoTEnterprise|Ultimate)", RegexOptions.IgnoreCase))
                        throw new InvalidOperationException(L.T("Эта редакция Windows не поддерживается для ввода в домен."));
                }
            }
            if (r.Operation == "Leave" && !state.PartOfDomain) throw new InvalidOperationException(L.T("Компьютер уже находится в рабочей группе."));
            if (r.Operation == "Leave") CheckLocalAdministrator(r.LocalUser, r.LocalPassword, state.Name);
            bool needsDomain = r.Operation != "Rename" || state.PartOfDomain;
            if (needsDomain)
            {
                if (string.IsNullOrEmpty(r.DomainUser)) throw new InvalidOperationException(L.T("Нужны доменные учётные данные."));
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
                throw new InvalidOperationException(L.T("Есть старая запись без устойчивого ID. Сначала выполните «Сбор информации» и подтвердите её привязку, затем изменяйте имя."));
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
