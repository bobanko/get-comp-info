using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace CompInfo.Wpf
{
    // «О программе» of a standalone program, laid out as in the WinForms programs (About in Ui.cs): the picture with
    // the language and theme switches under it; name, version, sections, contacts on the right; «Закрыть».
    // The theme changes at once (all windows repaint); a new language needs the windows built again:
    // the dialog closes with LanguageChanged, the caller builds the main window again and opens this once more.
    public class AboutWindow : Window
    {
        public const string Site = "chagear.top";

        public bool LanguageChanged { get; private set; }

        readonly string email;
        readonly TextBlock copied = new TextBlock();
        DispatcherTimer copiedTimer;

        // name marks its coloured part in square brackets («you[SBee]»), highlightKey is its colour (a WpfTheme key);
        // sections = heading, text, heading, text…; save keeps a choice in the ini (key, value).
        public AboutWindow(ImageSource picture, string name, string highlightKey, string email, string subtitle,
            string[] sections, Action<string, string> save)
        {
            this.email = email;
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
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            // the picture, and language and theme right under it: rarely changed, so they live here
            var left = new StackPanel { Width = 132, Margin = new Thickness(0, 0, 20, 0) };
            left.Children.Add(new Image { Source = picture, Width = 132, Height = 132, Stretch = Stretch.Uniform });
            string[] codes = { "uk", "en", "ru" };
            left.Children.Add(Switch(new[] { "УКР", "ENG", "РУС" }, false, new[] { "Українська", "English", "Русский" },
                Array.IndexOf(codes, L.Code), new Thickness(0, 14, 0, 6), delegate (int i)
                {
                    L.Choice = codes[i];
                    save("language", codes[i]);
                    LanguageChanged = true;
                    Close();
                }));
            string[] themes = { "light", "system", "dark" };
            left.Children.Add(Switch(new[] { Glyph.Sun, Glyph.Theme, Glyph.Moon }, true,
                new[] { L.T("Светлая тема"), L.T("Как в Windows"), L.T("Тёмная тема") },
                Math.Max(0, Array.IndexOf(themes, WpfTheme.Choice)), new Thickness(0), delegate (int i)
                {
                    WpfTheme.Apply(themes[i]);
                    save("theme", themes[i]);
                }));
            root.Children.Add(left);

            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            root.Children.Add(text);
            var title = Brand.Title(name, highlightKey, 21);
            text.Children.Add(title);
            text.Children.Add(Paragraph(subtitle, 0));
            var version = Paragraph(L.T("Версия ") + AppVersion.Text +
                (PowerShell.IsAdministrator() ? L.T(" · запущена с правами администратора") : ""), 12);
            version.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            text.Children.Add(version);
            for (int i = 0; i + 1 < sections.Length; i += 2)
            {
                var heading = Paragraph(sections[i], 0);
                heading.FontWeight = FontWeights.SemiBold;
                text.Children.Add(heading);
                text.Children.Add(Paragraph(sections[i + 1], 12));
            }
            var contact = Paragraph(L.T("Связь с разработчиком"), 0);
            contact.FontWeight = FontWeights.SemiBold;
            text.Children.Add(contact);

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
            text.Children.Add(mailRow);

            var site = new Button { Margin = new Thickness(-11, 0, 0, 0) };
            Glyph.Label(site, Glyph.Open, Site);
            site.SetResourceReference(StyleProperty, "Link");
            site.Click += delegate { OpenSite(Site); };
            text.Children.Add(site);

            var close = new Button { Content = L.T("Закрыть"), MinWidth = 110, IsDefault = true, IsCancel = true,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            close.SetResourceReference(StyleProperty, "AccentButton");
            close.Click += delegate { Close(); };   // IsCancel alone answers only Esc
            Grid.SetRow(close, 1);
            Grid.SetColumn(close, 1);
            root.Children.Add(close);

            WpfTheme.DarkTitle(this);
            Loaded += delegate { close.Focus(); };
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
                if (icons) { item.SetResourceReference(FontFamilyProperty, "IconFont"); item.FontSize = 15; }
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
            try { Process.Start(new ProcessStartInfo("https://" + site) { UseShellExecute = true }); }
            catch (Exception) { ClipText.Set("https://" + site); }
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
