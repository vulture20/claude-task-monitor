using System.IO;

namespace ClaudeTaskMonitor.Services;

/// <summary>Ablageorte von Claude Code und der Claude-Desktop-App.</summary>
public static class ClaudePaths
{
    /// <summary>Konfigurationsordner von Claude Code (%USERPROFILE%\.claude oder CLAUDE_CONFIG_DIR).</summary>
    public static string ConfigDirectory
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return !string.IsNullOrWhiteSpace(custom)
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        }
    }

    public static string SessionsDirectory => Path.Combine(ConfigDirectory, "sessions");

    public static string ProjectsDirectory => Path.Combine(ConfigDirectory, "projects");

    /// <summary>
    /// Ordner mit den Sitzungsdaten der Desktop-App (claude-code-sessions). Die Store-Version legt sie unter
    /// %LOCALAPPDATA%\Claude-Data\claude_&lt;Paket&gt;\ ab, die klassische Installation unter %APPDATA%\Claude\.
    /// </summary>
    public static IReadOnlyList<string> DesktopSessionDirectories()
    {
        var result = new List<string>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        try
        {
            var dataRoot = Path.Combine(local, "Claude-Data");
            if (Directory.Exists(dataRoot))
                result.AddRange(Directory.EnumerateDirectories(dataRoot, "claude_*").Select(d => Path.Combine(d, "claude-code-sessions")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        result.Add(Path.Combine(roaming, "Claude", "claude-code-sessions"));
        return result.Where(Directory.Exists).ToList();
    }
}
