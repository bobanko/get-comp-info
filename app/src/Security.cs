using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CompInfo
{
    public enum SecurityLevel { Good, Warning, Bad, Info, Error }

    public class SecurityProgress
    {
        public bool Indeterminate;
        public int Found, Checked, Total, Matches;

        public SecurityProgress(bool indeterminate, int found, int checkedFiles, int total, int matches)
        {
            Indeterminate = indeterminate; Found = found; Checked = checkedFiles; Total = total; Matches = matches;
        }
    }

    public class SecurityDetail
    {
        public string Text, Note, FilePath, FileType, ToolTip;

        public SecurityDetail(string text, string note, string filePath, string fileType)
            : this(text, note, filePath, fileType, "") { }

        public SecurityDetail(string text, string note, string filePath, string fileType, string toolTip)
        {
            Text = text ?? ""; Note = note ?? ""; FilePath = filePath ?? ""; FileType = fileType ?? ""; ToolTip = toolTip ?? "";
        }
    }

    public class SecurityCheck
    {
        public string Key, Title, Summary, Details, ActionKey;
        public SecurityLevel Level;
        // Files the user deleted from this check's list, and the summary the scan gave before that.
        public int DeletedFiles;
        public string ScanSummary;
        public readonly List<SecurityDetail> Items = new List<SecurityDetail>();

        public SecurityCheck(string key, string title, SecurityLevel level, string summary, string details)
        {
            Key = key; Title = title; Level = level; Summary = summary; Details = details ?? "";
            foreach (string line in Details.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                Items.Add(new SecurityDetail(line, "", "", ""));
        }
    }

    // Read-only local security review. Nothing is changed or sent outside this computer.
    public static class SecurityAudit
    {
        static readonly string[] UnwantedPrograms = { "WinRAR" };
        static readonly string[] DocumentKeywords =
        {
            "ТФД", "телефонний довідник", "телефонный справочник", "відомість", "ведомость", "вахтовка", "доповідь", "доклад",
            "для службового користування", "мосі", "дск-еп", "дск", "таємно", "особливої важливості"
        };
        static readonly HashSet<string> OfficeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm",
            ".xls", ".xlsx", ".xlsm", ".xlsb", ".xlt", ".xltx", ".xltm",
            ".ppt", ".pptx", ".pptm", ".pot", ".potx", ".potm", ".pps", ".ppsx", ".ppsm"
        };
        static readonly HashSet<string> OpenXmlExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".docx", ".docm", ".dotx", ".dotm", ".xlsx", ".xlsm", ".xltx", ".xltm",
            ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm"
        };

        delegate SecurityCheck CheckReader();

        class AuditStep
        {
            public string Key, Title;
            public CheckReader Reader;

            public AuditStep(string key, string title, CheckReader reader)
            {
                Key = key; Title = title; Reader = reader;
            }
        }

        // Fast checks first, the full disk scan last, so the table starts filling immediately.
        static List<AuditStep> Steps()
        {
            return new List<AuditStep>
            {
                new AuditStep("hostname", L.T("Имя компьютера"), CheckHostname),
                new AuditStep("domain", L.T("Домен"), CheckDomain),
                new AuditStep("antivirus", L.T("Антивирус"), CheckAntivirus),
                new AuditStep("vpn", "VPN", CheckVpn),
                new AuditStep("unwanted", L.T("Нежелательное ПО"), CheckUnwanted),
                new AuditStep("onedrive", "OneDrive", CheckOneDrive),
                new AuditStep("administrators", L.T("Локальные администраторы"), CheckAdministrators),
                new AuditStep("passwords", L.T("Пароли Windows"), CheckPasswords),
                new AuditStep("documents", L.T("Поиск ДСК-файлов"), CheckDocuments),
            };
        }

        static SecurityCheck CheckHostname()
        {
            string name = Environment.MachineName ?? "";
            string expected = L.T("Ожидаемый формат: AA-XXX-... или AAA-XXX-...; не больше 15 символов, латиница, цифры и дефис.");
            return ComputerNaming.MatchesPolicy(name)
                ? New(SecurityLevel.Good, L.T("Имя соответствует политике: ") + name, expected)
                : New(SecurityLevel.Bad, L.T("Имя не соответствует политике: ") + name, expected);
        }

        public static List<SecurityCheck> Plan()
        {
            return Plan(true);
        }

        // Without scanDocuments the document step stays in the table, marked as skipped.
        public static List<SecurityCheck> Plan(bool scanDocuments)
        {
            List<SecurityCheck> plan = new List<SecurityCheck>();
            foreach (AuditStep step in Steps())
                plan.Add(!scanDocuments && step.Key == "documents"
                    ? Skipped(step)
                    : new SecurityCheck(step.Key, step.Title, SecurityLevel.Info, L.T("Ожидает проверки"), ""));
            return plan;
        }

        static SecurityCheck Skipped(AuditStep step)
        {
            return new SecurityCheck(step.Key, step.Title, SecurityLevel.Info, L.T("Пропущено: сканирование документов выключено"),
                L.T("Чтобы проверить документы, отметьте «Сканировать документы» и запустите проверку снова."));
        }

        public static List<SecurityCheck> Run(Action<string> progress)
        {
            return Run(progress == null ? (Action<SecurityCheck>)null : delegate (SecurityCheck check) { progress(check.Title); }, null, null);
        }

        public static List<SecurityCheck> Run(Action<SecurityCheck> started, Action<SecurityCheck> completed)
        {
            return Run(started, completed, null);
        }

        public static List<SecurityCheck> Run(Action<SecurityCheck> started, Action<SecurityCheck> completed, Action<SecurityProgress> detailedProgress)
        {
            return Run(started, completed, detailedProgress, true);
        }

        public static List<SecurityCheck> Run(Action<SecurityCheck> started, Action<SecurityCheck> completed, Action<SecurityProgress> detailedProgress, bool scanDocuments)
        {
            List<SecurityCheck> result = new List<SecurityCheck>();
            foreach (AuditStep step in Steps())
            {
                if (!scanDocuments && step.Key == "documents")
                {
                    SecurityCheck skipped = Skipped(step);
                    result.Add(skipped);
                    if (completed != null) completed(skipped);
                    continue;
                }
                SecurityCheck pending = new SecurityCheck(step.Key, step.Title, SecurityLevel.Info, L.T("Проверяется…"), "");
                if (started != null) started(pending);
                SecurityCheck check = Read(step, detailedProgress);
                result.Add(check);
                if (completed != null) completed(check);
            }
            return result;
        }

        static SecurityCheck Read(AuditStep step, Action<SecurityProgress> detailedProgress)
        {
            try
            {
                SecurityCheck check = step.Key == "documents" ? CheckDocuments(detailedProgress) : step.Reader();
                check.Key = step.Key; check.Title = step.Title;
                return check;
            }
            catch (Exception e)
            {
                return new SecurityCheck(step.Key, step.Title, SecurityLevel.Error, L.T("Не удалось выполнить проверку"), e.Message);
            }
        }

        static SecurityCheck CheckDomain()
        {
            Dictionary<string, object> row = Wmi.First(Wmi.Cimv2, "SELECT Domain, PartOfDomain FROM Win32_ComputerSystem");
            string actual = Wmi.Str(row, "Domain") ?? "";
            string expected = null;
            try { expected = NetworkProfiles.Read(AppPaths.File("network-profiles.json")).DomainName; } catch (Exception) { }
            if (string.IsNullOrEmpty(expected)) expected = ComputerPolicy.Load().DomainName;
            bool member = Wmi.Bool(row, "PartOfDomain");
            bool correct = member && string.Equals(actual.TrimEnd('.'), (expected ?? "").TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
            if (correct)
                return New(SecurityLevel.Good, L.T("Компьютер в домене ") + actual, L.T("Ожидаемый домен: ") + expected);
            if (!member)
                return New(SecurityLevel.Bad, L.T("Компьютер в рабочей группе: ") + actual, L.T("Требуется домен: ") + expected);
            return New(SecurityLevel.Bad, L.T("Компьютер в другом домене: ") + actual, L.T("Требуется домен: ") + expected);
        }

        static SecurityCheck CheckAntivirus()
        {
            ServiceState eset = Service("ekrn");
            ServiceState defender = Service("WinDefend");
            List<string> products = new List<string>();
            try
            {
                foreach (Dictionary<string, object> row in Wmi.Query(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct"))
                {
                    string name = Wmi.Str(row, "displayName");
                    if (!string.IsNullOrEmpty(name) && !products.Contains(name)) products.Add(name);
                }
            }
            catch (Exception) { }
            string details = "ESET: " + ServiceText(eset) + "\r\nWindows Defender: " + ServiceText(defender);
            if (products.Count > 0) details += "\r\n" + L.T("Антивирусы Windows: ") + string.Join(", ", products.ToArray());
            if (eset.Running)
                return New(SecurityLevel.Good, defender.Running ? L.T("ESET и Windows Defender запущены") : L.T("ESET запущен (предпочтительный вариант)"), details);
            if (defender.Running)
                return New(eset.Installed ? SecurityLevel.Warning : SecurityLevel.Good,
                    eset.Installed ? L.T("Windows Defender запущен, ESET установлен, но не работает") : L.T("Windows Defender запущен (допустимо)"), details);
            return New(SecurityLevel.Bad, L.T("Активный антивирус не найден"), details);
        }

        class ServiceState { public bool Installed, Running; public string State = ""; }

        static ServiceState Service(string name)
        {
            ServiceState s = new ServiceState();
            foreach (Dictionary<string, object> row in Wmi.Query(Wmi.Cimv2, "SELECT State FROM Win32_Service WHERE Name='" + name.Replace("'", "''") + "'"))
            {
                s.Installed = true;
                s.State = Wmi.Str(row, "State") ?? "";
                s.Running = string.Equals(s.State, "Running", StringComparison.OrdinalIgnoreCase);
            }
            return s;
        }

        static string ServiceText(ServiceState s)
        {
            if (!s.Installed) return L.T("не установлен");
            return s.Running ? L.T("запущен") : L.T("установлен, но не запущен");
        }

        static SecurityCheck CheckVpn()
        {
            List<string> apps = InstalledPrograms();
            List<string> found = new List<string>();
            foreach (string app in apps) if (LooksLikeVpn(app)) AddUnique(found, app);
            List<string> adapters = new List<string>();
            foreach (Adapter a in Adapter.List())
            {
                string text = a.Name + " " + a.Description;
                if (LooksLikeVpn(text)) AddUnique(adapters, a.Name + (a.Status.Length > 0 ? " (" + a.Status + ")" : ""));
            }
            List<string> details = new List<string>();
            if (found.Count > 0) details.Add(L.T("Программы: ") + string.Join(", ", found.ToArray()));
            if (adapters.Count > 0) details.Add(L.T("Адаптеры: ") + string.Join(", ", adapters.ToArray()));
            return New(SecurityLevel.Info, found.Count == 0 && adapters.Count == 0 ? L.T("VPN не обнаружен") : L.T("Обнаружены признаки VPN"),
                details.Count == 0 ? L.T("Проверка информационная и не влияет на итог.") : string.Join("\r\n", details.ToArray()) + "\r\n" + L.T("Проверка информационная и не влияет на итог."));
        }

        static bool LooksLikeVpn(string value)
        {
            string s = (value ?? "").ToLowerInvariant();
            foreach (string word in new[] { "vpn", "openvpn", "wireguard", "tailscale", "forticlient", "fortinet", "anyconnect", "globalprotect", "palo alto", "check point", "checkpoint", "zscaler", "tap-windows", "wintun" })
                if (s.Contains(word)) return true;
            return false;
        }

        static SecurityCheck CheckUnwanted()
        {
            List<string> installed = InstalledPrograms(), found = new List<string>();
            foreach (string app in installed)
                foreach (string unwanted in UnwantedPrograms)
                    if (app.IndexOf(unwanted, StringComparison.OrdinalIgnoreCase) >= 0) AddUnique(found, app);
            return found.Count == 0
                ? New(SecurityLevel.Good, L.T("Нежелательное ПО не найдено"), L.T("Проверено: ") + string.Join(", ", UnwantedPrograms))
                : New(SecurityLevel.Bad, L.F("Найдено нежелательных программ: {0}", found.Count), string.Join("\r\n", found.ToArray()));
        }

        static List<string> InstalledPrograms()
        {
            List<string> result = new List<string>();
            ReadPrograms(RegistryHive.LocalMachine, RegistryView.Registry64, result);
            ReadPrograms(RegistryHive.LocalMachine, RegistryView.Registry32, result);
            ReadPrograms(RegistryHive.CurrentUser, RegistryView.Default, result);
            result.Sort(StringComparer.CurrentCultureIgnoreCase);
            return result;
        }

        static void ReadPrograms(RegistryHive hive, RegistryView view, List<string> result)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null) return;
                    foreach (string subName in uninstall.GetSubKeyNames())
                        using (RegistryKey sub = uninstall.OpenSubKey(subName))
                        {
                            string name = sub == null ? null : Convert.ToString(sub.GetValue("DisplayName"));
                            if (!string.IsNullOrWhiteSpace(name)) AddUnique(result, name.Trim());
                        }
                }
            }
            catch (Exception) { }
        }

        static SecurityCheck CheckOneDrive()
        {
            OneDriveInfo info = OneDriveInfo.Read();
            List<string> folders = new List<string>();
            foreach (string root in info.Roots)
            {
                try
                {
                    foreach (string folder in Directory.GetDirectories(root)) AddUnique(folders, folder);
                }
                catch (Exception) { }
            }
            List<string> lines = new List<string>();
            lines.Add(L.T("Синхронизация настроена: ") + (info.Accounts > 0 ? L.F("да, учётных записей: {0}", info.Accounts) : L.T("нет")));
            lines.Add(L.T("Клиент OneDrive запущен: ") + (info.Running ? L.T("да") : L.T("нет")));
            lines.Add(L.T("Автозапуск OneDrive: ") + (info.AutoStart ? L.T("включён") : L.T("выключен")));
            if (info.Roots.Count > 0) lines.Add(L.T("Папки синхронизации: ") + string.Join("; ", info.Roots.ToArray()));
            bool enabled = info.Accounts > 0 && (info.Running || info.AutoStart);
            SecurityCheck check;
            if (enabled && folders.Count > 0)
                check = New(SecurityLevel.Bad, L.F("Синхронизация OneDrive включена, найдено папок: {0}", folders.Count), string.Join("\r\n", lines.ToArray()));
            else if (enabled)
                check = New(SecurityLevel.Warning, L.T("Синхронизация OneDrive включена, папок внутри нет"), string.Join("\r\n", lines.ToArray()));
            else if (info.Running || info.AutoStart)
                check = New(SecurityLevel.Warning, L.T("OneDrive включён, но учётная запись синхронизации не найдена"), string.Join("\r\n", lines.ToArray()));
            else if (info.Accounts > 0)
                check = New(SecurityLevel.Good, L.T("Синхронизация OneDrive настроена, но отключена"), string.Join("\r\n", lines.ToArray()));
            else
                check = New(SecurityLevel.Good, L.T("Синхронизация OneDrive не включена"), string.Join("\r\n", lines.ToArray()));
            if (info.Running || info.AutoStart) check.ActionKey = "disable-onedrive";
            return check;
        }

        public static SecurityCheck CheckOneDriveNow()
        {
            AuditStep step = new AuditStep("onedrive", "OneDrive", CheckOneDrive);
            return Read(step, null);
        }

        // Deletes a found document for good, bypassing the Recycle Bin: the scan checks the bin too.
        // A file found in the bin ($R...) takes its $I... record along, so the bin shows no empty entry.
        public static void DeleteDocument(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException(L.T("Файл больше не существует."), path);
            if ((file.Attributes & FileAttributes.ReadOnly) != 0) file.Attributes &= ~FileAttributes.ReadOnly;
            file.Delete();
            string info = RecycleInfoFile(path);
            if (info != null)
            {
                try { if (File.Exists(info)) File.Delete(info); }
                catch (Exception) { }
            }
        }

        internal static string RecycleInfoFile(string path)
        {
            string name = Path.GetFileName(path) ?? "";
            string folder = Path.GetDirectoryName(path) ?? "";
            if (!name.StartsWith("$R", StringComparison.OrdinalIgnoreCase)) return null;
            if (folder.IndexOf(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase) < 0) return null;
            return Path.Combine(folder, "$I" + name.Substring(2));
        }

        public static void DisableOneDriveSync()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (run != null) run.DeleteValue("OneDrive", false);
            using (RegistryKey approved = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
                approved.SetValue("OneDrive", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            foreach (Process process in Process.GetProcessesByName("OneDrive"))
            {
                using (process)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
        }

        class OneDriveInfo
        {
            public bool Running, AutoStart;
            public int Accounts;
            public readonly List<string> Roots = new List<string>();

            public static OneDriveInfo Read()
            {
                OneDriveInfo info = new OneDriveInfo();
                try { info.Running = Process.GetProcessesByName("OneDrive").Length > 0; } catch (Exception) { }
                foreach (string name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
                {
                    string value = Environment.GetEnvironmentVariable(name);
                    if (!string.IsNullOrEmpty(value) && Directory.Exists(value)) AddUnique(info.Roots, Path.GetFullPath(value));
                }
                try
                {
                    using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        string command = run == null ? null : Convert.ToString(run.GetValue("OneDrive"));
                        info.AutoStart = !string.IsNullOrEmpty(command);
                    }
                    using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
                    {
                        byte[] value = approved == null ? null : approved.GetValue("OneDrive") as byte[];
                        if (value != null && value.Length > 0 && (value[0] == 3 || value[0] == 7)) info.AutoStart = false;
                    }
                    using (RegistryKey accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts"))
                        if (accounts != null)
                            foreach (string account in accounts.GetSubKeyNames())
                                using (RegistryKey key = accounts.OpenSubKey(account))
                                {
                                    string value = key == null ? null : Convert.ToString(key.GetValue("UserFolder"));
                                    if (string.IsNullOrEmpty(value)) continue;
                                    info.Accounts++;
                                    if (Directory.Exists(value)) AddUnique(info.Roots, Path.GetFullPath(value));
                                }
                }
                catch (Exception) { }
                return info;
            }
        }

        class DocumentScan
        {
            public readonly List<DocumentMatch> Matches = new List<DocumentMatch>();
            public int Files, SkippedFolders, RecycleFiles, MatchCount;
            public bool MatchLimit;
            public bool RecycleChecked;
            public string RecycleError = "";
        }

        class DocumentMatch
        {
            public string Path, DisplayPath, Note, Type, ToolTip;

            public DocumentMatch(string path, List<KeywordHit> hits)
                : this(path, path, hits, false, "") { }

            public DocumentMatch(string path, string displayPath, List<KeywordHit> hits, bool recycle, string deletedAt)
            {
                Path = path; DisplayPath = displayPath; Note = MatchNote(displayPath, hits, recycle);
                ToolTip = MatchToolTip(hits, deletedAt);
                Type = DocumentType(displayPath);
            }
        }

        class KeywordHit
        {
            public string Keyword;
            public int Count;

            public KeywordHit(string keyword, int count) { Keyword = keyword; Count = count; }
        }

        class DocumentCandidate
        {
            public string Path, DisplayPath, FileName, Extension, DeletedAt;
            public bool Recycle;

            public DocumentCandidate(string path)
            {
                Path = DisplayPath = path; FileName = System.IO.Path.GetFileName(path); Extension = System.IO.Path.GetExtension(path);
            }

            public DocumentCandidate(RecycleFile file)
            {
                Path = file.Path; FileName = file.OriginalName; Extension = System.IO.Path.GetExtension(file.OriginalName);
                DisplayPath = file.OriginalFolder.Length == 0 ? file.OriginalName : System.IO.Path.Combine(file.OriginalFolder, file.OriginalName);
                DeletedAt = file.DeletedAt; Recycle = true;
            }
        }

        static SecurityCheck CheckDocuments()
        {
            return CheckDocuments(null);
        }

        static SecurityCheck CheckDocuments(Action<SecurityProgress> progress)
        {
            return DocumentResult(ScanDocuments(ScanRoots(), true, progress));
        }

        internal static SecurityCheck CheckDocumentsAt(IEnumerable<string> roots)
        {
            return CheckDocumentsAt(roots, null);
        }

        internal static SecurityCheck CheckDocumentsAt(IEnumerable<string> roots, Action<SecurityProgress> progress)
        {
            return DocumentResult(ScanDocuments(roots, false, progress));
        }

        static DocumentScan ScanDocuments(IEnumerable<string> roots, bool includeRecycle, Action<SecurityProgress> progress)
        {
            DocumentScan scan = new DocumentScan();
            List<DocumentCandidate> candidates = new List<DocumentCandidate>();
            if (progress != null) progress(new SecurityProgress(true, 0, 0, 0, 0));
            foreach (string root in roots) CollectDirectory(root, scan, candidates, progress);
            if (includeRecycle) CollectRecycleBin(scan, candidates, progress);
            scan.Files = candidates.Count;
            if (progress != null) progress(new SecurityProgress(false, scan.Files, 0, scan.Files, 0));
            for (int i = 0; i < candidates.Count; i++)
            {
                InspectDocument(candidates[i], scan);
                if (progress != null && ((i + 1) % 10 == 0 || i + 1 == candidates.Count))
                    progress(new SecurityProgress(false, scan.Files, i + 1, scan.Files, scan.MatchCount));
            }
            return scan;
        }

        static SecurityCheck DocumentResult(DocumentScan scan)
        {
            string summary = L.F("Проверено: {0}; помечено как ДСК: {1}", scan.Files, scan.MatchCount);
            if (scan.MatchCount == 0)
                return New(scan.RecycleError.Length == 0 ? SecurityLevel.Good : SecurityLevel.Warning, summary, "");
            List<string> lines = new List<string>();
            foreach (DocumentMatch match in scan.Matches) lines.Add(match.DisplayPath + "  [" + match.Note + "]");
            SecurityCheck check = New(SecurityLevel.Bad, summary, string.Join("\r\n", lines.ToArray()));
            check.Items.Clear();
            foreach (DocumentMatch match in scan.Matches)
                check.Items.Add(new SecurityDetail(match.DisplayPath, match.Note, match.Path, match.Type, match.ToolTip));
            return check;
        }

        static void CollectRecycleBin(DocumentScan scan, List<DocumentCandidate> candidates, Action<SecurityProgress> progress)
        {
            scan.RecycleChecked = true;
            try
            {
                foreach (RecycleFile file in RecycleBin.Files())
                {
                    string extension = Path.GetExtension(file.OriginalName);
                    if (!OfficeExtensions.Contains(extension) || !File.Exists(file.Path)) continue;
                    scan.RecycleFiles++;
                    candidates.Add(new DocumentCandidate(file));
                    ReportCollected(progress, candidates.Count);
                }
            }
            catch (Exception e) { scan.RecycleError = e.Message; }
        }

        static List<string> ScanRoots()
        {
            List<string> roots = new List<string>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
                try { if (drive.IsReady && drive.DriveType == DriveType.Fixed) AddUnique(roots, drive.RootDirectory.FullName); } catch (Exception) { }
            return roots;
        }

        static readonly HashSet<string> SkipTopFolders = new HashSet<string>(new[]
        {
            "windows", "program files", "program files (x86)", "programdata", "$recycle.bin", "system volume information", "recovery",
            "perflogs", "msocache", "config.msi"
        }, StringComparer.OrdinalIgnoreCase);

        static void CollectDirectory(string root, DocumentScan scan, List<DocumentCandidate> candidates, Action<SecurityProgress> progress)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        string extension = Path.GetExtension(file);
                        if (!OfficeExtensions.Contains(extension)) continue;
                        candidates.Add(new DocumentCandidate(file));
                        ReportCollected(progress, candidates.Count);
                    }
                    foreach (string child in Directory.GetDirectories(directory))
                    {
                        DirectoryInfo info = new DirectoryInfo(child);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (IsTopLevel(root, child) && SkipTopFolders.Contains(info.Name)) continue;
                        pending.Push(child);
                    }
                }
                catch (UnauthorizedAccessException) { scan.SkippedFolders++; }
                catch (IOException) { scan.SkippedFolders++; }
            }
        }

        static void ReportCollected(Action<SecurityProgress> progress, int found)
        {
            if (progress != null && found % 25 == 0) progress(new SecurityProgress(true, found, 0, 0, 0));
        }

        static void InspectDocument(DocumentCandidate file, DocumentScan scan)
        {
            List<KeywordHit> hits = FindKeywordHits(Path.GetFileNameWithoutExtension(file.FileName), file.Path,
                OpenXmlExtensions.Contains(file.Extension));
            if (hits.Count == 0) return;
            scan.MatchCount++;
            if (scan.Matches.Count >= 200) { scan.MatchLimit = true; return; }
            scan.Matches.Add(file.Recycle
                ? new DocumentMatch(file.Path, file.DisplayPath, hits, true, file.DeletedAt)
                : new DocumentMatch(file.Path, hits));
        }

        static bool IsTopLevel(string root, string child)
        {
            try
            {
                string parent = (Path.GetDirectoryName(child.TrimEnd(Path.DirectorySeparatorChar)) ?? "").TrimEnd(Path.DirectorySeparatorChar);
                string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                return string.Equals(parent, basePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        static List<KeywordHit> FindKeywordHits(string fileName, string path, bool inspectContents)
        {
            int[] content = inspectContents ? KeywordCountsInOpenXml(path) : new int[DocumentKeywords.Length];
            List<KeywordHit> result = new List<KeywordHit>();
            for (int i = 0; i < DocumentKeywords.Length; i++)
            {
                int count = CountOccurrences(fileName, DocumentKeywords[i]) + content[i];
                if (count > 0) result.Add(new KeywordHit(DocumentKeywords[i], count));
            }
            return result;
        }

        static int[] KeywordCountsInOpenXml(string path)
        {
            int[] counts = new int[DocumentKeywords.Length];
            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Length > 50L * 1024 * 1024) return counts;
                using (ZipArchive archive = ZipFile.OpenRead(path))
                {
                    long read = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('\\', '/');
                        if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                            !(name.StartsWith("word/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))) continue;
                        if (entry.Length > 8L * 1024 * 1024 || read + entry.Length > 16L * 1024 * 1024) break;
                        read += entry.Length;
                        using (Stream stream = entry.Open())
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                        {
                            string text = Regex.Replace(Regex.Replace(reader.ReadToEnd(), "<[^>]+>", " "), @"\s+", " ");
                            for (int i = 0; i < DocumentKeywords.Length; i++) counts[i] += CountOccurrences(text, DocumentKeywords[i]);
                        }
                    }
                }
            }
            catch (Exception) { }
            return counts;
        }

        static int CountOccurrences(string text, string keyword)
        {
            int count = 0, start = 0;
            text = text ?? "";
            while (start < text.Length)
            {
                int found = text.IndexOf(keyword, start, StringComparison.CurrentCultureIgnoreCase);
                if (found < 0) break;
                // «ДСК-ЕП» is a stronger, separate marker; do not count its «ДСК» part twice.
                if (string.Equals(keyword, "дск", StringComparison.OrdinalIgnoreCase) &&
                    text.IndexOf("дск-еп", found, StringComparison.CurrentCultureIgnoreCase) == found)
                {
                    start = found + "дск-еп".Length;
                    continue;
                }
                count++;
                start = found + Math.Max(1, keyword.Length);
            }
            return count;
        }

        static string MatchNote(string path, List<KeywordHit> hits, bool recycle)
        {
            List<string> tags = LocationTags(path, recycle);
            int total = 0;
            foreach (KeywordHit hit in hits) total += hit.Count;
            string tagText = string.Join(" ", tags.ToArray());
            return tagText.Length > 0 ? tagText + " · " + total : total.ToString();
        }

        static string MatchToolTip(List<KeywordHit> hits, string deletedAt)
        {
            List<string> lines = new List<string>();
            foreach (KeywordHit hit in hits) lines.Add(DisplayKeyword(hit.Keyword) + " — " + hit.Count);
            if (!string.IsNullOrEmpty(deletedAt)) lines.Add(L.T("Удалён: ") + deletedAt);
            return string.Join("\r\n", lines.ToArray());
        }

        internal static List<string> LocationTags(string path, bool recycle)
        {
            List<string> tags = new List<string>();
            string p = "\\" + (path ?? "").Replace('/', '\\').Trim('\\') + "\\";
            if (recycle) tags.Add(L.T("#корзина"));
            if (IsClaudeLocation(p)) tags.Add("#Claude");
            if (ContainsPath(p, "telegram desktop") || ContainsPath(p, "telegram")) tags.Add(L.T("#телеграм"));
            if (p.IndexOf("whatsapp", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("#WhatsApp");
            if (ContainsPath(p, "desktop") || ContainsPath(p, "рабочий стол") || ContainsPath(p, "робочий стіл")) tags.Add("#Desktop");
            if (ContainsPath(p, "documents") || ContainsPath(p, "документы") || ContainsPath(p, "документи")) tags.Add(L.T("#документы"));
            if (ContainsPath(p, "downloads") || ContainsPath(p, "загрузки") || ContainsPath(p, "завантаження")) tags.Add(L.T("#даунлоадс"));
            return tags;
        }

        static bool IsClaudeLocation(string path)
        {
            if (ContainsPath(path, ".claude")) return true;
            if (!ContainsPath(path, "claude")) return false;
            return path.IndexOf("\\appdata\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ContainsPath(path, "cache") || ContainsPath(path, "code cache");
        }

        static bool ContainsPath(string path, string folder)
        {
            return path.IndexOf("\\" + folder + "\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string DisplayKeyword(string keyword)
        {
            if (string.Equals(keyword, "дск", StringComparison.OrdinalIgnoreCase)) return "ДСК";
            if (string.Equals(keyword, "дск-еп", StringComparison.OrdinalIgnoreCase)) return "ДСК-ЕП";
            if (string.Equals(keyword, "мосі", StringComparison.OrdinalIgnoreCase)) return "МОСІ";
            return keyword;
        }

        static string DocumentType(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension.StartsWith(".doc") || extension.StartsWith(".dot")) return "WORD";
            if (extension.StartsWith(".xls") || extension.StartsWith(".xlt")) return "EXCEL";
            return "PPT";
        }

        static SecurityCheck CheckAdministrators()
        {
            List<string> users = new List<string>();
            HashSet<string> administratorSids = LocalAdministratorMembers.Read();
            using (PrincipalContext machine = new PrincipalContext(ContextType.Machine))
            {
                foreach (LocalPassword account in LocalPassword.Read())
                {
                    if (account.Disabled) continue;
                    using (UserPrincipal user = UserPrincipal.FindByIdentity(machine, IdentityType.SamAccountName, account.Name))
                        if (user != null && user.Sid != null && administratorSids.Contains(user.Sid.Value)) AddUnique(users, account.Name);
                }
            }
            users.Sort(StringComparer.CurrentCultureIgnoreCase);
            string details = users.Count == 0 ? L.T("Включённых локальных администраторов не найдено.") : string.Join("\r\n", users.ToArray());
            if (users.Count == 0) return New(SecurityLevel.Warning, L.T("Локальные администраторы не найдены"), details);
            if (AdministratorLevel(users.Count) == SecurityLevel.Good) return New(SecurityLevel.Good, L.F("Локальных администраторов: {0} — допустимо", users.Count), details);
            if (AdministratorLevel(users.Count) == SecurityLevel.Warning) return New(SecurityLevel.Warning, L.F("Локальных администраторов: {0} — больше нормы", users.Count), details);
            return New(SecurityLevel.Bad, L.F("Локальных администраторов: {0} — критически много", users.Count), details);
        }

        internal static SecurityLevel AdministratorLevel(int count)
        {
            if (count == 0) return SecurityLevel.Warning;
            if (count <= 2) return SecurityLevel.Good;
            if (count <= 4) return SecurityLevel.Warning;
            return SecurityLevel.Bad;
        }

        static SecurityCheck CheckPasswords()
        {
            List<LocalPassword> users = LocalPassword.Read();
            List<string> expired = new List<string>();
            foreach (LocalPassword user in users)
            {
                if (user.Disabled) continue;
                if (user.Expired) expired.Add(user.Name);
            }
            if (expired.Count == 0) return New(SecurityLevel.Good, L.T("Просроченных паролей локальных учётных записей нет"), "");
            return New(SecurityLevel.Bad, L.T("Просроченные пароли: ") + string.Join(", ", expired.ToArray()), string.Join("\r\n", expired.ToArray()));
        }

        static SecurityCheck New(SecurityLevel level, string summary, string details)
        {
            return new SecurityCheck("", "", level, summary, details);
        }

        static void AddUnique(List<string> values, string value)
        {
            foreach (string old in values) if (string.Equals(old, value, StringComparison.OrdinalIgnoreCase)) return;
            values.Add(value);
        }
    }

    static class LocalAdministratorMembers
    {
        const int MAX_PREFERRED_LENGTH = -1, ERROR_MORE_DATA = 234;

        [StructLayout(LayoutKind.Sequential)]
        struct LOCALGROUP_MEMBERS_INFO_0 { public IntPtr sid; }

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        static extern int NetLocalGroupGetMembers(string server, string group, int level, out IntPtr buffer, int preferredLength,
            out int entriesRead, out int totalEntries, ref IntPtr resumeHandle);
        [DllImport("Netapi32.dll")] static extern int NetApiBufferFree(IntPtr buffer);

        public static HashSet<string> Read()
        {
            SecurityIdentifier sid = new SecurityIdentifier("S-1-5-32-544");
            string translated = sid.Translate(typeof(NTAccount)).Value;
            int slash = translated.IndexOf('\\');
            string group = slash >= 0 ? translated.Substring(slash + 1) : translated;
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr resume = IntPtr.Zero;
            int code;
            do
            {
                IntPtr buffer;
                int entries, total;
                code = NetLocalGroupGetMembers(null, group, 0, out buffer, MAX_PREFERRED_LENGTH, out entries, out total, ref resume);
                if (code != 0 && code != ERROR_MORE_DATA)
                    throw new InvalidOperationException(L.T("Не удалось прочитать локальную группу администраторов, код ") + code + ".");
                try
                {
                    int size = Marshal.SizeOf(typeof(LOCALGROUP_MEMBERS_INFO_0));
                    IntPtr current = buffer;
                    for (int i = 0; i < entries; i++)
                    {
                        LOCALGROUP_MEMBERS_INFO_0 native = (LOCALGROUP_MEMBERS_INFO_0)Marshal.PtrToStructure(current, typeof(LOCALGROUP_MEMBERS_INFO_0));
                        if (native.sid != IntPtr.Zero) result.Add(new SecurityIdentifier(native.sid).Value);
                        current = new IntPtr(current.ToInt64() + size);
                    }
                }
                finally { if (buffer != IntPtr.Zero) NetApiBufferFree(buffer); }
            } while (code == ERROR_MORE_DATA);
            return result;
        }
    }

    public class RecycleFile
    {
        public string Path = "", OriginalName = "", OriginalFolder = "", DeletedAt = "";
    }

    // The Recycle Bin is a virtual Shell folder. Enumerating it through Shell.Application preserves
    // the original display name and location without depending on the undocumented $I/$R file format.
    public static class RecycleBin
    {
        const int BitBucket = 10;
        const BindingFlags Member = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        public static List<RecycleFile> Files()
        {
            List<RecycleFile> result = new List<RecycleFile>();
            object shell = null, folder = null, items = null;
            try
            {
                Type type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) throw new InvalidOperationException(L.T("Windows Shell недоступен."));
                shell = Activator.CreateInstance(type);
                folder = Invoke(shell, "NameSpace", new object[] { BitBucket });
                if (folder == null) throw new InvalidOperationException(L.T("Не удалось открыть корзину Windows."));
                items = Invoke(folder, "Items", null);
                int count = Convert.ToInt32(Property(items, "Count"));
                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = Invoke(items, "Item", new object[] { i });
                        if (item == null) continue;
                        RecycleFile file = new RecycleFile();
                        file.Path = Convert.ToString(Property(item, "Path")) ?? "";
                        file.OriginalName = Convert.ToString(Property(item, "Name")) ?? "";
                        file.OriginalFolder = Convert.ToString(Invoke(item, "ExtendedProperty", new object[] { "System.Recycle.DeletedFrom" })) ?? "";
                        object deleted = Invoke(item, "ExtendedProperty", new object[] { "System.Recycle.DateDeleted" });
                        if (deleted is DateTime) file.DeletedAt = ((DateTime)deleted).ToString("g");
                        else if (deleted != null) file.DeletedAt = Convert.ToString(deleted) ?? "";
                        if (file.Path.Length > 0 && file.OriginalName.Length > 0) result.Add(file);
                    }
                    catch (Exception) { }
                    finally { Release(item); }
                }
            }
            finally
            {
                Release(items); Release(folder); Release(shell);
            }
            return result;
        }

        static object Property(object target, string name)
        {
            return target.GetType().InvokeMember(name, Member | BindingFlags.GetProperty, null, target, null);
        }

        static object Invoke(object target, string name, object[] args)
        {
            return target.GetType().InvokeMember(name, Member | BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, target, args);
        }

        static void Release(object value)
        {
            try { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
            catch (Exception) { }
        }
    }

    public class LocalPassword
    {
        public string Name;
        public bool Disabled, Expired;

        const int FILTER_NORMAL_ACCOUNT = 2, MAX_PREFERRED_LENGTH = -1, ERROR_MORE_DATA = 234;
        const uint UF_ACCOUNTDISABLE = 0x0002, UF_PASSWORD_EXPIRED = 0x800000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct USER_INFO_3
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string name;
            [MarshalAs(UnmanagedType.LPWStr)] public string password;
            public uint password_age, priv;
            [MarshalAs(UnmanagedType.LPWStr)] public string home_dir;
            [MarshalAs(UnmanagedType.LPWStr)] public string comment;
            public uint flags;
            [MarshalAs(UnmanagedType.LPWStr)] public string script_path;
            public uint auth_flags;
            [MarshalAs(UnmanagedType.LPWStr)] public string full_name;
            [MarshalAs(UnmanagedType.LPWStr)] public string usr_comment;
            [MarshalAs(UnmanagedType.LPWStr)] public string parms;
            [MarshalAs(UnmanagedType.LPWStr)] public string workstations;
            public uint last_logon, last_logoff, acct_expires, max_storage, units_per_week;
            public IntPtr logon_hours;
            public uint bad_pw_count, num_logons;
            [MarshalAs(UnmanagedType.LPWStr)] public string logon_server;
            public uint country_code, code_page;
            public uint user_id, primary_group_id;
            [MarshalAs(UnmanagedType.LPWStr)] public string profile;
            [MarshalAs(UnmanagedType.LPWStr)] public string home_dir_drive;
            public uint password_expired;
        }

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int NetUserEnum(string server, int level, int filter, out IntPtr buffer, int preferredLength,
            out int entriesRead, out int totalEntries, ref int resumeHandle);
        [DllImport("Netapi32.dll")] static extern int NetApiBufferFree(IntPtr buffer);

        public static List<LocalPassword> Read()
        {
            List<LocalPassword> result = new List<LocalPassword>();
            int resume = 0, code;
            do
            {
                IntPtr buffer;
                int entries, total;
                code = NetUserEnum(null, 3, FILTER_NORMAL_ACCOUNT, out buffer, MAX_PREFERRED_LENGTH, out entries, out total, ref resume);
                if (code != 0 && code != ERROR_MORE_DATA) throw new InvalidOperationException(L.T("Windows не вернула локальные учётные записи, код ") + code + ".");
                try
                {
                    int size = Marshal.SizeOf(typeof(USER_INFO_3));
                    IntPtr current = buffer;
                    for (int i = 0; i < entries; i++)
                    {
                        USER_INFO_3 native = (USER_INFO_3)Marshal.PtrToStructure(current, typeof(USER_INFO_3));
                        LocalPassword user = new LocalPassword();
                        user.Name = native.name ?? "";
                        user.Disabled = (native.flags & UF_ACCOUNTDISABLE) != 0;
                        user.Expired = native.password_expired != 0 || (native.flags & UF_PASSWORD_EXPIRED) != 0;
                        result.Add(user);
                        current = new IntPtr(current.ToInt64() + size);
                    }
                }
                finally { if (buffer != IntPtr.Zero) NetApiBufferFree(buffer); }
            } while (code == ERROR_MORE_DATA);
            result.Sort(delegate (LocalPassword x, LocalPassword y) { return string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase); });
            return result;
        }
    }
}
