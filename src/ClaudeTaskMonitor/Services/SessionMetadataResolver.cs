using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.Services;

/// <summary>
/// Ergänzt Sitzungen um Titel und Zusammenfassung aus der Desktop-App sowie Projekt und Thread aus dem Transkript.
/// Alle Dateien werden zwischengespeichert und nur neu gelesen, wenn sie sich geändert haben. Aus dem Transkript
/// werden ausschließlich Projekt-ID, Projektname und Thread-ID übernommen, keine weiteren Inhalte.
/// Nicht threadsicher: nur von einem Thread nacheinander aufrufen.
/// </summary>
public sealed partial class SessionMetadataResolver
{
    private static readonly TimeSpan StampCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LookupRetryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TranscriptRescanInterval = TimeSpan.FromSeconds(10);
    private const int TranscriptMaxLines = 200;
    private const long TranscriptMaxChars = 2 * 1024 * 1024;

    // Zeile aus dem System-Prompt einer Projektsitzung: [project thread](https://claude.ai/code/project/chan_…?thread=cmsg_…)
    [GeneratedRegex(@"\[project thread\]\(https://claude\.ai/code/project/(chan_[A-Za-z0-9]+)\?thread=(cmsg_[A-Za-z0-9]+)\)")]
    private static partial Regex ThreadLinkRegex();

    // Im JSON-String maskiert: Project: \"Name\" (id: `chan_…`)
    [GeneratedRegex(@"Project: \\""((?:[^""\\]|\\.){1,200}?)\\"" \(id: `(chan_[A-Za-z0-9]+)`")]
    private static partial Regex ProjectNameRegex();

    [GeneratedRegex(@"<project id=\\""(chan_[A-Za-z0-9]+)\\""")]
    private static partial Regex WakeProjectRegex();

    [GeneratedRegex(@"<thread ts=\\""(cmsg_[A-Za-z0-9]+)\\""")]
    private static partial Regex WakeThreadRegex();

    [GeneratedRegex(@"^local_[0-9a-fA-F-]{8,64}$")]
    private static partial Regex HostSessionIdRegex();

    [GeneratedRegex(@"^[0-9a-fA-F-]{8,64}$")]
    private static partial Regex SessionIdRegex();

