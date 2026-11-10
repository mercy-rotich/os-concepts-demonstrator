namespace OS_Concepts_Demonstrator.Models;

public enum ServiceStateCategory
{
    Running,
    Exited,
    Failed,
    Inactive,
    Other
}

/// <summary>One service (systemd "unit") as shown in the list.</summary>
public class ServiceInfoModel
{
    // Full unit name, e.g. "cron.service".
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    // Was the unit's definition found and loaded? "loaded", "masked", "not loaded"...
    public string LoadState { get; set; } = string.Empty;

    // High-level state: active, inactive, failed, activating, deactivating...
    public string ActiveState { get; set; } = string.Empty;

    // Detailed state: running, exited, dead, failed...
    public string SubState { get; set; } = string.Empty;

    // Whether it starts at boot: enabled, disabled, static, masked... ("n/a" if it has no unit file).
    public string UnitFileState { get; set; } = "n/a";

    public string DisplayName => Name.EndsWith(".service", System.StringComparison.Ordinal)
        ? Name.Substring(0, Name.Length - ".service".Length)
        : Name;

    public ServiceStateCategory Category =>
        ActiveState == "failed" ? ServiceStateCategory.Failed
        : ActiveState == "active" && SubState == "running" ? ServiceStateCategory.Running
        : ActiveState == "active" && SubState == "exited" ? ServiceStateCategory.Exited
        : ActiveState == "inactive" ? ServiceStateCategory.Inactive
        : ServiceStateCategory.Other;

    public bool IsRunning => Category == ServiceStateCategory.Running;

    public bool IsExited => Category == ServiceStateCategory.Exited;

    public bool IsFailed => Category == ServiceStateCategory.Failed;

    public bool IsInactive => Category == ServiceStateCategory.Inactive;

    public bool IsOther => Category == ServiceStateCategory.Other;

    public string StatusText => Category switch
    {
        ServiceStateCategory.Running => "running",
        ServiceStateCategory.Exited => "exited",
        ServiceStateCategory.Failed => "failed",
        ServiceStateCategory.Inactive => "inactive",
        _ => string.IsNullOrEmpty(SubState) ? ActiveState : $"{ActiveState} ({SubState})"
    };

    public string StatusExplanation => Category switch
    {
        ServiceStateCategory.Running =>
            "Running: the service has a live process doing its work right now.",
        ServiceStateCategory.Exited =>
            "Exited: its start-up task ran and finished successfully. Nothing stays running, which is normal for one-shot services that only set something up.",
        ServiceStateCategory.Failed =>
            "Failed: the service crashed, exited with an error, or could not start. Its logs usually say why.",
        ServiceStateCategory.Inactive =>
            "Inactive: not running right now. It may start at boot if enabled, or on demand when something needs it.",
        _ =>
            $"{ActiveState}: the service is changing state (starting, stopping or reloading)."
    };

    // What the "startup" column means, in plain words.
    public string StartupExplanation => UnitFileState switch
    {
        "enabled" => "enabled: starts automatically at boot",
        "enabled-runtime" => "enabled until reboot",
        "disabled" => "disabled: does not start at boot (can still be started by hand)",
        "static" => "static: has no boot setting; it is started only when another unit needs it",
        "masked" => "masked: blocked completely, it cannot be started",
        "indirect" => "indirect: enabled through another unit",
        "generated" => "generated: created automatically by a system generator",
        "transient" => "transient: created temporarily at run time",
        "n/a" => "no unit file (created at run time)",
        _ => UnitFileState
    };
}
