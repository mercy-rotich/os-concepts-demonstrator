using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Looks up facts about block devices in /sys (read-only). Every lookup can fail
/// on unusual systems, so missing information is returned as null, never thrown.
/// </summary>
public class BlockDeviceResolver
{
    private const string SysBlock = "/sys/block";
    private const string SysClassBlock = "/sys/class/block";

    // Names that are not physical storage (RAM disks, loop images, compressed RAM...).
    private static readonly string[] NonPhysicalPrefixes = { "loop", "ram", "zram", "nbd", "dm-", "md", "fd" };

    public BlockDeviceInfo? Resolve(string devicePath, int depth = 0)
    {
        try
        {
            string real = ResolveFileLink(devicePath);
            string name = Path.GetFileName(real);

            string classPath = Path.Combine(SysClassBlock, name);

            if (!Directory.Exists(classPath))
                return null;

            // /sys/class/block/<name> is a link to the real sysfs directory.
            string sysPath = ResolveDirectoryLink(classPath);

            var info = new BlockDeviceInfo
            {
                Name = name,
                DevicePath = real,
                SizeBytes = ReadLong(Path.Combine(sysPath, "size")) * 512 // sysfs counts 512-byte sectors
            };

            if (File.Exists(Path.Combine(sysPath, "partition")))
            {
                // A partition's sysfs directory sits inside its parent disk's directory.
                info.Kind = BlockDeviceKind.Partition;
                FillDiskFacts(info, Path.GetFileName(Path.GetDirectoryName(sysPath)));
            }
            else if (Directory.Exists(Path.Combine(sysPath, "slaves")) &&
                     Directory.GetDirectories(Path.Combine(sysPath, "slaves")).Length > 0)
            {
                // Device-mapper / RAID: built on top of other devices; follow the first one.
                info.Kind = BlockDeviceKind.Logical;
                info.LogicalName = ReadText(Path.Combine(sysPath, "dm", "name")) ?? name;

                string? lower = Directory.GetDirectories(Path.Combine(sysPath, "slaves"))
                    .Select(Path.GetFileName).FirstOrDefault();

                if (lower is not null && depth < 4)
                {
                    BlockDeviceInfo? below = Resolve("/dev/" + lower, depth + 1);

                    if (below is not null)
                    {
                        info.DiskName = below.DiskName;
                        info.DiskModel = below.DiskModel;
                        info.DiskSizeBytes = below.DiskSizeBytes;
                        info.IsRotational = below.IsRotational;
                        info.IsRemovable = below.IsRemovable;
                    }
                }
            }
            else
            {
                info.Kind = BlockDeviceKind.Disk;
                FillDiskFacts(info, name);
            }

            return info;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    /// <summary>Lists real disks and their partitions, whether or not they are mounted.</summary>
    public List<PhysicalDiskModel> GetPhysicalDisks()
    {
        var disks = new List<PhysicalDiskModel>();

        try
        {
            foreach (string path in Directory.GetDirectories(SysBlock))
            {
                string name = Path.GetFileName(path);

                if (NonPhysicalPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                    continue;

                string sysPath = ResolveDirectoryLink(path);
                long size = ReadLong(Path.Combine(sysPath, "size")) * 512;

                // Empty card readers / optical drives report size 0; they hold no storage now.
                if (size <= 0)
                    continue;

                var info = new BlockDeviceInfo();
                FillDiskFacts(info, name);

                var disk = new PhysicalDiskModel
                {
                    Name = name,
                    Model = info.DiskModel,
                    SizeBytes = size,
                    TypeText = info.DiskTypeText
                };

                var partitions = new List<(int Number, PartitionModel Partition)>();

                foreach (string sub in Directory.GetDirectories(sysPath))
                {
                    string partFile = Path.Combine(sub, "partition");

                    if (!File.Exists(partFile))
                        continue;

                    partitions.Add((
                        (int)ReadLong(partFile),
                        new PartitionModel
                        {
                            Name = Path.GetFileName(sub),
                            SizeBytes = ReadLong(Path.Combine(sub, "size")) * 512
                        }));
                }

                foreach (var (_, partition) in partitions.OrderBy(p => p.Number))
                    disk.Partitions.Add(partition);

                disks.Add(disk);
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // /sys not readable: return whatever was collected.
        }

        disks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return disks;
    }

    private static void FillDiskFacts(BlockDeviceInfo info, string? diskName)
    {
        if (string.IsNullOrEmpty(diskName))
            return;

        string diskPath = Path.Combine(SysBlock, diskName);

        info.DiskName = diskName;
        info.DiskModel = ReadText(Path.Combine(diskPath, "device", "model"));
        info.DiskSizeBytes = ReadLong(Path.Combine(diskPath, "size")) * 512;

        string? rotational = ReadText(Path.Combine(diskPath, "queue", "rotational"));
        info.IsRotational = rotational switch { "1" => true, "0" => false, _ => null };

        info.IsRemovable = ReadText(Path.Combine(diskPath, "removable")) == "1";

        if (info.Kind == BlockDeviceKind.Disk)
            info.SizeBytes = info.DiskSizeBytes;
    }

    private static string ResolveFileLink(string path)
    {
        FileSystemInfo? target = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
        return target?.FullName ?? path;
    }

    private static string ResolveDirectoryLink(string path)
    {
        FileSystemInfo? target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);
        return target?.FullName ?? path;
    }

    public static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    public static long ReadLong(string path)
    {
        string? text = ReadText(path);

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0;
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException
        || ex is UnauthorizedAccessException
        || ex is NotSupportedException
        || ex is ArgumentException;
}
