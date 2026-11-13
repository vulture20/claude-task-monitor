using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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

    /// <summary>Gewählte Deckkraft in Prozent (30–100). --opacity gilt nur für diesen Start, bis im Menü gewählt wird.</summary>
    private int _opacity;

    /// <summary>Maus über dem Fenster (Inhalt oder Titelleiste): dann voll deckend.</summary>
    private bool _hover;

    /// <summary>Nach Strg + Mausrad wird die neue Stufe kurz gezeigt, obwohl die Maus über dem Fenster ist.</summary>
    private DateTime _previewUntil;

    /// <summary>Prüft nur während Hover oder Vorschau, ob die Maus das Fenster verlassen hat.</summary>
    private readonly DispatcherTimer _hoverTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };

    private IntPtr _hwnd;
    private bool _layered;
    private byte? _appliedAlpha;

    public MainWindow()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        // --demo: feste Beispieldaten statt echter Prozesse, z. B. für Screenshots. Einstellungen werden nicht gespeichert.
        var demo = args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));
        _settings = demo ? new AppSettings { Persist = false, Width = 420, Height = 640 } : AppSettings.Load();

        // Sprache: --lang hat für diesen Start Vorrang vor der gespeicherten Wahl, die sonst gilt (Standard: automatisch).
        var language = ParseLanguageArgument(args) ?? Loc.Parse(_settings.Language) ?? UiLanguage.Auto;
        Loc.Instance.SetLanguage(language);

        // Transparenz: --opacity hat für diesen Start Vorrang vor der gespeicherten Wahl.
        _opacity = ParseOpacityArgument(args) ?? _settings.Opacity;
        _hoverTimer.Tick += (_, _) => UpdateHover();

        InitializeComponent();
        ApplyBounds(_settings);

        ISessionSource source = demo ? new DemoSessionSource() : MonitorService.CreateDefault();
        _viewModel = new MainViewModel(source, _settings, demo ? DemoSessionSource.ExpandedKey : null);
        DataContext = _viewModel;
        Loaded += (_, _) => _viewModel.Start();
        StateChanged += (_, _) => _viewModel.IsPaused = WindowState == WindowState.Minimized;
        Closing += (_, _) => SaveBounds();
        Closed += (_, _) =>
        {
            _hoverTimer.Stop();
            _viewModel.Dispose();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Dunkle Titelleiste (Windows 10 20H1 und neuer); auf älteren Systemen wirkungslos.
        var hwnd = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, Marshal.SizeOf<int>());

        _hwnd = hwnd;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        ApplyOpacity();
    }

    /// <summary>Mausbewegung über Titelleiste und Rahmen zählt ebenfalls als Hover.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_NCMOUSEMOVE)
            BeginHover();
        return IntPtr.Zero;
    }

    // ---------- Transparenz ----------

    /// <summary>
    /// Setzt die Deckkraft des ganzen Fensters über ein Layered Window (WS_EX_LAYERED + LWA_ALPHA).
    /// So bleiben Rahmen und Titelleiste erhalten; WPF-AllowsTransparency würde WindowStyle=None verlangen.
    /// Bei 100 % wird der Layered-Stil wieder entfernt, das Fenster verhält sich dann wie ohne die Funktion.
    /// Klicks gehen nie durch das Fenster (kein WS_EX_TRANSPARENT).
    /// </summary>
    private void ApplyOpacity()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        if (_opacity >= AppSettings.MaxOpacity)
        {
            if (_layered)
            {
                var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style & ~NativeMethods.WS_EX_LAYERED));
                _layered = false;
                _appliedAlpha = null;
            }
            return;
        }

        if (!_layered)
        {
            var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style | NativeMethods.WS_EX_LAYERED));
            _layered = true;
            _appliedAlpha = null;
        }

        var effective = _hover && DateTime.UtcNow >= _previewUntil ? AppSettings.MaxOpacity : _opacity;
        var alpha = (byte)Math.Round(255.0 * effective / AppSettings.MaxOpacity);
        if (_appliedAlpha == alpha)
            return;
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, alpha, NativeMethods.LWA_ALPHA);
        _appliedAlpha = alpha;
    }

    /// <summary>Übernimmt eine neue Stufe aus Menü oder Mausrad und speichert sie.</summary>
    private void SetOpacity(int percent)
    {
        percent = AppSettings.NormalizeOpacity(percent);
        if (percent == _opacity)
            return;
        _opacity = percent;
        _settings.Opacity = percent;
        _settings.Save();
        ApplyOpacity();
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e) => BeginHover();

    private void BeginHover()
    {
        if (!_hover)
        {
            _hover = true;
            ApplyOpacity();
        }
        if (!_hoverTimer.IsEnabled)
            _hoverTimer.Start();
    }

    /// <summary>
    /// Fragt die Mausposition gegen das Fensterrechteck ab. Zuverlässiger als MouseLeave, das über der
    /// Titelleiste und beim Öffnen des Kontextmenüs nicht passend feuert.
    /// </summary>
    private void UpdateHover()
    {
        var inside = _hwnd != IntPtr.Zero
            && NativeMethods.GetCursorPos(out var p)
            && NativeMethods.GetWindowRect(_hwnd, out var r)
            && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        _hover = inside;
        ApplyOpacity();
        if (!inside && DateTime.UtcNow >= _previewUntil)
            _hoverTimer.Stop();
    }

    /// <summary>Strg + Mausrad: Deckkraft in 10er-Schritten ändern, die neue Stufe kurz anzeigen.</summary>
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0)
            return;
        e.Handled = true;
        _previewUntil = DateTime.UtcNow.AddMilliseconds(1200);
        _appliedAlpha = null;
        SetOpacity(_opacity + Math.Sign(e.Delta) * AppSettings.OpacityStep);
        ApplyOpacity();
        if (!_hoverTimer.IsEnabled)
            _hoverTimer.Start();
    }

    private void OpacityMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, OpacityMenu))
            return;
        OpacityMenu.Items.Clear();
        for (var percent = AppSettings.MaxOpacity; percent >= AppSettings.MinOpacity; percent -= AppSettings.OpacityStep)
        {
            var item = new MenuItem
            {
                Header = percent == AppSettings.MaxOpacity ? Loc.Instance["MenuOpacityOpaque"] : Loc.F("MenuOpacityFormat", percent),
                IsChecked = percent == _opacity,
                Tag = percent,
            };
            item.Click += OpacityItem_Click;
            OpacityMenu.Items.Add(item);
        }
        OpacityMenu.Items.Add(new Separator());
        OpacityMenu.Items.Add(new MenuItem { Header = Loc.Instance["MenuOpacityHint"], IsEnabled = false });
    }

    private void OpacityItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: int percent })
            SetOpacity(percent);
    }

    /// <summary>Liest <c>--opacity 70</c> bzw. <c>--opacity=70</c> (auch mit %); ungültige Werte werden ignoriert.</summary>
    private static int? ParseOpacityArgument(string[] args)
    {
        string? value = null;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--opacity=", StringComparison.OrdinalIgnoreCase))
                value = arg["--opacity=".Length..];
            else if (string.Equals(arg, "--opacity", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                value = args[i + 1];
        }

        if (value is null || !int.TryParse(value.Trim().TrimEnd('%').Trim(), out var percent))
            return null;
        return AppSettings.NormalizeOpacity(percent);
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
