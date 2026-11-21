using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace CompInfo.Wpf
{
    // Unexpected errors of the three programs (WINDOWS_APP.md, «Отчёт о сбое»): a text file with what the developer
    // needs to find the error, and a message that offers to copy it or show it in Explorer. Nothing is sent anywhere:
    // the user decides whether to mail the report. The user and computer names are replaced in it, so a path such as
    // C:\Users\<name>\… does not give them away.
    public static class CrashReport
    {
        // "SUSpector", "youSBee", "CHAGEAR Toolbox" and where to send the report (the address of «О программе»).
        static string program = "", email = "";
        // The folder of the reports; null — %LOCALAPPDATA%\CHAGEAR\crash-reports.
        static string folder;
        const int Keep = 20;
        static bool showing;

        // Catches the unhandled errors of the window thread (the program goes on) and of the other threads
        // (Windows ends the program after the message).
        public static void Install(Application app, string programName, string mail, string reportFolder = null)
        {
            program = programName;
            email = mail;
            folder = reportFolder;
            app.DispatcherUnhandledException += (s, e) => { e.Handled = true; Handle(e.Exception, false); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var error = e.ExceptionObject as Exception;
                // the file first: if the message cannot be shown any more, the report still exists
                string text = Build(error, e.IsTerminating);
                string file = Save(text);
                try { app.Dispatcher.Invoke(new Action(() => Show(error, text, file, e.IsTerminating))); }
                catch (Exception) { }
            };
            // <program>.exe --crash-test: an error right after the start, to see the message and where the report goes
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--crash-test") > 0)
                app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(() => { throw new InvalidOperationException("Crash test (--crash-test)"); }));
        }

        static void Handle(Exception error, bool fatal)
        {
            string text = Build(error, fatal);
            Show(error, text, Save(text), fatal);
        }

        static void Show(Exception error, string text, string file, bool fatal)
        {
            // an error inside the message (or a stream of them) gets its file, not a second message
            if (showing) return;
            showing = true;
            try
            {
                Window owner = Application.Current == null ? null : Application.Current.MainWindow;
                string message = (error == null ? L.T("неизвестно") : Hide(error.Message)) + "\n\n" +
                    (file != null ? L.T("Отчёт об ошибке сохранён. Пришлите его разработчику на ") + email +
                        L.T(" — это поможет исправить ошибку. Имя пользователя и компьютера в отчёт не попадают.")
                    : L.T("Отчёт сохранить не удалось, но его можно скопировать и прислать разработчику на ") + email + ".") +
                    (fatal ? "\n\n" + L.T("Программа будет закрыта.") : "");
                var answers = file != null
                    ? new[] { L.T("Скопировать отчёт"), L.T("Показать файл"), L.T("Закрыть") }
                    : new[] { L.T("Скопировать отчёт"), L.T("Закрыть") };
                int choice = Dialog.Choose(owner, L.T("Непредвиденная ошибка"), message, Severity.Error, answers);
                if (choice == 0) Copy(owner, text);
                else if (choice == 1 && file != null) Reveal(file);
            }
            catch (Exception) { }
            finally { showing = false; }
        }

        static void Copy(Window owner, string text)
        {
            // the clipboard can be busy with another program for a moment
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { Clipboard.SetText(text); Dialog.Show(owner, L.T("Отчёт скопирован. Вставьте его в письмо разработчику на ") + email + ".", Severity.Success); return; }
                catch (Exception) { System.Threading.Thread.Sleep(100); }
            }
            Dialog.Show(owner, L.T("Не удалось скопировать отчёт: буфер обмена занят другой программой."));
        }

        static void Reveal(string file)
        {
            try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch (Exception) { }
        }

        // ---------- the report ----------

        public static string Build(Exception error, bool fatal)
        {
            var b = new StringBuilder();
            b.AppendLine(program + " " + AppVersion.Text + " — crash report");
            b.AppendLine("Time:      " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            b.AppendLine("Windows:   " + Safe(WindowsVersion));
            b.AppendLine(".NET:      " + Safe(FrameworkVersion));
            b.AppendLine("Language:  " + L.Code + " (Windows: " + Safe(() => System.Globalization.CultureInfo.CurrentUICulture.Name) + ")");
            b.AppendLine("Admin:     " + Safe(() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? "yes" : "no"));
            b.AppendLine("Runs from: " + Safe(DriveKind));
            b.AppendLine("Ends:      " + (fatal ? "yes, Windows closes the program" : "no, the program goes on"));
            b.AppendLine();
            b.AppendLine(error == null ? "(no exception object)" : error.ToString());
            return Hide(b.ToString());
        }

        static string Safe(Func<string> read)
        {
            try { return read(); } catch (Exception e) { return "? (" + e.GetType().Name + ")"; }
        }

        static string WindowsVersion()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (key == null) return Environment.OSVersion.VersionString;
                // ProductName says «Windows 10» on Windows 11 too: the build tells them apart (22000 and later is 11).
                return key.GetValue("ProductName") + " " + key.GetValue("DisplayVersion") + ", build " +
                    key.GetValue("CurrentBuild") + "." + key.GetValue("UBR") + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit");
            }
        }

        static string FrameworkVersion()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                return "4.x release " + (key == null ? "?" : key.GetValue("Release")) + (Environment.Is64BitProcess ? ", 64-bit process" : ", 32-bit process");
        }

        static string DriveKind()
        {
            string root = Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory);
            if (root.StartsWith(@"\\")) return "network folder";
            return new DriveInfo(root).DriveType == DriveType.Removable ? "removable drive" : "fixed disk";
        }

        // The user and computer names out of the text (paths, messages of Windows).
        static string Hide(string text)
        {
            text = Replace(text, Environment.UserName, "<user>");
            text = Replace(text, Environment.MachineName, "<computer>");
            return text;
        }

        static string Replace(string text, string value, string mark)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 3) return text;
            var b = new StringBuilder();
            int at = 0, found;
            while ((found = text.IndexOf(value, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                b.Append(text, at, found - at).Append(mark);
                at = found + value.Length;
            }
            return b.Append(text, at, text.Length - at).ToString();
        }

        // ---------- the file ----------

        static string Folder
        {
            get
            {
                return folder ?? Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CHAGEAR"), "crash-reports");
            }
        }

        // <program>-crash-<time>.txt; the oldest ones go when there are more than Keep. Null when it cannot be written.
        static string Save(string text)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string name = new string(program.Where(c => char.IsLetterOrDigit(c)).ToArray());
                string file = Path.Combine(Folder, name + "-crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
                File.WriteAllText(file, text, new UTF8Encoding(true));
                foreach (var old in new DirectoryInfo(Folder).GetFiles(name + "-crash-*.txt").OrderByDescending(f => f.Name).Skip(Keep))
                {
                    try { old.Delete(); } catch (Exception) { }
                }
                return file;
            }
            catch (Exception) { return null; }
        }
    }
}
