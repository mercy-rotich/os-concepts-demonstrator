using System.Collections.Generic;

namespace OS_Concepts_Demonstrator.Models;

public class DetailRow
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>Extra facts about one selected service, from "systemctl show" and the journal.</summary>
public class ServiceDetailsModel
{
    public List<DetailRow> Rows { get; } = new();

    // Recent log lines, or a sentence explaining why none can be shown.
    public string Logs { get; set; } = string.Empty;

    // Set when the details could not be read at all.
    public string? Error { get; set; }
}
