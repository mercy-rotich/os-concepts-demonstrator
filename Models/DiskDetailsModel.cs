using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.Models;

/// <summary>Slowly-changing facts about a disk (read from /sys).</summary>
public class DiskDetailsModel
{
    public string? Model { get; set; }

    public long SizeBytes { get; set; }

    public string TypeText { get; set; } = "type unknown";

    // The active I/O scheduler, e.g. "none" or "mq-deadline"; null if unreadable.
    public string? Scheduler { get; set; }

    public string SchedulerText => Scheduler ?? "unknown";
}
