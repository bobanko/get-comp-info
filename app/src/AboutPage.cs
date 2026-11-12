using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CompInfo
{
    // «О программе», as in SupraWeed: icon, version, what the program does and how to reach the developer.
    public class AboutPage : Page
    {
        public const string WebInterface = "comp-info.chagear.top";

        static string[] Sections { get { return new[]
        {
            L.T("Где хранятся данные"),
            L.T("Всё лежит рядом с программой: записи компьютеров — в папке data (один файл на компьютер), выгрузки CSV — в exports. ") +
            L.T("Программа ничего не устанавливает и работает прямо с флешки, в том числе без интернета."),
            L.T("Облако"),
            L.T("Синхронизация отправляет записи в общую базу и получает изменения коллег. Для неё нужен интернет и личный ключ доступа ") +
            L.T("из веб-интерфейса. Записи, удалённые на флешке вручную, из облака не удаляются."),
            L.T("Права администратора"),
            L.T("Программа запускается без них. Разрешение Windows (окно UAC) понадобится только для настройки сети, ") +
            L.T("смены имени компьютера и работы с доменом — такие разделы отмечены в меню щитом."),
            L.T("Клавиши"),
            L.T("Ctrl+1…9 — разделы меню, Ctrl+0 — «О программе», Ctrl+Tab — следующий раздел. F5 — обновить или проверить заново, ") +
            L.T("Ctrl+S — сохранить сбор, Ctrl+F — поиск в базе (стрелки и Enter — по таблице), Ctrl+E — экспорт в CSV, Ctrl+Enter — применить настройки сети, имени или домена."),
        }; } }

        public AboutPage()
        {
            Key = "about";
            Title = L.T("О программе");
            Description = L.T("Версия, назначение программы и связь с разработчиком.");
            Icon = Glyph.Info;
            int em = Font.Height;
            AutoScroll = true;
            // AutoScroll owns a native scrollbar; opt it into the active theme too.
            Theme.Native(this);
            int width = em * 30;

            FlowLayoutPanel text;
            TableLayoutPanel t = About.Build(Font, width, L.Code == "ru" ? Ui.FaceRuImage : Ui.FaceImage, L.T("CHAGEAR Тулбокс"), L.T("Учёт компьютеров при подготовке и выдаче сотрудникам"),
                L.T("Программа собирает сведения о компьютере (имя, серийный номер, сеть, ESET), хранит их на флешке и синхронизирует ") +
                L.T("с облачной базой. Здесь же — настройка сети по профилям корпусов, имя компьютера и домен, быстрая проверка связи ") +
                L.T("и серийные номера USB-накопителей."), Sections, out text);
            t.Location = new Point(0, 0);
            text.Controls.Add(About.SiteRow(Font, Glyph.Cloud, WebInterface, About.Line(L.T("веб-интерфейс базы"), Font, Theme.Muted, width, 0)));
            text.Controls.Add(About.SiteRow(Font, Glyph.Open, About.WebSite, null));
            text.Controls.Add(About.FolderRow(Font, AppPaths.Root));
            Controls.Add(t);
        }
    }
}
