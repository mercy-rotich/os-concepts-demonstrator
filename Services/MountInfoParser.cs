using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>One line of /proc/self/mountinfo.</summary>
public class MountRecord
{
    // "major:minor" of the filesystem's device. Mounts of the SAME filesystem
    // (btrfs subvolumes, bind mounts) share this value.
    public string MajorMinor { get; set; } = string.Empty;

    // The part of the filesystem that is mounted here ("/" = all of it).
    public string Root { get; set; } = string.Empty;

    public string MountPoint { get; set; } = string.Empty;

    public string Options { get; set; } = string.Empty;

    public string FileSystemType { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string SuperOptions { get; set; } = string.Empty;
}

/// <summary>Reads the kernel's list of mounts. Read-only.</summary>
public static class MountInfoParser
{
    public static List<MountRecord> Parse(string path)
    {
        var records = new List<MountRecord>();

        foreach (string line in File.ReadLines(path))
        {
            MountRecord? record = ParseLine(line);

            if (record is not null)
                records.Add(record);
        }

        return records;
    }

    // Format: id parent maj:min root mountpoint options [optional fields...] - fstype source superoptions
    private static MountRecord? ParseLine(string line)
    {
        string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // The lone "-" separates the variable-length optional fields from the rest.
        int separator = Array.IndexOf(fields, "-");

        if (fields.Length < 7 || separator < 6 || separator + 2 >= fields.Length)
            return null;

        return new MountRecord
        {
            MajorMinor = fields[2],
            Root = Unescape(fields[3]),
            MountPoint = Unescape(fields[4]),
            Options = fields[5],
            FileSystemType = fields[separator + 1],
            Source = Unescape(fields[separator + 2]),
            SuperOptions = separator + 3 < fields.Length ? fields[separator + 3] : string.Empty
        };
    }

    // The kernel writes spaces and some other characters as octal escapes, e.g. "\040".
    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var bytes = new List<byte>();

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && IsOctal(value, i + 1))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool IsOctal(string value, int start)
    {
        if (start + 3 > value.Length)
            return false;

        for (int i = start; i < start + 3; i++)
        {
            if (value[i] < '0' || value[i] > '7')
                return false;
        }

        return true;
    }
}
