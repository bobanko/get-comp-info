using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace CompInfo.Wpf
{
    // The settings window. Styled like the other programs (StandaloneWindow): the name and a line on the left,
    // "About" on the right (theme and language live there too). The body holds the copy options, each saved at once.
    public class SettingsWindow : StandaloneWindow
    {
        readonly IniSettings settings;

        // The settings changed — the tray controller re-reads them.
        public event Action SettingsChanged;
        // The language changed in "About" — the controller closes this window and opens it again (to pick up the translation).
        public event Action ReopenRequested;

        public SettingsWindow(IniSettings settings)
            : this(settings, new StackPanel()) { }

        SettingsWindow(IniSettings settings, StackPanel page)
            : base("Flash[Stash]Bee", "Violet", L.T("Копирует вставленную флешку в папку на диске."), page)
        {
            this.settings = settings;
            // Fixed width, height follows the content (no point letting the user stretch it); not resizable.
            Width = 640;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            About = AboutDialog;
            ShowAbout = ShowAboutHandler;

            // Every block has an icon on the left, like the sections of "About".
            page.Children.Add(Section(Glyph.Folder, Field(
                L.T("Папка назначения"),
                L.T("Внутри — своя папка на каждую флешку (по серийнику). Повторная вставка не плодит копию, а досинхронизируется в ту же папку."),
                DestinationRow())));

            page.Children.Add(Section(Glyph.Usb, LimitRow("maxSizeOn", "maxSizeGb", "64", 1,
                L.T("Не копировать флешки, где занято больше"), L.T("ГБ"), null,
                L.T("Слишком большие флешки пропускаются целиком.")), LimitIconTop));

            page.Children.Add(Section(Glyph.Document, FileLimitRow(), LimitIconTop));

            page.Children.Add(Section(Glyph.Filter, FilterSection()));

            page.Children.Add(Section(Glyph.Blocked, BlacklistSection()));

            page.Children.Add(Section(Glyph.Package, CheckRow("zip",
                L.T("Упаковывать скопированное в ZIP"),
                L.T("Каждая флешка — один zip-файл без сжатия; папка после упаковки удаляется."))));

            page.Children.Add(Section(Glyph.Power, AutostartRow()));

            // no gap under the last block
            ((FrameworkElement)page.Children[page.Children.Count - 1]).Margin = new Thickness(0);
        }

        // ---------- rows ----------

        // The icon is centred on the first line of its block: a title or a checkbox (1 px down),
        // or a checkbox with a number field next to it (LimitIconTop).
        const double LimitIconTop = 6;

        // A settings block with an icon on the left in the brand colour (the same look as the sections of "About").
        static Grid Section(string glyph, FrameworkElement body, double iconTop = 1)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Glyph.Block(glyph, 16, "Violet");
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, iconTop, 0, 0);
            grid.Children.Add(icon);
            body.Margin = new Thickness(0);
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            return grid;
        }

        static StackPanel Field(string title, string hint, UIElement control)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            control.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0));
            sp.Children.Add((FrameworkElement)control);
            sp.Children.Add(Hint(hint));
            return sp;
        }

        static TextBlock Hint(string text)
        {
            var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            return t;
        }

        DockPanel DestinationRow()
        {
            var row = new DockPanel { LastChildFill = true };
            // docked right in this order: [box] [Обзор…] [Открыть]
            var open = new Button { Margin = new Thickness(8, 0, 0, 0), MinWidth = 110 };
            Glyph.Label(open, Glyph.Open, L.T("Открыть"));
            DockPanel.SetDock(open, Dock.Right);
            var browse = new Button { Margin = new Thickness(8, 0, 0, 0), MinWidth = 110 };
            Glyph.Label(browse, Glyph.Folder, L.T("Обзор…"));
            DockPanel.SetDock(browse, Dock.Right);
            var box = new TextBox { Text = settings.Get("baseDir", ArchiveOptions.DefaultBaseDir) };
            // Open the backup folder in Explorer (created first if it does not exist yet).
            open.Click += delegate
            {
                string dir = box.Text.Trim();
                if (dir.Length == 0) dir = ArchiveOptions.DefaultBaseDir;
                try { System.IO.Directory.CreateDirectory(dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
                catch (Exception) { }
            };
            box.LostFocus += delegate { Save("baseDir", box.Text.Trim()); };
            browse.Click += delegate
            {
                using (var dlg = new Forms.FolderBrowserDialog())
                {
                    dlg.Description = L.T("Куда складывать скопированное");
                    try { if (System.IO.Directory.Exists(box.Text.Trim())) dlg.SelectedPath = box.Text.Trim(); } catch (Exception) { }
                    if (dlg.ShowDialog() == Forms.DialogResult.OK)
                    {
                        box.Text = dlg.SelectedPath;
                        Save("baseDir", box.Text.Trim());
                    }
                }
            };
            row.Children.Add(open);
            row.Children.Add(browse);
            row.Children.Add(box);
            return row;
        }

        // ---------- blacklist ----------

        // "Do not copy these flash drives": the blacklisted serials (each with a remove button) and a list of the
        // flash drives inserted right now to add one. Refreshed when a device is plugged in or removed.
        StackPanel BlacklistSection()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            sp.Children.Add(new TextBlock { Text = L.T("Не копировать эти флешки"), FontWeight = FontWeights.SemiBold });

            var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(list);

            var addRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 8, 0, 0) };
            var add = new Button { Margin = new Thickness(8, 0, 0, 0), MinWidth = 110 };
            Glyph.Label(add, "", L.T("Добавить"));   // Add
            DockPanel.SetDock(add, Dock.Right);
            var combo = new ComboBox();
            addRow.Children.Add(add);
            addRow.Children.Add(combo);
            sp.Children.Add(addRow);
            sp.Children.Add(Hint(L.T("Флешки с этими серийными номерами не копируются. Вставьте флешку и добавьте её из списка.")));

            Action refresh = null;
            refresh = delegate
            {
                List<string> serials = ReadExcluded();
                List<FlashDrive> present = FlashDrives.Present();

                list.Children.Clear();
                if (serials.Count == 0)
                {
                    var empty = new TextBlock { Text = L.T("Список пуст") };
                    empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                    list.Children.Add(empty);
                }
                foreach (string serial in serials)
                {
                    string s = serial;
                    FlashDrive known = present.Find(d => string.Equals(d.Serial, s, StringComparison.OrdinalIgnoreCase));
                    var row = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 2) };
                    var del = new Button { Padding = new Thickness(8, 3, 8, 3), ToolTip = L.T("Убрать из списка") };
                    del.Content = Glyph.Block(Glyph.Cross, 11, null);
                    System.Windows.Automation.AutomationProperties.SetName(del, L.T("Убрать из списка"));
                    DockPanel.SetDock(del, Dock.Right);
                    del.Click += delegate
                    {
                        List<string> now = ReadExcluded();
                        now.RemoveAll(x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase));
                        WriteExcluded(now);
                        refresh();
                    };
                    row.Children.Add(del);
                    row.Children.Add(new TextBlock
                    {
                        Text = known != null ? known.Display + " — " + s : s,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    list.Children.Add(row);
                }

                combo.Items.Clear();
                foreach (FlashDrive d in present)
                    if (d.Serial.Length > 0 && !serials.Exists(x => string.Equals(x, d.Serial, StringComparison.OrdinalIgnoreCase)))
                        combo.Items.Add(new ComboBoxItem { Content = d.Display + " — " + d.Serial, Tag = d.Serial });
                bool any = combo.Items.Count > 0;
                if (!any) combo.Items.Add(new ComboBoxItem { Content = L.T("Нет вставленных флешек с серийником") });
                combo.SelectedIndex = 0;
                combo.IsEnabled = any;
                add.IsEnabled = any;
            };

            add.Click += delegate
            {
                var item = combo.SelectedItem as ComboBoxItem;
                string serial = item != null ? item.Tag as string : null;
                if (string.IsNullOrEmpty(serial)) return;
                List<string> now = ReadExcluded();
                if (!now.Exists(x => string.Equals(x, serial, StringComparison.OrdinalIgnoreCase))) now.Add(serial);
                WriteExcluded(now);
                refresh();
            };

            // A flash drive plugged in or removed: refresh after a moment (the volume needs time to get ready).
            DevicesChanged += delegate
            {
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                t.Tick += delegate { t.Stop(); refresh(); };
                t.Start();
            };

            refresh();
            return sp;
        }

        List<string> ReadExcluded()
        {
            var list = new List<string>();
            foreach (string s in (settings.Get("excludeSerials", "") ?? "").Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = s.Trim();
                if (t.Length > 0 && !list.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) list.Add(t);
            }
            return list;
        }

        void WriteExcluded(List<string> serials)
        {
            Save("excludeSerials", string.Join(",", serials));
        }

        const int SpinMax = 1000000;

        // A number spinner: a themed TextBox with up/down buttons (WPF has no built-in NumericUpDown).
        // Digits only, clamped to [min, SpinMax]; each change is saved at once.
        UIElement SpinBox(string key, string def, int step, int min)
        {
            int start;
            string text = settings.Get(key, def);
            if (!int.TryParse(text, out start) || start < min) text = def;   // e.g. an old "0"
            var box = new TextBox
            {
                Text = text,
                Style = null,                       // no implicit TextBox chrome: this one sits inside the border below
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 3, 0, 3),
                MinWidth = 54
            };
            box.SetResourceReference(Control.ForegroundProperty, "Text");
            box.SetResourceReference(TextBoxBase.CaretBrushProperty, "Text");
            box.PreviewTextInput += delegate (object s, TextCompositionEventArgs e) { e.Handled = !AllDigits(e.Text); };
            DataObject.AddPastingHandler(box, OnlyDigitsPaste);
            // Up/Down arrow keys raise and lower the value.
            box.PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Up) { Bump(box, key, step, min); e.Handled = true; }
                else if (e.Key == Key.Down) { Bump(box, key, -step, min); e.Handled = true; }
            };
            box.LostFocus += delegate { CommitNumber(box, key, def, min); };

            RepeatButton up = SpinnerButton(""), down = SpinnerButton("");   // ChevronUp / ChevronDown
            up.Click += delegate { Bump(box, key, step, min); };
            down.Click += delegate { Bump(box, key, -step, min); };

            var buttons = new Grid { Width = 22 };
            buttons.RowDefinitions.Add(new RowDefinition());
            buttons.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(up, 0); Grid.SetRow(down, 1);
            buttons.Children.Add(up); buttons.Children.Add(down);

            var inner = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(buttons, Dock.Right);
            inner.Children.Add(buttons);
            inner.Children.Add(box);

            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Width = 96,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(9, 0, 0, 0),
                Child = inner
            };
            border.SetResourceReference(Border.BackgroundProperty, "FieldBack");
            border.SetResourceReference(Border.BorderBrushProperty, "Border");
            return border;
        }

        static RepeatButton SpinnerButton(string glyph)
        {
            var b = new RepeatButton { Content = Glyph.Block(glyph, 9, "Muted") };
            b.SetResourceReference(FrameworkElement.StyleProperty, "SpinnerButton");
            return b;
        }

        void Bump(TextBox box, string key, int delta, int min)
        {
            int n;
            if (!int.TryParse(box.Text.Trim(), out n)) n = min;
            n += delta;
            if (n < min) n = min;
            if (n > SpinMax) n = SpinMax;
            box.Text = n.ToString();
            box.CaretIndex = box.Text.Length;
            Save(key, n.ToString());
        }

        void CommitNumber(TextBox box, string key, string def, int min)
        {
            int n;
            if (int.TryParse(box.Text.Trim(), out n) && n >= min) { if (n > SpinMax) n = SpinMax; box.Text = n.ToString(); Save(key, n.ToString()); }
            else
            {
                string kept = settings.Get(key, def);
                box.Text = int.TryParse(kept, out n) && n >= min ? kept : def;
            }
        }

        static bool AllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (!char.IsDigit(c)) return false;
            return true;
        }

        static void OnlyDigitsPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)) && AllDigits(((string)e.DataObject.GetData(typeof(string))).Trim())) return;
            e.CancelCommand();
        }

        TextBox ExtensionsBox()
        {
            var box = new TextBox { Text = settings.Get("extensions", "") };
            box.LostFocus += delegate { Save("extensions", box.Text.Trim()); };
            return box;
        }

        // A size limit on one row: [x] text [number] unit [extra]. The checkbox turns the limit on and off
        // (instead of the old "0 = no limit"); the number is greyed out while it is off.
        StackPanel LimitRow(string onKey, string numKey, string def, int step, string text, string unit,
            FrameworkElement extra, string hint)
        {
            int n;
            bool numberOk = int.TryParse(settings.Get(numKey, def), out n) && n > 0;
            bool on = ArchiveOptions.SizeLimitOn(settings, onKey, numberOk);

            var sp = new StackPanel();
            // a WrapPanel: when a longer translation does not fit, the extra checkbox moves to the next line
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            var check = new CheckBox { Content = text, IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            var spin = (FrameworkElement)SpinBox(numKey, def, step, 1);
            spin.VerticalAlignment = VerticalAlignment.Center;
            spin.Margin = new Thickness(10, 0, 0, 0);
            var unitText = new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
            row.Children.Add(check);
            row.Children.Add(spin);
            row.Children.Add(unitText);
            if (extra != null) row.Children.Add(extra);
            sp.Children.Add(row);

            Action<bool> enable = delegate (bool yes)
            {
                spin.IsEnabled = yes;
                spin.Opacity = yes ? 1 : 0.45;
                unitText.Opacity = yes ? 1 : 0.45;
                if (extra != null) extra.IsEnabled = yes;
            };
            enable(on);
            check.Checked += delegate
            {
                int v;   // an older ini kept "0" here to mean "off": put the default back
                if (!int.TryParse(settings.Get(numKey, ""), out v) || v < 1) settings.Set(numKey, def);
                Save(onKey, "on");
                enable(true);
            };
            check.Unchecked += delegate { Save(onKey, "off"); enable(false); };

            var hintBlock = Hint(hint);
            hintBlock.Margin = new Thickness(26, 4, 0, 0);   // under the checkbox text
            sp.Children.Add(hintBlock);
            return sp;
        }

        // The file-size limit and the "placeholder" checkbox on one row.
        StackPanel FileLimitRow()
        {
            var stubs = new CheckBox
            {
                Content = L.T("Вместо них — пустышка"),
                IsChecked = settings.Get("stubs", "on") == "on",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 4, 0, 4)
            };
            stubs.Checked += delegate { Save("stubs", "on"); };
            stubs.Unchecked += delegate { Save("stubs", "off"); };
            return LimitRow("maxFileOn", "maxFileMb", "200", 10,
                L.T("Не копировать файлы больше"), L.T("МБ"), stubs,
                L.T("Крупные файлы обычно не нужны. Пустышка — текстовый файл с именем, размером и датой пропущенного файла."));
        }

        // The file-type filter: one list, and a checkbox that turns it from "only these" into "all but these".
        StackPanel FilterSection()
        {
            bool exclude = settings.Get("extensionsExclude", "off") == "on";
            var sp = new StackPanel();
            var title = new TextBlock { FontWeight = FontWeights.SemiBold };
            Action setTitle = delegate
            {
                title.Text = exclude ? L.T("Не копировать эти типы файлов") : L.T("Копировать только эти типы файлов");
            };
            setTitle();
            sp.Children.Add(title);

            var box = ExtensionsBox();
            box.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(box);

            var check = new CheckBox { Content = L.T("Наоборот: копировать всё, кроме этих типов"), IsChecked = exclude, Margin = new Thickness(0, 8, 0, 0) };
            check.Checked += delegate { exclude = true; setTitle(); Save("extensionsExclude", "on"); };
            check.Unchecked += delegate { exclude = false; setTitle(); Save("extensionsExclude", "off"); };
            sp.Children.Add(check);

            sp.Children.Add(Hint(L.T("Через запятую, например: pdf, docx, jpg. Пусто — копировать все файлы.")));
            return sp;
        }

        StackPanel CheckRow(string key, string title, string hint, string def = "off")
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            var check = new CheckBox { Content = title, IsChecked = settings.Get(key, def) == "on" };
            check.Checked += delegate { Save(key, "on"); };
            check.Unchecked += delegate { Save(key, "off"); };
            sp.Children.Add(check);
            var hintBlock = Hint(hint);
            hintBlock.Margin = new Thickness(26, 2, 0, 0);   // under the checkbox text
            sp.Children.Add(hintBlock);
            return sp;
        }

        StackPanel AutostartRow()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            var check = new CheckBox { Content = L.T("Запускать при входе в Windows"), IsChecked = Autostart.Enabled };
            check.Checked += delegate { Autostart.Set(true); };
            check.Unchecked += delegate { Autostart.Set(false); };
            sp.Children.Add(check);
            var hintBlock = Hint(L.T("Для текущего пользователя, без прав администратора."));
            hintBlock.Margin = new Thickness(26, 2, 0, 0);
            sp.Children.Add(hintBlock);
            return sp;
        }

        void Save(string key, string value)
        {
            settings.Set(key, value);
            var h = SettingsChanged;
            if (h != null) h();
        }

        // ---------- About ----------

        void ShowAboutHandler(StandaloneWindow owner)
        {
            AboutWindow about = About();
            about.Owner = this;
            about.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            about.ShowDialog();
            if (about.LanguageChanged)
            {
                L.ApplyChoice();
                var h = ReopenRequested;
                if (h != null) h();
                Close();
            }
        }

        AboutWindow AboutDialog()
        {
            return new AboutWindow(
                new BitmapImage(new Uri("pack://application:,,,/flashstashbee.png")),
                "Flash[Stash]Bee", "Violet", "flashstashbee@chagear.top", "FlashStashBee",
                L.T("Фоновая утилита: копирует вставленную флешку в папку на диске."),
                Sections, settings.Set,
                L.T("Висит в трее. Вставили флешку — её содержимое само копируется в папку на диске."),
                Icons);
        }

        static readonly string[] Icons = { Glyph.Usb, Glyph.Folder, Glyph.Shield, Glyph.Info };

        static string[] Sections
        {
            get
            {
                return new[]
                {
                    L.T("Как работает"),
                    L.T("Фиолетовая флешка в трее. Значок в углу: три точки — ждёт флешку, стрелка вниз — идёт копирование, пауза, «!» — ошибка, число — сколько флешек обработано. Двойной клик по значку закрывает программу."),
                    L.T("Куда копирует"),
                    L.T("В выбранную папку, внутри — своя папка на каждую флешку (по серийнику); повторная вставка досинхронизируется в неё, без дубликатов."),
                    L.T("Безопасность"),
                    L.T("Копирует только тома, вставленные после запуска. Уже стоявшие при старте не трогает. Ненужные флешки можно внести в чёрный список в настройках."),
                    L.T("Только на этом компьютере"),
                    L.T("Ничего не отправляет в сеть — просто копирует файлы на диск."),
                };
            }
        }
    }
}
