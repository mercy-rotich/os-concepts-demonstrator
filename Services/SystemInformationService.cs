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

            try
            {
                foreach (string line in File.ReadLines(cpuInfoFilePath))
                {
                    if (line.StartsWith("model name", StringComparison.Ordinal))
                    {
                        string[] parts = line.Split(':', 2);

                        if (parts.Length == 2)
                        {
                            return parts[1].Trim();
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return "Processor information unavailable";
            }

            return "Unknown Processor";
        }

        public string GetMachineName()
        {
            return Environment.MachineName;
        }

        /// <summary>
        /// Original API, kept so existing callers (Dashboard) keep working.
        /// Throws if memory information cannot be read or is invalid.
        /// </summary>
        public MemoryInfoModel GetMemoryInfo()
        {
            if (TryGetMemoryInfo(out MemoryInfoModel? info, out string error))
                return info!;

            throw new InvalidOperationException(error);
        }

        /// <summary>
        /// Safe version: never throws. Returns false and an error message when
        /// /proc/meminfo is unavailable, malformed, or contains impossible values.
        /// </summary>
        public bool TryGetMemoryInfo(out MemoryInfoModel? info, out string error)
        {
            const string memInfoFilePath = "/proc/meminfo";

            info = null;
            error = string.Empty;

            if (!File.Exists(memInfoFilePath))
            {
                error = "Memory information is unavailable on this system (/proc/meminfo not found).";
                return false;
            }

            long totalKb = -1;
            long availableKb = -1;

            try
            {
                foreach (string line in File.ReadLines(memInfoFilePath))
                {
                    string[] parts = line.Split(':', 2, StringSplitOptions.TrimEntries);

                    if (parts.Length < 2)
                        continue;

                    if (parts[0] != "MemTotal" && parts[0] != "MemAvailable")
                        continue;

                    string[] valueParts = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (valueParts.Length == 0 ||
                        !long.TryParse(valueParts[0], NumberStyles.None,
                                       CultureInfo.InvariantCulture, out long value))
                    {
                        error = $"Could not read the {parts[0]} value from /proc/meminfo.";
                        return false;
                    }

                    if (parts[0] == "MemTotal")
                        totalKb = value;
                    else
                        availableKb = value;

                    if (totalKb >= 0 && availableKb >= 0)
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = $"Could not read memory information: {ex.Message}";
                return false;
            }

            // Validation: values must exist and make physical sense.
            if (totalKb <= 0)
            {
                error = "Total memory was missing or invalid.";
                return false;
            }

            if (availableKb < 0)
            {
                error = "Available memory was missing or invalid.";
                return false;
            }

            if (availableKb > totalKb)
            {
                error = "Invalid reading: available memory is larger than total memory.";
                return false;
            }

            double totalGb = totalKb / 1024.0 / 1024.0;
            double availableGb = availableKb / 1024.0 / 1024.0;
            double usedGb = totalGb - availableGb;
            double usedPercentage = usedGb / totalGb * 100.0;

            if (double.IsNaN(usedPercentage) || double.IsInfinity(usedPercentage) ||
                usedPercentage < 0 || usedPercentage > 100)
            {
                error = "Invalid reading: usage percentage was outside 0-100%.";
                return false;
            }

            info = new MemoryInfoModel
            {
                TotalGb = totalGb,
                AvailableGb = availableGb,
                UsedGb = usedGb,
                UsedPercentage = usedPercentage
            };

            return true;
        }
    }
