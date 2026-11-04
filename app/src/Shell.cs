using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace CompInfo
{
    // System changes go through the same Windows PowerShell commands the console version used
    // (New-NetIPAddress, Add-Computer, ...), so their behaviour stays exactly as tested.
    public static class PowerShell
    {
        public static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static string Quote(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        // Runs the script hidden and returns what it printed. A failure throws with the script's own message.
        // Secrets go through standard input (base64 lines, read with Read-Secret), never through the command line.
        // elevate: ask for administrator rights (UAC) for this one command.
        public static string Run(string script, string[] secrets, bool elevate)
        {
            string errorFile = Path.Combine(Path.GetTempPath(), "comp-info-" + Guid.NewGuid().ToString("N") + ".error");
            string outputFile = errorFile.Replace(".error", ".output");
            string wrapped =
                "$ErrorActionPreference = 'Stop'\n" +
                "$ProgressPreference = 'SilentlyContinue'\n" +
                "function Read-Secret { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadLine())) }\n" +
                "try {\n" +
                "  $output = & {\n" + script + "\n  } | Out-String\n" +
                "  [IO.File]::WriteAllText(" + Quote(outputFile) + ", [string]$output, [Text.UTF8Encoding]::new($false))\n" +
                "  exit 0\n" +
                "}\ncatch {\n" +
                "  [IO.File]::WriteAllText(" + Quote(errorFile) + ", $_.Exception.Message, [Text.UTF8Encoding]::new($false))\n" +
                "  exit 1\n}\n";
            string arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
            ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), arguments);
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.CreateNoWindow = true;
            if (elevate)
            {
                if (secrets != null && secrets.Length > 0) throw new InvalidOperationException("Секреты нельзя передать в отдельно повышенный процесс.");
                info.UseShellExecute = true;
                info.Verb = "runas";
            }
            else
            {
                info.UseShellExecute = false;
                info.RedirectStandardInput = true;
            }
            try
            {
                Process p;
                try { p = Process.Start(info); }
                catch (Win32Exception e)
                {
                    if (e.NativeErrorCode == 1223) throw new OperationCanceledException("Права администратора не получены: запрос Windows был отклонён.");
                    throw;
                }
                using (p)
                {
                    if (!elevate)
                    {
                        foreach (string s in secrets ?? new string[0]) p.StandardInput.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")));
                        p.StandardInput.Close();
                    }
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        string message = File.Exists(errorFile) ? File.ReadAllText(errorFile, Encoding.UTF8).Trim() : "";
                        throw new InvalidOperationException(message.Length > 0 ? message : "Команда Windows завершилась с кодом " + p.ExitCode + ".");
                    }
                    return File.Exists(outputFile) ? File.ReadAllText(outputFile, Encoding.UTF8).Trim() : "";
                }
            }
            finally
            {
                try { File.Delete(errorFile); } catch (Exception) { }
                try { File.Delete(outputFile); } catch (Exception) { }
            }
        }
    }
}
