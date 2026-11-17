using System;
namespace OS_Concepts_Demonstrator.Models;

public class ProcessInfoModel
{
    public int ProcessId { get; set; }

    public string Name { get; set; } = string.Empty;

    public double MemoryUsageMb { get; set; }

    public DateTime? StartTime { get; set; }

    // Linux process state letter from /proc/[pid]/stat (for example R, S, T, Z).
    public string StatusCode { get; set; } = "?";

    // Human-readable state text derived from StatusCode.
    public string StatusText { get; set; } = "Unknown";

    // starttime field from /proc/[pid]/stat, used to reduce PID reuse risks.
    public long? StartTicks { get; set; }

    // Unix nice value (-20 highest priority, 19 lowest priority).
    public int? NiceValue { get; set; }
}
