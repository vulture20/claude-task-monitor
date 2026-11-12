using ClaudeTaskMonitor.Localization;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.Services;

/// <summary>
/// Erfundene Sitzungen für Screenshots und Vorführungen (Start mit <c>--demo</c>). Liest weder Prozesse noch
/// Dateien. Alle Namen, Pfade und IDs sind frei erfunden; die CPU-Last schwankt leicht, damit die Anzeige lebt.
/// </summary>
public sealed class DemoSessionSource : ISessionSource
{
    private const long Mb = 1024 * 1024;

    /// <summary>Diese Karte ist im Demo-Modus aufgeklappt, damit der Prozessbaum zu sehen ist.</summary>
    public const string ExpandedKey = "demo-1";

    private readonly DateTimeOffset _start = DateTimeOffset.Now;
    private int _tick;

    private sealed record DemoProcess(string Name, int Pid, int Depth, double Cpu, long WorkingSetMb, bool IsOrphan = false);

    private sealed record DemoSession(
        string Key,
        string Title,
        string Status,
        string? WaitingForKey,
        string SummaryKey,
        string Cwd,
        string? ProjectId,
        string? ProjectNameKey,
        string? ThreadId,
        int MinutesAgo,
        DemoProcess[] Processes);

    private static readonly DemoSession[] Sessions =
    {
        new("demo-1", "wetterstation-firmware", "busy", null,
            "Demo1Summary",
            @"C:\Projekte\wetterstation", "chan_demo0000000000wetter01", "DemoProjectWeather", "cmsg_demo00000000000000a4f2c1", 0,
            new DemoProcess[]
            {
                new("claude.exe", 11840, 0, 1.8, 312),
                new("bash.exe", 12204, 1, 0.1, 9),
                new("cmake.exe", 12588, 2, 0.4, 18),
                new("cc1plus.exe", 13016, 3, 11.6, 164),
                new("cc1plus.exe", 13052, 3, 10.9, 151),
                new("ninja.exe", 12940, 3, 0.2, 7),
            }),
        new("demo-2", "wetterstation-doku", "waiting", "Demo2WaitingFor",
            "Demo2Summary",
            @"C:\Projekte\wetterstation", "chan_demo0000000000wetter01", "DemoProjectWeather", "cmsg_demo00000000000000b81e07", 3,
            new DemoProcess[]
            {
                new("claude.exe", 9412, 0, 0.3, 268),
            }),
        new("demo-3", "fotoalbum-frontend", "busy", null,
            "Demo3Summary",
            @"D:\Code\fotoalbum-web", "chan_demo0000000000fotoalb2", "DemoProjectPhotos", "cmsg_demo00000000000000c35d90", 0,
            new DemoProcess[]
            {
                new("claude.exe", 15320, 0, 1.2, 297),
                new("node.exe", 15776, 1, 4.7, 214),
                new("esbuild.exe", 16104, 2, 2.3, 38),
                new("chrome-headless-shell.exe", 16388, 1, 3.1, 186),
            }),
        new("demo-4", "fotoalbum-api", "idle", null,
            "Demo4Summary",
            @"D:\Code\fotoalbum-api", "chan_demo0000000000fotoalb2", "DemoProjectPhotos", "cmsg_demo00000000000000d7a614", 12,
            new DemoProcess[]
            {
                new("claude.exe", 7736, 0, 0.0, 241),
            }),
        new("demo-5", "skripte", "ended", null,
            "Demo5Summary",
            @"C:\Tools\skripte", null, null, null, 25,
            new DemoProcess[]
            {
                new("python.exe", 20512, 0, 6.4, 122, IsOrphan: true),
            }),
    };

    public IReadOnlyList<SessionGroup> Scan()
    {
        var tick = _tick++;
        var groups = new List<SessionGroup>(Sessions.Length);
        foreach (var s in Sessions)
        {
            var processes = s.Processes
                .Select((p, i) => new ProcessSample(
                    $"{p.Pid}:{s.Key}",
                    p.Pid,
                    p.Name,
                    p.Depth,
                    Jitter(p.Cpu, tick, p.Pid + i),
                    p.WorkingSetMb * Mb,
                    p.IsOrphan))
                .ToList();

            var meta = new SessionMetadata(
                DesktopTitle: s.Title,
                TitleSource: null,
                Summary: Loc.T(s.SummaryKey),
                DesktopCwd: s.Cwd,
                LastActivityAt: _start.AddMinutes(-s.MinutesAgo),
                ProjectId: s.ProjectId,
                ProjectName: s.ProjectNameKey is null ? null : Loc.T(s.ProjectNameKey),
                ThreadId: s.ThreadId);

            groups.Add(new SessionGroup(
                Key: s.Key,
                RootPid: s.Processes[0].Pid,
                Title: s.Title,
                Status: s.Status,
                WaitingFor: s.WaitingForKey is null ? null : Loc.T(s.WaitingForKey),
                Cwd: s.Cwd,
                SessionId: $"00000000-0000-4000-8000-00000000000{groups.Count + 1}",
                HostSessionId: null,
                Entrypoint: s.Status == "ended" ? null : "desktop",
                HasSessionFile: true,
                RootCreationTime: _start.AddMinutes(-s.MinutesAgo - 5).UtcDateTime.ToFileTimeUtc(),
                TotalCpuPercent: processes.Sum(p => p.CpuPercent),
                TotalWorkingSet: processes.Sum(p => p.WorkingSet),
                Processes: processes)
            {
                Metadata = meta,
            });
        }

        return groups;
    }

    public void ResetBaseline()
    {
    }

    public void Dispose()
    {
    }

    /// <summary>Leichte, gleichmäßige Schwankung um den Grundwert (±15 %), ohne Zufall.</summary>
    private static double Jitter(double baseValue, int tick, int seed)
    {
        if (baseValue <= 0)
            return 0;
        var factor = 1 + 0.15 * Math.Sin(tick * 0.9 + seed * 0.37);
        return Math.Round(baseValue * factor, 1);
    }
}
