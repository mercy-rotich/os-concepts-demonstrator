using System.Collections.Generic;

namespace OS_Concepts_Demonstrator.Models;

/// <summary>Cumulative counters for one disk, straight from /proc/diskstats (all "since boot").</summary>
public class DiskCounters
{
    public string Name { get; set; } = string.Empty;

    public long ReadsCompleted { get; set; }

    public long SectorsRead { get; set; }      // always 512-byte sectors

    public long ReadTimeMs { get; set; }

    public long WritesCompleted { get; set; }

    public long SectorsWritten { get; set; }

    public long WriteTimeMs { get; set; }

    public long InFlight { get; set; }         // requests being served right now

    public long IoTimeMs { get; set; }         // ms the device had at least one request pending
}

/// <summary>Cumulative counters for one network interface, from /proc/net/dev.</summary>
public class NetworkCounters
{
    public string Name { get; set; } = string.Empty;

    public long RxBytes { get; set; }

    public long RxPackets { get; set; }

    public long RxErrors { get; set; }

    public long RxDropped { get; set; }

    public long TxBytes { get; set; }

    public long TxPackets { get; set; }

    public long TxErrors { get; set; }

    public long TxDropped { get; set; }

    // Filled from /sys/class/net: "up", "down", ... and Wired / Wireless / Loopback / Virtual.
    public string State { get; set; } = "unknown";

    public string Kind { get; set; } = "Network";
}

/// <summary>Everything read from the kernel at one instant.</summary>
public class IoSnapshot
{
    // Milliseconds from a monotonic stopwatch, used to compute the real time between readings.
    public long TimestampMs { get; set; }

    public Dictionary<string, DiskCounters> Disks { get; } = new();

    public Dictionary<string, NetworkCounters> Interfaces { get; } = new();

    public long? TotalInterrupts { get; set; }

    public long? ContextSwitches { get; set; }

    // Human-readable notes about data that could not be read.
    public List<string> Warnings { get; } = new();
}
