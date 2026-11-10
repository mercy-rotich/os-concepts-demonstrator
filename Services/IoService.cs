using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Read-only access to I/O statistics exposed by the kernel in /proc and /sys.
/// Missing or unreadable sources never throw: they are reported through Warnings or empty results.
/// </summary>
public class IoService
{
    private const string DiskStatsPath = "/proc/diskstats";
    private const string NetDevPath = "/proc/net/dev";
    private const string StatPath = "/proc/stat";
    private const string InterruptsPath = "/proc/interrupts";
    private const string InputDevicesPath = "/proc/bus/input/devices";
    private const string DevicesPath = "/proc/devices";

    // Block devices that are not physical disks.
    private static readonly string[] NonPhysicalPrefixes = { "loop", "ram", "zram", "nbd", "fd", "dm-", "md", "sr" };

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly BlockDeviceResolver _resolver = new();
    private readonly Dictionary<string, DiskDetailsModel> _diskDetails = new();
    private readonly Dictionary<string, string> _networkKinds = new();

    private HashSet<string>? _physicalDisks;

    /// <summary>Forget cached device facts (called each time the page opens).</summary>
    public void ResetCache()
    {
        _physicalDisks = null;
        _diskDetails.Clear();
        _networkKinds.Clear();
    }

    public IoSnapshot ReadSnapshot()
    {
        var snapshot = new IoSnapshot { TimestampMs = _clock.ElapsedMilliseconds };

        ReadDiskStats(snapshot);
        ReadNetworkStats(snapshot);
        ReadSystemCounters(snapshot);

        return snapshot;
    }

    // ---------- Disks ----------

