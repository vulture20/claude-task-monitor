using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeTaskMonitor.Native;

/// <summary>Win32-/NT-Aufrufe für Prozessliste, Pfad, Kommandozeile und Titelleiste.</summary>
internal static class NativeMethods
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ---------- Prozessliste ----------

    public const int SystemProcessInformation = 5;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength, out int returnLength);

    /// <summary>Feld-Offsets in SYSTEM_PROCESS_INFORMATION (64 Bit).</summary>
    public static class SpiOffsets
    {
        public const int NextEntryOffset = 0;
        public const int CreateTime = 32;
        public const int UserTime = 40;
        public const int KernelTime = 48;
        public const int ImageNameLength = 56;
        public const int ImageNameBuffer = 64;
        public const int UniqueProcessId = 80;
        public const int InheritedFromUniqueProcessId = 88;
        public const int WorkingSetSize = 144;
    }

    // ---------- Einzelner Prozess ----------

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    /// <summary>ProcessCommandLineInformation (ab Windows 8.1, reicht mit PROCESS_QUERY_LIMITED_INFORMATION).</summary>
    public const int ProcessCommandLineInformation = 60;

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, IntPtr processInformation, int processInformationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    // ---------- Fenster ----------

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
