using System;
using System.Windows.Forms;

namespace CompInfo
{
    // SUSpector: the cybersecurity check of the toolbox as a standalone program for users (see SUSPECTOR.md).
    // The window, «О программе» and the start-up are shared with youSBee (Standalone.cs).
    public class SuspectorForm : StandaloneForm
    {
        // Only harmless UI preferences: theme, language and where the file table is.
        public static readonly IniSettings Settings = new IniSettings("SUSpector.ini", "SUSpector settings", "theme", "language", "layout");

        public SuspectorForm()
            : base("SUSpector",
                L.T("Проверка и подготовка компьютера перед внешней проверкой. Результаты остаются на этом компьютере."),
                new SecurityPage(), AboutDialog, UpdateButton())
        {
            SizeInEm(56, 34, 42, 28);
        }

        static Button UpdateButton()
        {
            Button update = Ui.Secondary(L.T("Обновление"), Glyph.Update, Ui.BaseFont);
            update.Click += delegate
            {
                using (SuspectorUpdateDialog dialog = new SuspectorUpdateDialog()) dialog.ShowDialog(update.FindForm());
            };
            return update;
        }

        static StandaloneAboutDialog AboutDialog()
        {
            return new StandaloneAboutDialog(Ui.SuspectorImage, "SUSpector",
                L.T("Помогает проверить и подготовить компьютер перед внешней проверкой безопасности."), Sections, Settings);
        }

        static string[] Sections { get { return new[]
        {
            L.T("Проверки"),
            L.T("Проверяет имя и домен компьютера, антивирус, VPN, нежелательное ПО, OneDrive, локальных администраторов, пароли Windows и документы с признаками ДСК."),
            L.T("Подготовка"),
            L.T("Пользователь сам запускает исправляющие действия: можно отключить синхронизацию OneDrive без удаления локальных файлов и безвозвратно удалить найденные документы."),
            L.T("Конфиденциальность"),
            L.T("Проверка выполняется локально. Результаты и найденные файлы никуда не отправляются."),
            L.T("Установка не нужна"),
            L.T("SUSpector — один файл, он работает прямо с флешки. Рядом с ним сохраняется только SUSpector.ini с выбранными темой, языком и расположением таблиц."),
            L.T("Обновление"),
            L.T("Кнопка «Обновление» сверяет версию с сайтом разработчика и при согласии заменяет SUSpector.exe новой версией. Без интернета программа работает как обычно."),
            L.T("Клавиши"),
            L.T("F5 — запустить проверку заново. Enter или двойной щелчок по найденному документу — открыть его."),
        }; } }
    }

    static class SuspectorProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            SuspectorUpdate.RemoveLeftovers(Application.ExecutablePath);
            SecurityPage.TableLayout = StandaloneApp.Option(args, "--layout", 1) ?? SuspectorForm.Settings.Get("layout", "bottom");
            SecurityPage.LayoutSaved = delegate (string value) { SuspectorForm.Settings.Set("layout", value); };
            return StandaloneApp.Run(args, "SUSpector", SuspectorForm.Settings, delegate { return new SuspectorForm(); });
        }
    }
}
