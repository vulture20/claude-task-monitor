using System.Diagnostics;
using System.Text.RegularExpressions;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.Services;

/// <summary>
/// Findet die von Claude gestarteten Prozessbäume und berechnet deren CPU-Last.
/// Jeder Aufruf von <see cref="Scan"/> ist eine Abtastung; die CPU-Last ergibt sich aus der
/// Differenz zur vorherigen Abtastung. Nicht threadsicher: nur von einem Thread nacheinander aufrufen.
/// </summary>
public sealed partial class ProcessScanner : IDisposable
{
    private const string ClaudeExe = "claude.exe";
    private const string DesktopPathMarker = @"\WindowsApps\Claude_";
    private const string CliPathMarker = @"\claude-code\";

    /// <summary>So lange bleibt eine PID nach ihrem letzten Auftreten als möglicher Sponsor bekannt.</summary>
    private static readonly TimeSpan SponsorMemory = TimeSpan.FromMinutes(10);

    [GeneratedRegex(@"--sponsor[\s=]+(\d+)")]
    private static partial Regex SponsorRegex();

    private readonly SessionRegistry _sessions;
    private readonly ProcessSnapshot _snapshot = new();
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);

    private Dictionary<string, long> _previousCpu = new();
    private long _previousTimestamp;

    /// <summary>Merkliste: Prozess (PID:Startzeit) → Sitzung, für alle Prozesse, die zuletzt einer Sitzung zugeordnet waren.</summary>
    private Dictionary<string, string> _adoptions = new();

    /// <summary>PID → (Sitzung, zuletzt gesehen), für die Auflösung von --sponsor auch nach Ende des Sponsors.</summary>
    private readonly Dictionary<int, (string GroupKey, DateTime LastSeen)> _pidHistory = new();

    /// <summary>Letzter bekannter Stand jeder Sitzung, um verwaiste Prozesse beendeter Sitzungen anzuzeigen.</summary>
    private readonly Dictionary<string, SessionGroup> _groupMemory = new();

    /// <summary>Gelesene --sponsor-PIDs (null = kein Sponsor), je Prozess nur einmal ermittelt.</summary>
    private Dictionary<string, int?> _sponsorCache = new();

    /// <summary>Programmpfade von claude.exe-Prozessen, je Prozess nur einmal ermittelt.</summary>
    private Dictionary<string, string?> _imagePathCache = new();

    public ProcessScanner(SessionRegistry sessions) => _sessions = sessions;

    /// <summary>Nächste Abtastung ohne Vergleichswert (z. B. nach einer Pause): CPU-Last startet dann bei 0.</summary>
    public void ResetBaseline() => _previousTimestamp = 0;

    public void Dispose() => _snapshot.Dispose();

    public IReadOnlyList<SessionGroup> Scan()
    {
        var timestamp = Stopwatch.GetTimestamp();
        var now = DateTime.UtcNow;
        // Abgelaufene Zeit in 100-ns-Einheiten (gleiche Einheit wie die Prozesszeiten).
        double elapsed100Ns = _previousTimestamp == 0
            ? 0
            : (timestamp - _previousTimestamp) * 10_000_000.0 / Stopwatch.Frequency;

        var ctx = new ScanContext(_snapshot.Take(), elapsed100Ns, _previousCpu, _imagePathCache);

        // 1. Sitzungswurzeln und ihre Prozessbäume.
        var builders = new Dictionary<string, GroupBuilder>();
        var roots = FindRoots(ctx);
        ctx.RootPids.UnionWith(roots.Select(r => r.Process.Pid));
        foreach (var root in roots)
        {
            var builder = new GroupBuilder(CreateTemplate(root));
            builders[builder.Template.Key] = builder;
            CollectTree(ctx, builder, root.Process, depth: 0, isOrphan: false);
        }

        // 2. Verwaiste Prozesse: zuerst über die Merkliste, dann über --sponsor.
        foreach (var (process, groupKey) in FindOrphans(ctx))
        {
            if (ctx.Reached.ContainsKey(process.Pid))
                continue; // bereits als Nachfahre eines früheren Waisen erfasst
            if (!builders.TryGetValue(groupKey, out var builder))
            {
                if (!_groupMemory.TryGetValue(groupKey, out var remembered))
                    continue;
                builder = new GroupBuilder(remembered with { Status = "ended", WaitingFor = null, Processes = Array.Empty<ProcessSample>() });
                builders[groupKey] = builder;
            }

            var depth = builder.Template.Status == "ended" ? 0 : 1;
            CollectTree(ctx, builder, process, depth, isOrphan: true);
        }

        var groups = builders.Values.Select(b => b.Build()).OrderBy(g => g.RootCreationTime).ToList();
        Remember(groups, now);

        _previousCpu = ctx.CurrentCpu;
        _imagePathCache = ctx.UsedImagePaths;
        _previousTimestamp = timestamp;
        return groups;
    }

    // ---------- Wurzeln ----------

    private sealed record Root(RawProcess Process, SessionFile? Session);

    private List<Root> FindRoots(ScanContext ctx)
    {
        var roots = new Dictionary<int, Root>();

        // 1. Sitzungsdateien von Claude Code: nur gültig, wenn der Prozess lebt und die Startzeit übereinstimmt
        //    (schützt vor liegengebliebenen Dateien und wiederverwendeten PIDs).
        foreach (var session in _sessions.Load())
        {
            if (!ctx.Processes.TryGetValue(session.Pid, out var process))
                continue;
            if (session.ProcStart != 0 && session.ProcStart != process.CreationTime)
                continue;
            roots[session.Pid] = new Root(process, session);
        }

        // 2. Rückfall: CLI-Kindprozesse der Claude-Desktop-App ohne (gültige) Sitzungsdatei.
        foreach (var p in ctx.Processes.Values)
        {
            if (!IsClaudeExe(p) || !IsDesktopMain(ctx, p))
                continue;
            if (!ctx.Children.TryGetValue(p.Pid, out var kids))
                continue;

            foreach (var kid in kids)
            {
                var child = ctx.Processes[kid];
                if (roots.ContainsKey(kid) || !IsClaudeExe(child) || child.CreationTime < p.CreationTime)
                    continue;
                var path = ctx.ImagePath(child);
                if (path is null || path.IndexOf(CliPathMarker, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                roots[kid] = new Root(child, null);
            }
        }

        return roots.Values.ToList();
    }

    private static bool IsClaudeExe(RawProcess p) => p.Name.Equals(ClaudeExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>Hauptprozess der Desktop-App: claude.exe aus WindowsApps\Claude_*, dessen Elternprozess nicht selbst die Desktop-App ist.</summary>
    private static bool IsDesktopMain(ScanContext ctx, RawProcess p)
    {
        var path = ctx.ImagePath(p);
        if (path is null || path.IndexOf(DesktopPathMarker, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (ctx.Processes.TryGetValue(p.ParentPid, out var parent) && IsClaudeExe(parent))
        {
            var parentPath = ctx.ImagePath(parent);
            if (parentPath is not null && parentPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                return false; // Electron-Hilfsprozess
        }

        return true;
    }

    private static SessionGroup CreateTemplate(Root root)
    {
        var s = root.Session;
        var p = root.Process;
        return new SessionGroup(
            Key: Key(p),
            RootPid: p.Pid,
            Title: string.IsNullOrWhiteSpace(s?.Name) ? $"Claude Code (PID {p.Pid})" : s!.Name!,
            Status: s?.Status ?? "unknown",
            WaitingFor: s?.WaitingFor,
            Cwd: s?.Cwd,
            SessionId: s?.SessionId,
            HostSessionId: s?.HostSessionId,
            Entrypoint: s?.Entrypoint,
            HasSessionFile: s is not null,
            RootCreationTime: p.CreationTime,
            TotalCpuPercent: 0,
            TotalWorkingSet: 0,
            Processes: Array.Empty<ProcessSample>());
    }

    // ---------- Bäume ----------

    private void CollectTree(ScanContext ctx, GroupBuilder builder, RawProcess self, int depth, bool isOrphan)
    {
        var key = Key(self);
        ctx.CurrentCpu[key] = self.CpuTime;
        ctx.Reached[self.Pid] = builder.Template.Key;

        double cpu = 0;
        if (ctx.Elapsed100Ns > 0 && ctx.PreviousCpu.TryGetValue(key, out var previous))
            cpu = Math.Clamp((self.CpuTime - previous) / (ctx.Elapsed100Ns * _processorCount) * 100.0, 0, 100);

        builder.Samples.Add(new ProcessSample(key, self.Pid, self.Name, depth, cpu, self.WorkingSet, isOrphan));

        if (depth > 64 || !ctx.Children.TryGetValue(self.Pid, out var kids))
            return;

        foreach (var kid in kids)
        {
            // Eine eigene Sitzung (z. B. verschachteltes Claude Code) bekommt eine eigene Gruppe.
            if (ctx.RootPids.Contains(kid) || ctx.Reached.ContainsKey(kid))
                continue;
            var child = ctx.Processes[kid];
            // Ein Kind kann nicht vor seinem Elternprozess gestartet sein: sonst wurde die Eltern-PID wiederverwendet.
            if (child.CreationTime < self.CreationTime)
                continue;
            CollectTree(ctx, builder, child, depth + 1, isOrphan: false);
        }
    }

    // ---------- Waisen ----------

    /// <summary>
    /// Prozesse, die nicht mehr über ihren Elternprozess erreichbar sind, aber zu einer Sitzung gehören:
    /// a) laut Merkliste im letzten Durchlauf einer Sitzung zugeordnet (gleiche PID und Startzeit),
    /// b) mit --sponsor &lt;pid&gt; gestartet, wobei der Sponsor zu einer Sitzung gehört oder gehörte.
    /// Sortiert nach Startzeit, damit Eltern vor ihren Kindern übernommen werden.
    /// </summary>
    private List<(RawProcess Process, string GroupKey)> FindOrphans(ScanContext ctx)
    {
        var result = new Dictionary<int, (RawProcess Process, string GroupKey)>();

        foreach (var (key, groupKey) in _adoptions)
        {
            var pid = PidOf(key);
            if (ctx.Reached.ContainsKey(pid) || !ctx.Processes.TryGetValue(pid, out var process) || Key(process) != key)
                continue;
            result[pid] = (process, groupKey);
        }

        var sponsorCache = new Dictionary<string, int?>();
        foreach (var p in ctx.Processes.Values)
        {
            // Kandidaten: Elternprozess beendet (oder PID inzwischen neu vergeben), noch keiner Sitzung zugeordnet.
            if (p.Pid <= 4 || ctx.Reached.ContainsKey(p.Pid) || result.ContainsKey(p.Pid))
                continue;
            if (ctx.Processes.TryGetValue(p.ParentPid, out var parent) && parent.CreationTime <= p.CreationTime)
                continue;

            var key = Key(p);
            if (!_sponsorCache.TryGetValue(key, out var sponsor))
                sponsor = ReadSponsor(p.Pid);
            sponsorCache[key] = sponsor;

            if (sponsor is not { } sponsorPid)
                continue;
            if (ctx.Reached.TryGetValue(sponsorPid, out var groupKey) || TryGetHistory(sponsorPid, out groupKey))
                result[p.Pid] = (p, groupKey);
        }

        _sponsorCache = sponsorCache;
        return result.Values.OrderBy(o => o.Process.CreationTime).ToList();
    }

    private bool TryGetHistory(int pid, out string groupKey)
    {
        if (_pidHistory.TryGetValue(pid, out var entry))
        {
            groupKey = entry.GroupKey;
            return true;
        }

        groupKey = string.Empty;
        return false;
    }

    private static int? ReadSponsor(int pid)
    {
        var commandLine = ProcessSnapshot.QueryCommandLine(pid);
        if (commandLine is null)
            return null;
        var match = SponsorRegex().Match(commandLine);
        return match.Success && int.TryParse(match.Groups[1].Value, out var sponsor) ? sponsor : null;
    }

    private void Remember(List<SessionGroup> groups, DateTime now)
    {
        var adoptions = new Dictionary<string, string>();
        foreach (var group in groups)
        {
            foreach (var sample in group.Processes)
            {
                adoptions[sample.Key] = group.Key;
                _pidHistory[sample.Pid] = (group.Key, now);
            }

            if (group.Status != "ended")
                _groupMemory[group.Key] = group with { Processes = Array.Empty<ProcessSample>() };
        }

        _adoptions = adoptions;

        foreach (var pid in _pidHistory.Where(e => now - e.Value.LastSeen > SponsorMemory).Select(e => e.Key).ToList())
            _pidHistory.Remove(pid);

        // Sitzungsstand nur behalten, solange noch Prozesse oder mögliche Sponsoren darauf verweisen.
        var referenced = adoptions.Values.Concat(_pidHistory.Values.Select(v => v.GroupKey)).ToHashSet();
        foreach (var key in _groupMemory.Keys.Where(k => !referenced.Contains(k)).ToList())
            _groupMemory.Remove(key);
    }

    private static string Key(RawProcess p) => $"{p.Pid}:{p.CreationTime}";

    private static int PidOf(string key) => int.Parse(key.AsSpan(0, key.IndexOf(':')));

    // ---------- Hilfstypen ----------

    private sealed class GroupBuilder(SessionGroup template)
    {
        public SessionGroup Template { get; } = template;
        public List<ProcessSample> Samples { get; } = new();

        public SessionGroup Build() => Template with
        {
            TotalCpuPercent = Math.Min(100, Samples.Sum(s => s.CpuPercent)),
            TotalWorkingSet = Samples.Sum(s => s.WorkingSet),
            Processes = Samples,
        };
    }

    /// <summary>Zustand eines Durchlaufs: Prozessliste, Kindliste und Pfad-Cache.</summary>
    private sealed class ScanContext
    {
        private readonly Dictionary<string, string?> _previousImagePaths;

        public ScanContext(
            Dictionary<int, RawProcess> processes,
            double elapsed100Ns,
            Dictionary<string, long> previousCpu,
            Dictionary<string, string?> previousImagePaths)
        {
            Processes = processes;
            Elapsed100Ns = elapsed100Ns;
            PreviousCpu = previousCpu;
            _previousImagePaths = previousImagePaths;
            Children = new Dictionary<int, List<int>>();
            foreach (var p in processes.Values)
            {
                if (p.ParentPid == p.Pid)
                    continue;
                if (!Children.TryGetValue(p.ParentPid, out var list))
                    Children[p.ParentPid] = list = new List<int>();
                list.Add(p.Pid);
            }

            foreach (var list in Children.Values)
                list.Sort();
        }

        public Dictionary<int, RawProcess> Processes { get; }
        public Dictionary<int, List<int>> Children { get; }
        public double Elapsed100Ns { get; }
        public Dictionary<string, long> PreviousCpu { get; }
        public Dictionary<string, long> CurrentCpu { get; } = new();
        public HashSet<int> RootPids { get; } = new();

        /// <summary>In diesem Durchlauf einer Sitzung zugeordnete PIDs → Sitzungsschlüssel.</summary>
        public Dictionary<int, string> Reached { get; } = new();

        /// <summary>Pfade, die in diesem Durchlauf gebraucht wurden (werden für den nächsten übernommen).</summary>
        public Dictionary<string, string?> UsedImagePaths { get; } = new();

        public string? ImagePath(RawProcess p)
        {
            var key = Key(p);
            if (UsedImagePaths.TryGetValue(key, out var path))
                return path;
            if (!_previousImagePaths.TryGetValue(key, out path))
                path = ProcessSnapshot.QueryImagePath(p.Pid);
            UsedImagePaths[key] = path;
            return path;
        }
    }
}
