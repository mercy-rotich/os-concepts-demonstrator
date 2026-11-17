namespace OS_Concepts_Demonstrator.Models;

// One contiguous region of SIMULATED memory. The simulator keeps an ordered list of
// these that covers every address from 0 to the total size with no gaps:
// each block is either allocated to a process or a free "hole".
public class MemoryBlockModel
{
    // Unique number for an allocation (0 for free holes). Lets the user pick a block to free.
    public int AllocationId { get; set; }

    // First address of the block, in KB from the start of simulated memory.
    public int StartKb { get; set; }

    public int SizeKb { get; set; }

    public bool IsFree { get; set; }

    // Simulated process that owns the block. Empty for free holes.
    public string ProcessName { get; set; } = string.Empty;

    public int EndKb => StartKb + SizeKb;

    public string AddressRange => $"{StartKb} - {EndKb} KB";

    public string StatusText => IsFree ? "Free" : "Allocated";

    public string OwnerText => IsFree ? "(hole)" : $"{ProcessName}  #{AllocationId}";

    public const string FreeColor = "#374151";

    private static readonly string[] AllocatedPalette =
        { "#8B5CF6", "#38BDF8", "#F472B6", "#34D399", "#FBBF24", "#FB923C", "#A78BFA", "#2DD4BF" };

    // Brush string shared by the list swatch and the memory map, so they always match.
    // Free holes are grey; each allocation gets its own colour so neighbours are distinguishable.
    public string StatusColor => IsFree
        ? FreeColor
        : AllocatedPalette[(AllocationId - 1 + AllocatedPalette.Length) % AllocatedPalette.Length];
}
