
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

public class ProcessService
{
    private const int PriorityProcess = 0;
    private const int SignalStop = 19; // SIGSTOP
    private const int SignalContinue = 18; // SIGCONT
    private const int SignalTerminate = 15; // SIGTERM
    private const int SignalKill = 9; // SIGKILL

    private static readonly HashSet<int> ProtectedProcessIds = new() { 0, 1, 2 };

    public List<ProcessInfoModel> GetRunningProcesses()
    {
        List<ProcessInfoModel> processes = new();

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                ProcessInfoModel processInfo = new()
                {
                    ProcessId = process.Id,
                    Name = process.ProcessName,
                    MemoryUsageMb = process.WorkingSet64 / (1024.0 * 1024.0)
                };

                // Try to retrieve the process start time.
                try
                {
                    processInfo.StartTime = process.StartTime;
                }
                catch (Exception)
                {
                    // Start time may not be accessible.
                    processInfo.StartTime = null;
                }

                (string statusCode, string statusText, long? startTicks, int? niceValue) = ReadProcessState(process.Id);
                processInfo.StatusCode = statusCode;
                processInfo.StatusText = statusText;
                processInfo.StartTicks = startTicks;
                processInfo.NiceValue = niceValue;

                processes.Add(processInfo);
            }
            catch (Exception)
            {
                // Skip processes whose essential information cannot be accessed.
                continue;
            }
            finally
            {
                process.Dispose();
            }
        }

        processes.Sort((a, b) =>
            string.Compare(
                a.Name,
                b.Name,
                StringComparison.OrdinalIgnoreCase));

        return processes;
    }

    public ProcessActionResult SuspendProcess(ProcessInfoModel process)
    {
        return SendSignal(process, SignalStop, "suspended", "suspend");
    }

    public ProcessActionResult ResumeProcess(ProcessInfoModel process)
    {
        return SendSignal(process, SignalContinue, "resumed", "resume");
    }

    public ProcessActionResult TerminateProcess(ProcessInfoModel process)
    {
        return SendSignal(process, SignalTerminate, "sent SIGTERM", "terminate");
    }

    public ProcessActionResult ForceKillProcess(ProcessInfoModel process, bool confirmed)
    {
        if (!confirmed)
        {
            return new ProcessActionResult
            {
                Success = false,
                Message = "Force kill requires explicit confirmation."
            };
        }

        return SendSignal(process, SignalKill, "force killed", "force kill");
    }

    public ProcessActionResult ChangePriority(ProcessInfoModel process, int newNice)
    {
        if (newNice < -20 || newNice > 19)
        {
            return new ProcessActionResult
            {
                Success = false,
                Message = "Nice value must be between -20 and 19."
            };
        }

        if (process.ProcessId <= 0)
        {
            return new ProcessActionResult
            {
                Success = false,
                IsProcessMissing = true,
                Message = "The selected process ID is invalid."
            };
        }

        if (IsProtectedTarget(process))
        {
            return new ProcessActionResult
            {
                Success = false,
                IsProtectedProcess = true,
                Message = "This protected system process priority cannot be changed from this panel."
            };
        }

        (bool exists, long? currentStartTicks, string _, int? currentNice) = ProbeProcess(process.ProcessId);

        if (!exists)
        {
            return new ProcessActionResult
            {
                Success = false,
                IsProcessMissing = true,
                Message = "The process has already exited."
            };
        }

        if (!IdentityMatches(process, currentStartTicks))
        {
            return new ProcessActionResult
            {
                Success = false,
                IsIdentityMismatch = true,
                Message = "Could not confirm this is still the same process (PID reuse or unreadable start time). Refresh and select it again."
            };
        }

        if (currentNice is not null && currentNice.Value == newNice)
        {
            return new ProcessActionResult
            {
                Success = true,
                Message = $"Priority is already set to nice {newNice}."
            };
        }

        if (NativeLibc.SetPriority(PriorityProcess, (uint)process.ProcessId, newNice) != 0)
        {
            int error = Marshal.GetLastWin32Error();
            return error switch
            {
                1 or 13 => new ProcessActionResult
                {
                    Success = false,
                    IsPermissionError = true,
                    Message = "Permission denied: you can usually only increase nice values (lower priority) without elevated rights."
                },
                3 => new ProcessActionResult
                {
                    Success = false,
                    IsProcessMissing = true,
                    Message = "The process no longer exists."
                },
                _ => new ProcessActionResult
                {
                    Success = false,
                    Message = $"Could not change process priority (errno {error})."
                }
            };
        }

        return new ProcessActionResult
        {
            Success = true,
            Message = $"Priority updated: process {process.ProcessId} now has nice {newNice}."
        };
    }

    private ProcessActionResult SendSignal(
        ProcessInfoModel process,
        int signal,
        string successVerb,
        string actionVerb)
    {
        if (process.ProcessId <= 0)
        {
            return new ProcessActionResult
            {
                Success = false,
                IsProcessMissing = true,
                Message = "The selected process ID is invalid."
            };
        }

        if (IsProtectedTarget(process))
        {
            return new ProcessActionResult
            {
                Success = false,
                IsProtectedProcess = true,
                Message = "This protected system process cannot be controlled from this panel."
            };
        }

        (bool exists, long? currentStartTicks, string statusCode, int? _) = ProbeProcess(process.ProcessId);

        if (!exists)
        {
            return new ProcessActionResult
            {
                Success = false,
                IsProcessMissing = true,
                Message = "The process has already exited."
            };
        }

        if (!IdentityMatches(process, currentStartTicks))
        {
            return new ProcessActionResult
            {
                Success = false,
                IsIdentityMismatch = true,
                Message = "Could not confirm this is still the same process (PID reuse or unreadable start time). Refresh and select it again."
            };
        }

        if (actionVerb == "suspend" && statusCode == "T")
        {
            return new ProcessActionResult
            {
                Success = true,
                Message = "The process is already suspended."
            };
        }

        if (actionVerb == "resume" && statusCode != "T")
        {
            return new ProcessActionResult
            {
                Success = true,
                Message = "The process is already running."
            };
        }

        if ((actionVerb == "terminate" || actionVerb == "force kill") && statusCode == "Z")
        {
            return new ProcessActionResult
            {
                Success = true,
                Message = "The process is already in zombie state and cannot be signaled further."
            };
        }

        if (NativeLibc.Kill(process.ProcessId, signal) != 0)
        {
            int error = Marshal.GetLastWin32Error();
            return BuildSignalErrorResult(error, actionVerb);
        }

        // A stopped process cannot act on SIGTERM until it is continued, so wake it up.
        if (signal == SignalTerminate && statusCode == "T")
            NativeLibc.Kill(process.ProcessId, SignalContinue);

        return new ProcessActionResult
        {
            Success = true,
            Message = $"Process {process.ProcessId} was {successVerb} successfully."
        };
    }

    private static ProcessActionResult BuildSignalErrorResult(int errno, string actionVerb)
    {
        return errno switch
        {
            1 => new ProcessActionResult
            {
                Success = false,
                IsPermissionError = true,
                Message = $"Permission denied: you do not have rights to {actionVerb} this process."
            },
            3 => new ProcessActionResult
            {
                Success = false,
                IsProcessMissing = true,
                Message = "The process no longer exists."
            },
            _ => new ProcessActionResult
            {
                Success = false,
                Message = $"Could not {actionVerb} the process (errno {errno})."
            }
        };
    }

    // Single source of truth for "must not be controlled from this panel". Used by the UI too.
    public static bool IsProtected(ProcessInfoModel process) => IsProtectedTarget(process);

    private static bool IdentityMatches(ProcessInfoModel process, long? currentStartTicks)
    {
        // Both values are needed to prove identity; refuse when either is unknown.
        return process.StartTicks is not null
            && currentStartTicks is not null
            && process.StartTicks == currentStartTicks;
    }

    private static bool IsProtectedTarget(ProcessInfoModel process)
    {
        if (ProtectedProcessIds.Contains(process.ProcessId))
            return true;

        if (process.ProcessId == Environment.ProcessId)
            return true;

        if (process.Name.Equals("systemd", StringComparison.OrdinalIgnoreCase)
            || process.Name.Equals("init", StringComparison.OrdinalIgnoreCase)
            || process.Name.Equals("kthreadd", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int? parentPid = ReadParentPid(process.ProcessId);

        // Kernel threads are children of kthreadd (PID 2).
        if (parentPid == 2)
            return true;

        // The process that launched this app (terminal / IDE / session).
        if (process.ProcessId == ReadParentPid(Environment.ProcessId))
            return true;

        return false;
    }

    private static int? ReadParentPid(int pid)
    {
        try
        {
            string line = File.ReadAllText($"/proc/{pid}/stat");
            int closeParen = line.LastIndexOf(')');
            if (closeParen < 0 || closeParen + 2 >= line.Length)
                return null;

            string[] parts = line.Substring(closeParen + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ppid)
                ? ppid
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static (bool Exists, long? StartTicks, string StatusCode, int? NiceValue) ProbeProcess(int pid)
    {
        string statPath = $"/proc/{pid}/stat";

        if (!File.Exists(statPath))
            return (false, null, "?", null);

        try
        {
            string line = File.ReadAllText(statPath);
            return ParseStatLine(line);
        }
        catch
        {
            return (true, null, "?", null);
        }
    }

    private static (string StatusCode, string StatusText, long? StartTicks, int? NiceValue) ReadProcessState(int pid)
    {
        (bool exists, long? startTicks, string statusCode, int? niceValue) = ProbeProcess(pid);

        if (!exists)
            return ("?", "Exited", null, null);

        return (statusCode, MapStatusText(statusCode), startTicks, niceValue);
    }

    private static (bool Exists, long? StartTicks, string StatusCode, int? NiceValue) ParseStatLine(string statLine)
    {
        int closeParen = statLine.LastIndexOf(')');

        if (closeParen < 0 || closeParen + 2 >= statLine.Length)
            return (true, null, "?", null);

        string afterName = statLine.Substring(closeParen + 2);
        string[] parts = afterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 20)
            return (true, null, "?", null);

        string statusCode = parts[0];

        long? startTicks = long.TryParse(parts[19], NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedStart)
            ? parsedStart
            : null;

        int? niceValue = int.TryParse(parts[16], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedNice)
            ? parsedNice
            : null;

        return (true, startTicks, statusCode, niceValue);
    }

    private static string MapStatusText(string statusCode)
    {
        return statusCode switch
        {
            "R" => "Running",
            "S" => "Sleeping",
            "D" => "Waiting on I/O",
            "T" => "Stopped",
            "t" => "Tracing stop",
            "Z" => "Zombie",
            "X" => "Dead",
            "I" => "Idle",
            _ => "Unknown"
        };
    }
}
