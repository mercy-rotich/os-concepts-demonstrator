namespace OS_Concepts_Demonstrator.Models;

public enum DataAvailability
{
    // Fully readable on this system.
    Available,

    // Some of it is readable (for example only your own processes).
    Partial,

    // Not present, or the kernel / file permissions do not let this user read it.
    Unavailable
}

/// <summary>One place information could come from, and whether this system actually provides it.</summary>
public class DataSourceStatusModel
{
    public string Name { get; set; } = string.Empty;

    // What the source would tell us.
    public string Provides { get; set; } = string.Empty;

    public DataAvailability Availability { get; set; }

    // The reason, in plain words, based on what was checked.
    public string Detail { get; set; } = string.Empty;

    public bool IsAvailable => Availability == DataAvailability.Available;

    public bool IsPartial => Availability == DataAvailability.Partial;

    public bool IsUnavailable => Availability == DataAvailability.Unavailable;

    public string StatusText => Availability switch
    {
        DataAvailability.Available => "available",
        DataAvailability.Partial => "partly available",
        _ => "not available"
    };
}
