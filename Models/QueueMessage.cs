using System;

namespace OS_Concepts_Demonstrator.Models;

/// <summary>One discrete message in the message-passing demo.</summary>
public record QueueMessage(int Id, string Text, DateTime SentAt)
{
    public string Display => $"#{Id} \"{Text}\"";
}

public enum QueueEventKind
{
    Sent,
    SenderBlocked,
    SenderResumed,
    Received
}

/// <summary>Raised (from background threads) whenever something happens to the queue.</summary>
public record QueueEvent(QueueEventKind Kind, QueueMessage Message, int Depth);
