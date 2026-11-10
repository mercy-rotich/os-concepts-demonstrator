using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

public enum ServiceScope
{
    // Services of the whole machine.
    System,

    // Services belonging to the logged-in user's own session.
    User
}

/// <summary>
/// Read-only view of systemd. Only these queries are ever run: list-units, list-unit-files,
/// show, is-system-running and journalctl reading. Nothing here can start, stop, enable or change a service.
/// </summary>
public class SystemdService
{
    private static readonly string[] SystemctlLocations = { "/usr/bin/systemctl", "/bin/systemctl" };
    private static readonly string[] JournalctlLocations = { "/usr/bin/journalctl", "/bin/journalctl" };

    // Only plain unit names may reach a command line: letters, digits and a few punctuation marks,
    // and never starting with "-" so a name can not look like a command-line option.
    private static readonly Regex ServiceNamePattern =
        new(@"^[A-Za-z0-9:_.\\@][A-Za-z0-9:_.\\@-]*\.service$", RegexOptions.Compiled);

    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DetailTimeout = TimeSpan.FromSeconds(5);

    private const int MaxDependencyNames = 10;
    private const int MaxLogLines = 15;
    private const int MaxLogLineLength = 220;

    private static string? Find(string[] locations) => locations.FirstOrDefault(File.Exists);

    public async Task<ServiceManagerInfo> GetManagerInfoAsync()
    {
        var info = new ServiceManagerInfo
        {
            InitName = ReadFirstLine("/proc/1/comm") ?? "unknown",

            // The standard way programs test for systemd (what sd_booted() does).
            IsSystemd = Directory.Exists("/run/systemd/system")
        };

        string? systemctl = Find(SystemctlLocations);

        if (info.IsSystemd && systemctl is not null)
        {
            // Exits with a non-zero code when degraded; the text on stdout is what we want.
            CommandResult result = await CommandRunner.RunAsync(systemctl, new[] { "is-system-running" }, DetailTimeout);

            if (result.Started && !result.TimedOut)
                info.SystemState = result.StandardOutput.Trim().Length > 0 ? result.StandardOutput.Trim() : "unknown";
        }

        return info;
    }

    public async Task<(List<ServiceInfoModel> Services, string? Error)> GetServicesAsync(ServiceScope scope)
    {
        string? systemctl = Find(SystemctlLocations);

        if (systemctl is null)
            return (new List<ServiceInfoModel>(), "The 'systemctl' command was not found on this system.");

        string[] common = { "--type=service", "--no-pager", "--no-legend", "--plain", "--full" };

        CommandResult units = await CommandRunner.RunAsync(
            systemctl, Scoped(scope, new[] { "list-units", "--all" }.Concat(common)), ListTimeout);

        if (!units.Started || units.TimedOut || units.ExitCode != 0)
        {
            string reason = units.StandardError.Trim();

            if (scope == ServiceScope.User)
                reason = "Your user service manager could not be reached. " + reason;

            return (new List<ServiceInfoModel>(), $"Could not list services: {(reason.Length > 0 ? reason : "unknown error")}");
        }

        // Unit files add services that are installed but not currently loaded.
        CommandResult files = await CommandRunner.RunAsync(
            systemctl, Scoped(scope, new[] { "list-unit-files" }.Concat(common)), ListTimeout);

        Dictionary<string, string> fileStates = files.Started && !files.TimedOut && files.ExitCode == 0
            ? ParseUnitFiles(files.StandardOutput)
            : new Dictionary<string, string>();

        var services = new Dictionary<string, ServiceInfoModel>(StringComparer.Ordinal);

        foreach (ServiceInfoModel service in ParseUnits(units.StandardOutput))
        {
            // "not-found" entries are names other units mention that are not installed: noise.
            if (service.LoadState == "not-found")
                continue;

            service.UnitFileState = fileStates.TryGetValue(service.Name, out string? state) ? state : "n/a";
            services[service.Name] = service;
        }

        foreach ((string name, string state) in fileStates)
        {
            // Templates ("getty@.service") are blueprints, aliases are just alternative names.
            if (services.ContainsKey(name) || name.Contains("@.", StringComparison.Ordinal) || state == "alias")
                continue;

            services[name] = new ServiceInfoModel
            {
                Name = name,
                Description = "Installed but not currently loaded",
                LoadState = "not loaded",
                ActiveState = "inactive",
                SubState = "dead",
                UnitFileState = state
            };
        }

        List<ServiceInfoModel> result = services.Values
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return (result, null);
    }

