using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ClaudeTaskMonitor.Native;
using ClaudeTaskMonitor.Services;
using ClaudeTaskMonitor.ViewModels;

namespace ClaudeTaskMonitor;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(MonitorService.CreateDefault());
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.Start();
        Closed += (_, _) => _viewModel.Dispose();
    }

    /// <summary>
    /// Doppelklick auf eine Sitzungskarte öffnet den Thread im Browser. Der erste Klick hat die Karte bereits
    /// auf- bzw. zugeklappt; das wird rückgängig gemacht, der zweite Klick erreicht den Umschalter nicht.
    /// </summary>
    private void SessionCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: SessionGroupViewModel session } || !session.CanOpenThread)
            return;

        e.Handled = true;
        session.IsExpanded = !session.IsExpanded;
        session.OpenThread();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Dunkle Titelleiste (Windows 10 20H1 und neuer); auf älteren Systemen wirkungslos.
        var hwnd = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, Marshal.SizeOf<int>());
    }
}
