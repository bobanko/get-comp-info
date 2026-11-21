using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace CompInfo.Wpf
{
    // The account and password through the dialog of Windows itself (CredUIPromptForWindowsCredentials, as for a network
    // folder): the program never shows a password field and never keeps a password, only the name (user's choice 21.11.2026).
    public static class CredentialPrompt
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CREDUI_INFO
        {
            public int cbSize;
            public IntPtr hwndParent;
            public string pszMessageText;
            public string pszCaptionText;
            public IntPtr hbmBanner;
        }

        [DllImport("credui.dll", CharSet = CharSet.Unicode)]
        static extern int CredUIPromptForWindowsCredentials(ref CREDUI_INFO info, int authError, ref uint authPackage, IntPtr inAuthBuffer,
            uint inAuthBufferSize, out IntPtr outAuthBuffer, out uint outAuthBufferSize, ref bool save, int flags);

        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredPackAuthenticationBuffer(int flags, string user, string password, IntPtr packed, ref int size);

        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CredUnPackAuthenticationBuffer(int flags, IntPtr buffer, uint size, StringBuilder user, ref int userMax,
            StringBuilder domain, ref int domainMax, StringBuilder password, ref int passwordMax);

        [DllImport("ole32.dll")]
        static extern void CoTaskMemFree(IntPtr p);

        const int CREDUIWIN_GENERIC = 0x1;              // any user name as typed (DOMAIN\name, .\name), the password as typed
        const int CRED_PACK_GENERIC_CREDENTIALS = 0x4;

        // The dialog of Windows over owner; user comes in filled (when known) and goes out as typed.
        // false: the user cancelled it.
        public static bool Ask(Window owner, string caption, string message, ref string user, out string password)
        {
            password = null;
            var info = new CREDUI_INFO
            {
                cbSize = Marshal.SizeOf(typeof(CREDUI_INFO)),
                hwndParent = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle,
                pszCaptionText = caption,
                pszMessageText = message,
            };
            IntPtr inBuffer = IntPtr.Zero;
            int inSize = 0;
            if (!string.IsNullOrEmpty(user))
            {
                CredPackAuthenticationBuffer(CRED_PACK_GENERIC_CREDENTIALS, user, "", IntPtr.Zero, ref inSize);
                if (inSize > 0)
                {
                    inBuffer = Marshal.AllocCoTaskMem(inSize);
                    if (!CredPackAuthenticationBuffer(CRED_PACK_GENERIC_CREDENTIALS, user, "", inBuffer, ref inSize))
                    {
                        Marshal.FreeCoTaskMem(inBuffer);
                        inBuffer = IntPtr.Zero;
                        inSize = 0;
                    }
                }
            }
            uint package = 0;
            IntPtr outBuffer;
            uint outSize;
            bool save = false;
            int result;
            try
            {
                result = CredUIPromptForWindowsCredentials(ref info, 0, ref package, inBuffer, (uint)inSize, out outBuffer, out outSize, ref save, CREDUIWIN_GENERIC);
            }
            finally { if (inBuffer != IntPtr.Zero) Marshal.FreeCoTaskMem(inBuffer); }
            if (result != 0) return false;   // 1223: cancelled

            try
            {
                var name = new StringBuilder(514);
                var domain = new StringBuilder(338);
                var secret = new StringBuilder(514);
                int nameMax = name.Capacity, domainMax = domain.Capacity, secretMax = secret.Capacity;
                bool ok = CredUnPackAuthenticationBuffer(0, outBuffer, outSize, name, ref nameMax, domain, ref domainMax, secret, ref secretMax);
                if (!ok)
                {
                    nameMax = name.Capacity; domainMax = domain.Capacity; secretMax = secret.Capacity;
                    ok = CredUnPackAuthenticationBuffer(CRED_PACK_GENERIC_CREDENTIALS, outBuffer, outSize, name, ref nameMax, domain, ref domainMax, secret, ref secretMax);
                }
                if (!ok) return false;
                user = domain.Length > 0 ? domain + "\\" + name : name.ToString();
                password = secret.ToString();
                secret.Clear();
                return true;
            }
            finally
            {
                // the password is wiped from the buffer of Windows before it is given back
                if (outBuffer != IntPtr.Zero)
                {
                    Marshal.Copy(new byte[outSize], 0, outBuffer, (int)outSize);
                    CoTaskMemFree(outBuffer);
                }
            }
        }
    }

    // An account of a form: a button with its name that opens the dialog of Windows, and «забыть» after it.
    // The password lives only in memory until the change is made.
    public class AccountChoice : System.Windows.Controls.StackPanel
    {
        readonly System.Windows.Controls.Button pick = new System.Windows.Controls.Button { MinWidth = 260 };
        readonly System.Windows.Controls.Button forget = new System.Windows.Controls.Button { Margin = new Thickness(8, 0, 0, 0) };
        readonly string caption, message;
        public string User { get; private set; }
        public string Password { get; private set; }
        // A new name chosen or forgotten (the domain account keeps its name in comp-info.ini).
        public Action<string> UserChanged;

        public AccountChoice(string caption, string message)
        {
            this.caption = caption;
            this.message = message;
            Orientation = System.Windows.Controls.Orientation.Horizontal;
            User = "";
            pick.Click += delegate { Ask(); };
            Glyph.Label(forget, Glyph.Cross, L.T("Забыть"));
            forget.ToolTip = L.T("Забыть имя и введённый пароль");
            forget.Click += delegate { Set("", null); if (UserChanged != null) UserChanged(""); };
            Children.Add(pick);
            Children.Add(forget);
            Show();
        }

        public bool HasPassword { get { return Password != null; } }

        public void Set(string user, string password)
        {
            User = (user ?? "").Trim();
            Password = password;
            Show();
        }

        public void ClearPassword() { Password = null; Show(); }

        // The dialog of Windows; false when the user cancelled it.
        public bool Ask()
        {
            string user = User, password;
            if (!CredentialPrompt.Ask(Window.GetWindow(this), caption, message, ref user, out password)) return false;
            bool renamed = !string.Equals(user, User, StringComparison.OrdinalIgnoreCase);
            Set(user, password);
            if (renamed && UserChanged != null) UserChanged(User);
            return true;
        }

        void Show()
        {
            string text = User.Length == 0 ? L.T("Указать учётную запись…")
                : User + "  ·  " + (HasPassword ? L.T("пароль введён") : L.T("ввести пароль"));
            Glyph.Label(pick, HasPassword ? Glyph.Check : Glyph.Person, text);
            forget.Visibility = User.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void FocusButton() { pick.Focus(); }
    }
}
