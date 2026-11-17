namespace OS_Concepts_Demonstrator.Models;

public class ProcessActionResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public bool IsPermissionError { get; set; }

    public bool IsIdentityMismatch { get; set; }

    public bool IsProtectedProcess { get; set; }

    public bool IsProcessMissing { get; set; }
}
