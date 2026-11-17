namespace OS_Concepts_Demonstrator.Models;

// Outcome of a file operation (create, rename, delete, copy, move), with a plain-language message.
public class FileOperationResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    // Full path of the item that now exists (created / renamed / pasted), so the UI can select it.
    public string? ResultPath { get; set; }

    // The operation was refused by the safety policy (outside home, protected item ...).
    public bool IsBlockedBySafetyPolicy { get; set; }

    public bool IsPermissionError { get; set; }

    // The destination already exists; the caller may ask the user and retry with overwrite allowed.
    public bool NeedsOverwriteConfirmation { get; set; }

    public static FileOperationResult Ok(string message, string? resultPath = null) =>
        new() { Success = true, Message = message, ResultPath = resultPath };

    public static FileOperationResult Fail(string message) =>
        new() { Success = false, Message = message };

    public static FileOperationResult Blocked(string message) =>
        new() { Success = false, Message = message, IsBlockedBySafetyPolicy = true };
}
