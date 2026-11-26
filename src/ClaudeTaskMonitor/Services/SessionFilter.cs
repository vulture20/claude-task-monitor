using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.Services;

/// <summary>Zeitfilter: Untergrenze der letzten Aktivität („diese Angabe oder neuer“), Tagesgrenze ist Mitternacht Ortszeit.</summary>
public enum TimeFilter
{
    All,
    Today,
    Yesterday,
    ThreeDays,
    Week,
}

/// <summary>Aktivitätsfilter für ganze Sitzungen bzw. Threads.</summary>
public enum ActivityFilter
{
    All,
    Active,
    Inactive,
}

/// <summary>Entscheidet, welche Sitzungen bei gewähltem Zeit- und Aktivitätsfilter angezeigt werden. Beide Filter wirken zusammen.</summary>
public static class SessionFilter
{
    /// <summary>
    /// Aktiv: Claude arbeitet an der Sitzung oder wartet auf eine Antwort (busy, waiting) bzw. der Status ist mangels
    /// Sitzungsdatei unbekannt. Inaktiv: bereit (idle) oder beendet (nur noch verwaiste Prozesse).
    /// Ein laufender Prozess allein unterscheidet nicht, denn jede angezeigte Sitzung hat mindestens einen.
    /// </summary>
    public static bool IsActive(SessionGroup group) => group.Status is not ("idle" or "ended");

    /// <summary>
    /// Zeitpunkt der letzten Aktivität: jetzt, solange die Sitzung aktiv ist, sonst lastActivityAt der Desktop-App,
    /// mindestens aber der Start des Wurzelprozesses.
    /// </summary>
    public static DateTimeOffset LastActivity(SessionGroup group, DateTimeOffset now)
    {
        if (IsActive(group))
            return now;
        DateTimeOffset? started = group.RootCreationTime > 0
            ? DateTimeOffset.FromFileTime(group.RootCreationTime)
            : null;
        var last = group.Metadata.LastActivityAt;
        if (last is { } l && started is { } s)
            return l > s ? l : s;
        return last ?? started ?? now;
    }

    /// <summary>Früheste noch angezeigte Aktivität (Mitternacht Ortszeit), null bei <see cref="TimeFilter.All"/>.</summary>
    public static DateTimeOffset? Cutoff(TimeFilter filter, DateTimeOffset now)
    {
        int? days = filter switch
        {
            TimeFilter.Today => 0,
            TimeFilter.Yesterday => 1,
            TimeFilter.ThreeDays => 3,
            TimeFilter.Week => 7,
            _ => null,
        };
        if (days is null)
            return null;
        var midnight = now.ToLocalTime().Date.AddDays(-days.Value);
        return new DateTimeOffset(midnight, TimeZoneInfo.Local.GetUtcOffset(midnight));
    }

    public static bool Matches(SessionGroup group, TimeFilter time, ActivityFilter activity, DateTimeOffset now)
    {
        if (activity == ActivityFilter.Active && !IsActive(group))
            return false;
        if (activity == ActivityFilter.Inactive && IsActive(group))
            return false;
        return Cutoff(time, now) is not { } cutoff || LastActivity(group, now) >= cutoff;
    }

    public static string ToSettingValue(TimeFilter filter) => filter switch
    {
        TimeFilter.Today => "today",
        TimeFilter.Yesterday => "yesterday",
        TimeFilter.ThreeDays => "3days",
        TimeFilter.Week => "week",
        _ => "all",
    };

    public static TimeFilter ParseTime(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "today" => TimeFilter.Today,
        "yesterday" => TimeFilter.Yesterday,
        "3days" => TimeFilter.ThreeDays,
        "week" => TimeFilter.Week,
        _ => TimeFilter.All,
    };

    public static string ToSettingValue(ActivityFilter filter) => filter switch
    {
        ActivityFilter.Active => "active",
        ActivityFilter.Inactive => "inactive",
        _ => "all",
    };

    public static ActivityFilter ParseActivity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "active" => ActivityFilter.Active,
        "inactive" => ActivityFilter.Inactive,
        _ => ActivityFilter.All,
    };
}
