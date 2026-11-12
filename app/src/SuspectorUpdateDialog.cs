using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompInfo
{
    public class SuspectorUpdateDialog : Form
    {
        readonly Label versions;
        readonly Label notes;
        readonly StatusLine status = new StatusLine();
        readonly ProgressBar progress = new ProgressBar();
        readonly Button check;
        readonly Button install;
        readonly Button restart;
        readonly Button close;
        JsonObject release;
        bool running;

        public SuspectorUpdateDialog()
        {
            Ui.Dialog(this);
            Text = L.T("Обновление SUSpector");
            int em = Font.Height;
            ClientSize = new Size(em * 38, em * 23);
            MinimumSize = new Size(em * 32, em * 19);
            Padding = new Padding(em);

            versions = Ui.Paragraph(L.T("Установлена версия: ") + AppVersion.Text, Font, Theme.Text);
            versions.Dock = DockStyle.Top;
            versions.Padding = new Padding(0, 0, 0, em / 2);
            check = Ui.Primary(L.T("Проверить обновления"), Glyph.Refresh, Font);
            check.Click += delegate { Check(); };
            install = Ui.Primary(L.T("Установить"), Glyph.Update, Font);
            install.Visible = false;
            install.Click += delegate { Install(); };
            restart = Ui.Primary(L.T("Перезапустить программу"), Glyph.Power, Font);
            restart.Visible = false;
            restart.Click += delegate { Restart(); };
            close = Ui.Secondary(L.T("Закрыть"), null, Font);
            close.Click += delegate { Close(); };
            FlowLayoutPanel actions = Ui.Row();
            actions.Dock = DockStyle.Top;
            actions.Padding = new Padding(0, 0, 0, em / 2);
            actions.Controls.Add(check);
            actions.Controls.Add(install);
            actions.Controls.Add(restart);
            actions.Controls.Add(close);

            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 28;
            progress.Height = em / 3;
            progress.Dock = DockStyle.Top;
            progress.Visible = false;
            status.Dock = DockStyle.Top;
            status.Padding = new Padding(0, em / 3, 0, em / 3);
            notes = Ui.Paragraph("", Font, Theme.Text);
            notes.Dock = DockStyle.Fill;
            notes.AutoEllipsis = true;

            Controls.Add(notes);
            Controls.Add(status);
            Controls.Add(progress);
            Controls.Add(actions);
            Controls.Add(versions);
            Shown += delegate { Check(); };
            FormClosing += delegate (object sender, FormClosingEventArgs e)
            {
                if (running) { e.Cancel = true; Ui.Info(this, L.T("Обновление"), L.T("Дождитесь окончания обновления.")); }
            };
        }

        void Busy(bool value)
        {
            running = value;
            progress.Visible = value;
            check.Enabled = install.Enabled = close.Enabled = !value;
        }

        async void Check()
        {
            Busy(true);
            install.Visible = restart.Visible = false;
            notes.Text = "";
            status.Set(Glyph.More, L.T("Проверка версии в облаке…"), Theme.Muted);
            try
            {
                release = await Task.Run(delegate { return SuspectorUpdate.Latest(); });
                string version = release.Text("version");
                Version current = SuspectorUpdate.ParseVersion(AppVersion.Text), latest = SuspectorUpdate.ParseVersion(version);
                DateTimeOffset published;
                string date = DateTimeOffset.TryParse(release.Text("publishedAt") ?? "", out published) ? L.T(" от ") + published.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "";
                versions.Text = L.T("Установлена версия: ") + AppVersion.Text + L.T("\r\nВерсия в облаке: ") + version + date;
                List<string> lines = new List<string>();
                IList list = release["notes"] as IList;
                if (list != null) foreach (object note in list) if (note != null && Convert.ToString(note).Length > 0) lines.Add("•  " + note);
                notes.Text = lines.Count > 0 ? L.T("Что нового:\r\n") + string.Join("\r\n", lines.GetRange(0, Math.Min(15, lines.Count))) : "";
                if (current != null && latest != null && latest <= current)
                {
                    status.Set(Glyph.Check, latest == current ? L.T("Установлена последняя версия.") : L.T("Установлена более новая версия SUSpector."), Theme.Good);
                    return;
                }
                if (Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".git")))
                {
                    status.Set(Glyph.Info, L.T("Это копия программы для разработки (есть папка .git). Обновляйте её через Git."), Theme.Warn);
                    return;
                }
                status.Set(Glyph.Update, L.T("Доступна новая версия программы."), Theme.Info);
                install.Text = L.T(" Установить версию ") + version;
                install.Visible = true;
            }
            catch (Exception error) { status.Set(Glyph.Warning, L.T("Не удалось проверить обновления: ") + Friendly(error), Theme.Bad); }
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
                status.Set(Glyph.More, L.T("Загрузка версии ") + version + "…", Theme.Muted);
                await Task.Run(delegate
                {
                    SuspectorUpdate.Download(version, zip);
                    if (SuspectorUpdate.Sha256(zip) != (release.Text("sha256") ?? "").ToLowerInvariant())
                    {
                        File.Delete(zip);
                        throw new InvalidDataException(L.T("Архив повреждён при загрузке: контрольная сумма не совпадает. Запустите обновление ещё раз."));
                    }
                });
                status.Set(Glyph.More, L.T("Установка…"), Theme.Muted);
                string target = Application.ExecutablePath;
                await Task.Run(delegate { SuspectorUpdate.Install(target, zip, version); });
                install.Visible = false;
                status.Set(Glyph.Check, L.T("SUSpector обновлён до версии ") + version + L.T(". Перезапустите программу."), Theme.Good);
                restart.Visible = true;
            }
            catch (Exception error) { status.Set(Glyph.Warning, L.T("Обновление не выполнено: ") + Friendly(error), Theme.Bad); }
            finally
            {
                try { if (File.Exists(zip)) File.Delete(zip); } catch (Exception) { }
                Busy(false);
            }
        }

        static string Friendly(Exception error)
        {
            System.Net.WebException web = error as System.Net.WebException;
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
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory, UseShellExecute = false });
                Application.Exit();
            }
            catch (Exception error) { Ui.Error(this, L.T("Не удалось перезапустить программу: ") + error.Message); }
        }
    }
}
