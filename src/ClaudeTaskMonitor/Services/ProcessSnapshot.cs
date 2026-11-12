using System.Runtime.InteropServices;
using System.Text;
using ClaudeTaskMonitor.Models;
using ClaudeTaskMonitor.Native;

namespace ClaudeTaskMonitor.Services;

/// <summary>Liest alle Prozesse des Systems in einem Durchgang und einzelne Prozessdetails.</summary>
internal static class ProcessSnapshot
{
    /// <summary>Snapshot aller Prozesse (PID, Eltern-PID, Name) per CreateToolhelp32Snapshot.</summary>
    public static Dictionary<int, RawProcess> Take()
    {
        var result = new Dictionary<int, RawProcess>(1024);
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot == NativeMethods.InvalidHandleValue || snapshot == IntPtr.Zero)
            return result;

        try
        {
            var entry = new NativeMethods.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32W>() };
            if (!NativeMethods.Process32FirstW(snapshot, ref entry))
                return result;

            do
            {
                var pid = (int)entry.th32ProcessID;
                result[pid] = new RawProcess(pid, (int)entry.th32ParentProcessID, entry.szExeFile ?? string.Empty);
            }
            while (NativeMethods.Process32NextW(snapshot, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }

        return result;
    }

    /// <summary>Startzeit, CPU-Zeit, Arbeitsspeicher und optional Programmpfad. Null bei Zugriffsfehler oder beendetem Prozess.</summary>
    public static ProcessDetails? Query(int pid, bool withImagePath)
    {
        if (pid <= 4)
            return null;

        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            if (!NativeMethods.GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
                return null;

            long workingSet = 0;
            var counters = new NativeMethods.PROCESS_MEMORY_COUNTERS { cb = (uint)Marshal.SizeOf<NativeMethods.PROCESS_MEMORY_COUNTERS>() };
            if (NativeMethods.GetProcessMemoryInfo(handle, out counters, counters.cb))
                workingSet = (long)counters.WorkingSetSize.ToUInt64();

            string? path = null;
            if (withImagePath)
            {
                var buffer = new StringBuilder(1024);
                var size = (uint)buffer.Capacity;
                if (NativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                    path = buffer.ToString();
            }

            return new ProcessDetails(creation, kernel + user, workingSet, path);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>Kommandozeile eines Prozesses oder null (Zugriff verweigert, beendet).</summary>
    public static string? QueryCommandLine(int pid)
    {
        if (pid <= 4)
            return null;

        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero)
            return null;

        var buffer = IntPtr.Zero;
        try
        {
            NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, IntPtr.Zero, 0, out var needed);
            if (needed <= 0 || needed > 1 << 20)
                return null;

            buffer = Marshal.AllocHGlobal(needed);
            if (NativeMethods.NtQueryInformationProcess(handle, NativeMethods.ProcessCommandLineInformation, buffer, needed, out _) != 0)
                return null;

            var text = Marshal.PtrToStructure<NativeMethods.UNICODE_STRING>(buffer);
            return text.Buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
            NativeMethods.CloseHandle(handle);
        }
    }
}
