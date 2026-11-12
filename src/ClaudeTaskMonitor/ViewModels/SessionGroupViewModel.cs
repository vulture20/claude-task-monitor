using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.ViewModels;

public sealed partial class SessionGroupViewModel : ObservableObject
{
    private const int ThreadShortLength = 6;

    // Automatisch vergebene Titel der Desktop-App ohne Aussagekraft, z. B. "Local session (from a project)".
    [GeneratedRegex(@"^(Local|Remote Control) session( \(from a project\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericTitleRegex();

    private string _headline = string.Empty;
    private string _status = string.Empty;
    private string _statusText = string.Empty;
    private string? _threadText;
    private string? _threadUrl;
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

    /// <summary>Zeile 1: Zusammenfassung des letzten Zugs, sonst Titel der Desktop-App, sonst Sitzungsname.</summary>
    public string Headline { get => _headline; private set => SetProperty(ref _headline, value); }

    /// <summary>Rohstatus (busy, idle, waiting, unknown, ended) für die Einfärbung.</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    /// <summary>Kurzform der Thread-ID (letzte Zeichen), null ohne Thread.</summary>
    public string? ThreadText { get => _threadText; private set => SetProperty(ref _threadText, value); }

    public string? ThreadUrl
    {
        get => _threadUrl;
        private set
        {
            if (SetProperty(ref _threadUrl, value))
                OnPropertyChanged(nameof(CanOpenThread));
        }
    }

    public bool CanOpenThread => ThreadUrl is not null;
    public double CpuPercent { get => _cpuPercent; private set => SetProperty(ref _cpuPercent, value); }
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }
    public string MemoryText { get => _memoryText; private set => SetProperty(ref _memoryText, value); }
    public string ProcessCountText { get => _processCountText; private set => SetProperty(ref _processCountText, value); }
    public string ToolTip { get => _toolTip; private set => SetProperty(ref _toolTip, value); }
    /// <summary>
    /// Aufgeklappt: Prozesszeilen werden gepflegt. Zugeklappt bleibt die Liste leer, damit zugeklappte Karten
    /// keine Zeilen-Updates (und damit keine Layout-Durchläufe) auslösen.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value))
                return;
            if (value && _lastGroup is not null)
                SyncProcesses(_lastGroup);
            else if (!value)
                Processes.Clear();
        }
    }

    private SessionGroup? _lastGroup;

    public void Update(SessionGroup group)
    {
        _lastGroup = group;
        var meta = group.Metadata;
        Headline = SingleLine(BuildHeadline(group));
        Status = group.Status;
        StatusText = Formatting.Status(group.Status, group.WaitingFor);
        ThreadText = meta.ThreadId is { Length: > ThreadShortLength } id ? "Thread …" + id[^ThreadShortLength..] : null;
        ThreadUrl = meta.ThreadUrl;
        CpuPercent = group.TotalCpuPercent;
        CpuText = Formatting.Cpu(group.TotalCpuPercent);
        MemoryText = Formatting.Memory(group.TotalWorkingSet);
        ProcessCountText = group.Processes.Count == 1 ? "1 Prozess" : $"{group.Processes.Count} Prozesse";
        ToolTip = BuildToolTip(group);

        if (IsExpanded)
            SyncProcesses(group);
    }

    private void SyncProcesses(SessionGroup group)
    {
        CollectionSync.Sync(
            Processes,
            group.Processes,
            p => p.Key,
            vm => vm.Key,
            p => new ProcessRowViewModel(p),
            (vm, p) => vm.Update(p));
    }

    /// <summary>Öffnet den Thread im Standardbrowser. Die URL besteht nur aus geprüften IDs (chan_…, cmsg_…).</summary>
    public void OpenThread()
    {
        if (ThreadUrl is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(ThreadUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Kein Standardbrowser o. Ä.: still ignorieren.
        }
    }

    private static string BuildHeadline(SessionGroup group)
    {
        var meta = group.Metadata;
        if (!string.IsNullOrWhiteSpace(meta.Summary))
            return meta.Summary!;
        if (!string.IsNullOrWhiteSpace(meta.DesktopTitle) && !GenericTitleRegex().IsMatch(meta.DesktopTitle!))
            return meta.DesktopTitle!;
        return group.Title;
    }

    private static string SingleLine(string text) => text.ReplaceLineEndings(" ").Trim();

    private static string BuildToolTip(SessionGroup group)
    {
        var meta = group.Metadata;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(meta.Summary))
            lines.Add(meta.Summary!);
        if (!string.IsNullOrWhiteSpace(meta.DesktopTitle))
            lines.Add($"Titel: {meta.DesktopTitle}");
        lines.Add($"Sitzung: {group.Title} (PID {group.RootPid})");
        if (meta.ProjectName is not null || meta.ProjectId is not null)
            lines.Add($"Projekt: {meta.ProjectName ?? meta.ProjectId}");
        if (meta.ThreadId is not null)
            lines.Add($"Thread: {meta.ThreadId}");
        if (!string.IsNullOrEmpty(group.SessionId))
            lines.Add($"Sitzungs-ID: {group.SessionId}");
        if (!string.IsNullOrEmpty(group.Cwd))
            lines.Add($"Ordner: {group.Cwd}");
        if (meta.LastActivityAt is { } last)
            lines.Add($"Letzte Aktivität: {last.ToLocalTime():g}");
        if (!string.IsNullOrEmpty(group.Entrypoint))
            lines.Add($"Gestartet über: {group.Entrypoint}");
        if (!group.HasSessionFile)
            lines.Add("Keine Sitzungsdatei gefunden (über die Desktop-App erkannt)");
        if (group.Status == "ended")
            lines.Add("Sitzung beendet, es laufen nur noch verwaiste Prozesse");
        if (meta.ThreadUrl is not null)
            lines.Add("Doppelklick: Thread im Browser öffnen");
        return string.Join(Environment.NewLine, lines);
    }
}
