using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Read-only access to mounted drives and directory contents.
/// Never throws for ordinary filesystem problems; errors are returned as messages.
/// </summary>
public class StorageService
{
    // Upper limit so a folder with a huge number of files cannot freeze the UI.
    private const int MaxEntries = 2000;

    private const string MountInfoPath = "/proc/self/mountinfo";

    // Filesystems that live on another computer.
    private static readonly HashSet<string> NetworkFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "nfs", "nfs4", "cifs", "smb3", "smbfs", "sshfs", "fuse.sshfs", "ceph", "glusterfs", "fuse.glusterfs"
    };

    // What common virtual filesystems are for. Anything not listed gets a generic description.
    private static readonly Dictionary<string, string> VirtualDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tmpfs"] = "Temporary files stored in RAM",
        ["ramfs"] = "Files stored in RAM",
        ["devtmpfs"] = "Device files managed by the kernel",
        ["proc"] = "Kernel interface: process and system information",
        ["sysfs"] = "Kernel interface: devices and drivers",
        ["cgroup"] = "Kernel interface: resource control groups",
        ["cgroup2"] = "Kernel interface: resource control groups",
        ["devpts"] = "Pseudo-terminals (terminal windows, SSH)",
        ["mqueue"] = "POSIX message queues (inter-process communication)",
        ["securityfs"] = "Kernel security module interface",
        ["debugfs"] = "Kernel debugging interface",
        ["tracefs"] = "Kernel tracing interface",
        ["pstore"] = "Crash information saved by the kernel",
        ["bpf"] = "Kernel BPF program interface",
        ["configfs"] = "Kernel object configuration interface",
        ["fusectl"] = "FUSE control interface",
        ["hugetlbfs"] = "Huge memory pages",
        ["autofs"] = "Automatic mounting trigger",
        ["binfmt_misc"] = "Executable format registration",
        ["efivarfs"] = "UEFI firmware variables",
        ["overlay"] = "Layered filesystem (containers)",
        ["squashfs"] = "Compressed read-only image"
    };

    // Conventional purposes of well-known Linux directories. This is a lookup of
    // *meaning*; which of these actually exist is decided by the running system.
    private static readonly Dictionary<string, string> WellKnownMountPoints = new(StringComparer.Ordinal)
    {
        ["/"] = "Root filesystem",
        ["/boot"] = "Boot files (kernel and boot loader)",
        ["/boot/efi"] = "EFI boot partition",
        ["/efi"] = "EFI boot partition",
        ["/home"] = "Home directory",
        ["/var"] = "Variable data (logs, caches, databases)",
        ["/tmp"] = "Temporary files",
        ["/usr"] = "Installed programs and libraries",
        ["/opt"] = "Optional add-on software",
        ["/srv"] = "Data served by this machine",
        ["/data"] = "Data storage"
    };

    /// <summary>
    /// Builds the full picture: physical disks, filesystems (grouped, so shared
    /// capacity is counted once) and virtual filesystems. Never throws.
    /// </summary>
    public StorageOverviewModel GetStorageOverview()
    {
        var overview = new StorageOverviewModel();

        if (!File.Exists(MountInfoPath))
        {
            overview.Error = "Mount information is unavailable on this system (/proc/self/mountinfo not found).";
            return overview;
        }

        List<MountRecord> mounts;

        try
        {
            mounts = MountInfoParser.Parse(MountInfoPath);
        }
        catch (Exception ex) when (IsExpectedFileSystemError(ex))
        {
            overview.Error = $"Could not read mount information: {ex.Message}";
            return overview;
        }

        Dictionary<string, DriveInfo> capacityByMountPoint = ReadCapacities();
        var resolver = new BlockDeviceResolver();

        // Key = the filesystem's device number + type: mounts with the same key are
        // the same filesystem (e.g. btrfs subvolumes, bind mounts).
        var groups = new Dictionary<string, FilesystemGroupModel>();

        foreach (MountRecord mount in mounts)
        {
            if (!IsStorageBacked(mount))
            {
                overview.VirtualFilesystems.Add(BuildVirtual(mount, capacityByMountPoint));
                continue;
            }

            string key = $"{mount.MajorMinor}|{mount.FileSystemType}";

            if (!groups.TryGetValue(key, out FilesystemGroupModel? group))
            {
                bool isNetwork = IsNetwork(mount);

                group = new FilesystemGroupModel
                {
                    Source = mount.Source,
                    FileSystemType = mount.FileSystemType,
                    IsNetwork = isNetwork,
                    Device = isNetwork ? null : resolver.Resolve(mount.Source)
                };

                groups[key] = group;
                overview.Filesystems.Add(group);
            }

            if (!group.HasCapacity &&
                capacityByMountPoint.TryGetValue(mount.MountPoint, out DriveInfo? drive))
            {
                TryFillCapacity(group, drive);
            }

            group.MountPoints.Add(BuildMountPoint(mount, group));
        }

        foreach (FilesystemGroupModel group in overview.Filesystems)
            group.MountPoints.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));

        overview.Filesystems.Sort((a, b) => string.Compare(
            a.MountPoints.FirstOrDefault()?.Path, b.MountPoints.FirstOrDefault()?.Path, StringComparison.Ordinal));

        overview.VirtualFilesystems.Sort((a, b) =>
            string.Compare(a.MountPoint, b.MountPoint, StringComparison.Ordinal));

        // Physical disks come from /sys (so unmounted partitions show too);
        // then we note which partitions are mounted where.
        overview.Disks.AddRange(resolver.GetPhysicalDisks());

        foreach (FilesystemGroupModel group in overview.Filesystems)
        {
            if (group.Device is null)
                continue;

            PartitionModel? partition = overview.Disks
                .SelectMany(d => d.Partitions)
                .FirstOrDefault(p => p.Name == group.Device.Name);

            partition?.MountedAt.AddRange(group.MountPoints.Select(m => m.Path));
        }

        return overview;
    }

    // statvfs numbers per mount point, via DriveInfo.
    private static Dictionary<string, DriveInfo> ReadCapacities()
    {
        var result = new Dictionary<string, DriveInfo>();

        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
                result.TryAdd(drive.Name, drive);
        }
        catch (Exception ex) when (IsExpectedFileSystemError(ex))
        {
            // Capacity will simply show as unavailable.
        }

        return result;
    }

    private static bool TryReadCapacity(DriveInfo drive, out long total, out long used, out long free)
    {
        total = used = free = 0;

        try
        {
            if (!drive.IsReady)
                return false;

            total = drive.TotalSize;

            if (total <= 0)
                return false;

            // Used = total minus ALL free blocks (what 'df' shows as Used).
            // Available = what a normal user may still use; Linux reserves some for root.
            used = Math.Clamp(total - drive.TotalFreeSpace, 0, total);
            free = Math.Clamp(drive.AvailableFreeSpace, 0, total);
            return true;
        }
        catch (Exception ex) when (IsExpectedFileSystemError(ex))
        {
            return false;
        }
    }

    private static void TryFillCapacity(FilesystemGroupModel group, DriveInfo drive)
    {
        if (TryReadCapacity(drive, out long total, out long used, out long free))
        {
            group.HasCapacity = true;
            group.TotalBytes = total;
            group.UsedBytes = used;
            group.FreeBytes = free;
        }
    }

    private static bool IsNetwork(MountRecord mount) =>
        NetworkFileSystems.Contains(mount.FileSystemType) || mount.Source.StartsWith("//", StringComparison.Ordinal);

    // True for real block devices and network shares. Loop devices (disk images),
    // tmpfs, proc, sysfs, overlay and the like are treated as virtual.
    private static bool IsStorageBacked(MountRecord mount)
    {
        if (IsNetwork(mount))
            return true;

        return mount.Source.StartsWith("/dev/", StringComparison.Ordinal)
               && !mount.Source.StartsWith("/dev/loop", StringComparison.Ordinal);
    }

    private static VirtualFilesystemModel BuildVirtual(MountRecord mount, Dictionary<string, DriveInfo> capacities)
    {
        string description;

        if (mount.Source.StartsWith("/dev/loop", StringComparison.Ordinal))
            description = "Mounted disk image (loop device, e.g. a snap package)";
        else if (VirtualDescriptions.TryGetValue(mount.FileSystemType, out string? known))
            description = known;
        else if (mount.FileSystemType.StartsWith("fuse.", StringComparison.OrdinalIgnoreCase))
            description = "User-space filesystem (FUSE)";
        else
            description = "Virtual filesystem - not physical storage";

        string size = string.Empty;

        // Only RAM-backed filesystems have a meaningful size to show.
        if ((mount.FileSystemType == "tmpfs" || mount.FileSystemType == "ramfs") &&
            capacities.TryGetValue(mount.MountPoint, out DriveInfo? drive) &&
            TryReadCapacity(drive, out long total, out long used, out _))
        {
            size = $"{ByteFormatter.Format(used)} of {ByteFormatter.Format(total)} used (RAM)";
        }

        return new VirtualFilesystemModel
        {
            MountPoint = mount.MountPoint,
            FileSystemType = mount.FileSystemType,
            Description = description,
            SizeText = size
        };
    }

    private static MountPointModel BuildMountPoint(MountRecord mount, FilesystemGroupModel group)
    {
        string detail = string.Empty;

        if (mount.FileSystemType == "btrfs")
        {
            // btrfs puts several independent trees ("subvolumes") in one filesystem.
            string? subvolume = mount.SuperOptions.Split(',')
                .FirstOrDefault(o => o.StartsWith("subvol=", StringComparison.Ordinal))?
                .Substring("subvol=".Length);

            if (!string.IsNullOrEmpty(subvolume))
                detail = $"btrfs subvolume {subvolume}";
        }
        else if (mount.Root != "/")
        {
            // Mounting only part of a filesystem somewhere else is a "bind mount".
            detail = $"Bind mount of {mount.Root}";
        }

        return new MountPointModel
        {
            Path = mount.MountPoint,
            Description = DescribeMountPoint(mount, group),
            Detail = detail,
            IsReadOnly = mount.Options.Split(',').Contains("ro")
        };
    }

    private static string DescribeMountPoint(MountRecord mount, FilesystemGroupModel group)
    {
        if (WellKnownMountPoints.TryGetValue(mount.MountPoint, out string? known))
            return known;

        if (group.IsNetwork)
            return "Network share";

        if (mount.MountPoint.StartsWith("/media/", StringComparison.Ordinal) ||
            mount.MountPoint.StartsWith("/run/media/", StringComparison.Ordinal) ||
            group.Device?.IsRemovable == true)
            return "Removable or external storage";

        if (mount.MountPoint.StartsWith("/mnt", StringComparison.Ordinal))
            return "Manually mounted storage";

        return "Mounted storage";
    }

    public DirectoryListingModel ListDirectory(string path)
    {
        var listing = new DirectoryListingModel();

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            listing.Error = $"'{path}' is not a valid path.";
            return listing;
        }

        listing.Path = fullPath;

        if (!Directory.Exists(fullPath))
        {
            listing.Error = $"The folder '{fullPath}' does not exist.";
            return listing;
        }

        try
        {
            var options = new EnumerationOptions
            {
                // false => if the folder itself cannot be opened we get an exception
                // (so we can say "permission denied") instead of a silent empty list.
                IgnoreInaccessible = false,
                RecurseSubdirectories = false,
                // Include hidden (dot) files, as 'ls -a' does.
                AttributesToSkip = FileAttributes.None
            };

            foreach (FileSystemInfo info in new DirectoryInfo(fullPath).EnumerateFileSystemInfos("*", options))
            {
                if (listing.Entries.Count >= MaxEntries)
                {
                    listing.Truncated = true;
                    break;
                }

                DirectoryEntryModel entry = BuildEntry(info);
                listing.Entries.Add(entry);

                if (entry.IsDirectory)
                {
                    listing.FolderCount++;
                }
                else
                {
                    listing.FileCount++;
                    listing.TotalFileBytes += entry.SizeBytes ?? 0;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            listing.Entries.Clear();
            listing.Error = $"Permission denied: you do not have access to '{fullPath}'.";
            return listing;
        }
        catch (DirectoryNotFoundException)
        {
            listing.Entries.Clear();
            listing.Error = $"The folder '{fullPath}' no longer exists.";
            return listing;
        }
        catch (IOException ex)
        {
            listing.Entries.Clear();
            listing.Error = $"Could not read '{fullPath}': {ex.Message}";
            return listing;
        }

        // Folders first, then alphabetical (ignoring case).
        listing.Entries.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return listing;
    }

    private static DirectoryEntryModel BuildEntry(FileSystemInfo info)
    {
        var entry = new DirectoryEntryModel
        {
            Name = info.Name,
            FullPath = info.FullName
        };

        // Every property below can fail for a single odd file (broken link,
        // permission problem, file deleted mid-scan), so each is guarded separately.
        try { entry.IsDirectory = info.Attributes.HasFlag(FileAttributes.Directory); }
        catch (Exception ex) when (IsExpectedFileSystemError(ex)) { }

        try
        {
            entry.LinkTarget = info.LinkTarget;
            entry.IsSymbolicLink = entry.LinkTarget != null;
        }
        catch (Exception ex) when (IsExpectedFileSystemError(ex)) { }

        if (!entry.IsDirectory && info is FileInfo file)
        {
            try { entry.SizeBytes = file.Length; }
            catch (Exception ex) when (IsExpectedFileSystemError(ex)) { }
        }

        try { entry.LastModified = info.LastWriteTime; }
        catch (Exception ex) when (IsExpectedFileSystemError(ex)) { }

        try { entry.Permissions = FormatPermissions(info.UnixFileMode, entry.IsDirectory, entry.IsSymbolicLink); }
        catch (Exception ex) when (IsExpectedFileSystemError(ex)) { }

        return entry;
    }

    /// <summary>Formats permission bits like 'ls -l', e.g. "drwxr-xr-x".</summary>
    private static string FormatPermissions(UnixFileMode mode, bool isDirectory, bool isLink)
    {
        var text = new StringBuilder(10);

        text.Append(isLink ? 'l' : isDirectory ? 'd' : '-');

        text.Append(mode.HasFlag(UnixFileMode.UserRead) ? 'r' : '-');
        text.Append(mode.HasFlag(UnixFileMode.UserWrite) ? 'w' : '-');
        text.Append(mode.HasFlag(UnixFileMode.UserExecute) ? 'x' : '-');

        text.Append(mode.HasFlag(UnixFileMode.GroupRead) ? 'r' : '-');
        text.Append(mode.HasFlag(UnixFileMode.GroupWrite) ? 'w' : '-');
        text.Append(mode.HasFlag(UnixFileMode.GroupExecute) ? 'x' : '-');

        text.Append(mode.HasFlag(UnixFileMode.OtherRead) ? 'r' : '-');
        text.Append(mode.HasFlag(UnixFileMode.OtherWrite) ? 'w' : '-');
        text.Append(mode.HasFlag(UnixFileMode.OtherExecute) ? 'x' : '-');

        return text.ToString();
    }

    // The kinds of failure we expect when inspecting a real file system.
    // Anything else is a genuine bug and should not be hidden.
    private static bool IsExpectedFileSystemError(Exception ex) =>
        ex is IOException
        || ex is UnauthorizedAccessException
        || ex is PlatformNotSupportedException
        || ex is NotSupportedException
        || ex is ArgumentException;
}
