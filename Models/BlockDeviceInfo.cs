namespace OS_Concepts_Demonstrator.Models;

public enum BlockDeviceKind
{
    Unknown,
    Disk,
    Partition,
    // Device-mapper volume (LVM, encrypted volume, ...) built on top of other devices.
    Logical
}

/// <summary>Facts about a block device, read from /sys. Any field may be missing.</summary>
public class BlockDeviceInfo
{
    public string Name { get; set; } = string.Empty;

    public string DevicePath { get; set; } = string.Empty;

    public BlockDeviceKind Kind { get; set; }

    public long SizeBytes { get; set; }

    // The physical disk this device lives on (itself, if it is a whole disk).
    public string? DiskName { get; set; }

    public string? DiskModel { get; set; }

    public long DiskSizeBytes { get; set; }

    // true = spinning hard disk, false = SSD/flash, null = unknown.
    public bool? IsRotational { get; set; }

    public bool IsRemovable { get; set; }

    public string? LogicalName { get; set; }

    public string DiskTypeText
    {
        get
        {
            string type = IsRotational switch
            {
                true => "HDD",
                false => "SSD / flash",
                _ => "type unknown"
            };

            return IsRemovable ? type + ", removable" : type;
        }
    }
}
