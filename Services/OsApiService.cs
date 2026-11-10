using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Shows how a program reaches the kernel. Everything here only asks questions: it reads this
/// process's own /proc files, calls read-only C library functions and reads files it is allowed to read.
/// </summary>
public class OsApiService
{
    private const int ClockRealtime = 0;
    private const int ClockMonotonic = 1;
    private const int ScClkTck = 2;
    private const int ScPageSize = 30;
    private const int ScProcessorsOnline = 84;

    private static readonly string[] SyscallHeaderPaths =
    {
        "/usr/include/x86_64-linux-gnu/asm/unistd_64.h",
        "/usr/include/asm/unistd_64.h"
    };

    private static readonly string[] TracerLocations = { "/usr/bin/strace", "/bin/strace", "/usr/bin/ltrace", "/bin/ltrace" };

    private static readonly string[] SmallFileCandidates = { "/etc/os-release", "/usr/lib/os-release", "/etc/hostname" };

    // ---------- Everything the page shows ----------

    public OsApiReadout Collect()
    {
        var readout = new OsApiReadout();

        Dictionary<string, int>? syscallNumbers = ReadSyscallNumbers();
        readout.SyscallsInHeader = syscallNumbers?.Count ?? 0;
        readout.LibcVersion = ReadLibcVersion();

        List<string> mapped = ReadMappedFiles();
        BuildLayers(readout, mapped);
        BuildRestrictions(readout);
        BuildLibcCalls(readout, syscallNumbers);
        BuildSyscallCatalog(readout, syscallNumbers);
        BuildSources(readout, mapped, syscallNumbers);

        return readout;
    }

    // ---------- Layers (this process's memory map) ----------

    // /proc/self/maps lists every file mapped into this process, one mapping per line.
    private static List<string> ReadMappedFiles()
    {
        var files = new List<string>();

        try
        {
            foreach (string line in File.ReadLines("/proc/self/maps"))
            {
                string[] parts = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 6)
                    continue;

                string path = parts[5].Trim();

                if ((path.StartsWith('/') || path == "[vdso]") && !files.Contains(path))
                    files.Add(path);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // The caller reports the layers as unavailable.
        }

        return files;
    }

    private static void BuildLayers(OsApiReadout readout, List<string> mapped)
    {
        string? Find(string fileName) => mapped.FirstOrDefault(p => Path.GetFileName(p).StartsWith(fileName, StringComparison.Ordinal));

        void Add(string title, string? path, string role)
        {
            if (path is not null)
                readout.Layers.Add(new ApiLayerModel { Title = title, Path = path, Role = role });
        }

        Add("Your code and the .NET runtime", Find("libcoreclr"),
            "Runs the C# in this application and compiles it to machine code as it goes (the JIT compiler is libclrjit).");

        Add(".NET system layer", Find("libSystem.Native"),
            "Microsoft's small native library. When C# code calls File.Open or Process.Start, this library turns the request into the matching C library call.");

        Add("C library (glibc)", Find("libc.so"),
            "Provides the standard functions such as open, read, fork and malloc. Most of them wrap one system call, some need none, and some use several.");

        Add("Dynamic loader", Find("ld-linux"),
            "Started before the program itself: it finds and maps the shared libraries listed above, then hands control to the program.");

        Add("vDSO (kernel code inside this process)", mapped.Contains("[vdso]") ? "[vdso]" : null,
            "A few pages of kernel-supplied code mapped into every process. Functions such as reading the clock run there directly, so they need no real system call.");

        string[] shown = readout.Layers.Select(l => l.Path).ToArray();

        foreach (string path in mapped.Where(p => !shown.Contains(p) && p != "[vvar]" && Path.GetFileName(p).Contains(".so", StringComparison.Ordinal)))
            readout.OtherLibraries.Add(path);
    }

    private static string ReadLibcVersion()
    {
        try
        {
            nint pointer = NativeLibc.GetLibcVersion();
            return Marshal.PtrToStringAnsi(pointer) ?? "unknown";
        }
        catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
        {
            return "unavailable";
        }
    }

