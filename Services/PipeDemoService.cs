using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Demonstrates anonymous pipes. A fixed, harmless child process (sed) is started with its
/// standard input and output connected to the application through OS pipes. Text goes in through
/// one pipe and the transformed text comes back through another.
/// The user's text is only ever DATA written to the pipe: it never becomes part of a command.
/// </summary>
public sealed class PipeDemoService : IDisposable
{
    private const int MaxMessageLength = 200;

    private static readonly string[] SedLocations = { "/usr/bin/sed", "/bin/sed" };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _child;

    public long BytesSent { get; private set; }

    public long BytesReceived { get; private set; }

    public string CommandText { get; private set; } = string.Empty;

    public bool IsRunning
    {
        get
        {
            try
            {
                return _child is not null && !_child.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public int? ChildPid => IsRunning ? _child!.Id : null;

    public bool TryStart(out string error)
    {
        error = string.Empty;

        if (IsRunning)
            return true;

        string? sed = SedLocations.FirstOrDefault(File.Exists);

        if (sed is null)
        {
            error = "The 'sed' utility was not found, so the child process cannot be started.";
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = sed,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // No byte-order mark: the child would otherwise receive three stray bytes first.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false)
        };

        // -u = unbuffered, so every reply is sent back immediately.
        // The script turns each input line into upper case.
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(@"s/.*/\U&/");

        try
        {
            Process? child = Process.Start(startInfo);

            if (child is null)
            {
                error = "The child process could not be started.";
                return false;
            }

            // Standard error is also a pipe; read and discard it so it can never fill up.
            child.ErrorDataReceived += (_, _) => { };
            child.BeginErrorReadLine();

            _child = child;
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
        {
            error = $"The child process could not be started: {ex.Message}";
            return false;
        }

        BytesSent = 0;
        BytesReceived = 0;
        CommandText = "sed -u -e 's/.*/\\U&/'   (turns each line into upper case)";
        return true;
    }

    /// <summary>Writes one line into the child's input pipe and waits for its reply on the output pipe.</summary>
    public async Task<(bool Ok, string Text)> ExchangeAsync(string text)
    {
        await _gate.WaitAsync();

        try
        {
            if (!IsRunning || _child is null)
                return (false, "The child process is not running.");

            string line = Sanitize(text);

            await _child.StandardInput.WriteLineAsync(line);
            await _child.StandardInput.FlushAsync();
            BytesSent += Encoding.UTF8.GetByteCount(line) + 1; // +1 for the newline

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            string? reply = await _child.StandardOutput.ReadLineAsync(timeout.Token);

            if (reply is null)
                return (false, "The child closed its end of the pipe (end of file).");

            BytesReceived += Encoding.UTF8.GetByteCount(reply) + 1;
            return (true, reply);
        }
        catch (OperationCanceledException)
        {
            return (false, "No reply within 2 seconds.");
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
        {
            // Writing to a pipe whose reader has gone away is the classic "broken pipe".
            return (false, $"Pipe error: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public string Preview(string text) => Sanitize(text);

    /// <summary>
    /// The child's open file descriptors, read from /proc. Pipe ends show as "pipe:[inode]";
    /// the same inode number appearing in this application proves both ends belong to one pipe.
    /// </summary>
    public List<string> DescribeChildFileDescriptors()
    {
        var lines = new List<string>();

        if (!IsRunning || _child is null)
            return lines;

        HashSet<string> ourPipes = ReadPipeIdentities("/proc/self/fd");

        int inheritedCount = 0;

        foreach ((int fd, string target) in ReadDescriptors($"/proc/{_child.Id}/fd"))
        {
            // Descriptors 0, 1 and 2 are the standard streams the pipes were attached to.
            if (fd > 2)
            {
                inheritedCount++;
                continue;
            }

            string role = fd switch { 0 => "standard input", 1 => "standard output", _ => "standard error" };
            string shared = ourPipes.Contains(target) ? "   <- the other end is open in this application" : string.Empty;

            lines.Add($"fd {fd} ({role})  ->  {target}{shared}");
        }

        // A child inherits every descriptor its parent leaves open (graphics libraries open many).
        // They have nothing to do with the pipes, so they are summarised instead of listed.
        if (inheritedCount > 0)
            lines.Add($"(plus {inheritedCount} other descriptors inherited from this application, such as graphics caches. Not part of the pipes, so not listed.)");

        return lines;
    }

    private static List<(int Fd, string Target)> ReadDescriptors(string directory)
    {
        var result = new List<(int, string)>();

        try
        {
            foreach (FileSystemInfo entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (int.TryParse(entry.Name, out int fd) && entry.LinkTarget is string target)
                    result.Add((fd, target));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Not readable: the caller just shows fewer lines.
        }

        return result.OrderBy(r => r.Item1).ToList();
    }

    private static HashSet<string> ReadPipeIdentities(string directory) =>
        ReadDescriptors(directory)
            .Select(d => d.Target)
            .Where(t => t.StartsWith("pipe:[", StringComparison.Ordinal))
            .ToHashSet();

    private static string Sanitize(string text)
    {
        // One line only, no control characters, limited length.
        var cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();

        return cleaned.Length > MaxMessageLength ? cleaned.Substring(0, MaxMessageLength) : cleaned;
    }

    public void Stop()
    {
        Process? child = _child;
        _child = null;

        if (child is null)
            return;

        try
        {
            if (!child.HasExited)
            {
                // Closing our end of the child's input pipe is a normal end-of-file: sed exits by itself.
                child.StandardInput.Close();

                if (!child.WaitForExit(1000))
                    child.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is Win32Exception)
        {
            // Already gone.
        }
        finally
        {
            child.Dispose();
        }
    }

    public void Dispose()
    {
        Stop();
        _gate.Dispose();
    }
}
