using System.Collections.Generic;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.Models;

public class PartitionModel
{
    public string Name { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public List<string> MountedAt { get; } = new();

    public string Text => $"/dev/{Name}  -  {ByteFormatter.Format(SizeBytes)}";

    public string MountedText => MountedAt.Count > 0
        ? "Mounted at: " + string.Join(", ", MountedAt)
        : "Not mounted";
}

public class PhysicalDiskModel
{
    public string Name { get; set; } = string.Empty;

    public string? Model { get; set; }

    public long SizeBytes { get; set; }

    public string TypeText { get; set; } = string.Empty;

    public List<PartitionModel> Partitions { get; } = new();

    public string Title => string.IsNullOrWhiteSpace(Model) ? $"Disk {Name}" : Model!;

    public string Subtitle => $"/dev/{Name}  |  {ByteFormatter.Format(SizeBytes)}  |  {TypeText}";

    public bool HasNoPartitions => Partitions.Count == 0;
}
