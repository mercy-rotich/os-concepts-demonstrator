using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Decides whether a file operation is allowed. Every create/rename/delete/copy/move asks this
/// class first, so the safety rules live in one place and cannot be bypassed by the UI.
///
/// Rules:
///  1. Changes are only allowed strictly inside the allowed root (the user's home folder).
///  2. Locations are compared by their REAL path (libc realpath), so a symlink inside home
///     that points to /etc cannot be used to reach /etc.
///  3. Hidden top-level items of home (~/.ssh, ~/.config, ~/.bashrc ...) are off limits.
///  4. Names must be a single, valid path component.
/// </summary>
public sealed class FileSafetyPolicy
{
    private const int MaxNameBytes = 255; // NAME_MAX on Linux filesystems

    public FileSafetyPolicy(string? allowedRoot = null)
    {
        string root = allowedRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // If the root itself cannot be resolved, nothing is modifiable (fail closed).
        AllowedRoot = TryRealPath(root) ?? string.Empty;
    }

    /// <summary>Canonical path of the only tree where changes are permitted.</summary>
    public string AllowedRoot { get; }

    /// <summary>Canonical absolute path with all symlinks resolved, or null if it does not exist / cannot be resolved.</summary>
    public static string? TryRealPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        nint resolved;
        try
        {
            resolved = NativeLibc.RealPath(path, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            return null;
        }

        if (resolved == 0)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(resolved);
        }
        finally
        {
            NativeLibc.Free(resolved);
        }
    }

    /// <summary>Null when a new item may be created inside this folder; otherwise the reason it may not.</summary>
    public string? CheckCanWriteInto(string directory)
    {
        string? real = TryRealPath(directory);

        if (real is null)
            return $"The folder '{directory}' does not exist or cannot be resolved.";

        if (!Directory.Exists(real))
            return $"'{directory}' is not a folder.";

        if (!IsSameOrInside(real, AllowedRoot))
            return OutsideRootMessage(real, directory);

        if (IsHiddenTopLevel(real))
            return "Hidden configuration folders in your home folder (such as ~/.config or ~/.ssh) are protected in this demonstrator.";

        return null;
    }

    /// <summary>
    /// Null when this existing entry may be renamed/deleted/moved; otherwise the reason.
    /// The entry itself is NOT followed if it is a symlink (operations act on the link), but
    /// the folder that contains it IS fully resolved.
    /// </summary>
    public string? CheckCanModifyEntry(string entryPath)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(entryPath);
        string? parent = Path.GetDirectoryName(trimmed);
        string name = Path.GetFileName(trimmed);

        if (parent is null || string.IsNullOrEmpty(name))
            return "The filesystem root cannot be changed.";

        string? realParent = TryRealPath(parent);

        if (realParent is null)
            return $"The folder containing '{name}' does not exist or cannot be resolved.";

        string realEntry = Path.Combine(realParent, name);

        if (!FileStatReader.Exists(realEntry))
            return $"'{name}' no longer exists. Refresh the folder.";

        if (!IsSameOrInside(realEntry, AllowedRoot) || PathsEqual(realEntry, AllowedRoot))
            return PathsEqual(realEntry, AllowedRoot)
                ? "Your home folder itself cannot be changed."
                : OutsideRootMessage(realEntry, entryPath);

        if (IsHiddenTopLevel(realEntry))
            return "Hidden configuration files and folders in your home folder (such as ~/.bashrc or ~/.config) are protected in this demonstrator.";

        return null;
    }

    /// <summary>Null when this is an acceptable single file/folder name; otherwise the reason.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Enter a name.";

        if (name != name.Trim())
            return "Names cannot start or end with spaces.";

        if (name is "." or "..")
            return "'.' and '..' are reserved: they mean 'this folder' and 'the parent folder'.";

        if (name.Contains('/'))
            return "Names cannot contain '/': on Linux '/' separates folders in a path.";

        if (name.Contains('\0'))
            return "Names cannot contain the null character.";

        if (Encoding.UTF8.GetByteCount(name) > MaxNameBytes)
            return $"The name is too long: Linux filesystems allow at most {MaxNameBytes} bytes per name.";

        return null;
    }

    // True if 'path' equals 'root' or lies below it. Compares whole components so that
    // /home/al is not treated as inside /home/a.
    private static bool IsSameOrInside(string path, string root)
    {
        if (string.IsNullOrEmpty(root))
            return false;

        if (PathsEqual(path, root))
            return true;

        string rootWithSlash = root.EndsWith('/') ? root : root + "/";
        return path.StartsWith(rootWithSlash, StringComparison.Ordinal);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.Ordinal);

    // ~/.something or anything beneath it.
    private bool IsHiddenTopLevel(string realPath)
    {
        if (!IsSameOrInside(realPath, AllowedRoot) || PathsEqual(realPath, AllowedRoot))
            return false;

        string relative = Path.GetRelativePath(AllowedRoot, realPath);
        return relative.StartsWith('.');
    }

    private string OutsideRootMessage(string realPath, string requestedPath)
    {
        string via = PathsEqual(realPath, Path.GetFullPath(requestedPath)) ? string.Empty : $" (it really points to '{realPath}')";
        return $"Changes are only allowed inside your home folder ({AllowedRoot}). '{requestedPath}' is outside it{via}. " +
               "System locations are read-only here so the demonstrator can never damage the operating system.";
    }
}
