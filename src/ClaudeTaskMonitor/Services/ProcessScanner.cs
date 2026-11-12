using System.Diagnostics;
using ClaudeTaskMonitor.Models;

namespace ClaudeTaskMonitor.Services;

/// <summary>
/// Findet die von Claude gestarteten Prozessbäume und berechnet deren CPU-Last.
/// Jeder Aufruf von <see cref="Scan"/> ist eine Abtastung; die CPU-Last ergibt sich aus der
/// Differenz zur vorherigen Abtastung. Nicht threadsicher: nur von einem Thread nacheinander aufrufen.
/// </summary>
public sealed class ProcessScanner
{
    private const string ClaudeExe = "claude.exe";
    private const string DesktopPathMarker = @"\WindowsApps\Claude_";
    private const string CliPathMarker = @"\claude-code\";

    private readonly SessionRegistry _sessions;
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);

    private Dictionary<string, long> _previousCpu = new();
    private long _previousTimestamp;

    public ProcessScanner(SessionRegistry sessions) => _sessions = sessions;

    public IReadOnlyList<SessionGroup> Scan()
    {
        var timestamp = Stopwatch.GetTimestamp();
        // Abgelaufene Zeit in 100-ns-Einheiten (gleiche Einheit wie die Prozesszeiten).
        double elapsed100Ns = _previousTimestamp == 0
            ? 0
            : (timestamp - _previousTimestamp) * 10_000_000.0 / Stopwatch.Frequency;

        var processes = ProcessSnapshot.Take();
        var children = BuildChildMap(processes);
        var details = new Dictionary<int, ProcessDetails?>();

        ProcessDetails? Detail(int pid)
        {
            if (details.TryGetValue(pid, out var cached))
                return cached;
            var withPath = processes.TryGetValue(pid, out var raw)
                && raw.Name.Equals(ClaudeExe, StringComparison.OrdinalIgnoreCase);
            var detail = ProcessSnapshot.Query(pid, withPath);
            details[pid] = detail;
            return detail;
        }

        var roots = FindRoots(processes, children, Detail);
        var rootPids = roots.Select(r => r.Pid).ToHashSet();

        var currentCpu = new Dictionary<string, long>();
        var groups = new List<SessionGroup>(roots.Count);
        foreach (var root in roots)
        {
            var samples = new List<ProcessSample>();
            CollectTree(root.Pid, root.Detail, depth: 0, processes, children, rootPids, Detail, elapsed100Ns, currentCpu, samples);
            groups.Add(CreateGroup(root, samples));
        }

        _previousCpu = currentCpu;
        _previousTimestamp = timestamp;

        return groups.OrderBy(g => g.RootCreationTime).ToList();
    }

    private sealed record Root(int Pid, ProcessDetails Detail, SessionFile? Session);

    private static Dictionary<int, List<int>> BuildChildMap(Dictionary<int, RawProcess> processes)
    {
        var map = new Dictionary<int, List<int>>();
        foreach (var p in processes.Values)
        {
            if (p.ParentPid == p.Pid)
                continue;
            if (!map.TryGetValue(p.ParentPid, out var list))
                map[p.ParentPid] = list = new List<int>();
            list.Add(p.Pid);
        }

        foreach (var list in map.Values)
            list.Sort();
        return map;
    }

    private List<Root> FindRoots(
        Dictionary<int, RawProcess> processes,
        Dictionary<int, List<int>> children,
        Func<int, ProcessDetails?> detail)
    {
        var roots = new Dictionary<int, Root>();

        // 1. Sitzungsdateien von Claude Code: nur gültig, wenn der Prozess lebt und die Startzeit übereinstimmt
        //    (schützt vor liegengebliebenen Dateien und wiederverwendeten PIDs).
        foreach (var session in _sessions.Load())
        {
            if (!processes.ContainsKey(session.Pid))
                continue;
            var d = detail(session.Pid);
            if (d is null)
                continue;
            if (session.ProcStart != 0 && session.ProcStart != d.CreationTime)
                continue;
            roots[session.Pid] = new Root(session.Pid, d, session);
        }

        // 2. Rückfall: CLI-Kindprozesse der Claude-Desktop-App ohne (gültige) Sitzungsdatei.
        foreach (var p in processes.Values)
        {
            if (!p.Name.Equals(ClaudeExe, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!IsDesktopMain(p, processes, detail))
                continue;
            if (!children.TryGetValue(p.Pid, out var kids))
                continue;

            var parentDetail = detail(p.Pid);
            foreach (var kid in kids)
            {
                if (roots.ContainsKey(kid))
                    continue;
                if (!processes[kid].Name.Equals(ClaudeExe, StringComparison.OrdinalIgnoreCase))
                    continue;
                var d = detail(kid);
                if (d?.ImagePath is null || d.ImagePath.IndexOf(CliPathMarker, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (parentDetail is not null && d.CreationTime < parentDetail.CreationTime)
                    continue;
                roots[kid] = new Root(kid, d, null);
            }
        }

        return roots.Values.ToList();
    }

    /// <summary>Hauptprozess der Desktop-App: claude.exe aus WindowsApps\Claude_*, dessen Elternprozess nicht selbst die Desktop-App ist.</summary>
    private static bool IsDesktopMain(RawProcess p, Dictionary<int, RawProcess> processes, Func<int, ProcessDetails?> detail)
    {
        var path = detail(p.Pid)?.ImagePath;
        if (path is null || path.IndexOf(DesktopPathMarker, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (processes.TryGetValue(p.ParentPid, out var parent)
            && parent.Name.Equals(ClaudeExe, StringComparison.OrdinalIgnoreCase))
        {
            var parentPath = detail(parent.Pid)?.ImagePath;
            if (parentPath is not null && parentPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                return false; // Electron-Hilfsprozess
        }

        return true;
    }

    private void CollectTree(
        int pid,
        ProcessDetails self,
        int depth,
        Dictionary<int, RawProcess> processes,
        Dictionary<int, List<int>> children,
        HashSet<int> rootPids,
        Func<int, ProcessDetails?> detail,
        double elapsed100Ns,
        Dictionary<string, long> currentCpu,
        List<ProcessSample> samples)
    {
        var key = $"{pid}:{self.CreationTime}";
        currentCpu[key] = self.CpuTime;

        double cpu = 0;
        if (elapsed100Ns > 0 && _previousCpu.TryGetValue(key, out var previous))
            cpu = Math.Clamp((self.CpuTime - previous) / (elapsed100Ns * _processorCount) * 100.0, 0, 100);

        samples.Add(new ProcessSample(key, pid, processes[pid].Name, depth, cpu, self.WorkingSet));

        if (depth > 64 || !children.TryGetValue(pid, out var kids))
            return;

        foreach (var kid in kids)
        {
            // Eine eigene Sitzung (z. B. verschachteltes Claude Code) bekommt eine eigene Gruppe.
            if (rootPids.Contains(kid))
                continue;
            var d = detail(kid);
            if (d is null)
                continue;
            // Ein Kind kann nicht vor seinem Elternprozess gestartet sein: sonst wurde die Eltern-PID wiederverwendet.
            if (d.CreationTime < self.CreationTime)
                continue;
            CollectTree(kid, d, depth + 1, processes, children, rootPids, detail, elapsed100Ns, currentCpu, samples);
        }
    }

    private static SessionGroup CreateGroup(Root root, List<ProcessSample> samples)
    {
        var s = root.Session;
        return new SessionGroup(
            Key: $"{root.Pid}:{root.Detail.CreationTime}",
            RootPid: root.Pid,
            Title: string.IsNullOrWhiteSpace(s?.Name) ? $"Claude Code (PID {root.Pid})" : s!.Name!,
            Status: s?.Status ?? "unknown",
            WaitingFor: s?.WaitingFor,
            Cwd: s?.Cwd,
            SessionId: s?.SessionId,
            Entrypoint: s?.Entrypoint,
            HasSessionFile: s is not null,
            RootCreationTime: root.Detail.CreationTime,
            TotalCpuPercent: Math.Min(100, samples.Sum(x => x.CpuPercent)),
            TotalWorkingSet: samples.Sum(x => x.WorkingSet),
            Processes: samples);
    }
}
