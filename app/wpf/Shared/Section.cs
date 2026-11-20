using System.Windows.Input;

namespace CompInfo.Wpf
{
    // A page that can be a section of the toolbox window (Page of MainForm.cs on WinForms).
    public interface ISection
    {
        // Every time the section is opened.
        void Opened();

        // A key the main window did not use (F5, Ctrl+S, …): true when the section handled it.
        bool Shortcut(KeyEventArgs e);

        // The window is about to close, switch the section or be built again: false keeps it (an operation runs).
        bool CanLeave();

        // A USB device was plugged in or removed.
        void DevicesChanged();
    }

    // A section that another one can start (the synchronization, from «Синхронизировать» of the collection).
    public interface SyncStarter
    {
        void Start();
    }
}
