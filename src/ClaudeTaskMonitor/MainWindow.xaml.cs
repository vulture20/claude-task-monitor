using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ClaudeTaskMonitor.Localization;
using ClaudeTaskMonitor.Native;
using ClaudeTaskMonitor.Services;
using ClaudeTaskMonitor.ViewModels;

namespace ClaudeTaskMonitor;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    private readonly AppSettings _settings;
    private readonly MainViewModel _viewModel;

    /// <summary>Karte, auf der das Kontextmenü geöffnet wurde (für die Thread-Einträge).</summary>
    private SessionGroupViewModel? _menuSession;

    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        // --demo: feste Beispieldaten statt echter Prozesse, z. B. für Screenshots. Einstellungen werden nicht gespeichert.
        var demo = args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));
        _settings = demo ? new AppSettings { Persist = false, Width = 420, Height = 640 } : AppSettings.Load();

        // Sprache: --lang hat für diesen Start Vorrang vor der gespeicherten Wahl, die sonst gilt (Standard: automatisch).
        var language = ParseLanguageArgument(args) ?? Loc.Parse(_settings.Language) ?? UiLanguage.Auto;
        Loc.Instance.SetLanguage(language);

        InitializeComponent();
        ApplyBounds(_settings);

        ISessionSource source = demo ? new DemoSessionSource() : MonitorService.CreateDefault();
        _viewModel = new MainViewModel(source, _settings, demo ? DemoSessionSource.ExpandedKey : null);
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.Start();
        StateChanged += (_, _) => _viewModel.IsPaused = WindowState == WindowState.Minimized;
        Closing += (_, _) => SaveBounds();
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

    // ---------- Fensterposition ----------

    /// <summary>Übernimmt gespeicherte Position und Größe, wenn das Fenster damit auf dem virtuellen Bildschirm sichtbar bleibt.</summary>
    private void ApplyBounds(AppSettings settings)
    {
        if (settings.Width is { } w && settings.Height is { } h && w >= MinWidth && h >= MinHeight)
        {
            Width = w;
            Height = h;
        }

        if (settings.Left is not { } left || settings.Top is not { } top)
            return;

        var visible = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        // Mindestens die Titelleiste (100 × 30) muss erreichbar bleiben.
        if (visible.IntersectsWith(new Rect(left, top, 100, 30)))
        {
            Left = left;
            Top = top;
        }
    }

    private void SaveBounds()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty)
            return;
        _settings.Left = Math.Round(bounds.Left);
        _settings.Top = Math.Round(bounds.Top);
        _settings.Width = Math.Round(bounds.Width);
        _settings.Height = Math.Round(bounds.Height);
        _settings.Save();
    }

    // ---------- Karten ----------

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

    private void SessionHeader_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SessionGroupViewModel session } element)
            element.ToolTip = session.ToolTip;
    }

    private void SessionHeader_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element)
            element.ToolTip = null;
    }

    // ---------- Kontextmenü ----------

    private void Root_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _menuSession = FindSession(e.OriginalSource as DependencyObject);
        var hasThread = _menuSession?.CanOpenThread == true;
        var visibility = hasThread ? Visibility.Visible : Visibility.Collapsed;
        OpenThreadItem.Visibility = visibility;
        CopyThreadItem.Visibility = visibility;
        ThreadSeparator.Visibility = visibility;
    }

    private static SessionGroupViewModel? FindSession(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: SessionGroupViewModel session })
                return session;
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private void OpenThread_Click(object sender, RoutedEventArgs e) => _menuSession?.OpenThread();

    private void CopyThread_Click(object sender, RoutedEventArgs e)
    {
        if (_menuSession?.ThreadUrl is not { } url)
            return;
        try
        {
            Clipboard.SetText(url);
        }
        catch (COMException)
        {
            // Zwischenablage gerade von einem anderen Programm belegt.
        }
    }

    private void Interval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out var seconds))
            _viewModel.IntervalSeconds = seconds;
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Loc.Parse(tag) is { } language)
            _viewModel.Language = language;
    }

    /// <summary>Liest <c>--lang de|en|auto</c> bzw. <c>--lang=…</c>; unbekannte Werte werden ignoriert.</summary>
    private static UiLanguage? ParseLanguageArgument(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
                return Loc.Parse(arg["--lang=".Length..]);
            if (string.Equals(arg, "--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return Loc.Parse(args[i + 1]);
        }

        return null;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
