using OS_Concepts_Demonstrator.Services;

// Smoke test for the educational memory allocation simulator (no real memory is touched).
// Run: dotnet run --project tests/MemorySimulatorSmoke
int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name} {detail}");
    if (!ok) failures++;
}

// Invariant: blocks cover 0..Total in order with no gaps/overlaps and no two adjacent holes.
bool Consistent(MemoryAllocationSimulator s)
{
    int expected = 0;
    for (int i = 0; i < s.Blocks.Count; i++)
    {
        var b = s.Blocks[i];
        if (b.StartKb != expected || b.SizeKb <= 0) return false;
        if (i > 0 && b.IsFree && s.Blocks[i - 1].IsFree) return false;
        expected = b.EndKb;
    }
    return expected == s.TotalKb;
}

string Map(MemoryAllocationSimulator s) =>
    string.Join(" | ", s.Blocks.Select(b => b.IsFree ? $"free {b.SizeKb}" : $"{b.ProcessName} {b.SizeKb}"));

var sim = new MemoryAllocationSimulator(1024);
Check("starts as one free hole", sim.Blocks.Count == 1 && sim.FreeKb == 1024 && Consistent(sim));

var a = sim.Allocate("P1", 200);
var b = sim.Allocate("P2", 300);
var c = sim.Allocate("P3", 200);
Check("three allocations succeed", a.Success && b.Success && c.Success);
Check("first fit packs from address 0", a.Block!.StartKb == 0 && b.Block!.StartKb == 200 && c.Block!.StartKb == 500, Map(sim));
Check("stats after allocating", sim.AllocatedKb == 700 && sim.FreeKb == 324 && sim.UtilisationPercent > 68 && Consistent(sim));

// Free P1 and P3 -> holes 200 | P2 | 200+324 merged
var f1 = sim.Free(a.Block.AllocationId);
var f3 = sim.Free(c.Block!.AllocationId);
Check("freeing succeeds", f1.Success && f3.Success);
Check("freed P3 coalesced with trailing hole", sim.FreeHoleCount == 2 && sim.LargestHoleKb == 524 && Consistent(sim), Map(sim));
Check("double free rejected", !sim.Free(a.Block.AllocationId).Success);

// Fragmentation scenario: fill so free = 200 + 100 in two separate holes.
var d = sim.Allocate("P4", 424); // goes after P2 (first hole of 200 too small)
Check("first fit skips too-small hole", d.Success && d.Block!.StartKb == 500, Map(sim));
var e = sim.Allocate("P5", 100);  // goes into first hole (0..200) -> leaves 100 hole at 100..200
Check("first fit uses earliest hole", e.Success && e.Block!.StartKb == 0, Map(sim));
// Now free = 100 (at 100) + 100 (at 924) = 200 total, largest 100
var frag = sim.Allocate("BIG", 150);
Check("fragmentation failure detected", !frag.Success && frag.IsFragmentationFailure, frag.Message);
Check("fragmentation percent is 50%", Math.Abs(sim.ExternalFragmentationPercent - 50) < 0.01, $"{sim.ExternalFragmentationPercent:F1}%");

var tooMuch = sim.Allocate("HUGE", 500);
Check("insufficient total memory detected", !tooMuch.Success && !tooMuch.IsFragmentationFailure, tooMuch.Message);
Check("larger than all memory rejected", !sim.Allocate("X", 5000).Success);
Check("zero size rejected", !sim.Allocate("X", 0).Success);
Check("empty name rejected", !sim.Allocate("  ", 10).Success);

var exact = sim.Allocate("P6", 100);
Check("exact-fit hole is consumed completely", exact.Success && exact.Block!.StartKb == 100 && Consistent(sim), Map(sim));

sim.ReleaseAll();
Check("release all restores one hole", sim.Blocks.Count == 1 && sim.FreeKb == 1024 && sim.AllocationCount == 0);
Check("new allocation after release starts at 0", sim.Allocate("P1", 10).Block!.StartKb == 0);

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
