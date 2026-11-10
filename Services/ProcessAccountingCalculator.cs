using System;
using System.Collections.Generic;
using System.Globalization;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>Turns raw kernel counters into readable rows, rates, timestamps and explanations.</summary>
public static class ProcessAccountingCalculator
{
    private const double MinIntervalSeconds = 0.2;

    private const double TicksPerSecond = ProcessAccountingService.TicksPerSecond;

    public static double? IntervalSeconds(ProcessAccountingSnapshot? previous, ProcessAccountingSnapshot current)
    {
        if (previous is null)
            return null;

        double seconds = (current.TimestampMs - previous.TimestampMs) / 1000.0;
        return seconds >= MinIntervalSeconds ? seconds : null;
    }

    public static ProcessAccountingRowModel BuildRow(
        ProcessAccountingSample sample,
        ProcessAccountingSample? previous,
        double? seconds,
        DateTimeOffset bootTime,
        UserDirectory users)
    {
        double cpuSeconds = sample.TotalTicks / TicksPerSecond;

        var row = new ProcessAccountingRowModel
        {
            Pid = sample.Pid,
            Name = sample.Name,
            User = users.UserName(sample.Uid),
            CpuTotalSeconds = cpuSeconds,
            CpuTotalText = FormatCpuTime(cpuSeconds),
            IsKernelThread = sample.IsKernelThread,
            StartTime = bootTime + TimeSpan.FromSeconds(sample.StartTicks / TicksPerSecond),
            ResidentBytes = sample.ResidentBytes ?? 0,
            MemoryText = sample.ResidentBytes is long rss ? ByteFormatter.Format(rss) : "-",
            IsIoRestricted = !sample.IoReadable,
            IoText = sample.IoReadable
                ? $"{ByteFormatter.Format(sample.StorageReadBytes)} / {ByteFormatter.Format(sample.StorageWriteBytes)}"
                : "restricted"
        };

        // CPU share = ticks used during the interval / ticks that passed during the interval.
        // A new process (no earlier sample) or a process whose counter went down has no rate yet.
        if (previous is not null && seconds is not null && sample.TotalTicks >= previous.TotalTicks &&
            previous.StartTicks == sample.StartTicks)
        {
            double percent = (sample.TotalTicks - previous.TotalTicks) / (seconds.Value * TicksPerSecond) * 100.0;

            row.CpuPercent = percent;
            row.CpuPercentText = $"{percent:F1}%";
        }

        return row;
    }

    /// <summary>The detail rows for one process: only real values, and an explicit note where the kernel gives none.</summary>
    public static List<DetailRow> BuildDetails(
        ProcessAccountingSample sample,
        ProcessAccountingSnapshot snapshot,
        DateTimeOffset bootTime,
        UserDirectory users)
    {
        var rows = new List<DetailRow>();

        void Add(string label, string value) => rows.Add(new DetailRow { Label = label, Value = value });

        string parent = snapshot.Processes.TryGetValue(sample.ParentPid, out ProcessAccountingSample? p)
            ? $"{sample.ParentPid} ({p.Name})"
            : sample.ParentPid.ToString(CultureInfo.InvariantCulture);

        DateTimeOffset started = bootTime + TimeSpan.FromSeconds(sample.StartTicks / TicksPerSecond);

        Add("Process", $"{sample.Name}   (PID {sample.Pid}, parent {parent})");
        Add("Owner", users.UserName(sample.Uid) + (sample.IsKernelThread ? "   - a kernel thread, part of the kernel itself" : string.Empty));
        Add("State", DescribeState(sample.State));
        Add("Started", $"{started:ddd dd MMM yyyy, HH:mm:ss}");
        Add("Running for", FormatElapsed(snapshot.TakenAt - started));

        Add("CPU time in user mode", FormatCpuTime(sample.UserTicks / TicksPerSecond) + "   (running the program's own code)");
        Add("CPU time in kernel mode", FormatCpuTime(sample.SystemTicks / TicksPerSecond) + "   (the kernel working for it: system calls, I/O)");
        Add("CPU time of finished children",
            FormatCpuTime((sample.ChildUserTicks + sample.ChildSystemTicks) / TicksPerSecond) +
            "   (used by child processes it has waited for)");

        Add("Threads", sample.Threads.ToString(CultureInfo.InvariantCulture));

        Add("Memory now (resident)", sample.ResidentBytes is long rss ? ByteFormatter.Format(rss) : "not reported (the kernel gives no user memory for this process)");
        Add("Memory at its peak", sample.PeakResidentBytes is long peak ? ByteFormatter.Format(peak) : "not reported");

        Add("Page faults", $"{sample.MinorFaults:N0} minor (served from memory)   |   {sample.MajorFaults:N0} major (needed a read from disk)");

        Add("Context switches",
            sample.VoluntarySwitches is long voluntary && sample.InvoluntarySwitches is long involuntary
                ? $"{voluntary:N0} voluntary (it waited for I/O or a lock)   |   {involuntary:N0} involuntary (the scheduler took the CPU away)"
                : "not reported");

        if (sample.IoReadable)
        {
            Add("Data read / written through system calls",
                $"{ByteFormatter.Format(sample.ReadCharBytes)} / {ByteFormatter.Format(sample.WriteCharBytes)}   (includes cached data and pipes)");
            Add("Data read from / written to storage",
                $"{ByteFormatter.Format(sample.StorageReadBytes)} / {ByteFormatter.Format(sample.StorageWriteBytes)}   (what really reached the disk layer)");
        }
        else
        {
            Add("I/O counters", "restricted: the kernel only shows these to the process's owner or root. They are not zero, just not available to you.");
        }

        return rows;
    }

    public static string DescribeState(string state) => state switch
    {
        "R" => "R - running, or ready to run on a CPU",
        "S" => "S - sleeping, waiting for an event",
        "D" => "D - uninterruptible sleep, usually waiting for the disk",
        "Z" => "Z - zombie: finished, waiting for its parent to collect the result",
        "T" => "T - stopped",
        "t" => "t - stopped by a debugger",
        "I" => "I - idle kernel thread",
        "X" => "X - dead",
        _ => state
    };

    // 0.42 s, 1m 23s, 2h 14m, 3d 4h
    public static string FormatCpuTime(double seconds)
    {
        if (seconds < 60)
            return seconds.ToString("F2", CultureInfo.InvariantCulture) + " s";

        return FormatElapsed(TimeSpan.FromSeconds(seconds));
    }

    public static string FormatElapsed(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalMinutes < 1)
            return $"{span.Seconds} s";

        if (span.TotalHours < 1)
            return $"{span.Minutes}m {span.Seconds:D2}s";

        if (span.TotalDays < 1)
            return $"{span.Hours}h {span.Minutes:D2}m";

        return $"{(int)span.TotalDays}d {span.Hours}h";
    }
}
