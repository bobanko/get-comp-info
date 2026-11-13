using System;
using System.Drawing;
using System.Windows.Forms;

namespace CompInfo
{
    // youSBee: serial numbers of USB flash drives, the «USB-накопители» page of the toolbox as a standalone program
    // (see YOUSBEE.md). The window, «О программе» and the start-up are shared with SUSpector (Standalone.cs).
    public class YouSBeeForm : StandaloneForm
    {
        // Only harmless UI preferences: theme, language, window place and whether it stays on top.
        public static readonly IniSettings Settings = new IniSettings("youSBee.ini", "youSBee settings", "theme", "language", "window", "pinned");

        // «SBee» is honey yellow, as on chagear.top.
        static Color Highlight { get { return Theme.Dark ? Color.FromArgb(245, 184, 46) : Color.FromArgb(180, 122, 0); } }

        public YouSBeeForm()
            : this(new UsbPage(), Ui.Secondary("", Glyph.Pin, Ui.BaseFont)) { }

        // The pin, «Обновить» and «О программе» sit in the top right corner, so the page's own row with «Обновить» is hidden.
        YouSBeeForm(UsbPage page, Button pin)
            : base("you[SBee]", Highlight, L.T("Серийные номера флешек."), page, AboutDialog, true, pin, RefreshButton(page))
        {
            page.ShowToolbar = false;
            // On top of other windows, so it stays visible while the number goes into a request in the browser.
            // An icon-only button, as narrow as it is tall.
            int em = Font.Height;
            pin.Text = "";
            pin.ImageAlign = ContentAlignment.MiddleCenter;
            pin.AutoSize = false;
            int side;
            using (Button sample = Ui.Secondary(L.T("Обновить"), Glyph.Refresh, Font)) side = sample.PreferredSize.Height;
            pin.Size = new Size(side, side);
            TopMost = Settings.Get("pinned", "off") == "on";
            ShowPin(pin, TopMost);
            pin.Click += delegate
            {
                TopMost = !TopMost;
                Settings.Set("pinned", TopMost ? "on" : "off");
                ShowPin(pin, TopMost);
            };
            // A small utility: the table has only a few short columns.
            // Room for about five drives: rarely more than three are connected at once.
            SizeInEm(31, 18, 28, 15);
        }

        static void ShowPin(Button pin, bool pinned)
        {
            int em = pin.Font.Height;
            int size = (int)(em * 0.95f);
            if (!pinned) pin.Image = Glyph.Image(Glyph.Pin, size, Theme.Text);
            else
            {
                // The filled pin of the icon font has no needle: the outline pin is drawn over the fill.
                Bitmap image = Glyph.Image(Glyph.Pinned, size, Theme.Accent);
                using (Graphics g = Graphics.FromImage(image))
                using (Bitmap outline = Glyph.Image(Glyph.Pin, size, Theme.Accent))
                    g.DrawImage(outline, 0, 0);
                pin.Image = image;
            }
            pin.BackColor = pinned ? Theme.AccentSoft : Theme.Surface;
            new ToolTip().SetToolTip(pin, pinned ? L.T("Окно поверх остальных — нажмите, чтобы открепить") : L.T("Закрепить окно поверх остальных"));
        }

        static Button RefreshButton(UsbPage page)
        {
            Button refresh = Ui.Secondary(L.T("Обновить"), Glyph.Refresh, Ui.BaseFont);
            refresh.Click += delegate { page.RefreshList(); };
            Ui.Keys(refresh, "F5");
            return refresh;
        }

        static StandaloneAboutDialog AboutDialog()
        {
            return new StandaloneAboutDialog(Ui.YouSBeeImage, "you[SBee]", Highlight, "yousbee@chagear.top",
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
