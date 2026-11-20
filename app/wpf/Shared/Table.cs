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
    //   - a column with Fill > 0 shares the room the others leave, as * of a Grid (Fill="2" gets twice as much);
    //   - a column without Width (Auto) takes its content, a column with Width keeps it;
    //   - a column the user dragged keeps his width from then on; the Fill columns still end the table at its right edge.
    // A table with its own fitting (the device database) sets the widths inside Fitting and leaves the user's columns alone.
    public static class TableColumns
    {
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
            public ScrollViewer Scroll;
            // the widths of the other columns at the last fitting: a GridView measures an Auto column only with its rows
            public double Others = -1;
        }

        static readonly ConditionalWeakTable<ListView, State> states = new ConditionalWeakTable<ListView, State>();

        static State Of(ListView list) { return states.GetValue(list, l => new State()); }

        static void FitChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var list = o as ListView;
            if (list == null || !(bool)e.NewValue) return;
            list.SizeChanged += delegate { Refit(list); };
            list.Loaded += delegate { Hook(list); Refit(list); };
            ((INotifyCollectionChanged)list.Items).CollectionChanged += delegate { Queue(list); };
            // an Auto column got its width after the Fill columns took the room: fit again
            list.LayoutUpdated += delegate
            {
                State s = Of(list);
                if (s.Others >= 0 && Math.Abs(OthersWidth(list) - s.Others) > 0.5) Queue(list);
            };
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
                    Queue(list);   // the Fill columns take what is left
                };
            }
            if (s.Scroll == null)
            {
                s.Scroll = FindChild<ScrollViewer>(list);
                // the vertical scroll bar comes or goes: the room for the columns changes
                if (s.Scroll != null) s.Scroll.ScrollChanged += (sender, e) => { if (e.ViewportWidthChange != 0) Queue(list); };
            }
        }

        static void Queue(ListView list)
        {
            State s = Of(list);
            if (s.Queued) return;
            s.Queued = true;
            list.Dispatcher.BeginInvoke(new Action(() => { s.Queued = false; AutoSize(list); }), DispatcherPriority.Background);
        }

        // The Auto columns measure their content again (a GridView does it only once), then the Fill columns share the rest.
        static void AutoSize(ListView list)
        {
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
            Refit(list);
        }

        // The Fill columns share the width the other columns leave.
        public static void Refit(ListView list)
        {
            var view = list.View as GridView;
            if (view == null || list.ActualWidth <= 0) return;
            State s = Of(list);
            Hook(list);
            double available = s.Scroll != null && s.Scroll.ViewportWidth > 0 ? s.Scroll.ViewportWidth : list.ActualWidth - 2;
            double used = 0, weights = 0;
            var fill = new List<GridViewColumn>();
            foreach (GridViewColumn c in view.Columns)
            {
                double weight = GetFill(c);
                if (weight > 0 && !s.User.Contains(c)) { fill.Add(c); weights += weight; }
                else used += double.IsNaN(c.Width) ? c.ActualWidth : c.Width;
            }
            s.Others = used;
            if (fill.Count == 0) return;
            double rest = Math.Max(available - used - 1, 60 * fill.Count);
            Fitting(list, () =>
            {
                foreach (GridViewColumn c in fill) c.Width = Math.Max(40, Math.Floor(rest * GetFill(c) / weights));
            });
        }

        static double OthersWidth(ListView list)
        {
            var view = list.View as GridView;
            if (view == null) return 0;
            State s = Of(list);
            double used = 0;
            foreach (GridViewColumn c in view.Columns)
                if (!(GetFill(c) > 0 && !s.User.Contains(c))) used += double.IsNaN(c.Width) ? c.ActualWidth : c.Width;
            return used;
        }

        static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                var found = child as T ?? FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
