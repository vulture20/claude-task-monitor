using System.Globalization;
using ClaudeTaskMonitor.Localization;

namespace ClaudeTaskMonitor.ViewModels;

internal static class Formatting
{
    private static CultureInfo Culture => Loc.Instance.Culture;

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
        "busy" => Loc.T("StatusBusy"),
        "idle" => Loc.T("StatusIdle"),
        "waiting" => string.IsNullOrWhiteSpace(waitingFor) ? Loc.T("StatusWaiting") : Loc.F("StatusWaitingFor", waitingFor),
        "unknown" => Loc.T("StatusUnknown"),
        "ended" => Loc.T("StatusEnded"),
        _ => status,
    };
}
