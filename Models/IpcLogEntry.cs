using System;

namespace OS_Concepts_Demonstrator.Models;

/// <summary>One line of an IPC demo's activity log.</summary>
public class IpcLogEntry
{
    public string Time { get; set; } = DateTime.Now.ToString("HH:mm:ss.fff");

    // Who did it: "Parent", "Child", "Sender", "Receiver", "Writer", "Reader"...
    public string Actor { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool IsWarning { get; set; }
}
