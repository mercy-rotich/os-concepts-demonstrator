using System;
using System.IO;
using System.Globalization;
using OS_Concepts_Demonstrator.Models;
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

        public MemoryInfoModel GetMemoryInfo()
        {
            const string memInfoFilePath = "/proc/meminfo";

            long totalKb = 0;
            long availableKb = 0;

            foreach (string line in File.ReadLines(memInfoFilePath))
            {
                string[] parts = line.Split(
                    ':',
                    StringSplitOptions.TrimEntries);

                if (parts.Length < 2)
            
                    continue;
                

                string value = parts[1].Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)[0];

                if (parts[0] == "MemTotal")
                {
                    totalKb = long.Parse(value, CultureInfo.InvariantCulture);
                }
                else if (parts[0] == "MemAvailable")
                {
                    availableKb = long.Parse(value, CultureInfo.InvariantCulture);
                }

            if (totalKb > 0 && availableKb > 0)
                
                    break;
                
            }

            if (totalKb <= 0 || availableKb <= 0)
            {
                throw new InvalidOperationException(
                    "Unable to retrieve memory information.");
            }

            double totalGb = totalKb / 1024.0 / 1024.0;
            double availableGb = availableKb / 1024.0 / 1024.0;
            double usedGb = totalGb - availableGb;
            double usedPercentage = (usedGb / totalGb) * 100.0;

            return new MemoryInfoModel
            {
                TotalGb = totalGb,
                AvailableGb = availableGb,
                UsedGb = usedGb,
                UsedPercentage = usedPercentage
            };
        }
    }
