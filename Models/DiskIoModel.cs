namespace OS_Concepts_Demonstrator.Models;

/// <summary>One disk as displayed: totals since boot plus live rates.</summary>
public class DiskIoModel
{
    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    // Live values ("-" until a second reading exists).
    public string ReadRateText { get; set; } = "-";

    public string WriteRateText { get; set; } = "-";

    public string IopsText { get; set; } = "-";

    public string LatencyText { get; set; } = "-";

    public string UtilizationText { get; set; } = "-";

    public double UtilizationPercent { get; set; }

    public string InFlightText { get; set; } = "0";

    // Totals since boot.
    public string TotalReadText { get; set; } = string.Empty;

    public string TotalWrittenText { get; set; } = string.Empty;

    public string TotalOperationsText { get; set; } = string.Empty;

    // Numbers kept for the summary tiles.
    public double? ReadBytesPerSecond { get; set; }

    public double? WriteBytesPerSecond { get; set; }
}
