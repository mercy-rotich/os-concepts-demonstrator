using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>Outcome of the race-condition demonstration.</summary>
public record RaceDemoResult(int Expected, int WithoutLock, int WithLock);

/// <summary>
/// Demonstrates shared memory with a memory-mapped file in /dev/shm (RAM-backed, like shm_open).
/// The region is mapped twice: a writer view and a read-only reader view see the very same RAM.
/// A child process can open the same object, and /proc/self/maps shows the mappings.
/// The file is private (permissions 0600), randomly named, and deleted in Dispose.
/// </summary>
public sealed class SharedMemoryDemoService : IDisposable
{
    public const int RegionSize = 4096;

    private const int MaxTextChars = 250;          // 250 characters is at most 1000 UTF-8 bytes
    private const int LengthOffset = RegionSize - 4;   // int: how many text bytes are valid
    private const int CounterOffset = RegionSize - 8;  // int: shared counter for the race demo

    private static readonly string[] HeadLocations = { "/usr/bin/head", "/bin/head" };

    // Cleanup hooks for termination by a signal (closing the terminal, kill, Ctrl+C).
    private readonly List<PosixSignalRegistration> _signalHooks = new();

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _writerView;
    private MemoryMappedViewAccessor? _readerView;

    public string? BackingPath { get; private set; }

    public bool IsRamBacked => BackingPath?.StartsWith("/dev/shm/", StringComparison.Ordinal) == true;

    public bool IsReady => _map is not null;

    public bool TryInitialize(out string error)
    {
        error = string.Empty;

        if (IsReady)
            return true;

        string directory = Directory.Exists("/dev/shm") ? "/dev/shm" : Path.GetTempPath();
        string path = Path.Combine(directory, $"osdemo-{Guid.NewGuid():N}.shm");

        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite
            };

            // Only this user may touch the region (permissions 0600). Unix-only setting.
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            var stream = new FileStream(path, options);

            stream.SetLength(RegionSize);

            _map = MemoryMappedFile.CreateFromFile(
                stream, null, RegionSize, MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None, leaveOpen: false);

            _writerView = _map.CreateViewAccessor(0, RegionSize, MemoryMappedFileAccess.ReadWrite);
            _readerView = _map.CreateViewAccessor(0, RegionSize, MemoryMappedFileAccess.Read);

            BackingPath = path;