    public async Task<ServiceDetailsModel> GetDetailsAsync(string unitName, ServiceScope scope)
    {
        var details = new ServiceDetailsModel();

        if (!ServiceNamePattern.IsMatch(unitName))
        {
            details.Error = "This service name contains unexpected characters, so it was not queried.";
            return details;
        }

        string? systemctl = Find(SystemctlLocations);

        if (systemctl is null)
        {
            details.Error = "The 'systemctl' command was not found.";
            return details;
        }

        string[] properties =
        {
            "Type", "MainPID", "MemoryCurrent", "TasksCurrent", "ActiveState", "ActiveEnterTimestamp",
            "InactiveEnterTimestamp", "Restart", "NRestarts", "Result", "User", "ExecStart",
            "FragmentPath", "Requires", "Wants", "After", "Before", "WantedBy"
        };

        // "--" ends the options, so the unit name can never be mistaken for one.
        CommandResult show = await CommandRunner.RunAsync(
            systemctl,
            Scoped(scope, new[] { "show", "--no-pager", "--property=" + string.Join(',', properties), "--", unitName }),
            DetailTimeout);

        if (!show.Started || show.TimedOut || show.ExitCode != 0)
        {
            details.Error = $"Could not read details: {show.StandardError.Trim()}";
            return details;
        }

        Dictionary<string, string> values = ParseKeyValues(show.StandardOutput);
        BuildDetailRows(details, values, scope);

        details.Logs = await GetRecentLogsAsync(unitName, scope);

        return details;
    }

    // ---------- Parsing ----------

    // Each line: UNIT LOAD ACTIVE SUB DESCRIPTION (the description may contain spaces).
    private static IEnumerable<ServiceInfoModel> ParseUnits(string text)
    {
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.TrimStart(' ', '*', '●').Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 4 || !parts[0].EndsWith(".service", StringComparison.Ordinal))
                continue;

