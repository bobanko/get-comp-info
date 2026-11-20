using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CompInfo.Wpf
{
    // «Поиск по документам» of SUSpector: the search page of the toolbox (SearchView) in a window of its own,
    // to find a lost work file by its name or by a phrase in its text (user's choice, 21.11.2026).
    public class SearchWindow : Window
    {
        readonly SearchView page = new SearchView();

        public SearchWindow()
        {
            Title = L.T("Поиск по документам");
            Width = 980; Height = 620;
            MinWidth = 700; MinHeight = 420;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13.333;
            SetResourceReference(BackgroundProperty, "WindowBack");
            SetResourceReference(ForegroundProperty, "Text");
            page.Margin = new Thickness(18, 14, 18, 14);
            Content = page;

            Loaded += delegate { page.Opened(); };
            // Esc stops a running search (the page), otherwise closes the window.
            PreviewKeyDown += (s, e) =>
            {
                if (page.Shortcut(e)) e.Handled = true;
                else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            };
            Closing += (s, e) => { if (!page.CanLeave()) e.Cancel = true; };
        }
    }
}
