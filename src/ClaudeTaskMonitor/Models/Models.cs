namespace ClaudeTaskMonitor.Models;

/// <summary>Eintrag aus der Prozessliste des Systems (NtQuerySystemInformation).</summary>
/// <param name="CreationTime">FILETIME (UTC) des Prozessstarts.</param>
/// <param name="CpuTime">Kernel- plus User-Zeit in 100-ns-Einheiten.</param>
public sealed record RawProcess(int Pid, int ParentPid, string Name, long CreationTime, long CpuTime, long WorkingSet);

/// <summary>Inhalt von %USERPROFILE%\.claude\sessions\&lt;pid&gt;.json (nur die benötigten Felder).</summary>
public sealed class SessionFile
{
    public int Pid { get; set; }
    public string? SessionId { get; set; }
    public string? HostSessionId { get; set; }
    public string? Cwd { get; set; }
    public long StartedAt { get; set; }
    public long ProcStart { get; set; }
    public string? Name { get; set; }
    public string? Status { get; set; }
    public string? WaitingFor { get; set; }
    public string? Entrypoint { get; set; }
    public string? Kind { get; set; }
}

/// <summary>Ein Prozess im Baum einer Sitzung mit berechneter CPU-Last.</summary>
/// <param name="Key">Eindeutig über PID-Wiederverwendung hinweg (PID + Startzeit).</param>
/// <param name="Depth">0 = Wurzel der Sitzung.</param>
/// <param name="IsOrphan">Elternprozess beendet; über Merkliste oder --sponsor der Sitzung zugeordnet.</param>
public sealed record ProcessSample(string Key, int Pid, string Name, int Depth, double CpuPercent, long WorkingSet, bool IsOrphan = false);

/// <summary>Zusatzdaten einer Sitzung aus der Desktop-App und dem Transkript.</summary>
/// <param name="Summary">postTurnSummary.status_detail der Desktop-App (kurze Zusammenfassung des letzten Zugs).</param>
public sealed record SessionMetadata(
    string? DesktopTitle,
    string? TitleSource,
    string? Summary,
    string? DesktopCwd,
    DateTimeOffset? LastActivityAt,
    string? ProjectId,
    string? ProjectName,
    string? ThreadId)
{
    public static readonly SessionMetadata Empty = new(null, null, null, null, null, null, null, null);

    /// <summary>Link zum Thread im Claude-Projekt, nur wenn Projekt- und Thread-ID bekannt sind.</summary>
    public string? ThreadUrl => ProjectId is not null && ThreadId is not null
        ? $"https://claude.ai/code/project/{ProjectId}?thread={ThreadId}"
        : null;
}

/// <summary>Eine Claude-Sitzung (Wurzelprozess) samt Nachfahren.</summary>
/// <param name="Status">busy, idle, waiting, unknown (ohne Sitzungsdatei) oder ended (nur noch verwaiste Prozesse).</param>
public sealed record SessionGroup(
    string Key,
    int RootPid,
    string Title,
    string Status,
    string? WaitingFor,
    string? Cwd,
    string? SessionId,
    string? HostSessionId,
    string? Entrypoint,
    bool HasSessionFile,
    long RootCreationTime,
    double TotalCpuPercent,
    long TotalWorkingSet,
    IReadOnlyList<ProcessSample> Processes)
{
    public SessionMetadata Metadata { get; init; } = SessionMetadata.Empty;
}
