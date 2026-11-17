using System;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.Models;

public class DirectoryEntryModel
{
    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public bool IsSymbolicLink { get; set; }

    // Null for folders (calculating folder size would mean walking the whole tree).
    public long? SizeBytes { get; set; }

    public DateTime? LastModified { get; set; }

    // ls-style text such as "drwxr-xr-x", or "N/A" if unavailable.
    public string Permissions { get; set; } = "N/A";

    public string? LinkTarget { get; set; }

    // Only for search results: the folder the item is in, relative to where the search started.
    public string Location { get; set; } = string.Empty;

    public bool HasLocation => !string.IsNullOrEmpty(Location);

    public string TypeText =>
        IsSymbolicLink ? "Link" : IsDirectory ? "Folder" : "File";

    public string SizeText =>
        SizeBytes is long size ? ByteFormatter.Format(size) : "-";

    public string ModifiedText =>
        LastModified?.ToString("dd MMM yyyy, HH:mm") ?? "N/A";
}
