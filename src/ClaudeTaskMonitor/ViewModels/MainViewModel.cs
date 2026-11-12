using System.Collections.ObjectModel;
using System.Windows.Threading;
using ClaudeTaskMonitor.Models;
using ClaudeTaskMonitor.Services;

namespace ClaudeTaskMonitor.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISessionSource _monitor;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private bool _scanning;
    private bool _started;
    private bool _isPaused;
    private string _summaryText = "Suche Claude-Prozesse …";
    private string _totalCpuText = string.Empty;
    private bool _isEmpty;
    private string? _expandKey;

    /// <param name="expandKey">Sitzung, deren Karte nach dem ersten Durchlauf aufgeklappt wird (Demo-Modus).</param>
    public MainViewModel(ISessionSource monitor, AppSettings settings, string? expandKey = null)
    {
        _monitor = monitor;
        _expandKey = expandKey;
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(settings.IntervalSeconds) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Sitzungen nach Projekt gruppiert; „Ohne Projekt“ steht zuletzt.</summary>
    public ObservableCollection<ProjectSectionViewModel> Sections { get; } = new();

    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }
    public string TotalCpuText { get => _totalCpuText; private set => SetProperty(ref _totalCpuText, value); }
    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }

    public bool Topmost
    {
        get => _settings.Topmost;
        set
        {
            if (_settings.Topmost == value)
                return;
            _settings.Topmost = value;
            OnPropertyChanged();
            _settings.Save();
        }
    }

    /// <summary>Abtastintervall in Sekunden (1, 2 oder 5).</summary>
    public int IntervalSeconds
    {
        get => _settings.IntervalSeconds;
        set
        {
            if (!AppSettings.AllowedIntervals.Contains(value) || _settings.IntervalSeconds == value)
                return;
            _settings.IntervalSeconds = value;
            _timer.Interval = TimeSpan.FromSeconds(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsInterval1));
            OnPropertyChanged(nameof(IsInterval2));
            OnPropertyChanged(nameof(IsInterval5));
            _settings.Save();
        }
    }

    public bool IsInterval1 => IntervalSeconds == 1;
    public bool IsInterval2 => IntervalSeconds == 2;
    public bool IsInterval5 => IntervalSeconds == 5;

    /// <summary>Bei minimiertem Fenster wird nicht abgetastet.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (!SetProperty(ref _isPaused, value) || !_started)
                return;
            if (value)
            {
                _timer.Stop();
            }
            else
            {
                _monitor.ResetBaseline();
                _ = RefreshAsync();
                _timer.Start();
            }
        }
    }

    public async void Start()
    {
        _started = true;
        if (_isPaused)
            return;
        await RefreshAsync();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _monitor.Dispose();
    }

    private async Task RefreshAsync()
    {
        if (_scanning)
            return;
        _scanning = true;
        try
        {
            IReadOnlyList<SessionGroup> groups;
            try
            {
                groups = await Task.Run(_monitor.Scan);
            }
            catch (Exception)
            {
                // Ein fehlgeschlagener Durchlauf soll die Anzeige nicht beenden; nächster Takt versucht es erneut.
                return;
            }

            Apply(groups);
        }
        finally
        {
            _scanning = false;
        }
    }

    private void Apply(IReadOnlyList<SessionGroup> groups)
    {
        var sections = groups
            .GroupBy(g => g.Metadata.ProjectId ?? ProjectSectionViewModel.NoProjectKey)
            .Select(s => (Key: s.Key, Title: SectionTitle(s.Key, s), Groups: (IReadOnlyList<SessionGroup>)s.ToList()))
            .OrderBy(s => s.Key == ProjectSectionViewModel.NoProjectKey)
            .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        CollectionSync.Sync(
            Sections,
            sections,
            s => s.Key,
            vm => vm.Key,
            s =>
            {
                var vm = new ProjectSectionViewModel(s.Key);
                vm.Update(s.Title, s.Groups);
                return vm;
            },
            (vm, s) => vm.Update(s.Title, s.Groups));

        if (_expandKey is not null && Sections.SelectMany(s => s.Sessions).FirstOrDefault(s => s.Key == _expandKey) is { } expand)
        {
            expand.IsExpanded = true;
            _expandKey = null;
        }

        var processCount = groups.Sum(g => g.Processes.Count);
        var totalCpu = Math.Min(100, groups.Sum(g => g.TotalCpuPercent));
        IsEmpty = groups.Count == 0;
        TotalCpuText = Formatting.Cpu(totalCpu);
        SummaryText = groups.Count == 1
            ? $"1 Sitzung · {processCount} Prozesse"
            : $"{groups.Count} Sitzungen · {processCount} Prozesse";
    }

    private static string SectionTitle(string key, IEnumerable<SessionGroup> groups)
    {
        if (key == ProjectSectionViewModel.NoProjectKey)
            return "Ohne Projekt";
        var name = groups.Select(g => g.Metadata.ProjectName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        return name ?? "Projekt " + key;
    }
}