    // ---------- What this process may ask for ----------

    private static void BuildRestrictions(OsApiReadout readout)
    {
        string? seccomp = null;
        string? noNewPrivs = null;
        string? capabilities = null;

        try
        {
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith("Seccomp:", StringComparison.Ordinal))
                    seccomp = line.Substring(8).Trim();
                else if (line.StartsWith("NoNewPrivs:", StringComparison.Ordinal))
                    noNewPrivs = line.Substring(11).Trim();
                else if (line.StartsWith("CapEff:", StringComparison.Ordinal))
                    capabilities = line.Substring(7).Trim();
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            readout.Restrictions.Add(new DetailRow { Label = "Restrictions", Value = "could not be read" });
            return;
        }

        readout.Restrictions.Add(new DetailRow
        {
            Label = "System-call filter (seccomp)",
            Value = seccomp switch
            {
                "0" => "off: this process may attempt any system call",
                "1" => "strict: only read, write, exit and sigreturn are allowed",
                "2" => "filter active: a sandbox is limiting which system calls this process can make",
                _ => "not reported by this kernel"
            }
        });

        readout.Restrictions.Add(new DetailRow
        {
            Label = "Privileges (capabilities)",
            Value = DescribeCapabilities(capabilities)
        });

        if (noNewPrivs is not null)
        {
            readout.Restrictions.Add(new DetailRow
            {
                Label = "No new privileges",
                Value = noNewPrivs == "1"
                    ? "set: this process can never gain extra rights, even by running a setuid program"
                    : "not set"
            });
        }
    }

    private static string DescribeCapabilities(string? hex)
    {
        if (hex is null || !ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong mask))
            return "not reported by this kernel";

        if (mask == 0)
        {
            return "none: privileged system calls (mounting a disk, changing the clock, rebooting) would be refused with 'operation not permitted'";
        }

        int count = BitCountOf(mask);

        return $"{count} privilege(s) held (mask {hex}). Privileged system calls that need one of them are allowed.";
    }

    private static int BitCountOf(ulong value) => System.Numerics.BitOperations.PopCount(value);

    // ---------- Direct C library calls ----------

    private static void BuildLibcCalls(OsApiReadout readout, Dictionary<string, int>? numbers)
    {
        string Sys(string name) =>
            numbers is not null && numbers.TryGetValue(name, out int n) ? $"the {name} system call (#{n})" : $"the {name} system call";

        void Add(string function, string asks, string behind, Func<(string Libc, string Dotnet, string DotnetWay, ApiVerdict Verdict)> run)
        {
            var model = new LibcCallModel { Function = function, Asks = asks, Behind = behind };

            try
            {
                (string libc, string dotnet, string way, ApiVerdict verdict) = run();

                model.LibcValue = libc;
                model.DotnetValue = dotnet;
                model.DotnetWay = way;
                model.Verdict = verdict;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                model.LibcValue = "unavailable";
                model.DotnetValue = "-";
                model.DotnetWay = "-";
                model.Verdict = ApiVerdict.Unavailable;
                model.Behind = "This system does not provide glibc's libc.so.6, so the call cannot be made.";
            }

            readout.LibcCalls.Add(model);
        }

        string statusField(string key) => ReadStatusField(key);

        Add("getpid()", "the ID of this process", Sys("getpid"), () =>
        {
            int libc = NativeLibc.GetPid();
            int dotnet = Environment.ProcessId;
            return (libc.ToString(CultureInfo.InvariantCulture), dotnet.ToString(CultureInfo.InvariantCulture),
                "Environment.ProcessId", libc == dotnet ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("getppid()", "the ID of this process's parent", Sys("getppid"), () =>
        {
            int libc = NativeLibc.GetParentPid();
            string fromProc = ReadParentPidFromProc();
            return (libc.ToString(CultureInfo.InvariantCulture), fromProc, "reading field 4 of /proc/self/stat",
                libc.ToString(CultureInfo.InvariantCulture) == fromProc ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("getuid()", "the numeric ID of the user running this process", Sys("getuid"), () =>
        {
            uint libc = NativeLibc.GetUserId();
            string fromProc = FirstToken(statusField("Uid"));
            return (libc.ToString(CultureInfo.InvariantCulture), fromProc, "the Uid line of /proc/self/status",
                libc.ToString(CultureInfo.InvariantCulture) == fromProc ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("getgid()", "the numeric ID of this process's main group", Sys("getgid"), () =>
        {
            uint libc = NativeLibc.GetGroupId();
            string fromProc = FirstToken(statusField("Gid"));
            return (libc.ToString(CultureInfo.InvariantCulture), fromProc, "the Gid line of /proc/self/status",
                libc.ToString(CultureInfo.InvariantCulture) == fromProc ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("gethostname()", "the name of this computer", $"{Sys("uname")}, from which glibc takes the node name", () =>
        {
            byte[] buffer = new byte[256];
            string libc = NativeLibc.GetHostName(buffer, (nuint)buffer.Length) == 0 ? ReadText(buffer, 0, buffer.Length) : "failed";
            string dotnet = Environment.MachineName;
            return (libc, dotnet, "Environment.MachineName", libc == dotnet ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("uname()", "kernel name, release and hardware type", Sys("uname"), () =>
        {
            byte[] buffer = new byte[6 * 65];

            if (NativeLibc.UName(buffer) != 0)
                return ("failed", "-", "-", ApiVerdict.Unavailable);

            string kernel = ReadText(buffer, 0, 65);
            string release = ReadText(buffer, 130, 65);
            string machine = ReadText(buffer, 260, 65);
            string description = RuntimeInformation.OSDescription;

            return ($"{kernel} {release} ({machine})", description, "RuntimeInformation.OSDescription",
                description.Contains(release, StringComparison.Ordinal) ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("sysconf(_SC_PAGESIZE)", "the size of one memory page", "no system call: glibc reads it from information the kernel passed when the program started", () =>
        {
            long libc = NativeLibc.SysConf(ScPageSize);
            int dotnet = Environment.SystemPageSize;
            return ($"{libc:N0} bytes", $"{dotnet:N0} bytes", "Environment.SystemPageSize", libc == dotnet ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("sysconf(_SC_NPROCESSORS_ONLN)", "how many CPU cores are online", "no dedicated system call: glibc reads /sys/devices/system/cpu/online (open and read)", () =>
        {
            long libc = NativeLibc.SysConf(ScProcessorsOnline);
            int dotnet = Environment.ProcessorCount;
            return (libc.ToString(CultureInfo.InvariantCulture), dotnet.ToString(CultureInfo.InvariantCulture),
                "Environment.ProcessorCount (can be lower if the app is limited to some cores)", libc == dotnet ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("sysconf(_SC_CLK_TCK)", "how many 'ticks' per second the kernel uses in /proc CPU times", "no system call: a fixed value", () =>
        {
            long libc = NativeLibc.SysConf(ScClkTck);
            long assumed = ProcessAccountingService.TicksPerSecond;
            return ($"{libc} per second", $"{assumed} per second", "the value the Process Accounting page assumes",
                libc == assumed ? ApiVerdict.Same : ApiVerdict.Close);
        });

        Add("clock_gettime(CLOCK_REALTIME)", "the current wall-clock time", "usually no system call: runs inside the vDSO", () =>
        {
            NativeLibc.ClockGetTime(ClockRealtime, out TimeSpec now);
            DateTimeOffset libc = DateTimeOffset.FromUnixTimeSeconds(now.Seconds).AddTicks(now.Nanoseconds / 100);
            DateTime dotnet = DateTime.UtcNow;
            double differenceMs = Math.Abs((dotnet - libc.UtcDateTime).TotalMilliseconds);

            return (libc.UtcDateTime.ToString("HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture),
                dotnet.ToString("HH:mm:ss.fff 'UTC'", CultureInfo.InvariantCulture),
                "DateTime.UtcNow", differenceMs < 100 ? ApiVerdict.Close : ApiVerdict.Unavailable);
        });

        Add("clock_gettime(CLOCK_MONOTONIC)", "how long the system has been awake (never jumps backwards, but does not count time spent suspended)", "usually no system call: runs inside the vDSO", () =>
        {
            NativeLibc.ClockGetTime(ClockMonotonic, out TimeSpec monotonic);
            double libcMs = monotonic.Seconds * 1000.0 + monotonic.Nanoseconds / 1_000_000.0;
            long dotnetMs = Environment.TickCount64;

            return (FormatUptime(libcMs), FormatUptime(dotnetMs), "Environment.TickCount64",
                Math.Abs(libcMs - dotnetMs) < 250 ? ApiVerdict.Close : ApiVerdict.Unavailable);
        });

        Add("gnu_get_libc_version()", "which version of the C library is loaded", "no system call: a constant inside the library", () =>
            (readout.LibcVersion, "-", "nothing in .NET reports this", ApiVerdict.NoEquivalent));
    }

    private static string FormatUptime(double milliseconds)
    {
        TimeSpan span = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m {span.Seconds}s";
    }

    private static string ReadText(byte[] bytes, int offset, int length)
    {
        int end = Array.IndexOf(bytes, (byte)0, offset, length);
        int count = (end < 0 ? offset + length : end) - offset;

        return Encoding.UTF8.GetString(bytes, offset, count);
    }

    private static string FirstToken(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "?";

    private static string ReadStatusField(string key)
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith(key + ":", StringComparison.Ordinal))
                    return line.Substring(key.Length + 1).Trim();
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Falls through.
        }

        return string.Empty;
    }

    private static string ReadParentPidFromProc()
    {
        try
        {
            string text = File.ReadAllText("/proc/self/stat");
            int close = text.LastIndexOf(')');

            // After the closing parenthesis: state, then the parent's PID.
            string[] fields = text.Substring(close + 2).Split(' ');
            return fields.Length > 1 ? fields[1] : "?";
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return "?";
        }
    }

    // ---------- System-call numbers ----------

    // The kernel headers define "#define __NR_read 0" for every system call. The numbers differ per
    // processor architecture, so they are only read for x86-64, where the header layout is known.
    private static Dictionary<string, int>? ReadSyscallNumbers()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return null;

        string? path = SyscallHeaderPaths.FirstOrDefault(File.Exists);

        if (path is null)
            return null;

        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            foreach (string line in File.ReadLines(path))
            {
                const string prefix = "#define __NR_";

                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                string[] parts = line.Substring(prefix.Length).Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                    numbers[parts[0]] = number;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }

        return numbers.Count > 0 ? numbers : null;
    }

    private static void BuildSyscallCatalog(OsApiReadout readout, Dictionary<string, int>? numbers)
    {
        SyscallGroupModel Group(string title)
        {
            var group = new SyscallGroupModel { Title = title };
            readout.SyscallGroups.Add(group);
            return group;
        }

        void Add(SyscallGroupModel group, string name, string meaning, string dotnet, string usedHere) =>
            group.Items.Add(new SyscallInfoModel
            {
                Name = name,
                Number = numbers is not null && numbers.TryGetValue(name, out int n) ? n : null,
                Meaning = meaning,
                DotnetApi = dotnet,
                UsedHere = usedHere
            });

        SyscallGroupModel files = Group("Files and input/output");
        Add(files, "openat", "Open a file and receive a file descriptor (a small number that stands for the open file).", "File.Open, File.ReadAllText", "Every page that reads /proc or /sys.");
        Add(files, "read", "Copy bytes from an open file, pipe or socket into memory.", "FileStream.Read, StreamReader", "I/O Management and Process Accounting readings; the pipe demo.");
        Add(files, "write", "Copy bytes from memory into a file, pipe or socket.", "FileStream.Write, StreamWriter", "The pipe demo sending a line to the child.");
        Add(files, "close", "Release a file descriptor.", "FileStream.Dispose", "Every file the app opens.");
        Add(files, "getdents64", "List the entries of a directory.", "Directory.EnumerateFileSystemInfos", "The File & Storage browser; the scan of /proc.");
        Add(files, "statx", "Read a file's size, permissions, owner and times.", "FileInfo.Length, File.GetUnixFileMode", "File & Storage details; System Audit permission checks.");

        SyscallGroupModel processes = Group("Processes and threads");
        Add(processes, "clone", "Create a new process or thread.", "new Thread, Task.Run, Process.Start", "The sed child in the pipe demo; every background task.");
        Add(processes, "execve", "Replace the running program with another one.", "Process.Start", "Starting sed, head and systemctl.");
        Add(processes, "wait4", "Wait for a child to finish and collect its exit status.", "Process.WaitForExit", "The OS Services and IPC pages.");
        Add(processes, "exit_group", "End the whole process.", "returning from Main, Environment.Exit", "Closing the application.");
        Add(processes, "getpid", "Ask for the caller's process ID.", "Environment.ProcessId", "Shown on this page.");

        SyscallGroupModel memory = Group("Memory");
        Add(memory, "mmap", "Map memory, or a file, into the process's address space.", "MemoryMappedFile, garbage-collector heap growth", "The shared-memory demo.");
        Add(memory, "munmap", "Remove a mapping.", "disposing a memory-mapped view", "Cleaning up the shared-memory demo.");
        Add(memory, "mprotect", "Change whether memory may be read, written or executed.", "the JIT compiler preparing code", "The .NET runtime, all the time.");
        Add(memory, "brk", "Grow the traditional heap.", "C library malloc", "Inside the C library.");

        SyscallGroupModel ipc = Group("Communication and synchronisation");
        Add(ipc, "pipe2", "Create a pipe: two connected file descriptors.", "Process with redirected input and output", "The pipe demo.");
        Add(ipc, "futex", "A fast lock: sleep until another thread wakes you.", "lock, Monitor, SemaphoreSlim", "The race demo and the message queue.");
        Add(ipc, "socket", "Create a communication endpoint.", "Socket, NamedPipeServerStream", "The connection to the display server.");

        SyscallGroupModel time = Group("Time and system information");
        Add(time, "clock_gettime", "Read a clock.", "DateTime.UtcNow, Stopwatch", "Every timestamp; normally served by the vDSO without entering the kernel.");
        Add(time, "clock_nanosleep", "Sleep for a given time.", "Thread.Sleep, Task.Delay", "The refresh timers.");
        Add(time, "uname", "Kernel name, release and hardware type.", "RuntimeInformation.OSDescription", "The Dashboard's operating system line.");
    }

    // ---------- This process's own system-call counters ----------

    /// <summary>The counters from /proc/self/io: bytes and the number of read and write system calls.</summary>
    public static (long ReadBytes, long WriteBytes, long ReadCalls, long WriteCalls)? ReadOwnCounters()
    {
        long readBytes = 0, writeBytes = 0, readCalls = 0, writeCalls = 0;

        try
        {
            foreach (string line in File.ReadLines("/proc/self/io"))
            {
                int colon = line.IndexOf(':');

                if (colon <= 0 || !long.TryParse(line.AsSpan(colon + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long value))
                    continue;

                switch (line.Substring(0, colon))
                {
                    case "rchar": readBytes = value; break;
                    case "wchar": writeBytes = value; break;
                    case "syscr": readCalls = value; break;
                    case "syscw": writeCalls = value; break;
                }
            }

            return (readBytes, writeBytes, readCalls, writeCalls);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }
    }

    public enum ExperimentKind
    {
        ReadSmallFileOnce,
        ReadSmallFile200Times,
        ReadBytesOneAtATime,
        ReadBytesInOneCall
    }

    private const int BytesToRead = 5000;

    /// <summary>Runs one measured experiment: counters before, the work, counters after.</summary>
    public async Task<SyscallExperimentModel> RunExperimentAsync(ExperimentKind kind)
    {
        return await Task.Run(() => RunExperiment(kind));
    }

    private static SyscallExperimentModel RunExperiment(ExperimentKind kind)
    {
        var result = new SyscallExperimentModel { Time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) };

        // Decide which files to use BEFORE measuring: finding them reads other files, which would be counted.
        string smallFile = SmallFileCandidates.FirstOrDefault(File.Exists) ?? "/etc/hostname";
        string? bigFile = ReadMappedFiles().FirstOrDefault(p => Path.GetFileName(p).StartsWith("libc.so", StringComparison.Ordinal));

        // Taking a measurement is itself a few reads of /proc/self/io. Measure that cost once with
        // two back-to-back readings, so it can be subtracted from the experiment's numbers.
        var calibrationStart = ReadOwnCounters();
        var calibrationEnd = ReadOwnCounters();

        if (calibrationStart is null || calibrationEnd is null)
        {
            result.Title = "Experiment could not run";
            result.Explanation = "This process's I/O counters (/proc/self/io) could not be read, so system calls cannot be counted.";
            return result;
        }

        long overheadCalls = calibrationEnd.Value.ReadCalls - calibrationStart.Value.ReadCalls;
        long overheadBytes = calibrationEnd.Value.ReadBytes - calibrationStart.Value.ReadBytes;

        var before = ReadOwnCounters();

        if (before is null)
        {
            result.Title = "Experiment could not run";
            result.Explanation = "The counters could not be read.";
            return result;
        }

        var clock = Stopwatch.StartNew();

        try
        {
            switch (kind)
            {
                case ExperimentKind.ReadSmallFileOnce:
                    result.Title = $"Read {Path.GetFileName(smallFile)} once with File.ReadAllBytes";
                    File.ReadAllBytes(smallFile);
                    result.Explanation =
                        "One C# call. Behind it the library opens the file, asks for its size, reads it with a single read system call, and closes it. " +
                        "Only the read calls are counted here (open, size and close are different system calls).";
                    break;

                case ExperimentKind.ReadSmallFile200Times:
                    result.Title = $"Read {Path.GetFileName(smallFile)} 200 times";

                    for (int i = 0; i < 200; i++)
                        File.ReadAllBytes(smallFile);

                    result.Explanation = "200 C# calls. The number of system calls grows in step with them: each call to the library costs its own trips into the kernel.";
                    break;

                case ExperimentKind.ReadBytesOneAtATime:
                    result.Title = $"Read {BytesToRead:N0} bytes one byte at a time, with no buffer";
                    ReadOneByteAtATime(bigFile ?? smallFile);
                    result.Explanation =
                        "With buffering switched off, every ReadByte() call asks the kernel for exactly one byte. " +
                        "Each trip into the kernel has a fixed cost (switching mode, checking permissions), so this is very wasteful.";
                    break;

                default:
                    result.Title = $"Read the same {BytesToRead:N0} bytes with a single call";
                    ReadInOneCall(bigFile ?? smallFile);
                    result.Explanation = "The same data, requested in one go. One trip into the kernel brings back all of it. This is why programs read in large blocks and why stream classes keep a buffer.";
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            result.Title = "Experiment failed";
            result.Explanation = $"The file could not be read: {ex.Message}";
            return result;
        }

        clock.Stop();

        var after = ReadOwnCounters();

        if (after is null)
        {
            result.Explanation = "The counters could not be read afterwards.";
            return result;
        }

        // Subtract the cost of the measurement itself; never show a negative number.
        long calls = Math.Max(0, after.Value.ReadCalls - before.Value.ReadCalls - overheadCalls);
        long bytes = Math.Max(0, after.Value.ReadBytes - before.Value.ReadBytes - overheadBytes);

        result.SyscallsText = $"{calls:N0} read system calls";
        result.BytesText = $"{ByteFormatter.Format(bytes)} read";
        result.ElapsedText = clock.Elapsed.TotalMilliseconds < 1 ? "under 1 ms" : $"{clock.Elapsed.TotalMilliseconds:F1} ms";

        // Other threads of the application may read at the same moment, so this is a close count, not an exact one.
        result.Explanation += "  (Counted from /proc/self/io; other threads of this app reading at the same moment could add a few.)";

        return result;
    }

    private static void ReadOneByteAtATime(string path)
    {
        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = 0 // no buffer: every read goes straight to the kernel
        });

        for (int i = 0; i < BytesToRead; i++)
        {
            if (stream.ReadByte() < 0)
                break;
        }
    }

    private static void ReadInOneCall(string path)
    {
        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = 0
        });

        byte[] buffer = new byte[BytesToRead];
        stream.ReadAtLeast(buffer, 1, throwOnEndOfStream: false);
    }

    // ---------- What this system provides ----------

    private void BuildSources(OsApiReadout readout, List<string> mapped, Dictionary<string, int>? numbers)
    {
        DataSourceStatusModel Source(string name, string provides, DataAvailability availability, string detail) =>
            new() { Name = name, Provides = provides, Availability = availability, Detail = detail };

        bool libcWorks = readout.LibcVersion != "unavailable";

        readout.Sources.Add(libcWorks
            ? Source("Calling the C library directly", "libc.so.6 (P/Invoke)", DataAvailability.Available,
                $"glibc {readout.LibcVersion} is loaded and {readout.LibcCalls.Count(c => !c.IsUnavailable)} of {readout.LibcCalls.Count} functions answered.")
            : Source("Calling the C library directly", "libc.so.6 (P/Invoke)", DataAvailability.Unavailable,
                "glibc could not be loaded (a different C library or operating system), so direct calls are skipped."));

        readout.Sources.Add(mapped.Count > 0
            ? Source("Libraries loaded into this process", "/proc/self/maps", DataAvailability.Available, $"{mapped.Count} files are mapped into this process's memory.")
            : Source("Libraries loaded into this process", "/proc/self/maps", DataAvailability.Unavailable, "The memory map could not be read."));

        readout.Sources.Add(numbers is not null
            ? Source("System-call numbers", "Linux kernel headers (unistd_64.h)", DataAvailability.Available,
                $"{numbers.Count} system calls are defined in the headers installed on this computer (x86-64). These headers describe the kernel interface they were built from, not necessarily the exact running kernel.")
            : Source("System-call numbers", "Linux kernel headers (unistd_64.h)", DataAvailability.Unavailable,
                RuntimeInformation.ProcessArchitecture == Architecture.X64
                    ? "The kernel header file was not found (the linux-libc-dev package is not installed), so numbers are shown as n/a."
                    : $"System-call numbers differ per processor type and are only read for x86-64, not {RuntimeInformation.ProcessArchitecture}."));

        readout.Sources.Add(ReadOwnCounters() is not null
            ? Source("This app's own system-call counters", "/proc/self/io (syscr, syscw)", DataAvailability.Available,
                "The kernel counts every read-type and write-type system call this process makes. The experiments below use these counters.")
            : Source("This app's own system-call counters", "/proc/self/io (syscr, syscw)", DataAvailability.Unavailable, "The counters could not be read."));

        string? tracer = TracerLocations.FirstOrDefault(File.Exists);

        readout.Sources.Add(tracer is null
            ? Source("Live tracing of every system call", "strace or ltrace", DataAvailability.Unavailable,
                "No tracing tool is installed on this system, so a complete list of the system calls a program makes cannot be shown. " +
                "With strace installed, running 'strace -c ls' in a terminal would list every call 'ls' makes and how often.")
            : Source("Live tracing of every system call", Path.GetFileName(tracer), DataAvailability.Partial,
                $"{tracer} is installed. This page does not run it, so it never attaches to any process. Try 'strace -c ls' in a terminal."));
    }
}
