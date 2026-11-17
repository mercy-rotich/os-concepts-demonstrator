using System;

namespace OS_Concepts_Demonstrator.Models;

// One line in the simulator's allocation log.
public class AllocationHistoryEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // "ALLOCATE", "FREE", "RELEASE ALL" or "FAILED".
    public string Action { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public bool IsFailure { get; set; }

    public string TimeText => Timestamp.ToString("HH:mm:ss");

    public string ActionColor => IsFailure ? "#F87171" : Action switch
    {
        "ALLOCATE" => "#86EFAC",
        "FREE" => "#38BDF8",
        _ => "#FBBF24"
    };
}