    private void ReadDiskStats(IoSnapshot snapshot)
    {
        HashSet<string> physical = GetPhysicalDisks();

        try
        {
            if (!File.Exists(DiskStatsPath))
            {
                snapshot.Warnings.Add("Disk statistics are unavailable (/proc/diskstats not found).");
                return;
            }

            foreach (string line in File.ReadLines(DiskStatsPath))
            {
                // major minor name  reads reads_merged sectors_read ms_reading  writes writes_merged sectors_written ms_writing  in_flight ms_io ms_weighted ...
                string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (f.Length < 14)
                    continue;

                string name = f[2];

                // If /sys told us which disks are physical, show only those (hides partitions, loop devices...).
                bool show = physical.Count > 0
                    ? physical.Contains(name)
                    : !NonPhysicalPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));

                if (!show)
                    continue;

                if (!TryParse(f[3], out long reads) || !TryParse(f[5], out long sectorsRead) ||
                    !TryParse(f[6], out long readMs) || !TryParse(f[7], out long writes) ||
                    !TryParse(f[9], out long sectorsWritten) || !TryParse(f[10], out long writeMs) ||
                    !TryParse(f[11], out long inFlight) || !TryParse(f[12], out long ioMs))
                {
                    continue;
                }

                snapshot.Disks[name] = new DiskCounters
                {
                    Name = name,
                    ReadsCompleted = reads,
                    SectorsRead = sectorsRead,
                    ReadTimeMs = readMs,
                    WritesCompleted = writes,
                    SectorsWritten = sectorsWritten,
                    WriteTimeMs = writeMs,
                    InFlight = inFlight,
                    IoTimeMs = ioMs
                };
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            snapshot.Warnings.Add($"Could not read disk statistics: {ex.Message}");
        }
    }

    private HashSet<string> GetPhysicalDisks()
    {
        if (_physicalDisks is not null)
            return _physicalDisks;

        var result = new HashSet<string>();

        try
        {
            foreach (string path in Directory.GetDirectories("/sys/block"))
            {
                string name = Path.GetFileName(path);

                if (NonPhysicalPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                    continue;

                // Skip empty slots (card readers with no card).
                if (BlockDeviceResolver.ReadLong(Path.Combine(path, "size")) > 0)
                    result.Add(name);
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Leave empty: the caller falls back to name-based filtering.
        }

        _physicalDisks = result;
        return result;
    }

    public DiskDetailsModel GetDiskDetails(string diskName)
    {
        if (_diskDetails.TryGetValue(diskName, out DiskDetailsModel? cached))
            return cached;

        var details = new DiskDetailsModel();
        BlockDeviceInfo? info = _resolver.Resolve("/dev/" + diskName);

        if (info is not null)
        {
            details.Model = info.DiskModel;
            details.SizeBytes = info.DiskSizeBytes;
            details.TypeText = info.DiskTypeText;
        }

        details.Scheduler = ParseActiveScheduler(
            BlockDeviceResolver.ReadText($"/sys/block/{diskName}/queue/scheduler"));

        _diskDetails[diskName] = details;
        return details;
    }

    // The file looks like "none [mq-deadline] kyber"; the active scheduler is in brackets.
    private static string? ParseActiveScheduler(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        int start = text.IndexOf('[');
        int end = text.IndexOf(']');

        return start >= 0 && end > start ? text.Substring(start + 1, end - start - 1) : text.Trim();
    }

    // ---------- Network ----------

    private void ReadNetworkStats(IoSnapshot snapshot)
    {
        try
        {
            if (!File.Exists(NetDevPath))
            {
                snapshot.Warnings.Add("Network statistics are unavailable (/proc/net/dev not found).");
                return;
            }

            // The first two lines are column headings.
            foreach (string line in File.ReadLines(NetDevPath).Skip(2))
            {
                int colon = line.IndexOf(':');

                if (colon < 0)
                    continue;

                string name = line.Substring(0, colon).Trim();
                string[] f = line.Substring(colon + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // 8 receive columns then 8 transmit columns.
                if (f.Length < 16 ||
                    !TryParse(f[0], out long rxBytes) || !TryParse(f[1], out long rxPackets) ||
                    !TryParse(f[2], out long rxErrors) || !TryParse(f[3], out long rxDropped) ||
                    !TryParse(f[8], out long txBytes) || !TryParse(f[9], out long txPackets) ||
                    !TryParse(f[10], out long txErrors) || !TryParse(f[11], out long txDropped))
                {
                    continue;
                }

                snapshot.Interfaces[name] = new NetworkCounters
                {
                    Name = name,
                    RxBytes = rxBytes,
                    RxPackets = rxPackets,
                    RxErrors = rxErrors,
                    RxDropped = rxDropped,
                    TxBytes = txBytes,
                    TxPackets = txPackets,
                    TxErrors = txErrors,
                    TxDropped = txDropped,
                    State = BlockDeviceResolver.ReadText($"/sys/class/net/{name}/operstate") ?? "unknown",
                    Kind = GetNetworkKind(name)
                };
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            snapshot.Warnings.Add($"Could not read network statistics: {ex.Message}");
        }
    }

    private string GetNetworkKind(string name)
    {
        if (_networkKinds.TryGetValue(name, out string? cached))
            return cached;

        string kind = "Wired";

        try
        {
            string path = $"/sys/class/net/{name}";

            if (name == "lo")
            {
                kind = "Loopback";
            }
            else if (Directory.Exists(Path.Combine(path, "wireless")) || Directory.Exists(Path.Combine(path, "phy80211")))
            {
                kind = "Wireless";
            }
            else
            {
                // Interfaces with no hardware behind them (bridges, containers, VPNs) live under /virtual/.
                string real = new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName ?? path;

                if (real.Contains("/virtual/", StringComparison.Ordinal))
                    kind = "Virtual";
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            kind = "Network";
        }

        _networkKinds[name] = kind;
        return kind;
    }

    // ---------- Interrupts and context switches ----------

    private void ReadSystemCounters(IoSnapshot snapshot)
    {
        try
        {
            if (!File.Exists(StatPath))
            {
                snapshot.Warnings.Add("Interrupt statistics are unavailable (/proc/stat not found).");
                return;
            }

            foreach (string line in File.ReadLines(StatPath))
            {
                string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (f.Length < 2)
                    continue;

                // "intr <total> <per-interrupt counts...>" and "ctxt <total>".
                if (f[0] == "intr" && TryParse(f[1], out long interrupts))
                    snapshot.TotalInterrupts = interrupts;
                else if (f[0] == "ctxt" && TryParse(f[1], out long switches))
                    snapshot.ContextSwitches = switches;
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            snapshot.Warnings.Add($"Could not read interrupt statistics: {ex.Message}");
        }
    }

    /// <summary>The interrupt sources that have fired most since boot.</summary>
    public List<InterruptSourceModel> ReadTopInterrupts(int count)
    {
        var sources = new List<InterruptSourceModel>();

        try
        {
            if (!File.Exists(InterruptsPath))
                return sources;

            string[] lines = File.ReadAllLines(InterruptsPath);

            if (lines.Length == 0)
                return sources;

            // The header names one column per CPU ("CPU0 CPU1 ...").
            int cpuCount = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            foreach (string line in lines.Skip(1))
            {
                string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (f.Length < cpuCount + 1 || !f[0].EndsWith(':'))
                    continue;

                long total = 0;
                bool valid = true;

                for (int i = 1; i <= cpuCount; i++)
                {
                    if (!TryParse(f[i], out long perCpu))
                    {
                        valid = false;
                        break;
                    }

                    total += perCpu;
                }

                if (!valid || total == 0)
                    continue;

                string[] description = f.Skip(cpuCount + 1).ToArray();
                bool isNumbered = int.TryParse(f[0].TrimEnd(':'), NumberStyles.None, CultureInfo.InvariantCulture, out _);

                string name;
                string detail;

                if (!isNumbered)
                {
                    // CPU-internal interrupts (LOC, CAL, TLB...) have a description but no device.
                    name = description.Length > 0 ? string.Join(' ', description) : f[0].TrimEnd(':');
                    detail = "CPU-internal interrupt";
                }
                else if (description.Length >= 3)
                {
                    // "<controller> <number-trigger> <device names...>"
                    name = string.Join(' ', description.Skip(2));
                    detail = $"{description[0]} {description[1]}";
                }
                else
                {
                    name = description.Length > 0 ? string.Join(' ', description) : "(unnamed)";
                    detail = string.Empty;
                }

                sources.Add(new InterruptSourceModel
                {
                    Irq = f[0].TrimEnd(':'),
                    Name = name,
                    Detail = detail,
                    Count = total
                });
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return new List<InterruptSourceModel>();
        }

        return sources.OrderByDescending(s => s.Count).Take(count).ToList();
    }

    // ---------- Input devices and drivers ----------

    public List<InputDeviceModel> ReadInputDevices()
    {
        var devices = new List<InputDeviceModel>();

        try
        {
            if (!File.Exists(InputDevicesPath))
                return devices;

            string name = string.Empty;
            string handlers = string.Empty;
            string bus = string.Empty;

            // Records are separated by blank lines.
            foreach (string line in File.ReadLines(InputDevicesPath).Append(string.Empty))
            {
                if (line.Length == 0)
                {
                    if (name.Length > 0)
                        devices.Add(BuildInputDevice(name, handlers, bus));

                    name = handlers = bus = string.Empty;
                }
                else if (line.StartsWith("N: Name=", StringComparison.Ordinal))
                {
                    name = line.Substring("N: Name=".Length).Trim('"');
                }
                else if (line.StartsWith("H: Handlers=", StringComparison.Ordinal))
                {
                    handlers = line.Substring("H: Handlers=".Length).Trim();
                }
                else if (line.StartsWith("I: Bus=", StringComparison.Ordinal))
                {
                    bus = line.Substring("I: Bus=".Length).Split(' ')[0];
                }
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return new List<InputDeviceModel>();
        }

        return devices;
    }

    private static InputDeviceModel BuildInputDevice(string name, string handlers, string bus)
    {
        string category;
        string lower = name.ToLowerInvariant();

        // The kernel gives many non-keyboard devices a "kbd" handler (power buttons, hotkeys,
        // the PC speaker), so the name is checked first. Real keyboards also have an "leds" handler
        // (Caps Lock and Num Lock lights), which is what finally identifies them.
        if (lower.StartsWith("hda ", StringComparison.Ordinal))
            category = "Audio jack / HDMI sensor";
        else if (lower.Contains("speaker") || lower.Contains("beep"))
            category = "Speaker / beeper";
        else if (lower.Contains("button") || lower.Contains("switch"))
            category = "Button / switch";
        else if (lower.Contains("video bus") || lower.Contains("hotkeys") || lower.Contains("wmi"))
            category = "Hotkeys / system keys";
        else if (lower.Contains("touchpad"))
            category = "Touchpad";
        else if (handlers.Contains("mouse", StringComparison.Ordinal))
            category = "Mouse / pointer";
        else if (handlers.Contains("leds", StringComparison.Ordinal))
            category = "Keyboard";
        else if (handlers.Contains("js", StringComparison.Ordinal))
            category = "Game controller";
        else if (handlers.Contains("kbd", StringComparison.Ordinal))
            category = "Hotkeys / system keys";
        else
            category = "Other input device";

        bool isPrimary = category is "Keyboard" or "Mouse / pointer" or "Touchpad" or "Game controller";

        // Bus numbers are defined in the kernel's input.h.
        string connection = bus switch
        {
            "0003" => "USB",
            "0005" => "Bluetooth",
            "0011" => "PS/2 (i8042)",
            "0018" => "I2C",
            "0019" => "Internal / virtual",
            _ => "Other bus"
        };

        return new InputDeviceModel
        {
            Name = name,
            Category = category,
            Connection = connection,
            IsPrimary = isPrimary,
            Handlers = handlers.Length > 0 ? handlers : "none"
        };
    }

    /// <summary>Registered drivers from /proc/devices: character devices first, then block devices.</summary>
    public (List<RegisteredDriverModel> Character, List<RegisteredDriverModel> Block) ReadDrivers()
    {
        var character = new List<RegisteredDriverModel>();
        var block = new List<RegisteredDriverModel>();

        try
        {
            if (!File.Exists(DevicesPath))
                return (character, block);

            List<RegisteredDriverModel>? current = null;

            foreach (string line in File.ReadLines(DevicesPath))
            {
                if (line.StartsWith("Character devices", StringComparison.Ordinal))
                {
                    current = character;
                }
                else if (line.StartsWith("Block devices", StringComparison.Ordinal))
                {
                    current = block;
                }
                else if (current is not null)
                {
                    string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (f.Length == 2 && int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
                        current.Add(new RegisteredDriverModel { Major = major, Name = f[1] });
                }
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return (new List<RegisteredDriverModel>(), new List<RegisteredDriverModel>());
        }

        return (character, block);
    }

    private static bool TryParse(string text, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool IsExpected(Exception ex) =>
        ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException;
}
