using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.Services;

/// <summary>Ein Abtastdurchlauf: Prozessbäume ermitteln und um Sitzungsdaten (Titel, Projekt, Thread) ergänzen.</summary>
public sealed class MonitorService : IDisposable
{
    private readonly ProcessScanner _scanner;
    private readonly SessionMetadataResolver _metadata;

    public MonitorService(ProcessScanner scanner, SessionMetadataResolver metadata)
    {
        _scanner = scanner;
        _metadata = metadata;
    }

    public static MonitorService CreateDefault() =>
        new(new ProcessScanner(new SessionRegistry(ClaudePaths.SessionsDirectory)), new SessionMetadataResolver());

    public IReadOnlyList<SessionGroup> Scan()
    {
        var groups = _scanner.Scan();
        var enriched = groups.Select(g => g with { Metadata = _metadata.Resolve(g) }).ToList();
        _metadata.Prune(enriched);
        return enriched;
    }

    /// <summary>Nach einer Pause: CPU-Last nicht über die ganze Pause mitteln, sondern neu beginnen.</summary>
    public void ResetBaseline() => _scanner.ResetBaseline();

    public void Dispose() => _scanner.Dispose();
}
