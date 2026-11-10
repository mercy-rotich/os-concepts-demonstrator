using System.Globalization;

namespace OS_Concepts_Demonstrator.Models;

public class InterruptSourceModel
{
    public string Irq { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    // Total across all CPUs since boot.
    public long Count { get; set; }

    public string CountText => Count.ToString("N0", CultureInfo.InvariantCulture);
}
