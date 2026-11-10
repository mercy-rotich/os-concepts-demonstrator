using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Reads the kernel's live per-process accounting counters from /proc (read-only).
/// A process can disappear while it is being read; that is normal and the process is simply skipped.
/// </summary>
public class ProcessAccountingService
{
    // The kernel's "kernel thread" marker in the process flags (PF_KTHREAD).
    private const long KernelThreadFlag = 0x00200000;

    // /proc reports CPU times in "user ticks" of 1/100 second (USER_HZ) on Linux.
    public const int TicksPerSecond = 100;

    private static readonly string[] AccountingFileLocations =
    {
        "/var/log/account/pacct", "/var/account/pacct", "/var/log/pacct", "/var/adm/pacct"
    };

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public UserDirectory Users { get; } = new();

    public DateTimeOffset BootTime { get; } = ReadBootTime();

    public ProcessAccountingSnapshot ReadSnapshot()
    {
        var snapshot = new ProcessAccountingSnapshot
        {
            TimestampMs = _clock.ElapsedMilliseconds,
            TakenAt = DateTimeOffset.Now
        };

        try
        {
            foreach (string directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
                    continue;

                ProcessAccountingSample? sample = ReadProcess(pid, directory);

                if (sample is null)
                    continue;

                snapshot.Processes[pid] = sample;

                if (sample.IsKernelThread)
                    snapshot.KernelThreadCount++;

                if (sample.IoReadable)
                    snapshot.IoReadableCount++;
                else
                    snapshot.IoRestrictedCount++;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            snapshot.Warnings.Add($"Could not list processes: {ex.Message}");
        }

        return snapshot;
    }

    private static ProcessAccountingSample? ReadProcess(int pid, string directory)
    {
        string statText;

        try
        {
            statText = File.ReadAllText(Path.Combine(directory, "stat"));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null; // the process ended, or cannot be inspected at all
        }

        // The command name is in parentheses and may itself contain spaces or parentheses,
        // so everything is located relative to the LAST closing parenthesis.
        int open = statText.IndexOf('(');
        int close = statText.LastIndexOf(')');

        if (open < 0 || close < open || close + 2 >= statText.Length)
            return null;

        string[] f = statText.Substring(close + 2).Split(' ');

        // Field numbers below are the kernel's (see "man 5 proc"), minus 3 because
        // fields 1 and 2 (pid and name) are handled separately.
        if (f.Length < 22)
            return null;

        long Number(int index) =>
            long.TryParse(f[index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ? value : 0;

        var sample = new ProcessAccountingSample
        {
            Pid = pid,
            Name = statText.Substring(open + 1, close - open - 1),
            State = f[0],
            ParentPid = (int)Number(1),
            IsKernelThread = (Number(6) & KernelThreadFlag) != 0,
            MinorFaults = Number(7),
            MajorFaults = Number(9),
            UserTicks = Number(11),
            SystemTicks = Number(12),
            ChildUserTicks = Number(13),
            ChildSystemTicks = Number(14),
            Threads = (int)Number(17),
            StartTicks = Number(19)
        };

        ReadStatus(sample, directory);
        ReadIo(sample, directory);

        return sample;
    }

    private static void ReadStatus(ProcessAccountingSample sample, string directory)
    {
        try
        {
            foreach (string line in File.ReadLines(Path.Combine(directory, "status")))
            {
                int colon = line.IndexOf(':');

                if (colon <= 0)
                    continue;

                string key = line.Substring(0, colon);
                string[] value = line.Substring(colon + 1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                if (value.Length == 0)
                    continue;

                switch (key)
                {
                    case "Uid":
                        // "Uid: real effective saved filesystem" - the first is the owner.
                        if (uint.TryParse(value[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint uid))
                            sample.Uid = uid;
                        break;
                    case "VmRSS":
                        sample.ResidentBytes = KilobytesToBytes(value[0]);
                        break;
                    case "VmHWM":
                        sample.PeakResidentBytes = KilobytesToBytes(value[0]);
                        break;
                    case "voluntary_ctxt_switches":
                        sample.VoluntarySwitches = ParseCount(value[0]);
                        break;
                    case "nonvoluntary_ctxt_switches":
                        sample.InvoluntarySwitches = ParseCount(value[0]);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Left as "not reported".
        }
    }

    private static void ReadIo(ProcessAccountingSample sample, string directory)
    {
        try
        {
            foreach (string line in File.ReadLines(Path.Combine(directory, "io")))
            {
                int colon = line.IndexOf(':');

                if (colon <= 0 || !long.TryParse(line.AsSpan(colon + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long value))
                    continue;

                switch (line.Substring(0, colon))
                {
                    case "rchar": sample.ReadCharBytes = value; break;
                    case "wchar": sample.WriteCharBytes = value; break;
                    case "read_bytes": sample.StorageReadBytes = value; break;
                    case "write_bytes": sample.StorageWriteBytes = value; break;
                }
            }

            sample.IoReadable = true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Permission denied is the normal answer for other users' processes.
            sample.IoReadable = false;
        }
    }

    private static long? KilobytesToBytes(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long kb) ? kb * 1024 : null;

    private static long? ParseCount(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : null;

    private static DateTimeOffset ReadBootTime()
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/stat"))
            {
                if (line.StartsWith("btime ", StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan(6).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime();
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Fall through to the approximation below.
        }

        // Without the boot time, start times cannot be shown exactly; use "now minus uptime" if possible.
        return DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    /// <summary>Checks what this system really provides and states it in plain words.</summary>
    public List<DataSourceStatusModel> DescribeSources(ProcessAccountingSnapshot snapshot)
    {
        int total = snapshot.Processes.Count;
        var sources = new List<DataSourceStatusModel>
        {
            new()
            {
                Name = "CPU time, start time, state, page faults, threads",
                Provides = "/proc/<pid>/stat",
                Availability = total > 0 ? DataAvailability.Available : DataAvailability.Unavailable,
                Detail = total > 0
                    ? $"Read for all {total} running processes (including {snapshot.KernelThreadCount} kernel threads)."
                    : "No process could be read."
            },
            new()
            {
                Name = "Memory and context switches",
                Provides = "/proc/<pid>/status",
                Availability = total > 0 ? DataAvailability.Available : DataAvailability.Unavailable,
                Detail = "Resident memory, peak memory, voluntary and involuntary context switches and the owner. " +
                         "Kernel threads have no user memory, so those fields are shown as not reported."
            },
            DescribeIoSource(snapshot),
            DescribeAccountingFile()
        };

        return sources;
    }

    private static DataSourceStatusModel DescribeIoSource(ProcessAccountingSnapshot snapshot)
    {
        int total = snapshot.Processes.Count;
        int readable = snapshot.IoReadableCount;

        DataAvailability availability = readable == 0 ? DataAvailability.Unavailable
            : readable == total ? DataAvailability.Available
            : DataAvailability.Partial;

        return new DataSourceStatusModel
        {
            Name = "Bytes read and written by each process",
            Provides = "/proc/<pid>/io",
            Availability = availability,
            Detail = readable == total
                ? $"Readable for all {total} processes."
                : $"Readable for {readable} of {total} processes (the ones you own). The kernel hides the I/O counters of " +
                  $"the other {snapshot.IoRestrictedCount} from non-root users, so those rows say 'restricted' and are never counted as zero."
        };
    }

    private static DataSourceStatusModel DescribeAccountingFile()
    {
        string? existing = AccountingFileLocations.FirstOrDefault(File.Exists);

        var source = new DataSourceStatusModel
        {
            Name = "Records of processes that have already exited (BSD process accounting)",
            Provides = "pacct file, read by lastcomm and sa"
        };

        if (existing is null)
        {
            source.Availability = DataAvailability.Unavailable;
            source.Detail = "No accounting file was found (looked in /var/log/account, /var/account and /var/log). " +
                            "This facility is off unless an administrator installs the 'acct' package and runs 'accton'. " +
                            "Without it Linux forgets a process's usage the moment it exits, so only running processes can be shown here.";
            return source;
        }

        try
        {
            using FileStream stream = File.Open(existing, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            source.Availability = DataAvailability.Partial;
            source.Detail = $"An accounting file exists ({existing}) and is readable, but this application does not decode its binary records. Use the 'lastcomm' command to read it.";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            source.Availability = DataAvailability.Unavailable;
            source.Detail = $"An accounting file exists ({existing}) but your account is not allowed to read it.";
        }

        return source;
    }
}
