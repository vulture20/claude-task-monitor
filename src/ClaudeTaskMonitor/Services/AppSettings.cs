using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTaskMonitor.Services;

/// <summary>Benutzereinstellungen in %APPDATA%\ClaudeTaskMonitor\settings.json.</summary>
public sealed class AppSettings
{
    public static readonly int[] AllowedIntervals = { 1, 2, 5 };
    public const int DefaultInterval = 2;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Topmost { get; set; } = true;
    public int IntervalSeconds { get; set; } = DefaultInterval;

    /// <summary>false im Demo-Modus: Änderungen bleiben im Speicher, die Datei wird nicht angefasst.</summary>
    [JsonIgnore]
    public bool Persist { get; set; } = true;

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeTaskMonitor", "settings.json");

    /// <summary>Lädt die Einstellungen; bei fehlender oder fehlerhafter Datei die Standardwerte.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (settings is not null)
                {
                    if (!AllowedIntervals.Contains(settings.IntervalSeconds))
                        settings.IntervalSeconds = DefaultInterval;
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new AppSettings();
    }

    /// <summary>Speichert atomar (erst Temp-Datei, dann ersetzen). Fehler werden still ignoriert.</summary>
    public void Save()
    {
        if (!Persist)
            return;
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