            // If the application is stopped by a signal, Dispose would never run and the file
            // would stay in /dev/shm. Clean up on normal exit and on the common termination signals.
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            RegisterSignalCleanup();

            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                   ex is ArgumentException || ex is NotSupportedException)
        {
            BackingPath = path;
            Dispose();
            error = $"The shared memory region could not be created: {ex.Message}";
            return false;
        }
    }

    /// <summary>Writes text into shared memory through the writer view. Returns the number of bytes written.</summary>
    public int Write(string text)
    {
        if (_writerView is null)
            return 0;

        string cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (cleaned.Length > MaxTextChars)
            cleaned = cleaned.Substring(0, MaxTextChars);

        byte[] bytes = Encoding.UTF8.GetBytes(cleaned);

        _writerView.WriteArray(0, bytes, 0, bytes.Length);

        // The length goes last, so a reader never sees a length that is ahead of the data.
        _writerView.Write(LengthOffset, bytes.Length);

        return bytes.Length;
    }

    /// <summary>Reads the text back through the independent reader view.</summary>
    public string ReadThroughSecondView()
    {
        if (_readerView is null)
            return string.Empty;

        int length = Math.Clamp(_readerView.ReadInt32(LengthOffset), 0, MaxTextChars * 4);
        byte[] bytes = new byte[length];

        _readerView.ReadArray(0, bytes, 0, length);

        return Encoding.UTF8.GetString(bytes);
    }

    public int CurrentLength => _readerView is null ? 0 : Math.Clamp(_readerView.ReadInt32(LengthOffset), 0, MaxTextChars * 4);

    /// <summary>Starts a separate process (head) that reads the same shared-memory object.</summary>
    public async Task<(bool Ok, string Text, int Pid)> ReadWithChildProcessAsync()
    {
        string? head = HeadLocations.FirstOrDefault(File.Exists);
        int length = CurrentLength;

        if (BackingPath is null)
            return (false, "The region does not exist.", 0);

        if (head is null)
            return (false, "The 'head' utility was not found.", 0);

        if (length == 0)
            return (false, "Nothing has been written yet.", 0);

        var startInfo = new ProcessStartInfo
        {
            FileName = head,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = new UTF8Encoding(false)
        };

        // Fixed arguments: a byte count we computed and the path we created ourselves.
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(BackingPath);

        try
        {
            using Process? child = Process.Start(startInfo);

            if (child is null)
                return (false, "The child process could not be started.", 0);

            int pid = child.Id;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

            string output = await child.StandardOutput.ReadToEndAsync(timeout.Token);
            await child.WaitForExitAsync(timeout.Token);

            return (true, output, pid);
        }
        catch (OperationCanceledException)
        {
            return (false, "The child process did not finish in time.", 0);
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
        {
            return (false, $"The child process failed: {ex.Message}", 0);
        }
    }

    /// <summary>The lines of /proc/self/maps that belong to our region: address range, permissions, file.</summary>
    public List<string> GetMappingLines()
    {
        var lines = new List<string>();

        if (BackingPath is null)
            return lines;

        string fileName = Path.GetFileName(BackingPath);

        try
        {
            foreach (string line in File.ReadLines("/proc/self/maps"))
            {
                if (line.Contains(fileName, StringComparison.Ordinal))
                {
                    // Collapse the padding so the line fits the screen.
                    lines.Add(string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // /proc not readable: show nothing.
        }

        return lines;
    }

    /// <summary>
    /// Two threads each add 1 to a counter that lives in shared memory, first without any lock and then with one.
    /// Without a lock, the read-then-write steps of the two threads interleave and some additions are lost.
    /// </summary>
    public RaceDemoResult RunRaceDemo(int iterationsPerWorker)
    {
        MemoryMappedViewAccessor view = _writerView ?? throw new InvalidOperationException("Region is not ready.");

        int withoutLock = RunWorkers(view, iterationsPerWorker, useLock: false);
        int withLock = RunWorkers(view, iterationsPerWorker, useLock: true);

        return new RaceDemoResult(iterationsPerWorker * 2, withoutLock, withLock);
    }

    private static int RunWorkers(MemoryMappedViewAccessor view, int iterations, bool useLock)
    {
        view.Write(CounterOffset, 0);

        object gate = new();

        void Work()
        {
            for (int i = 0; i < iterations; i++)
            {
                if (useLock)
                {
                    lock (gate)
                    {
                        view.Write(CounterOffset, view.ReadInt32(CounterOffset) + 1);
                    }
                }
                else
                {
                    // Read, add, write back: another thread can slip in between these steps.
                    int value = view.ReadInt32(CounterOffset);
                    view.Write(CounterOffset, value + 1);
                }
            }
        }

        Task first = Task.Factory.StartNew(Work, TaskCreationOptions.LongRunning);
        Task second = Task.Factory.StartNew(Work, TaskCreationOptions.LongRunning);

        Task.WaitAll(first, second);

        return view.ReadInt32(CounterOffset);
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    private void RegisterSignalCleanup()
    {
        foreach (PosixSignal signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP })
        {
            try
            {
                _signalHooks.Add(PosixSignalRegistration.Create(signal, _ =>
                {
                    // Remove the file, then let the normal action (terminate) go ahead:
                    // we do not set context.Cancel, so the default behaviour still happens.
                    Dispose();
                }));
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is ArgumentException)
            {
                // This signal is not available here; skip it.
            }
        }
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        foreach (PosixSignalRegistration hook in _signalHooks)
            hook.Dispose();

        _signalHooks.Clear();

        _writerView?.Dispose();
        _readerView?.Dispose();
        _map?.Dispose();

        _writerView = null;
        _readerView = null;
        _map = null;

        if (BackingPath is not null)
        {
            try
            {
                File.Delete(BackingPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Leave it; it is in a temporary location and uniquely named.
            }

            BackingPath = null;
        }
    }
}
