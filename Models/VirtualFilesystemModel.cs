namespace OS_Concepts_Demonstrator.Models;

/// <summary>A mount that is not physical storage (kernel interface, RAM disk, disk image...).</summary>
public class VirtualFilesystemModel
{
    public string MountPoint { get; set; } = string.Empty;

    public string FileSystemType { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // For RAM-backed filesystems such as tmpfs; empty otherwise.
    public string SizeText { get; set; } = string.Empty;
}
