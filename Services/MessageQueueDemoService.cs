using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

/// <summary>
/// Demonstrates message passing through a bounded queue: a sender puts discrete messages in,
/// an independent (and deliberately slow) receiver takes them out. When the queue is full the
/// sender really waits, which is the "blocking send" of real message queues such as POSIX mqueue.
/// </summary>
public sealed class MessageQueueDemoService : IDisposable
{
    public const int Capacity = 5;

    // How long the receiver "works" on each message, so the queue visibly fills up.
    public static readonly TimeSpan ProcessingTime = TimeSpan.FromMilliseconds(900);

    private readonly Channel<QueueMessage> _channel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _receiver;

    private int _nextId;
    private int _sent;
    private int _received;

    // Raised from background threads; subscribers must marshal to the UI thread themselves.
    public event Action<QueueEvent>? EventOccurred;

    public int SentCount => Volatile.Read(ref _sent);

    public int ReceivedCount => Volatile.Read(ref _received);

    public int Depth => _channel.Reader.Count;

    public MessageQueueDemoService()
    {
        _channel = Channel.CreateBounded<QueueMessage>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // a full queue makes the sender wait
            SingleReader = true
        });

        _receiver = Task.Run(ReceiveLoopAsync);
    }

    public async Task SendAsync(string text, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        var message = new QueueMessage(Interlocked.Increment(ref _nextId), text, DateTime.Now);

        // TryWrite succeeds at once if there is room; otherwise the sender must wait for the receiver.
        if (!_channel.Writer.TryWrite(message))
        {
            Raise(QueueEventKind.SenderBlocked, message);

            await _channel.Writer.WriteAsync(message, linked.Token);

            Raise(QueueEventKind.SenderResumed, message);
        }

        Interlocked.Increment(ref _sent);
        Raise(QueueEventKind.Sent, message);
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            await foreach (QueueMessage message in _channel.Reader.ReadAllAsync(_lifetime.Token))
            {
                Interlocked.Increment(ref _received);
                Raise(QueueEventKind.Received, message);

                await Task.Delay(ProcessingTime, _lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void Raise(QueueEventKind kind, QueueMessage message) =>
        EventOccurred?.Invoke(new QueueEvent(kind, message, Depth));

    public void Dispose()
    {
        EventOccurred = null;

        _lifetime.Cancel();
        _channel.Writer.TryComplete();

        try
        {
            _receiver.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // The receiver was cancelled; nothing to report.
        }

        _lifetime.Dispose();
    }
}
