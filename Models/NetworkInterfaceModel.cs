namespace OS_Concepts_Demonstrator.Models;

public class NetworkInterfaceModel
{
    public string Name { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string DownloadRateText { get; set; } = "-";

    public string UploadRateText { get; set; } = "-";

    public string TotalReceivedText { get; set; } = string.Empty;

    public string TotalSentText { get; set; } = string.Empty;

    public string PacketsText { get; set; } = string.Empty;

    public string ProblemsText { get; set; } = string.Empty;

    // Real traffic only: loopback and virtual interfaces are left out of the summary tiles.
    public bool CountsTowardsSummary { get; set; }

    public double? RxBytesPerSecond { get; set; }

    public double? TxBytesPerSecond { get; set; }
}
