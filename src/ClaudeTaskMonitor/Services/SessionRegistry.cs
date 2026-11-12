using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.Services;

/// <summary>
/// Liest die Sitzungsdateien von Claude Code (%USERPROFILE%\.claude\sessions\&lt;pid&gt;.json).
/// Dateien werden nur neu gelesen, wenn sich ihr Änderungszeitpunkt ändert.
/// </summary>
public sealed class SessionRegistry
{
    // procStart steht als String in der Datei ("134389703834318488"), andere Zahlen als Zahl.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly string _directory;
    private readonly Dictionary<string, (DateTime Stamp, SessionFile? Data)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SessionRegistry(string directory) => _directory = directory;

    public static string DefaultDirectory => ClaudePaths.SessionsDirectory;

    public IReadOnlyList<SessionFile> Load()
    {
        var result = new List<SessionFile>();
        IEnumerable<string> files;
        try
        {
            if (!Directory.Exists(_directory))
                return result;
            files = Directory.EnumerateFiles(_directory, "*.json").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            seen.Add(file);
            var data = Read(file);
            if (data is { Pid: > 0 })
                result.Add(data);
        }

        foreach (var stale in _cache.Keys.Where(k => !seen.Contains(k)).ToList())
            _cache.Remove(stale);

        return result;
    }

    private SessionFile? Read(string file)
    {
        DateTime stamp;
        try
        {
            stamp = File.GetLastWriteTimeUtc(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _cache.TryGetValue(file, out var old) ? old.Data : null;
        }

        if (_cache.TryGetValue(file, out var cached) && cached.Stamp == stamp)
            return cached.Data;

        try
        {
            // FileShare.ReadWrite: Claude Code schreibt die Datei laufend neu.
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var data = JsonSerializer.Deserialize<SessionFile>(stream, JsonOptions);
            _cache[file] = (stamp, data);
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Halb geschriebene Datei: alten Stand behalten, beim nächsten Durchlauf erneut versuchen.
            return cached.Data;
        }
    }
}
