using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>Paths found by a name search.</summary>
public class SearchOutcome
{
    public List<string> Paths { get; } = new();

    public bool Truncated { get; set; }

    // Folders that could not be read (usually permission denied) and were skipped.
    public int SkippedFolders { get; set; }

    public string? Error { get; set; }
}

/// <summary>
/// Performs real file operations for the Storage page. Every operation that changes the
/// filesystem first asks <see cref="FileSafetyPolicy"/>; reading metadata is allowed anywhere
/// the user already has read access.
/// </summary>
public class FileOperationsService
{
    private readonly FileSafetyPolicy _policy;
    private readonly UserDirectory _users = new();

    public FileOperationsService(FileSafetyPolicy? policy = null)
    {
        _policy = policy ?? new FileSafetyPolicy();
    }

    public string AllowedRoot => _policy.AllowedRoot;

    /// <summary>Null if new items may be created in this folder, otherwise the reason (for the UI hint).</summary>
    public string? WhyFolderIsReadOnly(string directory) => _policy.CheckCanWriteInto(directory);

    // ---------------------------------------------------------------- create

    public FileOperationResult CreateFile(string directory, string name)
    {
        if (FileSafetyPolicy.ValidateName(name) is string nameError)
            return FileOperationResult.Fail(nameError);

        if (_policy.CheckCanWriteInto(directory) is string blocked)
            return FileOperationResult.Blocked(blocked);

        string path = Path.Combine(directory, name);

        try
        {
            // CreateNew = the kernel's O_CREAT|O_EXCL: fails if anything already has this name,
            // so an existing file can never be overwritten by "New File".
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            return FileOperationResult.Ok(
                $"Created empty file '{name}'. Its permissions come from the default mode (rw-rw-rw-) minus your umask.", path);
        }
        catch (IOException) when (FileStatReader.Exists(path))
        {
            return FileOperationResult.Fail($"'{name}' already exists in this folder. Choose another name; nothing was overwritten.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return FromException(ex, $"create '{name}'");
        }
    }

    public FileOperationResult CreateDirectory(string directory, string name)
    {
        if (FileSafetyPolicy.ValidateName(name) is string nameError)
            return FileOperationResult.Fail(nameError);

        if (_policy.CheckCanWriteInto(directory) is string blocked)
            return FileOperationResult.Blocked(blocked);

        string path = Path.Combine(directory, name);

        // Directory.CreateDirectory silently succeeds if the folder exists, so check first.
        if (FileStatReader.Exists(path))
            return FileOperationResult.Fail($"'{name}' already exists in this folder. Choose another name.");

        try
        {
            Directory.CreateDirectory(path);
            return FileOperationResult.Ok(
                $"Created folder '{name}'. A new directory always has 2 hard links: its name in the parent, and its own '.' entry.", path);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return FromException(ex, $"create folder '{name}'");
        }
    }

    // ---------------------------------------------------------------- rename

    public FileOperationResult Rename(string entryPath, string newName)
    {
        if (FileSafetyPolicy.ValidateName(newName) is string nameError)
            return FileOperationResult.Fail(nameError);

        if (_policy.CheckCanModifyEntry(entryPath) is string blocked)
            return FileOperationResult.Blocked(blocked);

        string oldName = Path.GetFileName(Path.TrimEndingDirectorySeparator(entryPath));
        string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(entryPath))!;
        string newPath = Path.Combine(parent, newName);

        if (newName == oldName)
            return FileOperationResult.Ok("The name is unchanged.", entryPath);

        if (FileStatReader.Exists(newPath))
            return FileOperationResult.Fail($"'{newName}' already exists in this folder. Renaming would replace it, so nothing was changed.");

        // Only the directory entry changes: the inode, contents and timestamps stay the same,
        // and a symlink is renamed rather than its target.
        int errno = RenameNoReplace(entryPath, newPath);

        if (errno == 0)
            return FileOperationResult.Ok(
                $"Renamed '{oldName}' to '{newName}'. Only the directory entry changed: the inode number and data are the same.", newPath);

        return errno == EExist
            ? FileOperationResult.Fail($"'{newName}' already exists in this folder. Renaming would replace it, so nothing was changed.")
            : FromErrno(errno, $"rename '{oldName}'");
    }

