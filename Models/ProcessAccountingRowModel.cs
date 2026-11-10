namespace OS_Concepts_Demonstrator.Models;

/// <summary>One process as shown in the accounting table.</summary>
public class ProcessAccountingRowModel
{
    public int Pid { get; set; }

    public string Name { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string CpuTotalText { get; set; } = string.Empty;

    public double CpuTotalSeconds { get; set; }

    // Share of one CPU core used since the previous snapshot; null until two snapshots exist.
    public double? CpuPercent { get; set; }

    public string CpuPercentText { get; set; } = "-";

    public string MemoryText { get; set; } = "-";

    public long ResidentBytes { get; set; }

    public string IoText { get; set; } = string.Empty;

    public bool IsIoRestricted { get; set; }

    public bool IsKernelThread { get; set; }

    public System.DateTimeOffset StartTime { get; set; }
}
