using System.Collections.Generic;
using System.Linq;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.Models;

public class StorageOverviewModel
{
    public List<PhysicalDiskModel> Disks { get; } = new();

    public List<FilesystemGroupModel> Filesystems { get; } = new();

    public List<VirtualFilesystemModel> VirtualFilesystems { get; } = new();

    public string? Error { get; set; }

    // Totals count each filesystem ONCE, however many mount points it has.
    public string SummaryText
    {
        get
        {
            var local = Filesystems.Where(f => !f.IsNetwork && f.HasCapacity).ToList();

            long total = local.Sum(f => f.TotalBytes);
            long used = local.Sum(f => f.UsedBytes);
            long free = local.Sum(f => f.FreeBytes);

            int mounts = Filesystems.Sum(f => f.MountPoints.Count);

            return $"{Disks.Count} physical disk(s)  |  {Filesystems.Count} filesystem(s) mounted at {mounts} mount point(s)  |  " +
                   $"{ByteFormatter.Format(total)} total, {ByteFormatter.Format(used)} used, {ByteFormatter.Format(free)} available " +
                   "(each filesystem counted once)";
        }
    }
}
