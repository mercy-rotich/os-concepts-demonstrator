using System.Collections.Generic;
using System.Linq;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.Models;

/// <summary>
/// One filesystem instance, which may be mounted in several places.
/// Capacity belongs to the filesystem, so it is stored (and counted) once here.
/// </summary>
public class FilesystemGroupModel
{
    public string Source { get; set; } = string.Empty;

    public string FileSystemType { get; set; } = string.Empty;

    public bool IsNetwork { get; set; }

    public BlockDeviceInfo? Device { get; set; }

    public bool HasCapacity { get; set; }

    public long TotalBytes { get; set; }

    public long UsedBytes { get; set; }

    public long FreeBytes { get; set; }

    public List<MountPointModel> MountPoints { get; } = new();

    public double UsedPercentage =>
        TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0;

    public string Title => Source;

    public string Subtitle => $"{FileSystemType} filesystem";

    public string UsageLabel => HasCapacity ? $"{UsedPercentage:F1}% used" : "capacity unavailable";

    public string Summary => HasCapacity
        ? $"Total {ByteFormatter.Format(TotalBytes)}  |  Used {ByteFormatter.Format(UsedBytes)}  |  Available {ByteFormatter.Format(FreeBytes)}"
        : "Capacity could not be read for this filesystem.";

    // Physical device -> Partition -> Filesystem -> Mount points
    public string PhysicalDeviceText
    {
        get
        {
            if (IsNetwork)
                return "None - this is network storage";

            if (Device?.DiskName is null)
                return "Could not be determined";

            string model = string.IsNullOrWhiteSpace(Device.DiskModel) ? "unknown model" : Device.DiskModel;

            return $"{Device.DiskName} - {model}, {ByteFormatter.Format(Device.DiskSizeBytes)}, {Device.DiskTypeText}";
        }
    }

    public string PartitionText
    {
        get
        {
            if (IsNetwork)
                return "None";

            return Device?.Kind switch
            {
                BlockDeviceKind.Partition =>
                    $"{Device.Name} ({ByteFormatter.Format(Device.SizeBytes)}) on disk {Device.DiskName}",
                BlockDeviceKind.Disk =>
                    "None - the filesystem uses the whole disk",
                BlockDeviceKind.Logical =>
                    $"Logical volume {Device.LogicalName ?? Device.Name} ({ByteFormatter.Format(Device.SizeBytes)})",
                _ => "Could not be determined"
            };
        }
    }

    public string FilesystemText =>
        $"{FileSystemType}{(MountPoints.Count > 0 && MountPoints.All(m => m.IsReadOnly) ? ", read-only" : string.Empty)}";

    public bool IsShared => MountPoints.Count > 1;

    public string SharedNote => IsShared
        ? $"{MountPoints.Count} mount points share this ONE filesystem, so its capacity is counted only once."
        : string.Empty;
}
