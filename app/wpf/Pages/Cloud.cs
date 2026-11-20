using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // Connection with the personal key (CloudSession of CloudPages.cs on WinForms): from cloud-key.txt,
    // or asked from the user in KeyWindow.
    public static class CloudConnect
    {
        // The client with a checked key, or null when the user cancels.
        public static async Task<CloudClient> Connect(Window owner)
        {
            string root = AppPaths.Root;
            string apiUrl = CloudConfig.ReadApiUrl(root);
            string fileKey = CloudClient.KeyFromFile(root);
            string error = null;
            if (fileKey != null)
            {
                var client = new CloudClient(apiUrl, fileKey);
                string reason = null;
                JsonObject me = await Task.Run(() => { string r; JsonObject m = client.Me(out r); reason = r; return m; });
                if (me != null) { client.User = me; return client; }
                error = L.T("Ключ из cloud-key.txt не подошёл: ") + reason;
            }
            var dialog = new KeyWindow(apiUrl, fileKey != null, error) { Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            if (dialog.ShowDialog() != true) return null;
            if (dialog.SaveKey) CloudClient.SaveKey(root, dialog.Client.Key);
            return dialog.Client;
        }
    }

    // Asks the personal key; checks it with the cloud before closing (KeyDialog on WinForms).
    public class KeyWindow : Window
    {
        readonly string apiUrl;
        readonly PasswordBox key = new PasswordBox { Margin = new Thickness(0, 10, 0, 10) };
        readonly CheckBox save = new CheckBox();
        readonly StatusLine status = new StatusLine { Margin = new Thickness(0, 12, 0, 0) };
        readonly Button connect = new Button { IsDefault = true };
        public CloudClient Client { get; private set; }
        public bool SaveKey { get { return save.IsChecked == true; } }

        public KeyWindow(string apiUrl, bool replacing, string error)
        {
            this.apiUrl = apiUrl;
            Title = L.T("Ключ доступа к облаку");
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");

            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
            Content = root;
            root.Children.Add(new TextBlock { Text = Brand.Keep(L.T("Введите личный ключ доступа. Его создают в веб-интерфейсе облака (раздел «Ключи»).")), TextWrapping = TextWrapping.Wrap });
            System.Windows.Automation.AutomationProperties.SetName(key, L.T("Ключ доступа к облаку"));
            root.Children.Add(key);
            save.Content = replacing ? L.T("Заменить ключ в cloud-key.txt") : L.T("Сохранить ключ в этой копии программы");
            root.Children.Add(save);
            var warning = new TextBlock
            {
                Text = Brand.Keep(L.T("Тот, кто получит флешку с сохранённым ключом, сможет синхронизироваться от вашего имени. ") +
                    L.T("Если такая флешка потеряется, сразу отзовите ключ в веб-интерфейсе.")),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
            };
            warning.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            root.Children.Add(warning);
            if (error != null) status.Show(Severity.Error, Glyph.Warning, error);
            root.Children.Add(status);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            Glyph.Label(connect, Glyph.Cloud, L.T("Подключиться"));
            connect.SetResourceReference(StyleProperty, "AccentButton");
            connect.Click += delegate { Check(); };
            var cancel = new Button { Content = L.T("Отмена"), IsCancel = true, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { DialogResult = false; };   // IsCancel alone answers only Esc
            actions.Children.Add(connect);
            actions.Children.Add(cancel);
            root.Children.Add(actions);

            WpfTheme.DarkTitle(this);
            Loaded += delegate { key.Focus(); };
        }

        async void Check()
        {
            string value = key.Password.Trim();
            if (value.Length == 0) { status.Show(Severity.Error, Glyph.Warning, L.T("Ключ не может быть пустым.")); return; }
            connect.IsEnabled = false;
            status.Note(Glyph.More, L.T("Проверка ключа…"));
            try
            {
                var client = new CloudClient(apiUrl, value);
                string reason = null;
                JsonObject me = await Task.Run(() => { string r; JsonObject m = client.Me(out r); reason = r; return m; });
                if (me == null) { status.Show(Severity.Error, Glyph.Warning, reason); return; }
                client.User = me;
                Client = client;
                DialogResult = true;
            }
            catch (Exception e) { status.Show(Severity.Error, Glyph.Warning, e.Message); }
            finally { connect.IsEnabled = true; }
        }
    }
}