    // ---------------------------------------------------------------- delete

    public FileOperationResult Delete(string entryPath)
    {
        if (_policy.CheckCanModifyEntry(entryPath) is string blocked)
            return FileOperationResult.Blocked(blocked);

        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(entryPath));
        FileStat? stat = FileStatReader.TryStat(entryPath, out string statError);

        if (stat is null)
            return FileOperationResult.Fail($"Could not delete '{name}': {statError}");

        uint type = stat.Mode & FileStatReader.TypeMask;

        try
        {
            if (type == FileStatReader.TypeDirectory)
            {
                // rmdir() refuses non-empty folders, so nothing is ever deleted recursively.
                if (NativeLibc.RemoveDirectory(entryPath) == 0)
                    return FileOperationResult.Ok($"Deleted empty folder '{name}' (rmdir).");

                int errno = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                return errno == ENotEmpty || errno == EExist
                    ? FileOperationResult.Fail($"'{name}' is not empty. Only empty folders can be deleted here: this demonstrator never deletes recursively.")
                    : FromErrno(errno, $"delete '{name}'");
            }

            // Files, symlinks, pipes...: unlink() removes this NAME. The data is freed only when the
            // inode has no names left and no process still has the file open.
            if (NativeLibc.Unlink(entryPath) == 0)
            {
                string detail = type == FileStatReader.TypeSymlink
                    ? "Only the symbolic link was removed; whatever it pointed to is untouched."
                    : stat.HardLinkCount > 1
                        ? $"The inode still has {stat.HardLinkCount - 1} other name(s) (hard links), so its data is kept."
                        : "This was the inode's last name, so the kernel frees its data blocks once no process has it open.";
                return FileOperationResult.Ok($"Deleted '{name}' (unlink). {detail}");
            }

            return FromErrno(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), $"delete '{name}'");
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            return FileOperationResult.Fail("Deleting is unavailable: the C library functions could not be loaded.");
        }
    }

    // ---------------------------------------------------------------- copy

    /// <summary>
    /// Copies a regular file into a folder. Folders and symlinks are not copied (no recursion, no link
    /// following). When the destination exists and overwrite is false, NeedsOverwriteConfirmation is set.
    /// </summary>
    public FileOperationResult Copy(string sourcePath, string destinationDirectory, bool overwrite)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));
        FileStat? stat = FileStatReader.TryStat(sourcePath, out string statError);

        if (stat is null)
            return FileOperationResult.Fail($"Could not copy '{name}': {statError}");

        uint type = stat.Mode & FileStatReader.TypeMask;

        if (type == FileStatReader.TypeDirectory)
            return FileOperationResult.Fail("Copying folders is not supported: it would need a recursive copy of everything inside. Copy individual files instead.");

        if (type == FileStatReader.TypeSymlink)
            return FileOperationResult.Fail("Symbolic links are not copied, so a link can never be used to pull in a protected file. Open the real file and copy that instead.");

        if (type != FileStatReader.TypeRegular)
            return FileOperationResult.Fail("Only regular files can be copied (not devices, pipes or sockets).");

        if (_policy.CheckCanWriteInto(destinationDirectory) is string blocked)
            return FileOperationResult.Blocked(blocked);

        string destination = Path.Combine(destinationDirectory, name);

        // Copying into the folder the file is already in: make "name (copy).ext" instead of a clash.
        if (SameRealFolder(sourcePath, destinationDirectory))
            destination = UniqueCopyName(destinationDirectory, name);

        if (FileStatReader.TryStat(destination, out _) is FileStat existing)
        {
            if (CheckReplaceable(existing, Path.GetFileName(destination)) is FileOperationResult refuse)
                return refuse;

            if (!overwrite)
                return NeedsConfirmation(Path.GetFileName(destination), destinationDirectory);

            if (_policy.CheckCanModifyEntry(destination) is string blockedReplace)
                return FileOperationResult.Blocked(blockedReplace);
        }

        try
        {
            File.Copy(sourcePath, destination, overwrite);
            return FileOperationResult.Ok(
                $"Copied '{name}' to '{destination}'. The copy is a NEW inode with its own data blocks, owned by you; changing one file no longer affects the other.",
                destination);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return FromException(ex, $"copy '{name}'");
        }
    }

    // ---------------------------------------------------------------- move

    public FileOperationResult Move(string sourcePath, string destinationDirectory, bool overwrite)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath));

        if (_policy.CheckCanModifyEntry(sourcePath) is string blockedSource)
            return FileOperationResult.Blocked(blockedSource);

        if (_policy.CheckCanWriteInto(destinationDirectory) is string blockedDestination)
            return FileOperationResult.Blocked(blockedDestination);

        if (SameRealFolder(sourcePath, destinationDirectory))
            return FileOperationResult.Fail($"'{name}' is already in this folder. Open a different folder, then press Paste here.");

        FileStat? stat = FileStatReader.TryStat(sourcePath, out string statError);
        if (stat is null)
            return FileOperationResult.Fail($"Could not move '{name}': {statError}");

        bool isDirectory = (stat.Mode & FileStatReader.TypeMask) == FileStatReader.TypeDirectory;

        // A folder cannot be moved inside itself (the tree would become disconnected).
        string? realSource = FileSafetyPolicy.TryRealPath(sourcePath);
        string? realDestination = FileSafetyPolicy.TryRealPath(destinationDirectory);
        if (isDirectory && realSource is not null && realDestination is not null &&
            (realDestination == realSource || realDestination.StartsWith(realSource + "/", StringComparison.Ordinal)))
        {
            return FileOperationResult.Fail($"A folder cannot be moved into itself or into one of its own subfolders.");
        }

        string destination = Path.Combine(destinationDirectory, name);

        if (FileStatReader.TryStat(destination, out _) is FileStat existing)
        {
            if (CheckReplaceable(existing, name) is FileOperationResult refuse)
                return refuse;

            if (isDirectory)
                return FileOperationResult.Fail($"A file named '{name}' already exists there; a folder cannot replace a file.");

            if (!overwrite)
                return NeedsConfirmation(name, destinationDirectory);

            if (_policy.CheckCanModifyEntry(destination) is string blockedReplace)
                return FileOperationResult.Blocked(blockedReplace);
        }

        try
        {
            // Same filesystem: rename() just moves the directory entry. Instant, even for huge files,
            // because no data is copied. overwrite => plain rename (atomically replaces the confirmed file).
            int errno = overwrite
                ? (NativeLibc.Rename(sourcePath, destination) == 0 ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error())
                : RenameNoReplace(sourcePath, destination);

            if (errno == 0)
                return FileOperationResult.Ok(
                    $"Moved '{name}' to '{destinationDirectory}'. Same filesystem, so only the directory entry moved: the inode number and data blocks are unchanged.",
                    destination);

            if (errno == EExist)
                return NeedsConfirmation(name, destinationDirectory);

            if (errno != EXDev)
                return FromErrno(errno, $"move '{name}'");

            // Different filesystem (e.g. home -> USB stick): an inode cannot cross filesystems,
            // so the data must be copied and the original removed.
            if (isDirectory)
                return FileOperationResult.Fail("Moving a folder to a different filesystem would require a recursive copy and delete, which this demonstrator does not do.");

            if ((stat.Mode & FileStatReader.TypeMask) != FileStatReader.TypeRegular)
                return FileOperationResult.Fail("Only regular files can be moved between different filesystems.");

            File.Copy(sourcePath, destination, overwrite);

            if (NativeLibc.Unlink(sourcePath) != 0)
                return FileOperationResult.Fail($"'{name}' was copied to the destination, but the original could not be removed (errno {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}). Both copies now exist.");

            return FileOperationResult.Ok(
                $"Moved '{name}' to another filesystem: the data was copied to a NEW inode there and the original was deleted (rename() cannot cross filesystems).",
                destination);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return FromException(ex, $"move '{name}'");
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            return FileOperationResult.Fail("Moving is unavailable: the C library functions could not be loaded.");
        }
    }

    // ---------------------------------------------------------------- search

    /// <summary>
    /// Finds entries whose NAME contains the query (case-insensitive) below startDirectory.
    /// Read-only. The walk is done by hand so the rules are explicit:
    ///  - only REAL folders are entered; symlinked folders are listed but never followed
    ///    (no loops, no escaping into /etc through a link);
    ///  - it stays on the starting filesystem, like 'find -xdev' (so searching from / does not
    ///    descend into /proc, /sys or other mounts);
    ///  - folders that cannot be read are skipped and counted.
    /// </summary>
    public SearchOutcome Search(string startDirectory, string query, int maxResults, CancellationToken cancellationToken)
    {
        var outcome = new SearchOutcome();
        query = query.Trim();

        if (query.Length == 0)
        {
            outcome.Error = "Type part of a file name to search for.";
            return outcome;
        }

        FileStat? startStat = FileStatReader.TryStat(startDirectory, out string startError);
        if (startStat is null)
        {
            outcome.Error = $"Cannot search '{startDirectory}': {startError}";
            return outcome;
        }

        // '*' and '?' are wildcards; otherwise match the text anywhere in the name.
        string pattern = query.IndexOfAny(new[] { '*', '?' }) >= 0 ? query : $"*{query}*";

        var listOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.None,
            ReturnSpecialDirectories = false
        };

        var pending = new Stack<string>();
        pending.Push(startDirectory);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();

            try
            {
                foreach (FileSystemInfo info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", listOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, info.Name, ignoreCase: true))
                    {
                        if (outcome.Paths.Count >= maxResults)
                        {
                            outcome.Truncated = true;
                            return outcome;
                        }

                        outcome.Paths.Add(info.FullName);
                    }

                    // lstat the entry: descend only into real directories on the same filesystem.
                    FileStat? stat = FileStatReader.TryStat(info.FullName, out _);
                    if (stat is not null
                        && (stat.Mode & FileStatReader.TypeMask) == FileStatReader.TypeDirectory
                        && stat.DeviceId == startStat.DeviceId)
                    {
                        pending.Push(info.FullName);
                    }
                }
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                outcome.SkippedFolders++; // permission denied, vanished while searching, ...
            }
        }

        return outcome;
    }

    // ---------------------------------------------------------------- errno values (asm-generic/errno-base.h)

    private const int EPerm = 1, ENoEnt = 2, EAccess = 13, EExist = 17, EXDev = 18, EInval = 22, ERofs = 30, ENotEmpty = 39;

    // Overwriting is only ever offered for an existing regular file.
    private static FileOperationResult? CheckReplaceable(FileStat existing, string name) =>
        (existing.Mode & FileStatReader.TypeMask) switch
        {
            FileStatReader.TypeRegular => null,
            FileStatReader.TypeDirectory => FileOperationResult.Fail($"A folder named '{name}' already exists there. Folders are never replaced."),
            FileStatReader.TypeSymlink => FileOperationResult.Fail($"A symbolic link named '{name}' already exists there. Links are never replaced, because writing through them could change a file elsewhere."),
            _ => FileOperationResult.Fail($"'{name}' already exists there and is not a regular file, so it will not be replaced.")
        };

    private static FileOperationResult NeedsConfirmation(string name, string directory) => new()
    {
        Success = false,
        NeedsOverwriteConfirmation = true,
        Message = $"'{name}' already exists in '{directory}'. Replace it? The existing file's contents will be lost."
    };

    private static bool SameRealFolder(string entryPath, string directory)
    {
        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(entryPath));
        string? a = parent is null ? null : FileSafetyPolicy.TryRealPath(parent);
        string? b = FileSafetyPolicy.TryRealPath(directory);
        return a is not null && a == b;
    }

    // "report.txt" -> "report (copy).txt", then "report (copy 2).txt", ...
    private static string UniqueCopyName(string directory, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);

        for (int n = 1; ; n++)
        {
            string candidate = Path.Combine(directory, n == 1 ? $"{stem} (copy){extension}" : $"{stem} (copy {n}){extension}");
            if (!FileStatReader.Exists(candidate))
                return candidate;
        }
    }
    private const int AtFdCwd = -100;
    private const uint RenameNoReplaceFlag = 1;

    // Returns 0 on success, otherwise the errno. Uses renameat2(RENAME_NOREPLACE) so the kernel itself
    // guarantees nothing is overwritten. Some filesystems lack that flag (EINVAL); then fall back to
    // rename() after re-checking that the destination is still free.
    private static int RenameNoReplace(string oldPath, string newPath)
    {
        try
        {
            if (NativeLibc.RenameAt2(AtFdCwd, oldPath, AtFdCwd, newPath, RenameNoReplaceFlag) == 0)
                return 0;

            int errno = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            if (errno != EInval)
                return errno;

            if (FileStatReader.Exists(newPath))
                return EExist;

            return NativeLibc.Rename(oldPath, newPath) == 0 ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            return EInval;
        }
    }

    private static FileOperationResult FromErrno(int errno, string action) => errno switch
    {
        EPerm or EAccess => new FileOperationResult
        {
            Success = false,
            IsPermissionError = true,
            Message = $"Permission denied: the kernel refused to {action}. You need write (w) and enter (x) permission on the folder."
        },
        ENoEnt => FileOperationResult.Fail($"Could not {action}: the item or its folder no longer exists. Refresh the folder."),
        ERofs => FileOperationResult.Fail($"Could not {action}: the filesystem is mounted read-only."),
        ENotEmpty => FileOperationResult.Fail($"Could not {action}: the folder is not empty."),
        _ => FileOperationResult.Fail($"Could not {action} (errno {errno}).")
    };

    // ---------------------------------------------------------------- properties

    public FilePropertiesModel? GetProperties(string path, out string error)
    {
        FileStat? stat = FileStatReader.TryStat(path, out error);

        if (stat is null)
            return null;

        var properties = new FilePropertiesModel
        {
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
            FullPath = path,
            FileType = DescribeType(stat.Mode, path),
            Size = $"{ByteFormatter.Format((long)stat.SizeBytes)} ({stat.SizeBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes)",
            SizeOnDisk = $"{ByteFormatter.Format((long)(stat.BlocksAllocated * 512))} ({stat.BlocksAllocated} blocks of 512 bytes)",
            Created = stat.Created is DateTime created
                ? FormatTime(created)
                : "Not available (this filesystem or kernel does not record creation time)",
            Modified = FormatTime(stat.Modified),
            Accessed = FormatTime(stat.Accessed),
            StatusChanged = FormatTime(stat.StatusChanged),
            Permissions = $"{SymbolicPermissions(stat.Mode)}   (octal {Convert.ToString(stat.Mode & 0xFFF, 8).PadLeft(4, '0')})",
            PermissionsExplained = ExplainPermissions(stat.Mode),
            Owner = $"{_users.UserName(stat.UserId)} (uid {stat.UserId}), group {_users.GroupName(stat.GroupId)} (gid {stat.GroupId})",
            Inode = stat.Inode.ToString(CultureInfo.InvariantCulture),
            HardLinks = stat.HardLinkCount.ToString(CultureInfo.InvariantCulture),
        };

        if ((stat.Mode & FileStatReader.TypeMask) == FileStatReader.TypeSymlink)
        {
            string? target = null;
            try { target = new FileInfo(path).LinkTarget; } catch (Exception ex) when (IsExpected(ex)) { }

            string? finalTarget = FileSafetyPolicy.TryRealPath(path);
            properties.LinkTarget = $"{target ?? "?"}  →  resolves to {finalTarget ?? "(nothing: broken link)"}";
        }

        properties.ModifyStatus = _policy.CheckCanModifyEntry(path) is string reason
            ? $"Read-only here: {reason}"
            : "Can be renamed, moved or deleted by this demonstrator.";

        return properties;
    }

    // ---------------------------------------------------------------- helpers

    private static string DescribeType(uint mode, string path) => (mode & FileStatReader.TypeMask) switch
    {
        FileStatReader.TypeRegular => string.IsNullOrEmpty(Path.GetExtension(path))
            ? "Regular file"
            : $"Regular file ({Path.GetExtension(path).TrimStart('.').ToUpperInvariant()})",
        FileStatReader.TypeDirectory => "Directory",
        FileStatReader.TypeSymlink => "Symbolic link",
        FileStatReader.TypeCharDevice => "Character device",
        FileStatReader.TypeBlockDevice => "Block device",
        FileStatReader.TypeFifo => "Named pipe (FIFO)",
        FileStatReader.TypeSocket => "Socket",
        _ => "Unknown"
    };

    private static string SymbolicPermissions(uint mode)
    {
        var text = new StringBuilder(10);

        text.Append((mode & FileStatReader.TypeMask) switch
        {
            FileStatReader.TypeDirectory => 'd',
            FileStatReader.TypeSymlink => 'l',
            FileStatReader.TypeCharDevice => 'c',
            FileStatReader.TypeBlockDevice => 'b',
            FileStatReader.TypeFifo => 'p',
            FileStatReader.TypeSocket => 's',
            _ => '-'
        });

        for (int shift = 6; shift >= 0; shift -= 3)
        {
            uint bits = (mode >> shift) & 7;
            text.Append((bits & 4) != 0 ? 'r' : '-');
            text.Append((bits & 2) != 0 ? 'w' : '-');
            text.Append((bits & 1) != 0 ? 'x' : '-');
        }

        return text.ToString();
    }

    private static string ExplainPermissions(uint mode)
    {
        bool isDirectory = (mode & FileStatReader.TypeMask) == FileStatReader.TypeDirectory;

        string Describe(uint bits)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((bits & 4) != 0) parts.Add(isDirectory ? "list" : "read");
            if ((bits & 2) != 0) parts.Add(isDirectory ? "create/delete entries" : "write");
            if ((bits & 1) != 0) parts.Add(isDirectory ? "enter" : "execute");
            return parts.Count == 0 ? "no access" : string.Join(", ", parts);
        }

        return $"Owner: {Describe((mode >> 6) & 7)}.  Group: {Describe((mode >> 3) & 7)}.  Others: {Describe(mode & 7)}.";
    }

    private static string FormatTime(DateTime time) => time.ToString("dd MMM yyyy, HH:mm:ss");

    private static bool IsExpected(Exception ex) =>
        ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException;

    private static FileOperationResult FromException(Exception ex, string action) => ex switch
    {
        UnauthorizedAccessException => new FileOperationResult
        {
            Success = false,
            IsPermissionError = true,
            Message = $"Permission denied: the kernel refused to {action}. You need write (w) and enter (x) permission on the folder."
        },
        DirectoryNotFoundException or FileNotFoundException =>
            FileOperationResult.Fail($"Could not {action}: the item or its folder no longer exists. Refresh the folder."),
        _ => FileOperationResult.Fail($"Could not {action}: {ex.Message}")
    };
}
