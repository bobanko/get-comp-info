using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CompInfo.Wpf
{
    // «Обновление программы» (UpdatePage of CloudPages.cs on WinForms): the version in the cloud with the personal key,
    // «Что нового», download with the SHA-256 check, installation with a backup (ProgramUpdate), restart.
    public class UpdateView : DockPanel, ISection
    {
        readonly TextBlock versions = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        readonly TextBlock notes = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly StatusLine status = new StatusLine { Margin = new Thickness(0, 10, 0, 10) };
        readonly ProgressBar progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Hidden, Margin = new Thickness(0, 10, 0, 0) };
        readonly Button check = new Button(), install = new Button(), restart = new Button();
        CloudClient client;
        JsonObject release;
        bool running;

        public UpdateView()
        {
            versions.Text = L.T("Установлена версия: ") + AppVersion.Text;
            SetDock(versions, Dock.Top);
            Children.Add(versions);

            var actions = new WrapPanel();
            Glyph.Label(check, Glyph.Refresh, L.T("Проверить обновления"));
            check.ToolTip = L.T("Проверить обновления") + "   (F5)";
            Glyph.Label(install, Glyph.Update, L.T("Установить"));
            install.Visibility = Visibility.Collapsed;
            Glyph.Label(restart, Glyph.Power, L.T("Перезапустить программу"));
            restart.Visibility = Visibility.Collapsed;
            check.Click += delegate { Check(); };
            install.Click += delegate { Install(); };
            restart.Click += delegate { Restart(); };
            foreach (Button b in new[] { check, install, restart })
            {
                b.SetResourceReference(StyleProperty, "AccentButton");
                b.Margin = new Thickness(0, 0, 8, 0);
                actions.Children.Add(b);
            }
            SetDock(actions, Dock.Top);
            Children.Add(actions);
            progress.SetResourceReference(StyleProperty, "ThinProgress");
            SetDock(progress, Dock.Top);
            Children.Add(progress);
            SetDock(status, Dock.Top);
            Children.Add(status);
            Children.Add(new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        }

        public void Opened() { check.Focus(); }

        public bool Shortcut(KeyEventArgs e)
        {
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None) { if (check.IsEnabled) Check(); return true; }
            return false;
        }

        public bool CanLeave()
        {
            if (running) Dialog.Show(Window.GetWindow(this), L.T("Дождитесь окончания обновления."), Severity.Info, L.T("Обновление"));
            return !running;
        }

        public void DevicesChanged() { }

        static bool IsDevelopmentCopy { get { return Directory.Exists(Path.Combine(AppPaths.Root, ".git")); } }

        void Busy(bool value)
        {
            running = value;
            progress.Visibility = value ? Visibility.Visible : Visibility.Hidden;
            check.IsEnabled = install.IsEnabled = !value;
        }

        async void Check()
        {
            Busy(true);
            install.Visibility = Visibility.Collapsed;
            notes.Text = "";
            status.Note(Glyph.More, L.T("Проверка версии в облаке…"));
            try
            {
                client = await CloudConnect.Connect(Window.GetWindow(this));
                if (client == null) { status.Show(Severity.Warning, Glyph.Info, L.T("Проверка отменена: ключ не введён.")); return; }
                release = await Task.Run(() => client.LatestRelease());
                if (release == null) { status.Show(Severity.Warning, Glyph.Info, L.T("В облаке ещё нет опубликованных версий программы.")); return; }
                string version = release.Text("version");
                Version current = ProgramUpdate.Parse(AppVersion.Text), latest = ProgramUpdate.Parse(version);
                DateTimeOffset published;
                string date = DateTimeOffset.TryParse(release.Text("publishedAt") ?? "", out published) ? L.T(" от ") + published.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "";
                versions.Text = L.T("Установлена версия: ") + AppVersion.Text + L.T("\r\nВерсия в облаке: ") + version + date;
                if (current != null && latest != null && current == latest)
                {
                    status.Show(Severity.Success, Glyph.Check, L.T("Установлена последняя версия."));
                    return;
                }
                bool rollback = current != null && latest != null && latest < current;
                var lines = new List<string>();
                IList list = release["notes"] as IList;
                if (list != null) foreach (object n in list) if (n != null && Convert.ToString(n).Length > 0) lines.Add("•  " + n);
                notes.Text = lines.Count > 0 ? L.T("Что нового:\r\n") + string.Join("\r\n", lines.GetRange(0, Math.Min(15, lines.Count))) +
                    (lines.Count > 15 ? L.T("\r\n   и ещё ") + (lines.Count - 15) : "") : "";
                if (IsDevelopmentCopy)
                {
                    status.Show(Severity.Warning, Glyph.Info, L.T("Это копия программы для разработки (есть папка .git). Обновляйте её через Git."));
                    return;
                }
                if (rollback) status.Show(Severity.Warning, Glyph.Warning, L.T("Администратор сделал текущей более старую версию: установка будет откатом."));
                else status.Show(Severity.Info, Glyph.Update, L.T("Доступна новая версия программы."));
                Glyph.Label(install, Glyph.Update, L.T(" Установить версию ").Trim() + " " + version);
                install.Visibility = Visibility.Visible;
            }
            catch (Exception e) { status.Show(Severity.Error, Glyph.Warning, L.T("Не удалось проверить обновления: ") + e.Message); }
            finally { Busy(false); }
        }

        async void Install()
        {
            if (release == null || client == null) return;
            string version = release.Text("version");
            Busy(true);
            try
            {
                string root = AppPaths.Root;
                string updateRoot = Path.Combine(root, ".update");
                Directory.CreateDirectory(updateRoot);
                string zip = Path.Combine(updateRoot, "comp-info-" + version + ".zip");
                status.Note(Glyph.More, L.T("Загрузка версии ") + version + "…");
                string sha = (release.Text("sha256") ?? "").ToLowerInvariant();
                await Task.Run(() =>
                {
                    client.Download(client.ApiUrl + "/program/" + version + "/package", zip);
                    if (ProgramUpdate.Sha256(zip) != sha)
                    {
                        File.Delete(zip);
                        throw new InvalidDataException(L.T("Архив повреждён при загрузке: контрольная сумма не совпадает. Запустите обновление ещё раз."));
                    }
                });
                status.Note(Glyph.More, L.T("Установка…"));
                ProgramUpdate.Result result = await Task.Run(() => ProgramUpdate.Install(root, zip, version, AppVersion.Text));
                // The program keeps no network addresses itself: refresh their cache while online.
                string profiles = "";
                try { if (await Task.Run(() => client.UpdateNetworkProfiles(root)) == "updated") profiles = L.T("\r\nПрофили сети обновлены."); }
                catch (Exception e) { profiles = L.T("\r\nПрофили сети не загружены: ") + e.Message + L.T(". Они обновятся при синхронизации."); }
                install.Visibility = Visibility.Collapsed;
                status.Show(Severity.Success, Glyph.Check, L.T("Программа обновлена до версии ") + version + L.T(". Файлов установлено: ") + result.Installed +
                    (result.Removed.Count > 0 ? L.T(". Удалены устаревшие: ") + string.Join(", ", result.Removed) : "") +
                    L.T(".\r\nПрежняя версия сохранена в ") + result.Backup + profiles + L.T("\r\nПерезапустите программу, чтобы работать в новой версии."));
                restart.Visibility = Visibility.Visible;
            }
            catch (Exception e) { status.Show(Severity.Error, Glyph.Warning, L.T("Обновление не выполнено: ") + e.Message); }
            finally { Busy(false); }
        }

        void Restart()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(new ProcessStartInfo(exe, "--page update") { WorkingDirectory = AppPaths.Root, UseShellExecute = false });
                Window.GetWindow(this).Close();
            }
            catch (Exception e) { Dialog.Show(Window.GetWindow(this), L.T("Не удалось перезапустить программу: ") + e.Message); }
        }
    }
}
