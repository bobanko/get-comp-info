using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // «Обновление SUSpector» (SuspectorUpdateDialog.cs on WinForms): the version on the site, «Что нового»,
    // download with the SHA-256 check and the replacement of the running exe (SuspectorUpdate.cs), restart.
    public class UpdateWindow : Window
    {
        readonly TextBlock versions = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        readonly TextBlock notes = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly StatusLine status = new StatusLine { Margin = new Thickness(0, 10, 0, 10) };
        readonly ProgressBar progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Hidden, Margin = new Thickness(0, 10, 0, 0) };
        readonly Button check = new Button(), install = new Button(), restart = new Button(), close = new Button();
        JsonObject release;
        bool running;

        public UpdateWindow()
        {
            Title = L.T("Обновление SUSpector");
            Width = 600; Height = 440;
            MinWidth = 480; MinHeight = 340;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");

            var root = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
            Content = root;
            versions.Text = L.T("Установлена версия: ") + AppVersion.Text;
            DockPanel.SetDock(versions, Dock.Top);
            root.Children.Add(versions);

            var actions = new WrapPanel();
            Glyph.Label(check, Glyph.Refresh, L.T("Проверить обновления"));
            check.SetResourceReference(StyleProperty, "AccentButton");
            check.Click += delegate { Check(); };
            Glyph.Label(install, Glyph.Update, L.T("Установить"));
            install.SetResourceReference(StyleProperty, "AccentButton");
            install.Visibility = Visibility.Collapsed;
            install.Click += delegate { Install(); };
            Glyph.Label(restart, Glyph.Power, L.T("Перезапустить программу"));
            restart.SetResourceReference(StyleProperty, "AccentButton");
            restart.Visibility = Visibility.Collapsed;
            restart.Click += delegate { Restart(); };
            close.Content = L.T("Закрыть");
            close.MinWidth = 100;
            close.Click += delegate { Close(); };
            foreach (Button b in new[] { check, install, restart, close }) { b.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(b); }
            DockPanel.SetDock(actions, Dock.Top);
            root.Children.Add(actions);

            progress.SetResourceReference(StyleProperty, "ThinProgress");
            DockPanel.SetDock(progress, Dock.Top);
            root.Children.Add(progress);
            DockPanel.SetDock(status, Dock.Top);
            root.Children.Add(status);
            root.Children.Add(new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            WpfTheme.DarkTitle(this);
            Loaded += delegate { Check(); };
            Closing += (s, e) =>
            {
                if (!running) return;
                e.Cancel = true;
                Dialog.Show(this, L.T("Дождитесь окончания обновления."), Severity.Info, L.T("Обновление"));
            };
        }

        void Busy(bool value)
        {
            running = value;
            progress.Visibility = value ? Visibility.Visible : Visibility.Hidden;
            check.IsEnabled = install.IsEnabled = close.IsEnabled = !value;
        }

        static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        async void Check()
        {
            Busy(true);
            install.Visibility = restart.Visibility = Visibility.Collapsed;
            notes.Text = "";
            status.Note(Glyph.More, L.T("Проверка версии в облаке…"));
            try
            {
                release = await Task.Run(() => SuspectorUpdate.Latest());
                string version = release.Text("version");
                Version current = SuspectorUpdate.ParseVersion(AppVersion.Text), latest = SuspectorUpdate.ParseVersion(version);
                DateTimeOffset published;
                string date = DateTimeOffset.TryParse(release.Text("publishedAt") ?? "", out published) ? L.T(" от ") + published.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "";
                versions.Text = L.T("Установлена версия: ") + AppVersion.Text + L.T("\r\nВерсия в облаке: ") + version + date;
                var lines = new List<string>();
                IList list = release["notes"] as IList;
                if (list != null) foreach (object note in list) if (note != null && Convert.ToString(note).Length > 0) lines.Add("•  " + note);
                notes.Text = lines.Count > 0 ? L.T("Что нового:\r\n") + string.Join("\r\n", lines.GetRange(0, Math.Min(15, lines.Count))) : "";
                if (current != null && latest != null && latest <= current)
                {
                    status.Show(Severity.Success, Glyph.Check, latest == current ? L.T("Установлена последняя версия.") : L.T("Установлена более новая версия SUSpector."));
                    return;
                }
                if (Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".git")))
                {
                    status.Show(Severity.Warning, Glyph.Info, L.T("Это копия программы для разработки (есть папка .git). Обновляйте её через Git."));
                    return;
                }
                status.Show(Severity.Info, Glyph.Update, L.T("Доступна новая версия программы."));
                Glyph.Label(install, Glyph.Update, L.T(" Установить версию ").Trim() + " " + version);
                install.Visibility = Visibility.Visible;
            }
            catch (Exception error) { status.Show(Severity.Error, Glyph.Warning, L.T("Не удалось проверить обновления: ") + Friendly(error)); }
            finally { Busy(false); }
        }

        async void Install()
        {
            if (release == null) return;
            string version = release.Text("version");
            Busy(true);
            // Users keep SUSpector in Downloads or on the desktop: nothing is left next to it.
            string zip = Path.Combine(Path.GetTempPath(), "SUSpector-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                status.Note(Glyph.More, L.T("Загрузка версии ") + version + "…");
                await Task.Run(() =>
                {
                    SuspectorUpdate.Download(version, zip);
                    if (SuspectorUpdate.Sha256(zip) != (release.Text("sha256") ?? "").ToLowerInvariant())
                    {
                        File.Delete(zip);
                        throw new InvalidDataException(L.T("Архив повреждён при загрузке: контрольная сумма не совпадает. Запустите обновление ещё раз."));
                    }
                });
                status.Note(Glyph.More, L.T("Установка…"));
                string target = ExePath;
                await Task.Run(() => SuspectorUpdate.Install(target, zip, version));
                install.Visibility = Visibility.Collapsed;
                status.Show(Severity.Success, Glyph.Check, L.T("SUSpector обновлён до версии ") + version + L.T(". Перезапустите программу."));
                restart.Visibility = Visibility.Visible;
            }
            catch (Exception error) { status.Show(Severity.Error, Glyph.Warning, L.T("Обновление не выполнено: ") + Friendly(error)); }
            finally
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch (Exception) { }
                Busy(false);
            }
        }

        static string Friendly(Exception error)
        {
            var web = error as System.Net.WebException;
            if (web != null && web.Response is System.Net.HttpWebResponse)
            {
                int code = (int)((System.Net.HttpWebResponse)web.Response).StatusCode;
                if (code == 404) return L.T("Опубликованных версий SUSpector пока нет.");
            }
            return error.GetBaseException().Message;
        }

        void Restart()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ExePath) { WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory, UseShellExecute = false });
                Application.Current.Shutdown();
            }
            catch (Exception error) { Dialog.Show(this, L.T("Не удалось перезапустить программу: ") + error.Message); }
        }
    }
}