    [GeneratedRegex(@"[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumericRegex();

    private sealed class DesktopEntry
    {
        public string? Path;
        public DateTime NextLookup;
        public DateTime NextStampCheck;
        public DateTime Stamp;
        public string? Title;
        public string? TitleSource;
        public string? Summary;
        public string? Cwd;
        public DateTimeOffset? LastActivityAt;
    }

    private sealed class TranscriptEntry
    {
        public string? Path;
        public DateTime NextLookup;
        public DateTime NextScan;
        public long ScannedLength = -1;
        public bool Complete;
        public string? ProjectId;
        public string? ProjectName;
        public string? ThreadId;
    }

    private readonly Dictionary<string, DesktopEntry> _desktop = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TranscriptEntry> _transcripts = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _desktopDirectories = Array.Empty<string>();
    private DateTime _nextDesktopDirectoryLookup;

    public SessionMetadata Resolve(SessionGroup group)
    {
        var now = DateTime.UtcNow;
        var desktop = group.HostSessionId is { } host ? GetDesktop(host, now) : null;
        var transcript = group.SessionId is { } id ? GetTranscript(id, group.Cwd, now) : null;

        return new SessionMetadata(
            DesktopTitle: desktop?.Title,
            TitleSource: desktop?.TitleSource,
            Summary: desktop?.Summary,
            DesktopCwd: desktop?.Cwd,
            LastActivityAt: desktop?.LastActivityAt,
            ProjectId: transcript?.ProjectId,
            ProjectName: transcript?.ProjectName,
            ThreadId: transcript?.ThreadId);
    }

    /// <summary>Vergisst Einträge von Sitzungen, die nicht mehr angezeigt werden.</summary>
    public void Prune(IReadOnlyCollection<SessionGroup> active)
    {
        var hosts = active.Select(g => g.HostSessionId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sessions = active.Select(g => g.SessionId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _desktop.Keys.Where(k => !hosts.Contains(k)).ToList())
            _desktop.Remove(key);
        foreach (var key in _transcripts.Keys.Where(k => !sessions.Contains(k)).ToList())
            _transcripts.Remove(key);
    }

    // ---------- Desktop-App: claude-code-sessions\<Konto>\<Org>\<hostSessionId>.json ----------

    private DesktopEntry? GetDesktop(string hostSessionId, DateTime now)
    {
        if (!HostSessionIdRegex().IsMatch(hostSessionId))
            return null;

        if (!_desktop.TryGetValue(hostSessionId, out var entry))
            _desktop[hostSessionId] = entry = new DesktopEntry();

        if (entry.Path is null)
        {
            if (now < entry.NextLookup)
                return entry;
            entry.NextLookup = now + LookupRetryInterval;
            entry.Path = FindDesktopFile(hostSessionId, now);
            if (entry.Path is null)
                return entry;
        }

        if (now < entry.NextStampCheck)
            return entry;
        entry.NextStampCheck = now + StampCheckInterval;

        try
        {
            var stamp = File.GetLastWriteTimeUtc(entry.Path);
            if (stamp == entry.Stamp)
                return entry;
            if (!File.Exists(entry.Path))
            {
                entry.Path = null;
                return entry;
            }

            ReadDesktopFile(entry);
            entry.Stamp = stamp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Datei wird gerade geschrieben: beim nächsten Zeitstempel-Check erneut versuchen.
        }

        return entry;
    }

    private string? FindDesktopFile(string hostSessionId, DateTime now)
    {
        if (now >= _nextDesktopDirectoryLookup)
        {
            _desktopDirectories = ClaudePaths.DesktopSessionDirectories();
            _nextDesktopDirectoryLookup = now + LookupRetryInterval;
        }

        foreach (var dir in _desktopDirectories)
        {
            try
            {
                var file = Directory.EnumerateFiles(dir, hostSessionId + ".json", SearchOption.AllDirectories).FirstOrDefault();
                if (file is not null)
                    return file;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static void ReadDesktopFile(DesktopEntry entry)
    {
        using var stream = new FileStream(entry.Path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        entry.Title = GetString(root, "title");
        entry.TitleSource = GetString(root, "titleSource");
        entry.Cwd = GetString(root, "cwd");
        entry.Summary = root.TryGetProperty("postTurnSummary", out var summary) && summary.ValueKind == JsonValueKind.Object
            ? GetString(summary, "status_detail")
            : null;
        entry.LastActivityAt = root.TryGetProperty("lastActivityAt", out var last) && last.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ---------- Transkript: .claude\projects\<Ordner>\<sessionId>.jsonl ----------

    private TranscriptEntry? GetTranscript(string sessionId, string? cwd, DateTime now)
    {
        if (!SessionIdRegex().IsMatch(sessionId))
            return null;

        if (!_transcripts.TryGetValue(sessionId, out var entry))
            _transcripts[sessionId] = entry = new TranscriptEntry();

        if (entry.Complete)
            return entry;

        if (entry.Path is null)
        {
            if (now < entry.NextLookup)
                return entry;
            entry.NextLookup = now + LookupRetryInterval;
            entry.Path = FindTranscript(sessionId, cwd);
            if (entry.Path is null)
                return entry;
        }

        if (now < entry.NextScan)
            return entry;
        entry.NextScan = now + TranscriptRescanInterval;

        try
        {
            var length = new FileInfo(entry.Path).Length;
            if (length == entry.ScannedLength)
                return entry;
            ScanTranscript(entry);
            entry.ScannedLength = length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return entry;
    }

    private static string? FindTranscript(string sessionId, string? cwd)
    {
        var projects = ClaudePaths.ProjectsDirectory;
        var fileName = sessionId + ".jsonl";
        try
        {
            // Claude Code benennt den Ordner nach dem Arbeitsverzeichnis, Sonderzeichen werden zu '-'.
            if (!string.IsNullOrEmpty(cwd))
            {
                var guess = Path.Combine(projects, NonAlphanumericRegex().Replace(cwd, "-"), fileName);
                if (File.Exists(guess))
                    return guess;
            }

            if (!Directory.Exists(projects))
                return null;
            foreach (var dir in Directory.EnumerateDirectories(projects))
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>
    /// Liest die ersten Datensätze des Transkripts, bis Projekt, Projektname und Thread gefunden sind.
    /// Abbruch nach <see cref="TranscriptMaxLines"/> Zeilen bzw. <see cref="TranscriptMaxChars"/> Zeichen.
    /// </summary>
    private static void ScanTranscript(TranscriptEntry entry)
    {
        using var stream = new FileStream(entry.Path!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

        string? wakeProject = null, wakeThread = null, linkProject = null, linkThread = null, name = null, nameProject = null;
        var promptSnapshotSeen = false;
        long chars = 0;
        var lines = 0;
        var budgetExhausted = false;

        while (true)
        {
            if (lines >= TranscriptMaxLines || chars >= TranscriptMaxChars)
            {
                budgetExhausted = true;
                break;
            }

            var line = reader.ReadLine();
            if (line is null)
                break;
            lines++;
            chars += line.Length + 1;

            if (linkThread is null && ThreadLinkRegex().Match(line) is { Success: true } link)
            {
                linkProject = link.Groups[1].Value;
                linkThread = link.Groups[2].Value;
            }

            if (name is null && ProjectNameRegex().Match(line) is { Success: true } project)
            {
                name = DecodeJsonFragment(project.Groups[1].Value);
                nameProject = project.Groups[2].Value;
            }

            if (wakeProject is null && WakeProjectRegex().Match(line) is { Success: true } wp)
                wakeProject = wp.Groups[1].Value;
            if (wakeThread is null && WakeThreadRegex().Match(line) is { Success: true } wt)
                wakeThread = wt.Groups[1].Value;

            if (line.Contains("\"type\":\"prompt_snapshot\"", StringComparison.Ordinal))
                promptSnapshotSeen = true;

            if (linkThread is not null && name is not null)
                break;
        }

        // Der Link aus dem System-Prompt ist am verlässlichsten; der Wake-Umschlag ist der Rückfall.
        entry.ProjectId = linkProject ?? nameProject ?? wakeProject;
        entry.ThreadId = linkThread ?? wakeThread;
        entry.ProjectName = name is not null && (entry.ProjectId is null || nameProject == entry.ProjectId) ? name : null;

        // Fertig, wenn alles gefunden ist, das Lesebudget erschöpft ist oder der System-Prompt ohne Projekt vorliegt.
        entry.Complete = (entry.ProjectId is not null && entry.ThreadId is not null && entry.ProjectName is not null)
            || budgetExhausted
            || (promptSnapshotSeen && entry.ProjectId is null);
    }

    private static string? DecodeJsonFragment(string fragment)
    {
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + fragment + "\"");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
