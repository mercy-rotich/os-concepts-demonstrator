namespace OS_Concepts_Demonstrator.Models;

/// <summary>One place in the directory tree where a filesystem is attached.</summary>
public class MountPointModel
{
    public string Path { get; set; } = string.Empty;

    // Friendly purpose, e.g. "Root filesystem".
    public string Description { get; set; } = string.Empty;

    // Extra facts: btrfs subvolume or bind-mount source. May be empty.
    public string Detail { get; set; } = string.Empty;

    public bool IsReadOnly { get; set; }

    public string AccessText => IsReadOnly ? "read-only" : "read-write";

    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}
