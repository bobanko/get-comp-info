using System;

namespace CompInfo
{
    // youSBee: serial numbers of USB flash drives, the «USB-накопители» page of the toolbox as a standalone program
    // (see YOUSBEE.md). The window, «О программе» and the start-up are shared with SUSpector (Standalone.cs).
    public class YouSBeeForm : StandaloneForm
    {
        // Only harmless UI preferences: theme and language.
        public static readonly IniSettings Settings = new IniSettings("youSBee.ini", "youSBee settings", "theme", "language");

        public YouSBeeForm()
            : base("youSBee",
                L.T("Серийные номера подключённых флешек и USB-дисков. Список обновляется сам, когда накопитель подключают или отключают."),
                new UsbPage(), AboutDialog)
        {
            SizeInEm(46, 26, 34, 18);
        }

        static StandaloneAboutDialog AboutDialog()
        {
            return new StandaloneAboutDialog(Ui.YouSBeeImage, "youSBee",
                L.T("Показывает серийные номера флешек и USB-дисков, чтобы их было удобно переписать или вставить в заявку."), Sections, Settings);
        }

        static string[] Sections { get { return new[]
        {
            L.T("Как пользоваться"),
            L.T("Подключите флешку — она сразу появится в списке. Кнопка «Копировать», двойной щелчок или Ctrl+C копирует серийный номер."),
            L.T("Если номера нет"),
            L.T("Некоторые накопители не сообщают серийный номер Windows: тогда в строке так и написано."),
            L.T("Конфиденциальность"),
            L.T("Программа ничего не отправляет и не меняет: она только читает сведения о дисках из Windows."),
            L.T("Установка не нужна"),
            L.T("youSBee — один файл, он работает прямо с флешки. Права администратора не нужны. Рядом с ним сохраняется только youSBee.ini с выбранными темой и языком."),
            L.T("Клавиши"),
            L.T("F5 — обновить список. Ctrl+C — скопировать серийный номер выбранной флешки."),
        }; } }
    }

    static class YouSBeeProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            return StandaloneApp.Run(args, "youSBee", YouSBeeForm.Settings, delegate { return new YouSBeeForm(); });
        }
    }
}
