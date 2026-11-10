using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Collects audit-related information that a normal user may read: login history, current sessions,
/// the audit identity, kernel security settings and file permissions. Everything is read-only.
/// Anything the system does not let this user read is reported with the reason, never invented.
/// </summary>
public class AuditService
{
    private const int HistoryRecordsToRead = 900;
    private const int SessionsToShow = 20;
    private const int BootsToShow = 6;
    private const int LogLinesToShow = 15;

    private static readonly string[] WtmpPaths = { "/var/log/wtmp" };
    private static readonly string[] UtmpPaths = { "/run/utmp", "/var/run/utmp" };
    private static readonly string[] BtmpPaths = { "/var/log/btmp" };
    private static readonly string[] AuthLogPaths = { "/var/log/auth.log", "/var/log/secure" };
    private static readonly string[] DmesgLocations = { "/usr/bin/dmesg", "/bin/dmesg" };

    private readonly UserDirectory _users = new();

    public async Task<AuditReadout> CollectAsync()
    {
        var readout = new AuditReadout { Identity = ReadIdentity() };

        string? wtmp = WtmpPaths.FirstOrDefault(File.Exists);
        string? utmp = UtmpPaths.FirstOrDefault(File.Exists);

        // Current sessions first: they tell us which "open" sessions in the history are really still open.
        string? currentError = null;

        if (utmp is not null)
            currentError = TryReadCurrentSessions(utmp, readout.CurrentSessions);

        string? historyError = null;

        if (wtmp is not null)
            readout.History = ReadLoginHistory(wtmp, readout.CurrentSessions, out historyError);

        readout.History.Error = wtmp is null ? "No login history file (wtmp) exists on this system." : historyError;

        ReadFailedLogins(readout);
        ReadAuthLog(readout);
        await ReadKernelMessagesAsync(readout);

        readout.ConfigurationChecks.AddRange(RunConfigurationChecks());
        readout.PermissionChecks.AddRange(RunPermissionChecks());

        readout.Sources.AddRange(DescribeSources(readout, wtmp, utmp, currentError));

        return readout;
    }

    // ---------- Identity ----------

    private AuditIdentityModel ReadIdentity()
    {
        var identity = new AuditIdentityModel();

        void Add(string label, string value) => identity.Rows.Add(new DetailRow { Label = label, Value = value });

        string? loginUid = ReadSingleLine("/proc/self/loginuid");
        string? sessionId = ReadSingleLine("/proc/self/sessionid");

        // 4294967295 (-1) means "not set": the process did not come from a login session.
        const string Unset = "4294967295";

        if (loginUid is null)
        {
            Add("Audit login user", "not available: this kernel has no audit support (no /proc/self/loginuid)");
        }
        else if (loginUid == Unset)
        {
            Add("Audit login user", "not set: this process was not started from a login session (for example a system service)");
        }
        else if (uint.TryParse(loginUid, NumberStyles.None, CultureInfo.InvariantCulture, out uint uid))
        {
            Add("Audit login user",
                $"{_users.UserName(uid)} (ID {uid})   - fixed when you logged in. It does not change after su or sudo, " +
                "so audit records can always name the real person behind an action.");
        }

        if (sessionId is not null)
            Add("Audit session ID", sessionId == Unset ? "not set" : sessionId + "   - numbers this login session in audit records");

        ReadProcessIdentity(identity, Add);

        return identity;
    }

    private void ReadProcessIdentity(AuditIdentityModel identity, Action<string, string> add)
    {
        uint[]? uids = null;
        uint[]? gids = null;
        uint[] groups = Array.Empty<uint>();

        try
        {
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith("Uid:", StringComparison.Ordinal))
                    uids = ParseIds(line.Substring(4));
                else if (line.StartsWith("Gid:", StringComparison.Ordinal))
                    gids = ParseIds(line.Substring(4));
                else if (line.StartsWith("Groups:", StringComparison.Ordinal))
                    groups = ParseIds(line.Substring(7));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            add("Current user", "could not be read");
            return;
        }

