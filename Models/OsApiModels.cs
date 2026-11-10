using System.Collections.Generic;

namespace OS_Concepts_Demonstrator.Models;

/// <summary>One layer between the application and the kernel, found in this process's memory map.</summary>
public class ApiLayerModel
{
    public string Title { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;
}

public enum ApiVerdict
{
    // The two answers are identical.
    Same,

    // Equal within a small tolerance (clocks read a moment apart).
    Close,

    // .NET has no matching API, so there is nothing to compare with.
    NoEquivalent,

    // The call could not be made on this system.
    Unavailable
}

/// <summary>One direct call into the C library, compared with the .NET way of getting the same answer.</summary>
public class LibcCallModel
{
    public string Function { get; set; } = string.Empty;

    public string Asks { get; set; } = string.Empty;

    // Which system call (if any) is behind it, with its number from the kernel headers.
    public string Behind { get; set; } = string.Empty;

    public string LibcValue { get; set; } = string.Empty;

    public string DotnetWay { get; set; } = string.Empty;

    public string DotnetValue { get; set; } = string.Empty;

    public ApiVerdict Verdict { get; set; }

    public bool IsSame => Verdict is ApiVerdict.Same or ApiVerdict.Close;

    public bool IsNoEquivalent => Verdict == ApiVerdict.NoEquivalent;

    public bool IsUnavailable => Verdict == ApiVerdict.Unavailable;

    public string VerdictText => Verdict switch
    {
        ApiVerdict.Same => "same answer",
        ApiVerdict.Close => "same (read a moment apart)",
        ApiVerdict.NoEquivalent => "no .NET equivalent",
        _ => "not available"
    };
}

/// <summary>One system call in the catalog.</summary>
public class SyscallInfoModel
{
    public string Name { get; set; } = string.Empty;

    // From the kernel headers; null when the headers for this architecture were not read.
    public int? Number { get; set; }

    public string Meaning { get; set; } = string.Empty;

    public string DotnetApi { get; set; } = string.Empty;

    public string UsedHere { get; set; } = string.Empty;

    public string NumberText => Number is int n ? $"#{n}" : "n/a";
}

public class SyscallGroupModel
{
    public string Title { get; set; } = string.Empty;

    public List<SyscallInfoModel> Items { get; } = new();
}

/// <summary>The result of one measured experiment.</summary>
public class SyscallExperimentModel
{
    public string Title { get; set; } = string.Empty;

    public string Time { get; set; } = string.Empty;

    public string SyscallsText { get; set; } = string.Empty;

    public string BytesText { get; set; } = string.Empty;

    public string ElapsedText { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;
}

/// <summary>Everything the OS APIs page shows, collected in one read.</summary>
public class OsApiReadout
{
    public List<ApiLayerModel> Layers { get; } = new();

    public List<string> OtherLibraries { get; } = new();

    public List<DetailRow> Restrictions { get; } = new();

    public List<LibcCallModel> LibcCalls { get; } = new();

    public List<SyscallGroupModel> SyscallGroups { get; } = new();

    public List<DataSourceStatusModel> Sources { get; } = new();

    public int SyscallsInHeader { get; set; }

    public string LibcVersion { get; set; } = "unknown";
}
