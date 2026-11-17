using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>Raw inode metadata for one path, as returned by the Linux statx() system call.</summary>
public sealed class FileStat
{
    public uint Mode { get; init; }          // file type bits + permission bits
    public uint UserId { get; init; }
    public uint GroupId { get; init; }
    public uint HardLinkCount { get; init; }
    public ulong Inode { get; init; }
    public ulong SizeBytes { get; init; }
    public ulong BlocksAllocated { get; init; } // in 512-byte units, always
    public uint PreferredBlockSize { get; init; }
    public DateTime Accessed { get; init; }
    public DateTime Modified { get; init; }
    public DateTime StatusChanged { get; init; }
    public DateTime? Created { get; init; }  // null when the filesystem/kernel does not record it
    public ulong DeviceId { get; init; }     // which filesystem (device) holds the inode: (major << 32) | minor
}

/// <summary>
/// Reads file metadata straight from the kernel with statx(). Unlike .NET's FileInfo this gives
/// the inode number, hard-link count, owner ids, blocks on disk and a reliable answer to
/// "is the creation (birth) time actually known?". Read-only: statx never changes anything.
/// </summary>
public static class FileStatReader
{
    private const int AtFdCwd = -100;            // relative paths are resolved from the current dir
    private const int AtSymlinkNoFollow = 0x100; // describe the link itself, not its target
    private const uint StatxBasicStats = 0x7FF;
    private const uint StatxBirthTime = 0x800;

    // File type values in the top bits of st_mode (S_IFMT mask = 0xF000).
    public const uint TypeMask = 0xF000;
    public const uint TypeSocket = 0xC000;
    public const uint TypeSymlink = 0xA000;
    public const uint TypeRegular = 0x8000;
    public const uint TypeBlockDevice = 0x6000;
    public const uint TypeDirectory = 0x4000;
    public const uint TypeCharDevice = 0x2000;
    public const uint TypeFifo = 0x1000;

    /// <summary>Returns null with an errno-based message when the path cannot be inspected.</summary>
    public static FileStat? TryStat(string path, out string error)
    {
        error = string.Empty;
        var buffer = new byte[256];

        int result;
        try
        {
            result = NativeLibc.Statx(AtFdCwd, path, AtSymlinkNoFollow, StatxBasicStats | StatxBirthTime, buffer);
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            error = "statx() is not available on this system.";
            return null;
        }

        if (result != 0)
        {
            int errno = Marshal.GetLastWin32Error();
            error = errno switch
            {
                2 => "The item no longer exists.",
                13 => "Permission denied while reading its metadata.",
                _ => $"Could not read metadata (errno {errno})."
            };
            return null;
        }

        // struct statx layout (linux/stat.h), little-endian on x86-64 / arm64.
        ReadOnlySpan<byte> b = buffer;
        uint mask = BinaryPrimitives.ReadUInt32LittleEndian(b[0..]);

        return new FileStat
        {
            PreferredBlockSize = BinaryPrimitives.ReadUInt32LittleEndian(b[4..]),
            HardLinkCount = BinaryPrimitives.ReadUInt32LittleEndian(b[16..]),
            UserId = BinaryPrimitives.ReadUInt32LittleEndian(b[20..]),
            GroupId = BinaryPrimitives.ReadUInt32LittleEndian(b[24..]),
            Mode = BinaryPrimitives.ReadUInt16LittleEndian(b[28..]),
            Inode = BinaryPrimitives.ReadUInt64LittleEndian(b[32..]),
            SizeBytes = BinaryPrimitives.ReadUInt64LittleEndian(b[40..]),
            BlocksAllocated = BinaryPrimitives.ReadUInt64LittleEndian(b[48..]),
            Accessed = ReadTimestamp(b[64..]),
            Created = (mask & StatxBirthTime) != 0 ? ReadTimestamp(b[80..]) : null,
            StatusChanged = ReadTimestamp(b[96..]),
            Modified = ReadTimestamp(b[112..]),
            DeviceId = ((ulong)BinaryPrimitives.ReadUInt32LittleEndian(b[136..]) << 32)
                       | BinaryPrimitives.ReadUInt32LittleEndian(b[140..]),
        };
    }

    /// <summary>True if anything (file, folder, or even a broken symlink) exists at this exact path.</summary>
    public static bool Exists(string path) => TryStat(path, out _) is not null;

    // struct statx_timestamp { s64 tv_sec; u32 tv_nsec; s32 reserved; }
    private static DateTime ReadTimestamp(ReadOnlySpan<byte> b)
    {
        long seconds = BinaryPrimitives.ReadInt64LittleEndian(b);
        uint nanoseconds = BinaryPrimitives.ReadUInt32LittleEndian(b[8..]);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds / 100).LocalDateTime;
    }
}