        if (uids is { Length: >= 2 })
        {
            add("Current user",
                uids[0] == uids[1]
                    ? $"{_users.UserName(uids[0])} (ID {uids[0]})"
                    : $"real {_users.UserName(uids[0])}, effective {_users.UserName(uids[1])} (this process runs with another user's rights)");
        }

        if (gids is { Length: >= 1 })
            add("Primary group", $"{_users.GroupName(gids[0])} (ID {gids[0]})");

        foreach (uint gid in groups)
            identity.GroupNames.Add(_users.GroupName(gid));

        if (identity.GroupNames.Count > 0)
            add("Groups", string.Join(", ", identity.GroupNames));

        bool canReadLogs = identity.GroupNames.Contains("adm") || identity.GroupNames.Contains("systemd-journal");

        add("Allowed to read system logs",
            canReadLogs
                ? "yes (member of adm or systemd-journal)"
                : "no: only members of the adm or systemd-journal group can read system-wide logs");

        add("Can gain administrator rights",
            identity.GroupNames.Contains("sudo") || identity.GroupNames.Contains("wheel")
                ? "yes, through sudo (member of the sudo or wheel group). Each use of sudo is logged by the system."
                : "not through the usual sudo groups");
    }

    private static uint[] ParseIds(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => uint.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out uint v) ? v : 0u)
            .ToArray();

    // ---------- Login history ----------

    private static string? TryReadCurrentSessions(string path, List<LoginSessionModel> into)
    {
        try
        {
            (_, List<UtmpRecord> records) = UtmpReader.ReadLast(path, 400);

            foreach (UtmpRecord r in records.Where(r => r.Type == (short)UtmpType.UserProcess && r.User.Length > 0))
            {
                into.Add(new LoginSessionModel
                {
                    User = r.User,
                    Terminal = r.Line,
                    From = r.Host,
                    LoginTime = r.Time,
                    End = SessionEnd.StillLoggedIn
                });
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private static LoginHistoryModel ReadLoginHistory(string path, List<LoginSessionModel> current, out string? error)
    {
        var history = new LoginHistoryModel();
        error = null;

        try
        {
            (int total, List<UtmpRecord> records) = UtmpReader.ReadLast(path, HistoryRecordsToRead);
            history.TotalRecords = total;

            var open = new Dictionary<string, LoginSessionModel>(StringComparer.Ordinal); // open sessions by terminal
            var sessions = new List<LoginSessionModel>();

            void CloseAll(DateTimeOffset when)
            {
                foreach (LoginSessionModel session in open.Values)
                {
                    session.End = SessionEnd.EndedByReboot;
                    session.EndTime = when;
                }

                open.Clear();
            }

            // The file is in time order, so a session is "opened" by one record and "closed" by a later one.
            foreach (UtmpRecord r in records)
            {
                switch ((UtmpType)r.Type)
                {
                    case UtmpType.BootTime:
                        CloseAll(r.Time);
                        history.Boots.Add(new BootRecordModel { Time = r.Time, KernelVersion = r.Host });
                        break;

                    case UtmpType.RunLevel when r.User == "shutdown":
                        CloseAll(r.Time);
                        break;

                    case UtmpType.UserProcess when r.User.Length > 0:
                        // A new login on a terminal that never logged out: the old one has no record.
                        if (open.TryGetValue(r.Line, out LoginSessionModel? previous))
                            previous.End = SessionEnd.NoRecord;

                        var session = new LoginSessionModel
                        {
                            User = r.User,
                            Terminal = r.Line,
                            From = r.Host,
                            LoginTime = r.Time,
                            End = SessionEnd.NoRecord
                        };

                        open[r.Line] = session;
                        sessions.Add(session);
                        break;

                    case UtmpType.DeadProcess:
                        if (open.Remove(r.Line, out LoginSessionModel? closing))
                        {
                            closing.End = SessionEnd.LoggedOut;
                            closing.EndTime = r.Time;
                        }
                        break;
                }
            }

            // Sessions still open at the end of the file: logged in only if the live table agrees.
            foreach (LoginSessionModel session in open.Values)
            {
                bool stillThere = current.Any(c => c.Terminal == session.Terminal && c.User == session.User);
                session.End = stillThere ? SessionEnd.StillLoggedIn : SessionEnd.NoRecord;
            }

            history.Sessions.AddRange(sessions.OrderByDescending(s => s.LoginTime).Take(SessionsToShow));
            history.Boots.Reverse();
            history.Boots.RemoveRange(Math.Min(BootsToShow, history.Boots.Count), Math.Max(0, history.Boots.Count - BootsToShow));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            error = $"The login history could not be read: {ex.Message}";
        }

        return history;
    }

    private void ReadFailedLogins(AuditReadout readout)
    {
        string? path = BtmpPaths.FirstOrDefault(File.Exists);

        if (path is null)
        {
            readout.FailedLoginsReason = "No failed-login file (btmp) exists on this system.";
            return;
        }

        try
        {
            (int total, List<UtmpRecord> records) = UtmpReader.ReadLast(path, 100);

            foreach (UtmpRecord r in records.OrderByDescending(r => r.Time).Take(10))
            {
                readout.FailedLogins.Add(new FailedLoginModel
                {
                    UserTried = r.User,
                    Terminal = r.Line,
                    From = r.Host,
                    Time = r.Time
                });
            }

            if (total == 0)
                readout.FailedLoginsReason = "The file is readable and empty: no failed logins have been recorded.";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            readout.FailedLoginsReason =
                "Not readable by your account. The failed-login file is restricted to root and the 'utmp' group, " +
                "because the user names typed there can include mistyped passwords.";
        }
    }

    // ---------- Text logs ----------

    private void ReadAuthLog(AuditReadout readout)
    {
        string? path = AuthLogPaths.FirstOrDefault(File.Exists);

        if (path is null)
        {
            readout.AuthLogReason =
                "No authentication log file exists. This system does not write /var/log/auth.log; " +
                "authentication events go to the systemd journal instead.";
            return;
        }

        try
        {
            readout.AuthLogLines.AddRange(ReadTail(path, LogLinesToShow));

            if (readout.AuthLogLines.Count == 0)
                readout.AuthLogReason = $"{path} is readable but empty.";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            readout.AuthLogReason = $"{path} exists, but only root and the 'adm' group may read it.";
        }
    }

    private async Task ReadKernelMessagesAsync(AuditReadout readout)
    {
        string? restrictValue = ReadSingleLine("/proc/sys/kernel/dmesg_restrict");
        bool isRoot = readout.Identity.Rows.Any(r => r.Label == "Current user" && r.Value.StartsWith("root", StringComparison.Ordinal));

        if (restrictValue == "1" && !isRoot)
        {
            readout.KernelMessagesReason =
                "Restricted by the kernel (kernel.dmesg_restrict = 1). Kernel messages can reveal memory addresses, " +
                "so only privileged users may read them.";
            return;
        }

        string? dmesg = DmesgLocations.FirstOrDefault(File.Exists);

        if (dmesg is null)
        {
            readout.KernelMessagesReason = "The 'dmesg' command was not found.";
            return;
        }

        CommandResult result = await CommandRunner.RunAsync(dmesg, new[] { "--time-format=iso" }, TimeSpan.FromSeconds(5));

        if (!result.Started || result.TimedOut || result.ExitCode != 0)
        {
            readout.KernelMessagesReason = $"Kernel messages could not be read: {result.StandardError.Trim()}";
            return;
        }

        string[] lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        readout.KernelMessageLines.AddRange(lines.Skip(Math.Max(0, lines.Length - LogLinesToShow)));
    }

    /// <summary>The last lines of a text file, reading only its end so a huge log is not loaded.</summary>
    public static List<string> ReadTail(string path, int lineCount)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        const int window = 64 * 1024;
        long start = Math.Max(0, stream.Length - window);
        stream.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string[] lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // If we started in the middle of the file, the first line is probably cut off: drop it.
        IEnumerable<string> usable = start > 0 ? lines.Skip(1) : lines;
        string[] result = usable.ToArray();

        return result.Skip(Math.Max(0, result.Length - lineCount)).ToList();
    }

    // ---------- Configuration and permission checks ----------

    private sealed record SettingCheck(string Name, string Path, Func<long, bool> IsGood, string Recommended, string Explanation);

    private static readonly SettingCheck[] SettingChecks =
    {
        new("Address-space randomisation (ASLR)", "/proc/sys/kernel/randomize_va_space", v => v == 2, "2 (full)",
            "Loads programs at random memory addresses, so an attacker cannot rely on fixed locations."),
        new("Hide kernel addresses", "/proc/sys/kernel/kptr_restrict", v => v >= 1, "1 or 2",
            "Stops ordinary users seeing kernel memory addresses, which help attackers aim exploits."),
        new("Restrict kernel log access", "/proc/sys/kernel/dmesg_restrict", v => v == 1, "1",
            "Only privileged users may read kernel messages."),
        new("Restrict ptrace (reading other processes)", "/proc/sys/kernel/yama/ptrace_scope", v => v >= 1, "1 or higher",
            "At 0 any process can inspect and modify the memory of other processes of the same user, which malware can abuse. Debugging and security tools often need it at 0."),
        new("Protect symbolic links", "/proc/sys/fs/protected_symlinks", v => v == 1, "1",
            "Prevents tricking a program into following a malicious link in a shared directory such as /tmp."),
        new("Protect hard links", "/proc/sys/fs/protected_hardlinks", v => v == 1, "1",
            "Prevents creating hard links to files you do not own."),
        new("No core dumps from privileged programs", "/proc/sys/fs/suid_dumpable", v => v == 0, "0",
            "A crash dump of a privileged program could contain secrets."),
        new("Restrict unprivileged BPF", "/proc/sys/kernel/unprivileged_bpf_disabled", v => v >= 1, "1 or 2",
            "BPF runs code inside the kernel; ordinary users should not load it."),
        new("SYN-flood protection", "/proc/sys/net/ipv4/tcp_syncookies", v => v == 1, "1",
            "Helps the machine survive floods of half-open network connections."),
        new("Reverse-path filtering", "/proc/sys/net/ipv4/conf/all/rp_filter", v => v >= 1, "1 or 2",
            "Drops network packets whose source address could not have arrived on that interface (anti-spoofing)."),
        new("Ignore ICMP redirects", "/proc/sys/net/ipv4/conf/all/accept_redirects", v => v == 0, "0",
            "Redirect messages let another host change this machine's routes."),
        new("Not forwarding packets (not a router)", "/proc/sys/net/ipv4/ip_forward", v => v == 0, "0 unless acting as a router",
            "When 1, this machine passes traffic between networks, which is only wanted on a router or gateway.")
    };

    private List<SecurityCheckModel> RunConfigurationChecks()
    {
        var checks = new List<SecurityCheckModel>();

        foreach (SettingCheck check in SettingChecks)
        {
            string? text = ReadSingleLine(check.Path);
            bool parsed = long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value);

            checks.Add(new SecurityCheckModel
            {
                Name = check.Name,
                Setting = check.Path.Replace("/proc/sys/", "").Replace('/', '.'),
                CurrentValue = text is null ? "not present on this kernel" : text,
                Recommended = check.Recommended,
                Explanation = check.Explanation,
                Status = !parsed ? CheckStatus.Unknown : check.IsGood(value) ? CheckStatus.Good : CheckStatus.Review
            });
        }

        checks.Add(BuildMandatoryAccessControlCheck());

        return checks;
    }

    private SecurityCheckModel BuildMandatoryAccessControlCheck()
    {
        var check = new SecurityCheckModel
        {
            Name = "Mandatory access control (AppArmor or SELinux)",
            Setting = "/sys/module/apparmor, /sys/fs/selinux",
            Recommended = "one of them enabled",
            Explanation = "Confines each program to what its policy allows, even if it is exploited."
        };

        string? apparmor = ReadSingleLine("/sys/module/apparmor/parameters/enabled");
        string? selinux = ReadSingleLine("/sys/fs/selinux/enforce");

        if (apparmor == "Y")
        {
            check.CurrentValue = "AppArmor enabled";
            check.Status = CheckStatus.Good;
        }
        else if (selinux == "1")
        {
            check.CurrentValue = "SELinux enforcing";
            check.Status = CheckStatus.Good;
        }
        else if (selinux == "0")
        {
            check.CurrentValue = "SELinux present but only logging (permissive)";
            check.Status = CheckStatus.Review;
        }
        else
        {
            check.CurrentValue = "neither AppArmor nor SELinux was found active";
            check.Status = CheckStatus.Review;
        }

        return check;
    }

    private sealed record PermissionCheck(string Path, Func<UnixFileMode, bool> IsGood, string Recommended, string Explanation);

    private static readonly PermissionCheck[] PermissionChecks =
    {
        new("/etc/passwd", m => (m & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0, "not writable by group or others",
            "Lists every account. Anyone able to edit it could add a user with administrator rights. It is normally world-readable."),
        new("/etc/shadow", m => (m & (UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) == 0, "no access for others",
            "Holds the password hashes. If others could read it they could try to crack the passwords offline."),
        new("/etc/sudoers", m => (m & (UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) == 0, "read-only (440), no access for others",
            "Defines who may act as administrator."),
        new("/etc/ssh/sshd_config", m => (m & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0, "not writable by group or others",
            "Controls the remote-login server. Write access would let someone weaken it."),
        new("/etc/crontab", m => (m & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0, "not writable by group or others",
            "Scheduled jobs run with this file's authority, so it must not be editable by ordinary users."),
        new("/tmp", m => m.HasFlag(UnixFileMode.StickyBit), "sticky bit set (1777)",
            "Everyone may create files here, but the sticky bit stops users deleting each other's files.")
    };

    private static List<SecurityCheckModel> RunPermissionChecks()
    {
        var checks = new List<SecurityCheckModel>();

        foreach (PermissionCheck check in PermissionChecks)
        {
            var model = new SecurityCheckModel
            {
                Name = check.Path,
                Setting = check.Path,
                Recommended = check.Recommended,
                Explanation = check.Explanation
            };

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    model.CurrentValue = "Unix permission bits do not exist on this operating system";
                    model.Status = CheckStatus.Unknown;
                }
                else if (!File.Exists(check.Path) && !Directory.Exists(check.Path))
                {
                    model.CurrentValue = "file not present on this system";
                    model.Status = CheckStatus.Unknown;
                }
                else
                {
                    // Reading the permission bits does not need permission to read the file itself.
                    UnixFileMode mode = File.GetUnixFileMode(check.Path);

                    model.CurrentValue = DescribeMode(mode);
                    model.Status = check.IsGood(mode) ? CheckStatus.Good : CheckStatus.Review;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
            {
                model.CurrentValue = "could not be read";
                model.Status = CheckStatus.Unknown;
            }

            checks.Add(model);
        }

        return checks;
    }

    // e.g. "644  (rw-r--r--)" or "1777  (rwxrwxrwt)"
    private static string DescribeMode(UnixFileMode mode)
    {
        var text = new StringBuilder(9);

        text.Append(mode.HasFlag(UnixFileMode.UserRead) ? 'r' : '-');
        text.Append(mode.HasFlag(UnixFileMode.UserWrite) ? 'w' : '-');
        text.Append(mode.HasFlag(UnixFileMode.UserExecute) ? 'x' : '-');
        text.Append(mode.HasFlag(UnixFileMode.GroupRead) ? 'r' : '-');
        text.Append(mode.HasFlag(UnixFileMode.GroupWrite) ? 'w' : '-');
        text.Append(mode.HasFlag(UnixFileMode.GroupExecute) ? 'x' : '-');
        text.Append(mode.HasFlag(UnixFileMode.OtherRead) ? 'r' : '-');
        text.Append(mode.HasFlag(UnixFileMode.OtherWrite) ? 'w' : '-');

        bool otherExecute = mode.HasFlag(UnixFileMode.OtherExecute);
        text.Append(mode.HasFlag(UnixFileMode.StickyBit) ? (otherExecute ? 't' : 'T') : (otherExecute ? 'x' : '-'));

        return $"{Convert.ToString((int)mode, 8)}  ({text})";
    }

    // ---------- What this system exposes ----------

    private static List<DataSourceStatusModel> DescribeSources(
        AuditReadout readout, string? wtmp, string? utmp, string? currentSessionsError)
    {
        var sources = new List<DataSourceStatusModel>();

        sources.Add(wtmp is null || readout.History.Error is not null
            ? Source("Login and boot history", "/var/log/wtmp", DataAvailability.Unavailable,
                readout.History.Error ?? "No login history file exists.")
            : Source("Login and boot history", "/var/log/wtmp", DataAvailability.Available,
                $"Decoded from {readout.History.TotalRecords:N0} records; every login, logout and reboot with its time."));

        sources.Add(utmp is null || currentSessionsError is not null
            ? Source("Who is logged in now", "/run/utmp", DataAvailability.Unavailable,
                currentSessionsError ?? "The current-sessions file does not exist.")
            : Source("Who is logged in now", "/run/utmp", DataAvailability.Available,
                $"{readout.CurrentSessions.Count} session(s) currently recorded."));

        sources.Add(readout.FailedLogins.Count > 0
            ? Source("Failed login attempts", "/var/log/btmp", DataAvailability.Available, $"{readout.FailedLogins.Count} recent attempt(s) shown.")
            : Source("Failed login attempts", "/var/log/btmp",
                readout.FailedLoginsReason?.StartsWith("The file is readable", StringComparison.Ordinal) == true
                    ? DataAvailability.Available : DataAvailability.Unavailable,
                readout.FailedLoginsReason ?? "No failed logins recorded."));

        sources.Add(readout.AuthLogLines.Count > 0
            ? Source("Authentication log (logins, sudo, ssh)", "/var/log/auth.log", DataAvailability.Available, $"Last {readout.AuthLogLines.Count} lines shown.")
            : Source("Authentication log (logins, sudo, ssh)", "/var/log/auth.log", DataAvailability.Unavailable,
                readout.AuthLogReason ?? "No lines."));

        bool journalPresent = Directory.Exists("/run/systemd/journal") || Directory.Exists("/var/log/journal");
        bool journalAllowed = readout.Identity.GroupNames.Contains("adm") || readout.Identity.GroupNames.Contains("systemd-journal");

        sources.Add(!journalPresent
            ? Source("System journal (all system events)", "journalctl", DataAvailability.Unavailable, "The systemd journal is not in use on this system.")
            : journalAllowed
                ? Source("System journal (all system events)", "journalctl", DataAvailability.Available, "Your account may read system-wide messages.")
                : Source("System journal (all system events)", "journalctl", DataAvailability.Partial,
                    "Your account sees only its own session's messages. System-wide events need membership of the adm or systemd-journal group, so they are not shown here."));

        sources.Add(readout.KernelMessageLines.Count > 0
            ? Source("Kernel messages", "dmesg", DataAvailability.Available, $"Last {readout.KernelMessageLines.Count} messages shown.")
            : Source("Kernel messages", "dmesg", DataAvailability.Unavailable, readout.KernelMessagesReason ?? "No messages."));

        bool auditLog = File.Exists("/var/log/audit/audit.log") || Directory.Exists("/var/log/audit");
        bool auditDaemon = File.Exists("/run/auditd.pid") || File.Exists("/var/run/auditd.pid");
        bool kernelSupport = File.Exists("/proc/self/loginuid");

        sources.Add(Source("Linux Audit framework (auditd)", "/var/log/audit/audit.log",
            auditLog || auditDaemon ? DataAvailability.Partial : DataAvailability.Unavailable,
            auditLog || auditDaemon
                ? "auditd appears to be installed. Its log is restricted to root, so its records are not shown here."
                : "auditd is not installed or not running, so no detailed audit records (file access, system calls, privilege use) are being kept. " +
                  (kernelSupport ? "The kernel itself has audit support (your audit login ID proves it)." : "This kernel also has no audit support.")));

        return sources;
    }

    private static DataSourceStatusModel Source(string name, string provides, DataAvailability availability, string detail) =>
        new() { Name = name, Provides = provides, Availability = availability, Detail = detail };

    // ---------- Helpers ----------

    private static string? ReadSingleLine(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadLines(path).FirstOrDefault()?.Trim() : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }
}
