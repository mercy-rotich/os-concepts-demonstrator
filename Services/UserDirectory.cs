using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>Turns numeric user and group ids into names by reading /etc/passwd and /etc/group (read-only, cached).</summary>
public class UserDirectory
{
    private readonly Lazy<Dictionary<uint, string>> _users = new(() => Load("/etc/passwd"));
    private readonly Lazy<Dictionary<uint, string>> _groups = new(() => Load("/etc/group"));

    public string UserName(uint uid) => _users.Value.TryGetValue(uid, out string? name) ? name : uid.ToString(CultureInfo.InvariantCulture);

    public string GroupName(uint gid) => _groups.Value.TryGetValue(gid, out string? name) ? name : gid.ToString(CultureInfo.InvariantCulture);

    // Both files have "name:x:id:..." lines.
    private static Dictionary<uint, string> Load(string path)
    {
        var map = new Dictionary<uint, string>();

        try
        {
            foreach (string line in File.ReadLines(path))
            {
                string[] parts = line.Split(':');

                if (parts.Length >= 3 && uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint id))
                    map.TryAdd(id, parts[0]);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Names fall back to the numbers.
        }

        return map;
    }
}
