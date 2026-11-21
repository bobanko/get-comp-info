using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CompInfo.Wpf
{
    // The widths of a GridTable (Styles.xaml): a ListView with a GridView whose columns the user widens or narrows by the edge
    // in the header and drags to another place (the GridView does that itself), as in SupraWeed (user's choice 21.11.2026).
    //   - a column with Fill > 0 shares the room the others leave, as * of a Grid (Fill="2" gets twice as much),
    //     but never below MinFill: when the columns do not fit, the table scrolls sideways instead of crushing them;
    //   - a column without Width (Auto) takes its content, and at least its title;
    //   - a column with Width keeps it; a column the user dragged keeps his width from then on.
    // The fitting runs only when the table changes size, rows or visibility, before it is drawn. The room of the vertical
    // scroll bar is always left out, so its coming and going never fits the columns again (that made wrapped rows jump).
    // A table with its own fitting (the device database) sets the widths inside Fitting and leaves the user's columns alone.
    public static class TableColumns
    {
        public const double MinFill = 120;
        // the vertical scroll bar (10 px, Styles.xaml) and the few pixels of the GridView around the rows
        const double ScrollBarRoom = 20;

        public static readonly DependencyProperty FillProperty =
            DependencyProperty.RegisterAttached("Fill", typeof(double), typeof(TableColumns), new PropertyMetadata(0.0));
        public static double GetFill(DependencyObject o) { return (double)o.GetValue(FillProperty); }
        public static void SetFill(DependencyObject o, double value) { o.SetValue(FillProperty, value); }

        public static readonly DependencyProperty FitProperty =
            DependencyProperty.RegisterAttached("Fit", typeof(bool), typeof(TableColumns), new PropertyMetadata(false, FitChanged));
        public static bool GetFit(DependencyObject o) { return (bool)o.GetValue(FitProperty); }
        public static void SetFit(DependencyObject o, bool value) { o.SetValue(FitProperty, value); }

        class State
        {
            public int Fitting;
            public bool Queued;
            public readonly HashSet<GridViewColumn> User = new HashSet<GridViewColumn>();
            public readonly HashSet<GridViewColumn> Hooked = new HashSet<GridViewColumn>();
            // columns without Width: they follow their content, also of the rows that come later
            public readonly HashSet<GridViewColumn> Auto = new HashSet<GridViewColumn>();
        }

        static readonly ConditionalWeakTable<ListView, State> states = new ConditionalWeakTable<ListView, State>();

        static State Of(ListView list) { return states.GetValue(list, l => new State()); }

        static void FitChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var list = o as ListView;
            if (list == null || !(bool)e.NewValue) return;
            list.Loaded += delegate { Hook(list); Queue(list); };
            list.SizeChanged += (s, a) => { if (a.WidthChanged) Refit(list); };
            // rows that come later, and a table shown only now (it is measured only when visible)
            ((INotifyCollectionChanged)list.Items).CollectionChanged += delegate { Queue(list); };
            list.IsVisibleChanged += delegate { if (list.IsVisible) Queue(list); };
        }

        // The user dragged the edge of this column (its width is his until the table is built again).
        public static bool IsUserWidth(ListView list, GridViewColumn column) { return Of(list).User.Contains(column); }

        // Widths set by the program, not by the user.
        public static void Fitting(ListView list, Action set)
        {
            State s = Of(list);
            s.Fitting++;
            try { set(); }
            finally { s.Fitting--; }
        }

        static void Hook(ListView list)
        {
            var view = list.View as GridView;
            if (view == null) return;
            State s = Of(list);
            foreach (GridViewColumn c in view.Columns)
            {
                if (!s.Hooked.Add(c)) continue;
                if (double.IsNaN(c.Width) && GetFill(c) <= 0) s.Auto.Add(c);
                GridViewColumn column = c;
                ((INotifyPropertyChanged)column).PropertyChanged += (sender, e) =>
                {
                    if (e.PropertyName != "Width" || s.Fitting > 0) return;
                    s.User.Add(column);
                    Refit(list);   // the Fill columns take what is left
                };
            }
        }

        // Once per change, after the layout and before the table is drawn.
        static void Queue(ListView list)
        {
            State s = Of(list);
            if (s.Queued) return;
            s.Queued = true;
            list.Dispatcher.BeginInvoke(new Action(() => { s.Queued = false; AutoSize(list); }), DispatcherPriority.Loaded);
        }

        // The Auto columns measure their content again (a GridView does it only once) and keep at least the width of their
        // title (the GridView sizes them by the cells only), then the Fill columns share the rest.
        static void AutoSize(ListView list)
        {
            if (!list.IsVisible) return;
            Hook(list);
            State s = Of(list);
            Fitting(list, () =>
            {
                foreach (GridViewColumn c in s.Auto)
                {
                    if (s.User.Contains(c)) continue;
                    c.Width = c.ActualWidth;
                    c.Width = double.NaN;
                }
            });
            list.UpdateLayout();
            Fitting(list, () =>
            {
                foreach (GridViewColumn c in s.Auto)
                {
                    if (s.User.Contains(c)) continue;
                    double title = TitleWidth(list, c);
                    if (title > c.ActualWidth) c.Width = Math.Ceiling(title);
                }
            });
            Refit(list);
        }

        // The Fill columns share the width the other columns leave (at least MinFill each; the table scrolls sideways then).
        public static void Refit(ListView list)
        {
            var view = list.View as GridView;
            if (view == null || list.ActualWidth <= 0) return;
            State s = Of(list);
            Hook(list);
            double available = list.ActualWidth - ScrollBarRoom;
            double used = 0, weights = 0;
            var fill = new List<GridViewColumn>();
            foreach (GridViewColumn c in view.Columns)
            {
                double weight = GetFill(c);
                if (weight > 0 && !s.User.Contains(c)) { fill.Add(c); weights += weight; }
                else used += double.IsNaN(c.Width) ? c.ActualWidth : c.Width;
            }
            if (fill.Count == 0) return;
            double rest = available - used;
            Fitting(list, () =>
            {
                foreach (GridViewColumn c in fill)
                {
                    double w = Math.Max(MinFill, Math.Floor(rest * GetFill(c) / weights));
                    if (double.IsNaN(c.Width) || Math.Abs(c.Width - w) > 0.5) c.Width = w;
                }
            });
        }

        // The width the title of a column needs: its text in the semibold of GridTableHeader, its padding (6 + 6), a little more.
        static double TitleWidth(ListView list, GridViewColumn c)
        {
            string text = c.Header as string;
            if (string.IsNullOrEmpty(text)) return 0;
            var face = new Typeface(list.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face,
                list.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(list).PixelsPerDip);
            return formatted.WidthIncludingTrailingWhitespace + 12 + 4;
        }
    }
}
