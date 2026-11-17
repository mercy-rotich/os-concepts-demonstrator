namespace OS_Concepts_Demonstrator.Models;

// Outcome of a simulated allocate/free request, with a plain-language explanation.
public class AllocationResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    // Set when the request failed even though enough memory was free in total,
    // because no single hole was large enough (external fragmentation).
    public bool IsFragmentationFailure { get; set; }

    // The block that was allocated or freed, when there is one.
    public MemoryBlockModel? Block { get; set; }
}
