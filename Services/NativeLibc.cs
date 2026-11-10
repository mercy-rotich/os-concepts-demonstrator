using System.Runtime.InteropServices;

namespace OS_Concepts_Demonstrator.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct TimeSpec
{
    public long Seconds;
    public long Nanoseconds;
}

/// <summary>
/// Direct calls into the GNU C library (glibc). This is the same layer .NET itself sits on:
/// these are plain, read-only "ask a question" functions, never anything that changes the system.
/// If the library cannot be loaded (a different C library or operating system), callers catch the
/// resulting exception and report the call as unavailable.
/// </summary>
internal static class NativeLibc
{
    private const string Library = "libc.so.6";

    [DllImport(Library, EntryPoint = "getpid")]
    internal static extern int GetPid();

    [DllImport(Library, EntryPoint = "getppid")]
    internal static extern int GetParentPid();

    [DllImport(Library, EntryPoint = "getuid")]
    internal static extern uint GetUserId();

    [DllImport(Library, EntryPoint = "getgid")]
    internal static extern uint GetGroupId();

    [DllImport(Library, EntryPoint = "gethostname", SetLastError = true)]
    internal static extern int GetHostName(byte[] name, nuint length);

    // The buffer must hold a "struct utsname": six fields of 65 bytes each on Linux.
    [DllImport(Library, EntryPoint = "uname", SetLastError = true)]
    internal static extern int UName(byte[] buffer);

    [DllImport(Library, EntryPoint = "sysconf")]
    internal static extern long SysConf(int name);

    [DllImport(Library, EntryPoint = "clock_gettime", SetLastError = true)]
    internal static extern int ClockGetTime(int clockId, out TimeSpec time);

    [DllImport(Library, EntryPoint = "gnu_get_libc_version")]
    internal static extern nint GetLibcVersion();
}
