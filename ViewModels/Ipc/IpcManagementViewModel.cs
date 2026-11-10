using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Ipc;

public partial class IpcManagementViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogLines = 60;
    private const int RaceIterations = 1_000_000;

    private readonly CancellationTokenSource _lifetime = new();
    private bool _isDisposed;

    // ---------- Pipe ----------
    private PipeDemoService? _pipe;

    public ObservableCollection<IpcLogEntry> PipeLog { get; } = new();

    public ObservableCollection<string> PipeDescriptors { get; } = new();

    [ObservableProperty]
    private string pipeInput = "hello from the parent process";

    [ObservableProperty]
    private string pipeStatus = "The child process has not been started.";

    [ObservableProperty]
    private string pipeCounters = string.Empty;

    [ObservableProperty]
    private string pipeTerminalHint = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPipeCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendPipeMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopPipeCommand))]
    private bool isPipeRunning;

    // ---------- Message queue ----------
    private readonly MessageQueueDemoService _queue = new();
    private CancellationTokenSource? _burstCts;
    private int _blockedSenders;

    public ObservableCollection<IpcLogEntry> QueueLog { get; } = new();

    public int QueueCapacity => MessageQueueDemoService.Capacity;

    [ObservableProperty]
    private string queueInput = "hello";

    [ObservableProperty]
    private string queueDepthText = $"Waiting in queue: 0 of {MessageQueueDemoService.Capacity}";

    [ObservableProperty]
    private double queueDepthPercent;

    [ObservableProperty]
    private string queueCounters = "Sent: 0   Received: 0";

    [ObservableProperty]
    private bool isSenderBlocked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendBurstCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBurstCommand))]
    private bool isBurstRunning;

    // ---------- Shared memory ----------
    private SharedMemoryDemoService? _shared;

    public ObservableCollection<IpcLogEntry> SharedLog { get; } = new();

    public ObservableCollection<string> MappingLines { get; } = new();

    [ObservableProperty]
    private string sharedInput = "written straight into shared RAM";

    [ObservableProperty]
    private string sharedInfo = "The region is created the first time you use it.";

    [ObservableProperty]
    private string sharedTerminalHint = string.Empty;

    [ObservableProperty]
    private string raceResult = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunRaceDemoCommand))]
    private bool isRaceRunning;

    public IpcManagementViewModel()
    {
        _queue.EventOccurred += OnQueueEvent;
    }

    // =====================================================================
    //  PIPE
    // =====================================================================

    private bool CanStartPipe() => !IsPipeRunning;

    [RelayCommand(CanExecute = nameof(CanStartPipe))]
    private void StartPipe()
    {
        _pipe?.Dispose();
        _pipe = new PipeDemoService();

        if (!_pipe.TryStart(out string error))
        {
            AddLog(PipeLog, "Parent", error, warning: true);
            PipeStatus = "Could not start the child process.";
            _pipe.Dispose();
            _pipe = null;
            return;
        }

        IsPipeRunning = true;
        PipeStatus = $"Child process running (PID {_pipe.ChildPid}): {_pipe.CommandText}";
        PipeTerminalHint = $"In a terminal:  ls -l /proc/{_pipe.ChildPid}/fd";

        AddLog(PipeLog, "Parent", $"Created three pipes and started the child (PID {_pipe.ChildPid}). Its standard input, output and error are now pipe ends.");
        RefreshPipeDescriptors();
        UpdatePipeCounters();
    }

    private bool CanSendPipeMessage() => IsPipeRunning;

    [RelayCommand(CanExecute = nameof(CanSendPipeMessage))]
    private async Task SendPipeMessageAsync()
    {
        PipeDemoService? pipe = _pipe;

        if (pipe is null || string.IsNullOrWhiteSpace(PipeInput))
            return;

        string line = pipe.Preview(PipeInput);

        AddLog(PipeLog, "Parent", $"write(pipe) -> \"{line}\"");

        (bool ok, string text) = await pipe.ExchangeAsync(PipeInput);

        if (_isDisposed)
            return;

        if (ok)
        {
            AddLog(PipeLog, "Child", $"read(pipe), changed it, write(pipe) -> \"{text}\"");
            AddLog(PipeLog, "Parent", $"read(pipe) <- \"{text}\"");
        }
        else
        {
            AddLog(PipeLog, "Parent", text, warning: true);

            if (!pipe.IsRunning)
                MarkPipeStopped();
        }

        UpdatePipeCounters();
    }

    private bool CanStopPipe() => IsPipeRunning;

    [RelayCommand(CanExecute = nameof(CanStopPipe))]
    private void StopPipe()
    {
        AddLog(PipeLog, "Parent", "Closed the write end of the pipe. The child reads end-of-file and exits by itself.");
        StopPipeChild();
    }

    [RelayCommand]
    private void RefreshPipeDescriptors()
    {
        PipeDescriptors.Clear();

        if (_pipe is null)
            return;

        foreach (string line in _pipe.DescribeChildFileDescriptors())
            PipeDescriptors.Add(line);
    }

    private void StopPipeChild()
    {
        _pipe?.Dispose();
        _pipe = null;
        MarkPipeStopped();
    }

    private void MarkPipeStopped()
    {
        IsPipeRunning = false;
        PipeStatus = "The child process has exited. Start it again to repeat the demo.";
        PipeTerminalHint = string.Empty;
        PipeDescriptors.Clear();
    }

    private void UpdatePipeCounters()
    {
        PipeCounters = _pipe is null
            ? string.Empty
            : $"Bytes written to the child: {_pipe.BytesSent}   |   bytes read back: {_pipe.BytesReceived}";
    }

    // =====================================================================
    //  MESSAGE QUEUE
    // =====================================================================

    [RelayCommand]
    private async Task SendQueueMessageAsync()
    {
        string text = string.IsNullOrWhiteSpace(QueueInput) ? "hello" : QueueInput.Trim();

        await SendSafelyAsync(text, _lifetime.Token);
    }

    private bool CanStartBurst() => !IsBurstRunning;

    [RelayCommand(CanExecute = nameof(CanStartBurst))]
    private async Task SendBurstAsync()
    {
        IsBurstRunning = true;
        _burstCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        try
        {
            // Sending faster than the receiver can work: the queue fills and the sender has to wait.
            for (int i = 1; i <= 10; i++)
                await _queue.SendAsync($"burst {i} of 10", _burstCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_isDisposed)
                AddLog(QueueLog, "Sender", "Burst cancelled.", warning: true);
        }
        catch (ChannelClosedException)
        {
            // The page was closed.
        }
        finally
        {
            _burstCts?.Dispose();
            _burstCts = null;

            if (!_isDisposed)
                IsBurstRunning = false;
        }
    }

    private bool CanCancelBurst() => IsBurstRunning;

    [RelayCommand(CanExecute = nameof(CanCancelBurst))]
    private void CancelBurst()
    {
        _burstCts?.Cancel();
    }

    private async Task SendSafelyAsync(string text, CancellationToken token)
    {
        try
        {
            await _queue.SendAsync(text, token);
        }
        catch (OperationCanceledException)
        {
            // Page closed while the sender was waiting.
        }
        catch (ChannelClosedException)
        {
            // Page closed.
        }
    }

    // Called on background threads: hop to the UI thread before touching anything bound to the screen.
    private void OnQueueEvent(QueueEvent e)
    {
        Dispatcher.UIThread.Post(() => ApplyQueueEvent(e));
    }

    private void ApplyQueueEvent(QueueEvent e)
    {
        if (_isDisposed)
            return;

        switch (e.Kind)
        {
            case QueueEventKind.Sent:
                AddLog(QueueLog, "Sender", $"send {e.Message.Display}");
                break;

            case QueueEventKind.SenderBlocked:
                _blockedSenders++;
                AddLog(QueueLog, "Sender", $"queue is FULL - {e.Message.Display} cannot be sent yet. The sender is blocked.", warning: true);
                break;

            case QueueEventKind.SenderResumed:
                _blockedSenders = Math.Max(0, _blockedSenders - 1);
                AddLog(QueueLog, "Queue", $"a slot was freed - {e.Message.Display} goes in. The sender continues.");
                break;

            case QueueEventKind.Received:
                AddLog(QueueLog, "Receiver", $"receive {e.Message.Display}  (working on it for {MessageQueueDemoService.ProcessingTime.TotalMilliseconds:F0} ms)");
                break;
        }

        IsSenderBlocked = _blockedSenders > 0;

        int depth = _queue.Depth;
        QueueDepthText = $"Waiting in queue: {depth} of {MessageQueueDemoService.Capacity}";
        QueueDepthPercent = (double)depth / MessageQueueDemoService.Capacity * 100.0;
        QueueCounters = $"Sent: {_queue.SentCount}   Received: {_queue.ReceivedCount}";
    }

    // =====================================================================
    //  SHARED MEMORY
    // =====================================================================

    private SharedMemoryDemoService? EnsureShared()
    {
        if (_shared is { IsReady: true })
            return _shared;

        _shared?.Dispose();
        _shared = new SharedMemoryDemoService();

        if (!_shared.TryInitialize(out string error))
        {
            AddLog(SharedLog, "System", error, warning: true);
            SharedInfo = "The shared memory region could not be created.";
            _shared.Dispose();
            _shared = null;
            return null;
        }

        string where = _shared.IsRamBacked ? "RAM-backed tmpfs" : "a temporary file (not RAM-backed on this system)";

        SharedInfo = $"Region: {_shared.BackingPath}  |  {SharedMemoryDemoService.RegionSize} bytes  |  {where}  |  permissions 0600 (only you)";
        SharedTerminalHint = $"In a terminal:  ls -l /dev/shm      and      grep osdemo /proc/{Environment.ProcessId}/maps";

        AddLog(SharedLog, "System", "Created the shared region and mapped it twice: a read-write view for the writer and a read-only view for the reader.");
        return _shared;
    }

    [RelayCommand]
    private void WriteSharedMemory()
    {
        SharedMemoryDemoService? shared = EnsureShared();

        if (shared is null)
            return;

        int bytes = shared.Write(SharedInput);

        AddLog(SharedLog, "Writer", $"stored {bytes} bytes directly into the mapped memory (no system call per byte, no copy through a pipe).");
        RefreshMappings();
    }

    [RelayCommand]
    private void ReadSharedMemory()
    {
        SharedMemoryDemoService? shared = EnsureShared();

        if (shared is null)
            return;

        string text = shared.ReadThroughSecondView();

        AddLog(SharedLog, "Reader", string.IsNullOrEmpty(text)
            ? "the region is still empty - write something first."
            : $"read through the second view -> \"{text}\"");
    }

    [RelayCommand]
    private async Task ReadWithChildProcessAsync()
    {
        SharedMemoryDemoService? shared = EnsureShared();

        if (shared is null)
            return;

        (bool ok, string text, int pid) = await shared.ReadWithChildProcessAsync();

        if (_isDisposed)
            return;

        if (ok)
            AddLog(SharedLog, $"Child (PID {pid})", $"a separate process opened the same shared object and read -> \"{text}\"");
        else
            AddLog(SharedLog, "Child", text, warning: true);
    }

    [RelayCommand]
    private void RefreshMappings()
    {
        MappingLines.Clear();

        if (_shared is null)
            return;

        foreach (string line in _shared.GetMappingLines())
            MappingLines.Add(line);
    }

    private bool CanRunRace() => !IsRaceRunning;

    [RelayCommand(CanExecute = nameof(CanRunRace))]
    private async Task RunRaceDemoAsync()
    {
        SharedMemoryDemoService? shared = EnsureShared();

        if (shared is null)
            return;

        IsRaceRunning = true;
        RaceResult = "Running two threads that each add 1 a million times...";

        try
        {
            RaceDemoResult result = await Task.Run(() => shared.RunRaceDemo(RaceIterations));

            if (_isDisposed)
                return;

            int lost = result.Expected - result.WithoutLock;

            RaceResult =
                $"Expected total: {result.Expected:N0}\n" +
                $"Without a lock: {result.WithoutLock:N0}   ({lost:N0} additions lost)\n" +
                $"With a lock:    {result.WithLock:N0}   ({result.Expected - result.WithLock:N0} lost)";
        }
        catch (ObjectDisposedException)
        {
            // The page was closed while the threads were running.
        }
        finally
        {
            if (!_isDisposed)
                IsRaceRunning = false;
        }
    }

    // =====================================================================
    //  Lifetime
    // =====================================================================

    /// <summary>
    /// Called when the page is left: stops the child process and removes the shared-memory file,
    /// so nothing stays behind in the background. Using a demo again simply recreates them.
    /// </summary>
    public void ReleaseResources()
    {
        if (_isDisposed)
            return;

        _burstCts?.Cancel();

        if (_pipe is not null)
            StopPipeChild();

        if (_shared is not null && !IsRaceRunning)
        {
            _shared.Dispose();
            _shared = null;
            MappingLines.Clear();
            SharedInfo = "The region was removed when you left the page. It is created again on first use.";
            SharedTerminalHint = string.Empty;
        }
    }

    private static void AddLog(ObservableCollection<IpcLogEntry> log, string actor, string text, bool warning = false)
    {
        // Newest first.
        log.Insert(0, new IpcLogEntry { Time = DateTime.Now.ToString("HH:mm:ss.fff"), Actor = actor, Text = text, IsWarning = warning });

        while (log.Count > MaxLogLines)
            log.RemoveAt(log.Count - 1);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        // Release the page's resources first (this uses the not-yet-disposed state),
        // then mark as disposed so late callbacks do nothing.
        _burstCts?.Cancel();
        _pipe?.Dispose();
        _pipe = null;
        _shared?.Dispose();
        _shared = null;

        _isDisposed = true;

        _lifetime.Cancel();
        _queue.EventOccurred -= OnQueueEvent;
        _queue.Dispose();
        _lifetime.Dispose();
    }
}
