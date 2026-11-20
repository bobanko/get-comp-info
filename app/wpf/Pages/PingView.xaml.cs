using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CompInfo.Wpf
{
    // «Быстрый ping» (PingPage of ToolPages.cs on WinForms): own address, gateway, DNS and domain or internet,
    // one request per node, stopping at the first failure (QuickPing of Network.cs).
    public partial class PingView : UserControl, ISection
    {
        readonly ObservableCollection<PingRow> rows = new ObservableCollection<PingRow>();
        bool running;

        public PingView()
        {
            InitializeComponent();
            Nodes.ItemsSource = rows;
            Glyph.Label(RunButton, Glyph.Refresh, L.T("Проверить ещё раз"));
            RunButton.ToolTip = L.T("Проверить ещё раз") + "   (F5)";
            NodeColumn.Header = L.T("Узел");
            AddressColumn.Header = L.T("Адрес");
            ResultColumn.Header = L.T("Результат");
        }

        public void Opened() { Run(); }

        public bool Shortcut(KeyEventArgs e)
        {
            if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None) { Run(); return true; }
            return false;
        }

        public bool CanLeave() { return true; }
        public void DevicesChanged() { }

        void Run_Click(object sender, RoutedEventArgs e) { Run(); }

        async void Run()
        {
            if (running) return;
            running = true;
            RunButton.IsEnabled = false;
            rows.Clear();
            Summary.Clear();
            Adapter.Note(Glyph.More, L.T("Определение активного адаптера и основного маршрута…"));
            try
            {
                NetworkProfiles profiles = NetworkProfiles.Read(AppPaths.File("network-profiles.json"));
                QuickPing.Plan plan = await Task.Run(() => QuickPing.Prepare(profiles));
                Adapter.Note(Glyph.Network, L.T("Адаптер: ") + plan.AdapterName + L.T(" (индекс ") + plan.AdapterIndex + ")");
                foreach (QuickPing.Target t in plan.Targets) rows.Add(new PingRow(t.Label, t.Address ?? L.T("не настроен")));
                for (int i = 0; i < plan.Targets.Count; i++)
                {
                    QuickPing.Target t = plan.Targets[i];
                    rows[i].Set("Running", L.T("отправка запроса, ожидание ответа…"));
                    string details = null;
                    string error = await Task.Run(() => { string d; string failure = QuickPing.Ping(t.Address, out d); details = d; return failure; });
                    if (error == null) { rows[i].Set("Ok", L.T("ответил: ") + details); continue; }
                    rows[i].Set("Failed", error);
                    for (int j = i + 1; j < plan.Targets.Count; j++) rows[j].Set("Skipped", L.T("не проверялся"));
                    Summary.Show(Severity.Warning, Glyph.Warning, L.T("Проверка остановлена на узле «") + t.Label + L.T("». Следующие узлы не проверялись."));
                    return;
                }
                Summary.Show(Severity.Success, Glyph.Check, L.T("Все узлы ответили."));
            }
            catch (Exception e)
            {
                Adapter.Show(Severity.Error, Glyph.Warning, e.Message);
            }
            finally
            {
                running = false;
                RunButton.IsEnabled = true;
            }
        }
    }

    // A node of the ping: Waiting, Running, Ok, Failed or Skipped.
    public class PingRow : INotifyPropertyChanged
    {
        public string Label { get; private set; }
        public string Address { get; private set; }
        public string State { get; private set; }
        public string Result { get; private set; }

        public PingRow(string label, string address) { Label = label; Address = address; State = "Waiting"; Result = ""; }

        public void Set(string state, string result)
        {
            State = state;
            Result = result;
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(""));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
