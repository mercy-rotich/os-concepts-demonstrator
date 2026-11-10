using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OS_Concepts_Demonstrator.Services;

public enum UtmpType : short
{
    Empty = 0,
    RunLevel = 1,
    BootTime = 2,
    NewTime = 3,
    OldTime = 4,
    InitProcess = 5,
    LoginProcess = 6,
    UserProcess = 7,
    DeadProcess = 8,
    Accounting = 9
}

/// <summary>One fixed-size record of the login databases (/run/utmp, /var/log/wtmp).</summary>
public class UtmpRecord
{
    public short Type { get; set; }

    public int Pid { get; set; }

    // Terminal, e.g. "pts/0" or "tty7". For boot records it is "~".
    public string Line { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    // Remote host or display; for boot records, the kernel version.
    public string Host { get; set; } = string.Empty;

    public DateTimeOffset Time { get; set; }
}

/// <summary>
/// Decodes the binary login databases. Each record is 384 bytes on 64-bit Linux (glibc "struct utmp").
/// Read-only; the file is opened with sharing so the system can keep appending to it.
/// </summary>
public static class UtmpReader
{
    public const int RecordSize = 384;

    // Byte offsets inside one record.
    private const int TypeOffset = 0;
    private const int PidOffset = 4;
    private const int LineOffset = 8;      // 32 bytes
    private const int UserOffset = 44;     // 32 bytes
    private const int HostOffset = 76;     // 256 bytes
    private const int SecondsOffset = 340; // 32-bit seconds since 1970

    /// <summary>Returns how many records the file holds and the last <paramref name="maxRecords"/> of them, oldest first.</summary>
    public static (int Total, List<UtmpRecord> Records) ReadLast(string path, int maxRecords)
    {
        var records = new List<UtmpRecord>();

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        int total = (int)(stream.Length / RecordSize);
        int skip = Math.Max(0, total - maxRecords);

        stream.Seek((long)skip * RecordSize, SeekOrigin.Begin);

        byte[] buffer = new byte[RecordSize];

        for (int i = skip; i < total; i++)
        {
            int read = 0;

            while (read < RecordSize)
            {
                int n = stream.Read(buffer, read, RecordSize - read);

                if (n == 0)
                    break;

                read += n;
            }

            if (read < RecordSize)
                break; // a partial record at the end (being written right now)

            records.Add(Decode(buffer));
        }

        return (total, records);
    }

    private static UtmpRecord Decode(byte[] b) => new()
    {
        Type = BitConverter.ToInt16(b, TypeOffset),
        Pid = BitConverter.ToInt32(b, PidOffset),
        Line = ReadText(b, LineOffset, 32),
        User = ReadText(b, UserOffset, 32),
        Host = ReadText(b, HostOffset, 256),
        Time = DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt32(b, SecondsOffset)).ToLocalTime()
    };

    // Text fields are zero-terminated and padded with zeros.
    private static string ReadText(byte[] bytes, int offset, int length)
    {
        int end = Array.IndexOf(bytes, (byte)0, offset, length);
        int count = (end < 0 ? offset + length : end) - offset;

        return Encoding.UTF8.GetString(bytes, offset, count);
    }
}
