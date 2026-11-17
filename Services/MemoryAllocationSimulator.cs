using System;
using System.Collections.Generic;
using System.Linq;
using OS_Concepts_Demonstrator.Models;

namespace OS_Concepts_Demonstrator.Services;

// EDUCATIONAL SIMULATION ONLY. Nothing here allocates or touches real system memory;
// it is ordinary C# bookkeeping over a pretend address space.
//
// Model: contiguous allocation with variable-sized partitions (dynamic partitioning).
// Memory is an ordered list of blocks covering addresses 0..TotalKb with no gaps.
// Every block is either allocated to one process or a free hole.
public class MemoryAllocationSimulator
{
    private readonly List<MemoryBlockModel> _blocks = new();

    private int _nextAllocationId = 1;

    public MemoryAllocationSimulator(int totalKb = 1024)
    {
        if (totalKb <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalKb), "Total memory must be positive.");

        TotalKb = totalKb;
        ReleaseAll();
    }

    public int TotalKb { get; }

    // Blocks in address order. Read-only to callers; only this class changes memory.
    public IReadOnlyList<MemoryBlockModel> Blocks => _blocks;

    public int AllocatedKb => _blocks.Where(b => !b.IsFree).Sum(b => b.SizeKb);

    public int FreeKb => TotalKb - AllocatedKb;

    public int AllocationCount => _blocks.Count(b => !b.IsFree);

    public int FreeHoleCount => _blocks.Count(b => b.IsFree);

    public int LargestHoleKb => _blocks.Where(b => b.IsFree).Select(b => b.SizeKb).DefaultIfEmpty(0).Max();

    public double UtilisationPercent => 100.0 * AllocatedKb / TotalKb;

    // External fragmentation = share of free memory that is NOT in the largest hole.
    // 0% means all free memory is one contiguous hole; near 100% means it is scattered in
    // small pieces, so large requests fail even though plenty is free in total.
    public double ExternalFragmentationPercent =>
        FreeKb == 0 ? 0 : 100.0 * (FreeKb - LargestHoleKb) / FreeKb;

    public AllocationResult Allocate(string processName, int sizeKb)
    {
        processName = processName.Trim();

        if (string.IsNullOrEmpty(processName))
            return Fail("Enter a process name for the allocation.");

        if (sizeKb <= 0)
            return Fail("The allocation size must be a positive number of KB.");

        if (sizeKb > TotalKb)
            return Fail($"{processName} asked for {sizeKb} KB, but the whole simulated memory is only {TotalKb} KB. " +
                        "No allocation strategy can satisfy a request larger than physical memory.");

        // Case 1: not enough free memory in total.
        if (sizeKb > FreeKb)
            return Fail($"Insufficient memory: {processName} asked for {sizeKb} KB but only {FreeKb} KB is free in total. " +
                        "Free some blocks first. (A real OS would swap or page out another process here.)");

        MemoryBlockModel? hole = FindHoleFirstFit(sizeKb);

        // Case 2: enough memory in total, but no single hole is big enough.
        if (hole is null)
        {
            return new AllocationResult
            {
                Success = false,
                IsFragmentationFailure = true,
                Message = $"External fragmentation: {FreeKb} KB is free in total, which is more than the {sizeKb} KB " +
                          $"{processName} asked for, but it is split across {FreeHoleCount} holes and the largest is only " +
                          $"{LargestHoleKb} KB. Contiguous allocation needs ONE hole of at least {sizeKb} KB."
            };
        }

        MemoryBlockModel allocated = SplitHole(hole, processName, sizeKb);

        return new AllocationResult
        {
            Success = true,
            Block = allocated,
            Message = $"First Fit placed {processName} ({sizeKb} KB) at {allocated.AddressRange}: " +
                      $"it scanned from address 0 and used the first hole of at least {sizeKb} KB."
        };
    }

    public AllocationResult Free(int allocationId)
    {
        int index = _blocks.FindIndex(b => !b.IsFree && b.AllocationId == allocationId);

        if (index < 0)
            return Fail("That block is not allocated (it may already have been freed).");

        MemoryBlockModel block = _blocks[index];

        // Keep a copy for the message/history before the block is merged away.
        var freedCopy = new MemoryBlockModel
        {
            AllocationId = block.AllocationId,
            ProcessName = block.ProcessName,
            StartKb = block.StartKb,
            SizeKb = block.SizeKb
        };

        block.IsFree = true;
        block.ProcessName = string.Empty;
        block.AllocationId = 0;

        bool merged = CoalesceAround(index);

        return new AllocationResult
        {
            Success = true,
            Block = freedCopy,
            Message = $"Freed {freedCopy.ProcessName} #{freedCopy.AllocationId} ({freedCopy.SizeKb} KB at {freedCopy.AddressRange})." +
                      (merged ? " It was next to another hole, so the holes were merged (coalesced) into one larger hole." : "")
        };
    }

    // Returns memory to its initial state: one hole covering everything.
    public void ReleaseAll()
    {
        _blocks.Clear();
        _blocks.Add(new MemoryBlockModel { StartKb = 0, SizeKb = TotalKb, IsFree = true });
    }

    // FIRST FIT: scan holes from the lowest address and take the first one that is big enough.
    // Fast (stops early) but tends to leave small leftover holes near the start of memory.
    private MemoryBlockModel? FindHoleFirstFit(int sizeKb)
    {
        return _blocks.FirstOrDefault(b => b.IsFree && b.SizeKb >= sizeKb);
    }

    // Carves an allocation out of the START of a hole. Whatever is left stays a smaller hole.
    private MemoryBlockModel SplitHole(MemoryBlockModel hole, string processName, int sizeKb)
    {
        var allocated = new MemoryBlockModel
        {
            AllocationId = _nextAllocationId++,
            ProcessName = processName,
            StartKb = hole.StartKb,
            SizeKb = sizeKb,
            IsFree = false
        };

        int holeIndex = _blocks.IndexOf(hole);

        if (hole.SizeKb == sizeKb)
        {
            // Exact fit: the hole disappears completely.
            _blocks[holeIndex] = allocated;
        }
        else
        {
            hole.StartKb += sizeKb;
            hole.SizeKb -= sizeKb;
            _blocks.Insert(holeIndex, allocated);
        }

        return allocated;
    }

    // Merges the free block at 'index' with free neighbours on either side.
    // Without this, memory would fill up with many tiny adjacent holes.
    private bool CoalesceAround(int index)
    {
        bool merged = false;

        // Merge with the next block.
        if (index + 1 < _blocks.Count && _blocks[index + 1].IsFree)
        {
            _blocks[index].SizeKb += _blocks[index + 1].SizeKb;
            _blocks.RemoveAt(index + 1);
            merged = true;
        }

        // Merge with the previous block.
        if (index > 0 && _blocks[index - 1].IsFree)
        {
            _blocks[index - 1].SizeKb += _blocks[index].SizeKb;
            _blocks.RemoveAt(index);
            merged = true;
        }

        return merged;
    }

    private static AllocationResult Fail(string message) => new() { Success = false, Message = message };
}
