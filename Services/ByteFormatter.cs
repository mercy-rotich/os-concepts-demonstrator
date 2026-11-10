using System.Globalization;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>Turns a byte count into readable text such as "1.5 GB".</summary>
public static class ByteFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes < 0)
            return "N/A";

        double value = bytes;
        int unit = 0;

        // Divide by 1024 until the number is small enough to read comfortably.
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Whole bytes need no decimals; larger units get one.
        string number = unit == 0
            ? value.ToString("F0", CultureInfo.InvariantCulture)
            : value.ToString("F1", CultureInfo.InvariantCulture);

        return $"{number} {Units[unit]}";
    }
}
