using System.Collections.ObjectModel;
using System.Windows.Threading;
using ClaudeTaskMonitor.Models;
using ClaudeTaskMonitor.Services;

namespace ClaudeTaskMonitor.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly MonitorService _monitor;
    private readonly DispatcherTimer _timer;
    private bool _scanning;
    private string _summaryText = "Suche Claude-Prozesse …";
    private string _totalCpuText = string.Empty;
    private bool _isEmpty;

    public MainViewModel(MonitorService monitor)
    {
        _monitor = monitor;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <summary>Sitzungen nach Projekt gruppiert; „Ohne Projekt“ steht zuletzt.</summary>
    public ObservableCollection<ProjectSectionViewModel> Sections { get; } = new();

    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }
    public string TotalCpuText { get => _totalCpuText; private set => SetProperty(ref _totalCpuText, value); }
    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }

    public async void Start()
    {
        await RefreshAsync();
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();

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
