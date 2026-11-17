using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Memory;

// Drives the educational Memory Allocation Simulator. It is deliberately independent of
// MemoryManagementViewModel (real RAM monitoring): no shared state, no system calls.
public partial class MemoryAllocationSimulatorViewModel : ViewModelBase
{
    private const int MaxHistoryEntries = 100;

    private readonly MemoryAllocationSimulator _simulator = new(totalKb: 1024);

    // Blocks in address order, shown in the list under the memory map.
    public ObservableCollection<MemoryBlockModel> Blocks { get; } = new();

    // Fresh list each time so the MemoryMapBar sees a new reference and redraws.
    public IEnumerable<MemoryBlockModel> MapBlocks => Blocks.ToList();

    // Newest entry first.
    public ObservableCollection<AllocationHistoryEntry> History { get; } = new();

    public int TotalKb => _simulator.TotalKb;

    [ObservableProperty]
    private string processNameInput = "P1";

    [ObservableProperty]
    private string sizeInput = "100";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeallocate))]
    [NotifyPropertyChangedFor(nameof(SelectedAllocationId))]
    private MemoryBlockModel? selectedBlock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessageColor))]
    private bool isMessageError;

    [ObservableProperty]
    private bool showReleaseAllConfirmation;

    // ---- statistics shown in the stat cards ----
    [ObservableProperty] private string allocatedText = string.Empty;
    [ObservableProperty] private string freeText = string.Empty;
    [ObservableProperty] private double utilisationPercent;
    [ObservableProperty] private string utilisationText = string.Empty;
    [ObservableProperty] private string holesText = string.Empty;
    [ObservableProperty] private string largestHoleText = string.Empty;
    [ObservableProperty] private string fragmentationText = string.Empty;

    public string TotalText => $"{TotalKb} KB";

    public bool CanDeallocate => SelectedBlock is { IsFree: false };

    public int SelectedAllocationId => SelectedBlock is { IsFree: false } b ? b.AllocationId : 0;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public string MessageColor => IsMessageError ? "#F87171" : "#86EFAC";

    public bool HasHistory => History.Count > 0;

    public MemoryAllocationSimulatorViewModel()
    {
        RefreshFromSimulator();
    }

    [RelayCommand]
    private void Allocate()
    {
        if (!int.TryParse(SizeInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sizeKb))
        {
            ShowMessage("The size must be a whole number of KB, for example 120.", isError: true);
            return;
        }

        string process = ProcessNameInput.Trim();
        AllocationResult result = _simulator.Allocate(process, sizeKb);

        ShowMessage(result.Message, !result.Success);

        if (result.Success && result.Block is not null)
        {
            AddHistory("ALLOCATE",
                $"{result.Block.ProcessName} #{result.Block.AllocationId}: {sizeKb} KB at {result.Block.AddressRange} (First Fit)");

            ProcessNameInput = SuggestNextProcessName(process);
            RefreshFromSimulator(selectId: result.Block.AllocationId);
        }
        else
        {
            string reason = result.IsFragmentationFailure ? "external fragmentation" : "request rejected";
            AddHistory("FAILED", $"{(process.Length > 0 ? process : "?")} asked for {SizeInput.Trim()} KB ({reason})", isFailure: true);
        }
    }

    [RelayCommand]
    private void Deallocate()
    {
        if (SelectedBlock is not { IsFree: false } block)
        {
            ShowMessage("Select an allocated block in the list first. Free holes cannot be deallocated.", isError: true);
            return;
        }

        AllocationResult result = _simulator.Free(block.AllocationId);
        ShowMessage(result.Message, !result.Success);

        if (result.Success && result.Block is not null)
        {
            AddHistory("FREE",
                $"{result.Block.ProcessName} #{result.Block.AllocationId}: {result.Block.SizeKb} KB at {result.Block.AddressRange}");
        }

        RefreshFromSimulator();
    }

    // Two-step: ask first (destructive, even though simulated), then confirm.
    [RelayCommand]
    private void AskReleaseAll()
    {
        if (_simulator.AllocationCount == 0)
        {
            ShowMessage("There are no allocations to release.", isError: false);
            return;
        }

        ShowReleaseAllConfirmation = true;
    }

    [RelayCommand]
    private void CancelReleaseAll() => ShowReleaseAllConfirmation = false;

    [RelayCommand]
    private void ConfirmReleaseAll()
    {
        ShowReleaseAllConfirmation = false;

        int count = _simulator.AllocationCount;
        int kb = _simulator.AllocatedKb;

        _simulator.ReleaseAll();

        ShowMessage($"Released {count} allocation(s), {kb} KB. Memory is one free hole again.", isError: false);
        AddHistory("RELEASE ALL", $"{count} allocation(s), {kb} KB returned");
        ProcessNameInput = "P1";
        RefreshFromSimulator();
    }

    // Copies the simulator's state into the bindable properties after every change.
    private void RefreshFromSimulator(int? selectId = null)
    {
        int keepId = selectId ?? SelectedAllocationId;

        Blocks.Clear();
        foreach (MemoryBlockModel block in _simulator.Blocks)
            Blocks.Add(block);

        SelectedBlock = keepId == 0 ? null : Blocks.FirstOrDefault(b => !b.IsFree && b.AllocationId == keepId);

        AllocatedText = $"{_simulator.AllocatedKb} KB";
        FreeText = $"{_simulator.FreeKb} KB";
        UtilisationPercent = _simulator.UtilisationPercent;
        UtilisationText = $"{_simulator.UtilisationPercent:F1}% utilised";
        HolesText = $"{_simulator.FreeHoleCount}";
        LargestHoleText = $"{_simulator.LargestHoleKb} KB";
        FragmentationText = $"{_simulator.ExternalFragmentationPercent:F1}%";

        OnPropertyChanged(nameof(MapBlocks));
    }

    private void AddHistory(string action, string details, bool isFailure = false)
    {
        History.Insert(0, new AllocationHistoryEntry { Action = action, Details = details, IsFailure = isFailure });

        while (History.Count > MaxHistoryEntries)
            History.RemoveAt(History.Count - 1);

        OnPropertyChanged(nameof(HasHistory));
    }

    private void ShowMessage(string text, bool isError)
    {
        Message = text;
        IsMessageError = isError;
    }

    // "P3" -> "P4" so the user can keep pressing Allocate. Anything else is left as typed.
    private static string SuggestNextProcessName(string current)
    {
        Match match = Regex.Match(current, @"^(.*?)(\d+)$");
        return match.Success && int.TryParse(match.Groups[2].Value, out int n)
            ? match.Groups[1].Value + (n + 1).ToString(CultureInfo.InvariantCulture)
            : current;
    }
}
