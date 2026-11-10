using System;
using System.Collections.Generic;

namespace OS_Concepts_Demonstrator.Models;

/// <summary>The cumulative counters the kernel keeps for one process (all values are "since the process started").</summary>
public class ProcessAccountingSample
{
    public int Pid { get; set; }

    public int ParentPid { get; set; }

    public string Name { get; set; } = string.Empty;

    // R running, S sleeping, D disk sleep, Z zombie, T stopped, I idle...
    public string State { get; set; } = "?";

    public uint Uid { get; set; }

    public bool IsKernelThread { get; set; }

    // Time the process started, in clock ticks after the system booted.
    public long StartTicks { get; set; }

    public long UserTicks { get; set; }

    public long SystemTicks { get; set; }

    public long ChildUserTicks { get; set; }

    public long ChildSystemTicks { get; set; }

    public int Threads { get; set; }

    public long MinorFaults { get; set; }

    public long MajorFaults { get; set; }

    // Null when the kernel does not report it (kernel threads have no user memory).
    public long? ResidentBytes { get; set; }

    public long? PeakResidentBytes { get; set; }

    public long? VoluntarySwitches { get; set; }

    public long? InvoluntarySwitches { get; set; }

    // I/O counters: only readable for processes of the same user (or root).
    public bool IoReadable { get; set; }

    public long ReadCharBytes { get; set; }

    public long WriteCharBytes { get; set; }

    public long StorageReadBytes { get; set; }

    public long StorageWriteBytes { get; set; }

    public long TotalTicks => UserTicks + SystemTicks;
}

public class ProcessAccountingSnapshot
{
    // Milliseconds on a monotonic stopwatch; used to measure the real time between two snapshots.
    public long TimestampMs { get; set; }

    public DateTimeOffset TakenAt { get; set; }

    public Dictionary<int, ProcessAccountingSample> Processes { get; } = new();

    public int IoReadableCount { get; set; }

    public int IoRestrictedCount { get; set; }

    public int KernelThreadCount { get; set; }

    public List<string> Warnings { get; } = new();
}
