using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CompInfo
{
    // «О программе», as in SupraWeed: icon, version, what the program does and how to reach the developer.
    public class AboutPage : Page
    {
        public const string Email = "toolbox@chagear.top";
        public const string WebSite = "chagear.top";
        public const string WebInterface = "comp-info.chagear.top";

        static readonly string[] Sections =
        {
            "Где хранятся данные",
            "Всё лежит рядом с программой: записи компьютеров — в папке data (один файл на компьютер), выгрузки CSV — в exports. " +
            "Программа ничего не устанавливает и работает прямо с флешки, в том числе без интернета.",
            "Облако",
            "Синхронизация отправляет записи в общую базу и получает изменения коллег. Для неё нужен интернет и личный ключ доступа " +
            "из веб-интерфейса. Записи, удалённые на флешке вручную, из облака не удаляются.",
            "Права администратора",
            "Программа запускается без них. Разрешение Windows (окно UAC) понадобится только для настройки сети, " +
            "смены имени компьютера и работы с доменом — такие разделы отмечены в меню щитом.",
        };

        public AboutPage()
        {
            Key = "about";
            Title = "О программе";
            Description = "Версия, назначение программы и связь с разработчиком.";
            Icon = Glyph.Info;
            int em = Font.Height;
            AutoScroll = true;
            int width = em * 30;

            TableLayoutPanel t = new TableLayoutPanel();
            t.AutoSize = true;
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.Location = new Point(0, 0);

            int side = em * 4;
            PictureBox pic = new PictureBox();
            pic.Image = Ui.AppImage;
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.Size = new Size(side, side);
            pic.Margin = new Padding(0, 0, em, 0);
            t.Controls.Add(pic, 0, 0);

            FlowLayoutPanel text = new FlowLayoutPanel();
            text.FlowDirection = FlowDirection.TopDown;
            text.WrapContents = false;
            text.AutoSize = true;
            text.Margin = Padding.Empty;
            t.Controls.Add(text, 1, 0);

            text.Controls.Add(Line("CHAGEAR Тулбокс", new Font("Segoe UI Semibold", 16f), Theme.Text, width, 0));
            text.Controls.Add(Line("Учёт компьютеров при подготовке и выдаче сотрудникам", Font, Theme.Text, width, 0));
            text.Controls.Add(Line("Версия " + AppVersion.Text + (PowerShell.IsAdministrator() ? " · запущена с правами администратора" : ""), Font, Theme.Muted, width, em / 2));
            text.Controls.Add(Line("Программа собирает сведения о компьютере (имя, серийный номер, сеть, ESET), хранит их на флешке и синхронизирует " +
                "с облачной базой. Здесь же — настройка сети по профилям корпусов, имя компьютера и домен, быстрая проверка связи " +
                "и серийные номера USB-накопителей.", Font, Theme.Text, width, em / 2));

            Font bold = new Font(Font, FontStyle.Bold);
            for (int i = 0; i + 1 < Sections.Length; i += 2)
            {
                text.Controls.Add(Line(Sections[i], bold, Theme.Text, width, 0));
                text.Controls.Add(Line(Sections[i + 1], Font, Theme.Text, width, em / 2));
            }

            text.Controls.Add(Line("Связь с разработчиком", bold, Theme.Text, width, 0));
            Label copied = Line("← нажмите, чтобы скопировать", Font, Theme.Muted, width, 0);
            // Service computers have no mail client: a click copies the address instead of opening mailto.
            text.Controls.Add(LinkRow(Glyph.Person, Email, copied, delegate
            {
                try { Clipboard.SetText(Email); copied.Text = "скопировано ✓"; } catch (Exception) { }
            }));
            text.Controls.Add(LinkRow(Glyph.Cloud, WebInterface, Line("веб-интерфейс базы", Font, Theme.Muted, width, 0), delegate { OpenSite(WebInterface); }));
            text.Controls.Add(LinkRow(Glyph.Open, WebSite, null, delegate { OpenSite(WebSite); }));
            text.Controls.Add(LinkRow(Glyph.Folder, "Открыть папку программы", null, delegate
            {
                try { Process.Start("explorer.exe", "\"" + AppPaths.Root + "\""); } catch (Exception) { }
            }));

            Controls.Add(t);
        }

        static Label Line(string s, Font font, Color color, int width, int bottom)
        {
            Label l = Ui.Text(s, font, color);
            l.MaximumSize = new Size(width, 0);
            l.Margin = new Padding(0, 0, 0, bottom);
            return l;
        }

        FlowLayoutPanel LinkRow(string glyph, string text, Label note, LinkLabelLinkClickedEventHandler click)
        {
            int em = Font.Height;
            FlowLayoutPanel row = Ui.Row();
            PictureBox icon = new PictureBox();
            icon.Image = Glyph.Image(glyph, em, Theme.Muted);
            icon.SizeMode = PictureBoxSizeMode.AutoSize;
            icon.Margin = new Padding(0, 3, em / 4, 3);
            LinkLabel link = new LinkLabel();
            link.Text = text;
            link.AutoSize = true;
            link.LinkColor = link.ActiveLinkColor = link.VisitedLinkColor = Theme.Info;
            link.Margin = new Padding(0, 3, 0, 3);
            link.LinkClicked += click;
            row.Controls.Add(icon);
            row.Controls.Add(link);
            if (note != null) { note.Margin = new Padding(em / 2, 3, 0, 3); row.Controls.Add(note); }
            return row;
        }

        // Opens in the default browser; if that fails (no browser, locked down PC), the address is copied.
        static void OpenSite(string site)
        {
            try { Process.Start(new ProcessStartInfo("https://" + site) { UseShellExecute = true }); }
            catch (Exception) { try { Clipboard.SetText("https://" + site); } catch (Exception) { } }
        }
    }
}
