using System.Runtime.InteropServices;
using System.Text;
using ClaudeTaskMonitor.Models;
using ClaudeTaskMonitor.Native;

namespace ClaudeTaskMonitor.Services;

/// <summary>
/// Liest alle Prozesse des Systems in einem Aufruf (NtQuerySystemInformation) samt Startzeit,
/// CPU-Zeiten und Arbeitsspeicher. Pfad und Kommandozeile gibt es nur einzeln per OpenProcess.
/// Eine Instanz hält ihren Puffer über mehrere Aufrufe; nicht threadsicher.
/// </summary>
internal sealed class ProcessSnapshot : IDisposable
{
    private const int InitialBufferSize = 1 << 20;
    private const int MaxBufferSize = 64 << 20;

    private IntPtr _buffer;
    private int _bufferSize;

    /// <summary>Prozessnamen des letzten Durchlaufs, damit nicht jede Sekunde neue Strings entstehen.</summary>
    private Dictionary<(int Pid, long Created), string> _names = new();

    public ProcessSnapshot()
    {
        if (IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Nur 64-Bit-Prozesse werden unterstützt.");
        _bufferSize = InitialBufferSize;
        _buffer = Marshal.AllocHGlobal(_bufferSize);
    }

    /// <summary>Alle Prozesse (ohne den Leerlaufprozess). Leer, wenn die Abfrage fehlschlägt.</summary>
    public Dictionary<int, RawProcess> Take()
    {
        var result = new Dictionary<int, RawProcess>(Math.Max(256, _names.Count + 32));
        if (!Query())
            return result;

        var names = new Dictionary<(int, long), string>(_names.Count + 32);
        var offset = 0L;
        while (true)
        {
            var entry = _buffer + (nint)offset;
            var pid = (int)Marshal.ReadInt64(entry, NativeMethods.SpiOffsets.UniqueProcessId);
            if (pid != 0)
            {
                var parent = (int)Marshal.ReadInt64(entry, NativeMethods.SpiOffsets.InheritedFromUniqueProcessId);
                var created = Marshal.ReadInt64(entry, NativeMethods.SpiOffsets.CreateTime);
                var cpu = Marshal.ReadInt64(entry, NativeMethods.SpiOffsets.UserTime) + Marshal.ReadInt64(entry, NativeMethods.SpiOffsets.KernelTime);
                var workingSet = Marshal.ReadInt64(entry, NativeMethods.SpiOffsets.WorkingSetSize);

                var key = (pid, created);
                if (!_names.TryGetValue(key, out var name))
                {
                    var length = (ushort)Marshal.ReadInt16(entry, NativeMethods.SpiOffsets.ImageNameLength);
                    var text = Marshal.ReadIntPtr(entry, NativeMethods.SpiOffsets.ImageNameBuffer);
                    name = text == IntPtr.Zero || length == 0 ? (pid == 4 ? "System" : string.Empty) : Marshal.PtrToStringUni(text, length / 2);
                }

                names[key] = name;
                result[pid] = new RawProcess(pid, parent, name, created, cpu, workingSet);
            }

            var next = (uint)Marshal.ReadInt32(entry, NativeMethods.SpiOffsets.NextEntryOffset);
            if (next == 0)
                break;
            offset += next;
        }

        _names = names;
        return result;
    }

    private bool Query()
    {
        while (true)
        {
            var status = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessInformation, _buffer, _bufferSize, out var needed);
            if (status == 0)
                return true;
            if (status != NativeMethods.StatusInfoLengthMismatch || _bufferSize >= MaxBufferSize)
                return false;

            // Puffer vergrößern; etwas Reserve, weil zwischen den Aufrufen Prozesse und Threads entstehen.
            var size = Math.Min(MaxBufferSize, Math.Max(_bufferSize * 2, needed + (needed / 4)));
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            _buffer = Marshal.AllocHGlobal(size);
            _bufferSize = size;
        }
    }

    public void Dispose()
    {
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    /// <summary>Vollständiger Programmpfad oder null (Zugriff verweigert, beendet).</summary>
    public static string? QueryImagePath(int pid)
    {
        if (pid <= 4)
            return null;

        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString() : null;
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
