using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace CompInfo.Wpf
{
    // «О программе», laid out as in the WinForms programs (About in Ui.cs): the picture with the language and theme
    // switches under it; name, version, sections, contacts on the right. The theme changes at once (all windows
    // repaint); a new language needs the windows built again, so a language choice calls languageChanged.
    // Used by AboutWindow (the standalone programs) and by the «О программе» section of the toolbox.
    public class AboutContent : Grid
    {
        public const string Site = "chagear.top";

        readonly string email;
        readonly TextBlock copied = new TextBlock();
        DispatcherTimer copiedTimer;

        // The right column, for links of the caller after the contacts.
        public readonly StackPanel Text = new StackPanel();

        // name marks its coloured part in square brackets («you[SBee]»), highlightKey is its colour (a WpfTheme key);
        // intro may be null; sections = heading, text, heading, text…; save keeps a choice in the settings (key, value);
        // icons (optional) — a glyph per section, shown in the colour of the name before its heading, as in HashPot;
        // page — the program's page on the site (chagear.top/<page>/), the folder of chagear-site/public.
        public AboutContent(ImageSource picture, string name, string highlightKey, string email, string page, string subtitle, string intro,
            string[] sections, Action<string, string> save, Action languageChanged, double textWidth, string[] icons = null)
        {
            this.email = email;
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(textWidth) });

            // the picture, and language and theme right under it: rarely changed, so they live here
            var left = new StackPanel { Width = 132, Margin = new Thickness(0, 0, 20, 0) };
            left.Children.Add(new Image { Source = picture, Width = 132, Height = 132, Stretch = Stretch.Uniform });
            string[] codes = { "uk", "en", "ru" };
            left.Children.Add(Switch(new[] { "УКР", "ENG", "РУС" }, false, new[] { "Українська", "English", "Русский" },
                Array.IndexOf(codes, L.Code), new Thickness(0, 14, 0, 6), delegate (int i)
                {
                    L.Choice = codes[i];
                    save("language", codes[i]);
                    languageChanged();
                }));
            string[] themes = { "light", "system", "dark" };
            left.Children.Add(Switch(new[] { Glyph.Sun, Glyph.Theme, Glyph.Moon }, true,
                new[] { L.T("Светлая тема"), L.T("Как в Windows"), L.T("Тёмная тема") },
                Math.Max(0, Array.IndexOf(themes, WpfTheme.Choice)), new Thickness(0), delegate (int i)
                {
                    WpfTheme.Apply(themes[i]);
                    save("theme", themes[i]);
                }));
            Children.Add(left);

            Grid.SetColumn(Text, 1);
            Children.Add(Text);
            Text.Children.Add(Brand.Title(name, highlightKey, 21));
            Text.Children.Add(Paragraph(subtitle, 0));
            var version = Paragraph(L.T("Версия ") + AppVersion.Text +
                (PowerShell.IsAdministrator() ? L.T(" · запущена с правами администратора") : ""), 12);
            version.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            Text.Children.Add(version);
            if (!string.IsNullOrEmpty(intro)) Text.Children.Add(Paragraph(intro, 12));
            for (int i = 0; i + 1 < sections.Length; i += 2)
            {
                var heading = Paragraph(sections[i], 0);
                heading.FontWeight = FontWeights.SemiBold;
                string icon = icons != null && i / 2 < icons.Length ? icons[i / 2] : null;
                if (icon == null)
                {
                    Text.Children.Add(heading);
                    Text.Children.Add(Paragraph(sections[i + 1], 12));
                    continue;
                }
                // the icon column: the text of every section starts at the same place, under its heading
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var glyph = Glyph.Block(icon, 16, highlightKey);
                glyph.VerticalAlignment = VerticalAlignment.Top;
                glyph.Margin = new Thickness(0, 3, 0, 0);
                row.Children.Add(glyph);
                var text = new StackPanel();
                text.Children.Add(heading);
                text.Children.Add(Paragraph(sections[i + 1], 0));
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                Text.Children.Add(row);
            }
            var contact = Paragraph(L.T("Связь с разработчиком"), 0);
            contact.FontWeight = FontWeights.SemiBold;
            Text.Children.Add(contact);

            // Service computers have no mail client: a click copies the address instead of opening mailto.
            var mailRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-11, 2, 0, 0) };
            var mail = new Button();
            Glyph.Label(mail, Glyph.Person, email);
            mail.SetResourceReference(StyleProperty, "Link");
            mail.Click += delegate { CopyMail(); };
            copied.VerticalAlignment = VerticalAlignment.Center;
            copied.Margin = new Thickness(4, 0, 0, 0);
            copied.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            copied.Text = L.T("← нажмите, чтобы скопировать");
            mailRow.Children.Add(mail);
            mailRow.Children.Add(copied);
            Text.Children.Add(mailRow);
            // the page of this program on the site (chagear.top/suspector), not the main page
            string link = Site + "/" + page;
            AddLink(Glyph.Open, link, null, delegate { OpenUrl("https://" + link + "/"); });
        }

        // A link row under the contacts: an icon, the link, an optional muted note after it.
        public void AddLink(string glyph, string text, string note, Action click)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-11, 0, 0, 0) };
            var link = new Button();
            Glyph.Label(link, glyph, text);
            link.SetResourceReference(StyleProperty, "Link");
            link.Click += delegate { click(); };
            row.Children.Add(link);
            if (note != null)
            {
                var muted = new TextBlock { Text = note, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                muted.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                row.Children.Add(muted);
            }
            Text.Children.Add(row);
        }

        static TextBlock Paragraph(string s, double bottom)
        {
            return new TextBlock { Text = Brand.Keep(s), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, bottom) };
        }

        // A segmented switch of three choices (text or icons of the font) with tooltips.
        static Border Switch(string[] labels, bool icons, string[] tips, int chosen, Thickness margin, Action<int> changed)
        {
            var grid = new UniformGrid { Rows = 1 };
            string group = Guid.NewGuid().ToString("N");
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                var item = new RadioButton { Content = labels[i], GroupName = group, ToolTip = tips[i], IsChecked = i == chosen };
                item.SetResourceReference(StyleProperty, "Segment");
                System.Windows.Automation.AutomationProperties.SetName(item, tips[i]);
                if (icons) { item.SetResourceReference(Control.FontFamilyProperty, "IconFont"); item.FontSize = 15; }
                else item.FontSize = 12;
                item.Checked += delegate { changed(index); };
                grid.Children.Add(item);
            }
            var frame = new Border { Child = grid, Margin = margin };
            frame.SetResourceReference(StyleProperty, "Segmented");
            return frame;
        }

        void CopyMail()
        {
            if (!ClipText.Set(email)) return;
            copied.Text = L.T("скопировано ✓");
            if (copiedTimer == null)
            {
                copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
                copiedTimer.Tick += delegate { copiedTimer.Stop(); copied.Text = L.T("← нажмите, чтобы скопировать"); };
            }
            copiedTimer.Stop();   // a second click starts the 2.5 s again
            copiedTimer.Start();
        }

        // Opens in the default browser; if that fails (no browser, locked down PC), the address is copied.
        public static void OpenSite(string site)
        {
            OpenUrl("https://" + site);
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { ClipText.Set(url); }
        }
    }

    // «О программе» of a standalone program: the content and «Закрыть». After a language choice it closes with
    // LanguageChanged; the caller builds the main window again and opens this once more.
    public class AboutWindow : Window
    {
        public bool LanguageChanged { get; private set; }

        public AboutWindow(ImageSource picture, string name, string highlightKey, string email, string page, string subtitle,
            string[] sections, Action<string, string> save, string intro = null, string[] icons = null)
        {
            Title = L.T("О программе");
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");

            var root = new Grid { Margin = new Thickness(20, 18, 20, 20) };
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            root.Children.Add(new AboutContent(picture, name, highlightKey, email, page, subtitle, intro, sections, save,
                delegate { LanguageChanged = true; Close(); }, 460, icons));

            var close = new Button { Content = L.T("Закрыть"), MinWidth = 110, IsDefault = true, IsCancel = true,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            close.SetResourceReference(StyleProperty, "AccentButton");
            close.Click += delegate { Close(); };   // IsCancel alone answers only Esc
            Grid.SetRow(close, 1);
            root.Children.Add(close);

            WpfTheme.DarkTitle(this);
            Loaded += delegate { close.Focus(); };
        }
    }

    public static class ClipText
    {
        // The clipboard may be held by another program for a moment: a few tries before giving up.
        public static bool Set(string text) { return Set(text, out _); }

        public static bool Set(string text, out string error)
        {
            error = null;
            for (int attempt = 0; ; attempt++)
            {
                try { Clipboard.SetDataObject(text, true); return true; }
                catch (Exception e)
                {
                    if (attempt >= 4) { error = e.Message; return false; }
                    System.Threading.Thread.Sleep(40);
                }
            }
        }
    }
}
