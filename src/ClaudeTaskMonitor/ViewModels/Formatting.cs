using System.Globalization;

namespace ClaudeTaskMonitor.ViewModels;

internal static class Formatting
{
    private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;

    public static string Cpu(double percent) => percent.ToString("0.0", Culture) + " %";

    public static string Memory(long bytes)
    {
        var mb = bytes / (1024.0 * 1024.0);
        return mb >= 1024
            ? (mb / 1024.0).ToString("0.0", Culture) + " GB"
            : mb.ToString("0", Culture) + " MB";
    }

    /// <summary>Statustext aus sessions\&lt;pid&gt;.json für die Anzeige.</summary>
    public static string Status(string status, string? waitingFor) => status switch
    {
        "busy" => "arbeitet",
        "idle" => "bereit",
        "waiting" => string.IsNullOrWhiteSpace(waitingFor) ? "wartet" : $"wartet: {waitingFor}",
        "unknown" => "ohne Sitzungsdatei",
        _ => status,
    };
}
