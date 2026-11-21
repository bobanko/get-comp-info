using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CompInfo
{
    // Hands the filled form of a section to the copy started with administrator rights, so the change goes on there
    // without filling the form and pressing the button again. Passwords are included: the pipe opens only for
    // Administrators (the copy without rights cannot be connected to by other programs of the user), and the
    // receiving copy checks, when Windows lets it, that the sender is this same program.
    public static class AdminHandoff
    {
        public static System.IO.Pipes.NamedPipeServerStream Create(out string name)
        {
            name = "comp-info-" + Guid.NewGuid().ToString("N");
            System.IO.Pipes.PipeSecurity security = new System.IO.Pipes.PipeSecurity();
            security.AddAccessRule(new System.IO.Pipes.PipeAccessRule(new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),
                System.IO.Pipes.PipeAccessRights.ReadWrite, System.Security.AccessControl.AccessControlType.Allow));
            return new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.Out, 1, System.IO.Pipes.PipeTransmissionMode.Byte,
                System.IO.Pipes.PipeOptions.Asynchronous, 0, 0, security);
        }

        // False when the new copy did not connect in time.
        public static async System.Threading.Tasks.Task<bool> Send(System.IO.Pipes.NamedPipeServerStream pipe, JsonObject form, int milliseconds)
        {
            System.Threading.Tasks.Task connect = pipe.WaitForConnectionAsync();
            if (await System.Threading.Tasks.Task.WhenAny(connect, System.Threading.Tasks.Task.Delay(milliseconds)) != connect) return false;
            await connect;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Json.Compact(form));
            await pipe.WriteAsync(bytes, 0, bytes.Length);
            await System.Threading.Tasks.Task.Run(delegate { pipe.WaitForPipeDrain(); });
            return true;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint processId);

        // The form, or null when there is none or it does not come from this program.
        public static JsonObject Receive(string name)
        {
            try
            {
                using (System.IO.Pipes.NamedPipeClientStream pipe = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.In))
                {
                    pipe.Connect(10000);
                    uint id;
                    if (GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out id))
                    {
                        string sender = null;
                        try { using (Process p = Process.GetProcessById((int)id)) sender = p.MainModule.FileName; }
                        catch (Exception) { sender = null; }   // another user's process may be closed to us
                        if (sender != null && !string.Equals(System.IO.Path.GetFullPath(sender), System.IO.Path.GetFullPath(Process.GetCurrentProcess().MainModule.FileName), StringComparison.OrdinalIgnoreCase))
                            return null;
                    }
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(pipe, System.Text.Encoding.UTF8))
                        return Json.ParseObject(reader.ReadToEnd());
                }
            }
            catch (Exception) { return null; }
        }
    }


    // The name of the domain account, remembered in comp-info.ini. Passwords are never kept: they are typed into the dialog
    // of Windows each time (CredentialPrompt). A password kept by an older version (only masked) is wiped at once
    // (user's choice 21.11.2026: a flash drive goes from computer to computer and can be lost).
    public static class DomainAccount
    {
        public static string Load()
        {
            if (Settings.Get("domainPassword", "").Length > 0) Settings.Set("domainPassword", "");
            return Settings.Get("domainUser", "");
        }

        public static void Save(string user) { Settings.Set("domainUser", (user ?? "").Trim()); }
    }
    // The record of this computer in the local database, saved right after a change of name, domain or address.
    public static class RecordUpdate
    {
        // A line for the status of the page: saved, or why it was not. Runs on a worker thread.
        public static string Save(Collection.Expected expected, out bool saved)
        {
            saved = false;
            try
            {
                string skipped;
                JsonObject record = Collection.SaveAfterChange(AppPaths.Data, expected, out skipped);
                if (record == null) return L.T("Запись в локальной базе не обновлена: ") + skipped + ".";
                saved = true;
                string ip = record.Text("ip");
                return L.T("Запись в локальной базе обновлена: ") + record.Text("hostname") + (string.IsNullOrEmpty(ip) ? "" : ", " + ip) +
                    L.T(". Синхронизируйте базу, когда будет интернет.");
            }
            catch (Exception e) { return L.T("Запись в локальной базе не обновлена: ") + e.Message; }
        }
    }
}
