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
                    processes.Add(new ProcessInfoModel
                    {
                        ProcessId = process.Id,
                        Name = process.ProcessName,
                        MemoryUsageMb = process.WorkingSet64 / (1024.0 * 1024.0) // Convert bytes to MB
                    });
                }
                catch (Exception)
                {
                    // Handle any exceptions that may occur while accessing process information
                    continue; // Skip this process and continue with the next one
                }
                finally
                {
                    process.Dispose(); // Dispose of the process object to free resources
                }
            }

            processes.Sort((a,b) =>
                 string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));



            return processes;
        }
    }