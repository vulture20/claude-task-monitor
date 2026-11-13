using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeTaskMonitor.Services;

/// <summary>Benutzereinstellungen in %APPDATA%\ClaudeTaskMonitor\settings.json.</summary>
public sealed class AppSettings
{
    public static readonly int[] AllowedIntervals = { 1, 2, 5 };
    public const int DefaultInterval = 2;

    /// <summary>Deckkraft des Fensters in Prozent: 100 (Standard, undurchsichtig) bis 30 in 10er-Schritten.</summary>
    public const int MinOpacity = 30;
    public const int MaxOpacity = 100;
    public const int OpacityStep = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public double? Left { get; set; }
    public double? Top { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Topmost { get; set; } = true;
    public int IntervalSeconds { get; set; } = DefaultInterval;

    /// <summary>Oberflächensprache: "auto" (Windows-Anzeigesprache), "de" oder "en".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Deckkraft des ganzen Fensters in Prozent (30–100).</summary>
    public int Opacity { get; set; } = MaxOpacity;

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
                    settings.Opacity = NormalizeOpacity(settings.Opacity);
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return new AppSettings();
    }

    /// <summary>Begrenzt auf 30–100 % und rundet auf 10er-Schritte.</summary>
    public static int NormalizeOpacity(int percent)
    {
        var rounded = (int)Math.Round(percent / (double)OpacityStep, MidpointRounding.AwayFromZero) * OpacityStep;
        return Math.Clamp(rounded, MinOpacity, MaxOpacity);
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
