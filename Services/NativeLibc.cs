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

    [DllImport(Library, EntryPoint = "kill", SetLastError = true)]
    internal static extern int Kill(int pid, int signal);

    [DllImport(Library, EntryPoint = "setpriority", SetLastError = true)]
    internal static extern int SetPriority(int which, uint who, int prio);

    // realpath(path, NULL): returns a malloc'd canonical absolute path with every symlink,
    // "." and ".." resolved, or 0 on failure. The caller must free() the result.
    [DllImport(Library, EntryPoint = "realpath", SetLastError = true)]
    internal static extern nint RealPath(string path, nint resolvedPath);

    [DllImport(Library, EntryPoint = "free")]
    internal static extern void Free(nint pointer);

    // statx(dirfd, path, flags, mask, buffer): file metadata from the inode.
    // The buffer must hold a "struct statx" (256 bytes). See FileStatReader for the layout.
    [DllImport(Library, EntryPoint = "statx", SetLastError = true)]
    internal static extern int Statx(int dirFd, string path, int flags, uint mask, byte[] buffer);

    // renameat2(..., RENAME_NOREPLACE): renames a directory entry, atomically failing with EEXIST
    // if the new name already exists. Acts on the entry itself, so symlinks are never followed.
    [DllImport(Library, EntryPoint = "renameat2", SetLastError = true)]
    internal static extern int RenameAt2(int oldDirFd, string oldPath, int newDirFd, string newPath, uint flags);

    [DllImport(Library, EntryPoint = "rename", SetLastError = true)]
    internal static extern int Rename(string oldPath, string newPath);

    // unlink(): removes one name (directory entry). For a symlink it removes the link, never the target.
    [DllImport(Library, EntryPoint = "unlink", SetLastError = true)]
    internal static extern int Unlink(string path);

    // rmdir(): removes a directory only if it is EMPTY (otherwise fails with ENOTEMPTY).
    [DllImport(Library, EntryPoint = "rmdir", SetLastError = true)]
    internal static extern int RemoveDirectory(string path);
}
