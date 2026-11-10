using System.Collections.Generic;

namespace OS_Concepts_Demonstrator.Models;

/// <summary>The result of reading one directory: either entries or an error message.</summary>
public class DirectoryListingModel
{
    public string Path { get; set; } = string.Empty;

    public List<DirectoryEntryModel> Entries { get; } = new();

    public int FolderCount { get; set; }

    public int FileCount { get; set; }

    // Combined size of the files directly in this folder (not sub-folders).
    public long TotalFileBytes { get; set; }

    // True when the folder had more items than the display limit.
    public bool Truncated { get; set; }

    public string? Error { get; set; }
}
