using System.Runtime.InteropServices;
using System.Windows;
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
        _viewModel = new MainViewModel(new ProcessScanner(new SessionRegistry(SessionRegistry.DefaultDirectory)));
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.Start();
        Closed += (_, _) => _viewModel.Dispose();
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