            yield return new ServiceInfoModel
            {
                Name = parts[0],
                LoadState = parts[1],
                ActiveState = parts[2],
                SubState = parts[3],
                Description = parts.Length == 5 ? parts[4].Trim() : string.Empty
            };
        }
    }

    // Each line: UNIT-FILE STATE [PRESET]
    private static Dictionary<string, string> ParseUnitFiles(string text)
    {
        var states = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2 && parts[0].EndsWith(".service", StringComparison.Ordinal))
                states[parts[0]] = parts[1];
        }

        return states;
    }

    private static Dictionary<string, string> ParseKeyValues(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = line.IndexOf('=');

            if (equals > 0)
                values[line.Substring(0, equals)] = line.Substring(equals + 1).Trim();
        }

        return values;
    }

    private static void BuildDetailRows(ServiceDetailsModel details, Dictionary<string, string> v, ServiceScope scope)
    {
        string Get(string key) => v.TryGetValue(key, out string? value) ? value : string.Empty;

        void Add(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "n/a" && value != "[not set]")
                details.Rows.Add(new DetailRow { Label = label, Value = value });
        }

        Add("Type", DescribeType(Get("Type")));

        int.TryParse(Get("MainPID"), NumberStyles.None, CultureInfo.InvariantCulture, out int pid);
        Add("Main process ID", pid > 0 ? pid.ToString(CultureInfo.InvariantCulture) : "none (no process is running)");

        if (pid > 0)
            Add("Memory", DescribeMemory(Get("MemoryCurrent"), pid));

        if (ulong.TryParse(Get("TasksCurrent"), NumberStyles.None, CultureInfo.InvariantCulture, out ulong tasks) &&
            tasks != ulong.MaxValue)
        {
            Add("Threads / tasks", tasks.ToString(CultureInfo.InvariantCulture));
        }

        bool active = Get("ActiveState") == "active";
        Add(active ? "Active since" : "Inactive since", active ? Get("ActiveEnterTimestamp") : Get("InactiveEnterTimestamp"));

        // With no User= setting a system service runs as root, while a user service runs as you.
        string defaultUser = scope == ServiceScope.User ? "you (this is a user service)" : "root (the default)";
        Add("Run as user", string.IsNullOrWhiteSpace(Get("User")) ? defaultUser : Get("User"));
        Add("Last result", Get("Result"));
        Add("Restart policy", DescribeRestart(Get("Restart")));

        if (int.TryParse(Get("NRestarts"), NumberStyles.None, CultureInfo.InvariantCulture, out int restarts))
            Add("Automatic restarts", restarts.ToString(CultureInfo.InvariantCulture));

        Add("Command", ExtractCommand(Get("ExecStart")));
        Add("Unit file", Get("FragmentPath"));
        Add("Requires", ShortenList(Get("Requires")));
        Add("Wants", ShortenList(Get("Wants")));
        Add("Starts after", ShortenList(Get("After")));
        Add("Starts before", ShortenList(Get("Before")));
        Add("Wanted by", ShortenList(Get("WantedBy")));
    }

    private static string DescribeType(string type) => type switch
    {
        "simple" => "simple (the main process is the service)",
        "forking" => "forking (the program starts a background copy of itself)",
        "oneshot" => "oneshot (runs a task and finishes)",
        "notify" => "notify (tells systemd when it is ready)",
        "dbus" => "dbus (ready once it claims a bus name)",
        "idle" => "idle (waits until other jobs are done)",
        "exec" => "exec (ready once the program starts)",
        _ => type
    };

    private static string DescribeRestart(string restart) => restart switch
    {
        "no" => "no (not restarted automatically)",
        "on-failure" => "on-failure (restarted if it crashes)",
        "always" => "always (restarted whenever it stops)",
        "on-abnormal" => "on-abnormal (restarted after crash or timeout)",
        "on-success" => "on-success (restarted after a clean exit)",
        _ => restart
    };

    private static string DescribeMemory(string memoryCurrent, int pid)
    {
        // systemd only counts memory per service when accounting is switched on.
        if (ulong.TryParse(memoryCurrent, NumberStyles.None, CultureInfo.InvariantCulture, out ulong bytes) &&
            bytes != ulong.MaxValue)
        {
            return ByteFormatter.Format((long)bytes) + " (counted by systemd for the whole service)";
        }

        // Otherwise read the main process's resident memory straight from /proc.
        long? rss = ReadResidentMemoryBytes(pid);

        return rss is long value
            ? ByteFormatter.Format(value) + " (main process only; systemd is not counting this service)"
            : "not available";
    }

    private static long? ReadResidentMemoryBytes(int pid)
    {
        try
        {
            foreach (string line in File.ReadLines($"/proc/{pid}/status"))
            {
                if (!line.StartsWith("VmRSS:", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long kb))
                    return kb * 1024;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // The process ended or is not readable.
        }

        return null;
    }

    // ExecStart looks like: { path=/usr/sbin/cron ; argv[]=/usr/sbin/cron -f ; ignore_errors=no ; ... }
    private static string ExtractCommand(string execStart)
    {
        Match match = Regex.Match(execStart, @"argv\[\]=(?<cmd>.*?) ;");

        return match.Success ? match.Groups["cmd"].Value.Trim() : string.Empty;
    }

    private static string ShortenList(string spaceSeparated)
    {
        string[] names = spaceSeparated.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (names.Length <= MaxDependencyNames)
            return string.Join(", ", names);

        return string.Join(", ", names.Take(MaxDependencyNames)) + $"  (+{names.Length - MaxDependencyNames} more)";
    }

    // ---------- Logs ----------

    public async Task<string> GetRecentLogsAsync(string unitName, ServiceScope scope)
    {
        string? journalctl = Find(JournalctlLocations);

        if (journalctl is null)
            return "The 'journalctl' command was not found, so logs cannot be shown.";

        if (!ServiceNamePattern.IsMatch(unitName))
            return "Logs were not requested because the service name is unusual.";

        // System services are matched with -u, a user's own services with --user-unit.
        string unitOption = scope == ServiceScope.User ? "--user-unit=" + unitName : "--unit=" + unitName;

        CommandResult result = await CommandRunner.RunAsync(
            journalctl,
            new[] { "--no-pager", "-n", MaxLogLines.ToString(CultureInfo.InvariantCulture), "-o", "short-iso", unitOption },
            DetailTimeout);

        if (!result.Started)
            return $"Logs could not be read: {result.StandardError}";

        if (result.TimedOut)
            return "Reading the logs took too long and was stopped.";

        string[] lines = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith("-- ", StringComparison.Ordinal))
            .Select(l => l.Length > MaxLogLineLength ? l.Substring(0, MaxLogLineLength) + "..." : l)
            .ToArray();

        if (lines.Length > 0)
            return string.Join('\n', lines);

        // journalctl prints this hint when the user lacks permission to read other users' and system logs.
        bool restricted = (result.StandardOutput + result.StandardError)
            .Contains("not seeing messages from other users", StringComparison.OrdinalIgnoreCase);

        return restricted
            ? "Your account is not allowed to read the system journal, so no log lines can be shown. " +
              "Members of the 'adm' or 'systemd-journal' group can read it. This is a deliberate protection, not an error."
            : "No recent log entries for this service.";
    }

    // ---------- Helpers ----------

    private static IEnumerable<string> Scoped(ServiceScope scope, IEnumerable<string> arguments) =>
        scope == ServiceScope.User ? new[] { "--user" }.Concat(arguments) : arguments;

    private static string? ReadFirstLine(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadLines(path).FirstOrDefault()?.Trim() : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }
}
