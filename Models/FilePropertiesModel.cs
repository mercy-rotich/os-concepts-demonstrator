namespace OS_Concepts_Demonstrator.Models;

// Everything the Properties panel shows for one file or folder. All values are display-ready text.
public class FilePropertiesModel
{
    public string Name { get; set; } = string.Empty;

    // Kind from the inode's type bits: Regular file, Directory, Symbolic link, Device, FIFO, Socket.
    public string FileType { get; set; } = string.Empty;

    public string Size { get; set; } = string.Empty;

    // Blocks actually reserved on the storage device (can differ from Size).
    public string SizeOnDisk { get; set; } = string.Empty;

    public string Created { get; set; } = string.Empty;

    public string Modified { get; set; } = string.Empty;

    public string Accessed { get; set; } = string.Empty;

    // ctime: last change to the inode itself (permissions, owner, link count, or content).
    public string StatusChanged { get; set; } = string.Empty;

    public string Permissions { get; set; } = string.Empty;

    public string PermissionsExplained { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string Inode { get; set; } = string.Empty;

    public string HardLinks { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    // Only for symlinks: what the link points to, and where it finally resolves.
    public string? LinkTarget { get; set; }

    public bool IsSymbolicLink => LinkTarget is not null;

    // Whether this item may be changed by the demonstrator (and if not, why).
    public string ModifyStatus { get; set; } = string.Empty;
}
