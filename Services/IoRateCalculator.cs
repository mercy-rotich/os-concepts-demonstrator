using System.Globalization;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Turns two snapshots of cumulative counters into live rates.
/// rate = (counter now - counter before) / seconds between the two readings.
/// A rate is null when it cannot be known (first reading, or a counter went backwards).
/// </summary>
public static class IoRateCalculator
{
    // Readings closer together than this give meaningless rates.
    private const double MinIntervalSeconds = 0.2;

    private const int SectorBytes = 512; // /proc/diskstats always counts 512-byte sectors

    public static double? IntervalSeconds(IoSnapshot? previous, IoSnapshot current)
    {
        if (previous is null)
            return null;

        double seconds = (current.TimestampMs - previous.TimestampMs) / 1000.0;
        return seconds >= MinIntervalSeconds ? seconds : null;
    }

    public static double? RatePerSecond(long previous, long current, double? seconds)
    {
        // Negative change means the counter reset or wrapped; do not report nonsense.
        if (seconds is null || current < previous)
            return null;

        return (current - previous) / seconds.Value;
    }

    public static DiskIoModel BuildDisk(
        DiskCounters current, DiskCounters? previous, double? seconds, DiskDetailsModel details)
    {
        var model = new DiskIoModel
        {
            Name = current.Name,
            Title = string.IsNullOrWhiteSpace(details.Model) ? $"Disk {current.Name}" : details.Model!,
            Subtitle = $"/dev/{current.Name}  |  {ByteFormatter.Format(details.SizeBytes)}  |  {details.TypeText}  |  I/O scheduler: {details.SchedulerText}",
            InFlightText = current.InFlight.ToString(CultureInfo.InvariantCulture),
            TotalReadText = ByteFormatter.Format(current.SectorsRead * SectorBytes),
            TotalWrittenText = ByteFormatter.Format(current.SectorsWritten * SectorBytes),
            TotalOperationsText =
                $"{current.ReadsCompleted:N0} reads, {current.WritesCompleted:N0} writes"
        };

        if (previous is null || seconds is null)
            return model;

        double? readRate = RatePerSecond(previous.SectorsRead, current.SectorsRead, seconds);
        double? writeRate = RatePerSecond(previous.SectorsWritten, current.SectorsWritten, seconds);

        model.ReadBytesPerSecond = readRate * SectorBytes;
        model.WriteBytesPerSecond = writeRate * SectorBytes;
        model.ReadRateText = FormatBytesPerSecond(model.ReadBytesPerSecond);
        model.WriteRateText = FormatBytesPerSecond(model.WriteBytesPerSecond);

        // IOPS = completed read + write operations per second.
        long operationsNow = current.ReadsCompleted + current.WritesCompleted;
        long operationsBefore = previous.ReadsCompleted + previous.WritesCompleted;
        double? iops = RatePerSecond(operationsBefore, operationsNow, seconds);

        if (iops is not null)
            model.IopsText = iops.Value.ToString("F0", CultureInfo.InvariantCulture);

        // Latency = time spent per operation = (ms spent reading and writing) / (operations completed).
        long operations = operationsNow - operationsBefore;
        long busyMs = (current.ReadTimeMs + current.WriteTimeMs) - (previous.ReadTimeMs + previous.WriteTimeMs);

        if (operations > 0 && busyMs >= 0)
            model.LatencyText = $"{(double)busyMs / operations:F2} ms";
        else if (operations == 0)
            model.LatencyText = "idle";

        // Utilisation = share of the interval in which at least one request was pending.
        if (current.IoTimeMs >= previous.IoTimeMs)
        {
            double busyShare = (current.IoTimeMs - previous.IoTimeMs) / (seconds.Value * 1000.0) * 100.0;
            model.UtilizationPercent = System.Math.Clamp(busyShare, 0, 100);
            model.UtilizationText = $"{model.UtilizationPercent:F0}%";
        }

        return model;
    }

    public static NetworkInterfaceModel BuildNetwork(
        NetworkCounters current, NetworkCounters? previous, double? seconds)
    {
        var model = new NetworkInterfaceModel
        {
            Name = current.Name,
            Subtitle = $"{current.Kind}  |  link: {current.State}",
            TotalReceivedText = $"{ByteFormatter.Format(current.RxBytes)} ({current.RxPackets:N0} packets)",
            TotalSentText = $"{ByteFormatter.Format(current.TxBytes)} ({current.TxPackets:N0} packets)",
            ProblemsText = $"Receive: {current.RxErrors:N0} errors, {current.RxDropped:N0} dropped   |   " +
                           $"Transmit: {current.TxErrors:N0} errors, {current.TxDropped:N0} dropped",
            CountsTowardsSummary = current.Kind == "Wired" || current.Kind == "Wireless"
        };

        if (previous is null || seconds is null)
            return model;

        model.RxBytesPerSecond = RatePerSecond(previous.RxBytes, current.RxBytes, seconds);
        model.TxBytesPerSecond = RatePerSecond(previous.TxBytes, current.TxBytes, seconds);
        model.DownloadRateText = FormatBytesPerSecond(model.RxBytesPerSecond);
        model.UploadRateText = FormatBytesPerSecond(model.TxBytesPerSecond);

        return model;
    }

    public static string FormatBytesPerSecond(double? bytesPerSecond) =>
        bytesPerSecond is null ? "-" : ByteFormatter.Format((long)bytesPerSecond.Value) + "/s";

    public static string FormatCountPerSecond(double? perSecond) =>
        perSecond is null ? "-" : perSecond.Value.ToString("N0", CultureInfo.InvariantCulture) + "/s";
}
