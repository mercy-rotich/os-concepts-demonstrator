
using System;
using System.Collections.Generic;
using System.Diagnostics;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

public class ProcessService
{
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
}
