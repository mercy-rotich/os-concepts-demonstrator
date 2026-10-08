using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OS_Concepts_Demonstrator.Services;

    public class SystemInformationService
    {
        public string GetOperatingSystem()
        {
            return RuntimeInformation.OSDescription;
        }

        public string GetProcessor()
        {
            const string cpuInfoFilePath = "/proc/cpuinfo";

            if (!File.Exists(cpuInfoFilePath))
            {
                return "Unknown Processor";
            }
            foreach (string line in File.ReadLines(cpuInfoFilePath))
            {
                if (line.StartsWith("model name"))
                {
                    string[] parts = line.Split(':',2);

                    if (parts.Length == 2)
                    {
                        return parts[1].Trim();
                    }
                }
            }
            return "Unknown Processor";
        }

        public string GetMachineName()
        {
            return Environment.MachineName;
        }



        public string GetMemoryInformation()
        {
            const string memoryInfoFilePath = "/proc/meminfo";

            if (!File.Exists(memoryInfoFilePath))
            {
                return "Memory information not available";
            }

            long totalMemory = 0;
            long availableMemory = 0;

            foreach (string line in File.ReadLines(memoryInfoFilePath))
            {
                if (line.StartsWith("MemTotal:"))
                {
                    string[] parts = line.Split(
                        ':', 
                    StringSplitOptions.RemoveEmptyEntries);

                    totalMemory = long.Parse(
                        parts[1].Trim().Split(' ')[0]);

                    
                }
                if (line.StartsWith("MemAvailable:"))
                {
                    string[] parts = line.Split(
                        ':', 
                    StringSplitOptions.RemoveEmptyEntries);

                   availableMemory = long.Parse(
                        parts[1].Trim().Split(' ')[0]);
                }
                
            }

            double totalGb = totalMemory / 1024.0 / 1024.0;
            double availableGb = availableMemory / 1024.0 / 1024.0;
            double usedGb = totalGb - availableGb;

            return $"{usedGb:F2} GB/{totalGb:F2} GB";
        }
    }
