using System.Collections.ObjectModel;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.ViewModels;

public sealed class SessionGroupViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _status = string.Empty;
    private string _statusText = string.Empty;
    private string _cpuText = string.Empty;
    private string _memoryText = string.Empty;
    private string _processCountText = string.Empty;
    private string _toolTip = string.Empty;
    private double _cpuPercent;
    private bool _isExpanded;

    public SessionGroupViewModel(SessionGroup group)
    {
        Key = group.Key;
        Update(group);
    }

    public string Key { get; }
    public ObservableCollection<ProcessRowViewModel> Processes { get; } = new();

    public string Title { get => _title; private set => SetProperty(ref _title, value); }

    /// <summary>Rohstatus (busy, idle, waiting, unknown) für die Einfärbung.</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public double CpuPercent { get => _cpuPercent; private set => SetProperty(ref _cpuPercent, value); }
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }
    public string ProcessCountText { get => _processCountText; private set => SetProperty(ref _processCountText, value); }
    public string ToolTip { get => _toolTip; private set => SetProperty(ref _toolTip, value); }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    public void Update(SessionGroup group)
    {
        Title = group.Title;
        Status = group.Status;
        StatusText = Formatting.Status(group.Status, group.WaitingFor);
        CpuPercent = group.TotalCpuPercent;
        CpuText = Formatting.Cpu(group.TotalCpuPercent);
        MemoryText = Formatting.Memory(group.TotalWorkingSet);
        ProcessCountText = group.Processes.Count == 1 ? "1 Prozess" : $"{group.Processes.Count} Prozesse";
        ToolTip = BuildToolTip(group);

        CollectionSync.Sync(
            Processes,
            group.Processes,
            p => p.Key,
            vm => vm.Key,
            p => new ProcessRowViewModel(p),
            (vm, p) => vm.Update(p));
    }

    private static string BuildToolTip(SessionGroup group)
    {
        var lines = new List<string> { $"PID {group.RootPid}" };
        if (!string.IsNullOrEmpty(group.SessionId))
            lines.Add($"Sitzung: {group.SessionId}");
        if (!string.IsNullOrEmpty(group.Cwd))
            lines.Add($"Ordner: {group.Cwd}");
        if (!string.IsNullOrEmpty(group.Entrypoint))
            lines.Add($"Gestartet über: {group.Entrypoint}");
        if (!group.HasSessionFile)
            lines.Add("Keine Sitzungsdatei gefunden (über die Desktop-App erkannt)");
        return string.Join(Environment.NewLine, lines);
    }
}
