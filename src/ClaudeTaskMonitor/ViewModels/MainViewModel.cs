using System.Collections.ObjectModel;
using System.Windows.Threading;
using ClaudeTaskMonitor.Models;
using ClaudeTaskMonitor.Services;

namespace ClaudeTaskMonitor.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly ProcessScanner _scanner;
    private readonly DispatcherTimer _timer;
    private bool _scanning;
    private string _summaryText = "Suche Claude-Prozesse …";
    private string _totalCpuText = string.Empty;
    private bool _isEmpty;

    public MainViewModel(ProcessScanner scanner)
    {
        _scanner = scanner;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<SessionGroupViewModel> Groups { get; } = new();

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
                groups = await Task.Run(_scanner.Scan);
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
        CollectionSync.Sync(
            Groups,
            groups,
            g => g.Key,
            vm => vm.Key,
            g => new SessionGroupViewModel(g),
            (vm, g) => vm.Update(g));

        var processCount = groups.Sum(g => g.Processes.Count);
        var totalCpu = Math.Min(100, groups.Sum(g => g.TotalCpuPercent));
        IsEmpty = groups.Count == 0;
        TotalCpuText = Formatting.Cpu(totalCpu);
        SummaryText = groups.Count == 1
            ? $"1 Sitzung · {processCount} Prozesse"
            : $"{groups.Count} Sitzungen · {processCount} Prozesse";
    }
}
