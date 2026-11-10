using System;
using System.Collections.Generic;

namespace OS_Concepts_Demonstrator.Models;

public enum SessionEnd
{
    // Still logged in right now.
    StillLoggedIn,

    // A logout was recorded.
    LoggedOut,

    // The system was rebooted or shut down while the session was open.
    EndedByReboot,

    // No logout record exists (for example the system crashed).
    NoRecord
}

/// <summary>One login session reconstructed from the login records.</summary>
public class LoginSessionModel
{
    public string User { get; set; } = string.Empty;

    public string Terminal { get; set; } = string.Empty;

    // Where the user connected from: a remote host, or the display (":0") for a local login.
    public string From { get; set; } = string.Empty;

    public DateTimeOffset LoginTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public SessionEnd End { get; set; }

    public string LoginText => LoginTime.ToString("ddd dd MMM yyyy, HH:mm");

    public string FromText => string.IsNullOrWhiteSpace(From) ? "local" : From;

    public bool IsActive => End == SessionEnd.StillLoggedIn;

    public string EndText => End switch
    {
        SessionEnd.StillLoggedIn => "still logged in",
        SessionEnd.LoggedOut => EndTime?.ToString("ddd dd MMM, HH:mm") ?? "logged out",
        SessionEnd.EndedByReboot => "ended by reboot or shutdown",
        _ => "no logout recorded"
    };

    public string DurationText
    {
        get
        {
            DateTimeOffset end = End == SessionEnd.StillLoggedIn ? DateTimeOffset.Now : EndTime ?? LoginTime;
            TimeSpan span = end - LoginTime;

            if (End == SessionEnd.NoRecord)
                return "unknown";

            return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h"
                : span.TotalHours >= 1 ? $"{span.Hours}h {span.Minutes:D2}m"
                : $"{Math.Max(0, span.Minutes)}m";
        }
    }
}

public class BootRecordModel
{
    public DateTimeOffset Time { get; set; }

    public string KernelVersion { get; set; } = string.Empty;

    public string TimeText => Time.ToString("ddd dd MMM yyyy, HH:mm:ss");
}

/// <summary>What was found when the login records were read.</summary>
public class LoginHistoryModel
{
    public List<LoginSessionModel> Sessions { get; } = new();

    public List<BootRecordModel> Boots { get; } = new();

    // How many records the file held in total.
    public int TotalRecords { get; set; }

    public string? Error { get; set; }
}

public enum CheckStatus
{
    Good,
    Review,
    Unknown
}

/// <summary>One configuration or permission check.</summary>
public class SecurityCheckModel
{
    public string Name { get; set; } = string.Empty;

    // The setting's path, e.g. /proc/sys/kernel/randomize_va_space.
    public string Setting { get; set; } = string.Empty;

    // What this system actually has (read now), or why it could not be read.
    public string CurrentValue { get; set; } = string.Empty;

    public string Recommended { get; set; } = string.Empty;

    // Why this setting matters.
    public string Explanation { get; set; } = string.Empty;

    public CheckStatus Status { get; set; }

    public bool IsGood => Status == CheckStatus.Good;

    public bool IsReview => Status == CheckStatus.Review;

    public bool IsUnknown => Status == CheckStatus.Unknown;

    public string StatusText => Status switch
    {
        CheckStatus.Good => "as recommended",
        CheckStatus.Review => "review",
        _ => "unknown"
    };
}

/// <summary>The identity the audit system would record for this process.</summary>
public class AuditIdentityModel
{
    public List<DetailRow> Rows { get; } = new();

    // Names of every group this process belongs to (used to tell what logs it may read).
    public List<string> GroupNames { get; } = new();
}

/// <summary>A failed login attempt (from btmp, which only privileged users can usually read).</summary>
public class FailedLoginModel
{
    public string UserTried { get; set; } = string.Empty;

    public string Terminal { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public DateTimeOffset Time { get; set; }

    public string TimeText => Time.ToString("ddd dd MMM yyyy, HH:mm:ss");
}

/// <summary>Everything the audit page shows, collected in one read.</summary>
public class AuditReadout
{
    public AuditIdentityModel Identity { get; set; } = new();

    public LoginHistoryModel History { get; set; } = new();

    public List<LoginSessionModel> CurrentSessions { get; } = new();

    public List<FailedLoginModel> FailedLogins { get; } = new();

    // When a list is empty, the reason is here (or null if it is genuinely empty).
    public string? FailedLoginsReason { get; set; }

    public List<string> AuthLogLines { get; } = new();

    public string? AuthLogReason { get; set; }

    public List<string> KernelMessageLines { get; } = new();

    public string? KernelMessagesReason { get; set; }

    public List<SecurityCheckModel> ConfigurationChecks { get; } = new();

    public List<SecurityCheckModel> PermissionChecks { get; } = new();

    public List<DataSourceStatusModel> Sources { get; } = new();
}
