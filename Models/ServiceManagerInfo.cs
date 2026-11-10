namespace OS_Concepts_Demonstrator.Models;

/// <summary>Information about the init system (the first process, PID 1) that manages services.</summary>
public class ServiceManagerInfo
{
    // Name of PID 1, e.g. "systemd".
    public string InitName { get; set; } = "unknown";

    public bool IsSystemd { get; set; }

    // Overall health reported by systemd: running, degraded, starting, ...
    public string SystemState { get; set; } = "unknown";

    public bool IsHealthy => SystemState == "running";

    public string Headline => IsSystemd
        ? $"Init system: systemd (PID 1)   |   System state: {SystemState}"
        : $"Init system: {InitName} (PID 1)";

    public string StateExplanation => !IsSystemd
        ? $"This system's init process is '{InitName}', not systemd. This page reads systemd, so services cannot be listed here."
        : SystemState switch
        {
            "running" => "Everything started normally.",
            "degraded" => "The system is up, but at least one service has failed (see the Failed filter below).",
            "starting" => "The system is still starting up.",
            "initializing" => "The system is early in start-up.",
            "stopping" => "The system is shutting down.",
            "maintenance" => "The system is in maintenance (rescue) mode.",
            "offline" => "systemd is not running.",
            _ => "The overall state could not be determined."
        };
}
