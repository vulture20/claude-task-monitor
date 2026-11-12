namespace ClaudeTaskMonitor.Models;

/// <summary>Eintrag aus dem System-Snapshot (Toolhelp), noch ohne Zeiten.</summary>
public sealed record RawProcess(int Pid, int ParentPid, string Name);

/// <summary>Per OpenProcess gelesene Werte eines Prozesses.</summary>
/// <param name="CreationTime">FILETIME (UTC) des Prozessstarts.</param>
/// <param name="CpuTime">Kernel- plus User-Zeit in 100-ns-Einheiten.</param>
public sealed record ProcessDetails(long CreationTime, long CpuTime, long WorkingSet, string? ImagePath);

/// <summary>Inhalt von %USERPROFILE%\.claude\sessions\&lt;pid&gt;.json (nur die benötigten Felder).</summary>
public sealed class SessionFile
{
    public int Pid { get; set; }
    public string? SessionId { get; set; }
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
public sealed record ProcessSample(string Key, int Pid, string Name, int Depth, double CpuPercent, long WorkingSet);

/// <summary>Eine Claude-Sitzung (Wurzelprozess) samt Nachfahren.</summary>
public sealed record SessionGroup(
    string Key,
    int RootPid,
    string Title,
    string Status,
    string? WaitingFor,
    string? Cwd,
    string? SessionId,
    string? Entrypoint,
    bool HasSessionFile,
    long RootCreationTime,
    double TotalCpuPercent,
    long TotalWorkingSet,
    IReadOnlyList<ProcessSample> Processes);
