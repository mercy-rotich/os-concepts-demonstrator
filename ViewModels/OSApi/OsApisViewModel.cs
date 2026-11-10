using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.OSApi;

public partial class OsApisViewModel : ViewModelBase
{
    private const int MaxExperimentsShown = 12;

    private readonly OsApiService _service = new();

    public ObservableCollection<DataSourceStatusModel> Sources { get; } = new();

    public ObservableCollection<ApiLayerModel> Layers { get; } = new();

    public ObservableCollection<string> OtherLibraries { get; } = new();

    public ObservableCollection<DetailRow> Restrictions { get; } = new();

    public ObservableCollection<LibcCallModel> LibcCalls { get; } = new();

    public ObservableCollection<SyscallGroupModel> SyscallGroups { get; } = new();

    // Newest first.
    public ObservableCollection<SyscallExperimentModel> Experiments { get; } = new();

    [ObservableProperty]
    private string otherLibrariesTitle = string.Empty;

    [ObservableProperty]
    private string catalogNote = string.Empty;

    [ObservableProperty]
    private string callsNote = string.Empty;

    [ObservableProperty]
    private string lastUpdated = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunReadOnceCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunReadManyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunOneByteAtATimeCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunOneCallCommand))]
    private bool isExperimentRunning;

    [ObservableProperty]
    private bool hasNoExperiments = true;

    public OsApisViewModel()
    {
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        OsApiReadout readout = _service.Collect();

        Fill(Sources, readout.Sources);
        Fill(Layers, readout.Layers);
        Fill(OtherLibraries, readout.OtherLibraries);
        Fill(Restrictions, readout.Restrictions);
        Fill(LibcCalls, readout.LibcCalls);
        Fill(SyscallGroups, readout.SyscallGroups);

        OtherLibrariesTitle = $"Other libraries loaded into this process ({readout.OtherLibraries.Count})";

        CatalogNote = readout.SyscallsInHeader > 0
            ? $"Numbers come from the Linux kernel headers installed here, which define {readout.SyscallsInHeader} system calls for x86-64. " +
              "The table says which .NET calls typically lead to each system call and where this application uses it; the exact calls depend on the runtime and C library versions."
            : "The system-call numbers could not be read on this system, so they show as n/a. The descriptions still apply. " +
              "The table says which .NET calls typically lead to each system call; the exact calls depend on the runtime and C library versions.";

        CallsNote = $"Answers were read at {DateTime.Now:HH:mm:ss}. Press Refresh to ask again, which matters for the clocks.";
        LastUpdated = $"Updated {DateTime.Now:HH:mm:ss}";
    }

    private bool CanRunExperiment() => !IsExperimentRunning;

    [RelayCommand(CanExecute = nameof(CanRunExperiment))]
    private Task RunReadOnceAsync() => RunAsync(OsApiService.ExperimentKind.ReadSmallFileOnce);

    [RelayCommand(CanExecute = nameof(CanRunExperiment))]
    private Task RunReadManyAsync() => RunAsync(OsApiService.ExperimentKind.ReadSmallFile200Times);

    [RelayCommand(CanExecute = nameof(CanRunExperiment))]
    private Task RunOneByteAtATimeAsync() => RunAsync(OsApiService.ExperimentKind.ReadBytesOneAtATime);

    [RelayCommand(CanExecute = nameof(CanRunExperiment))]
    private Task RunOneCallAsync() => RunAsync(OsApiService.ExperimentKind.ReadBytesInOneCall);

    private async Task RunAsync(OsApiService.ExperimentKind kind)
    {
        IsExperimentRunning = true;

        try
        {
            SyscallExperimentModel result = await _service.RunExperimentAsync(kind);

            Experiments.Insert(0, result);

            while (Experiments.Count > MaxExperimentsShown)
                Experiments.RemoveAt(Experiments.Count - 1);

            HasNoExperiments = false;
        }
        finally
        {
            IsExperimentRunning = false;
        }
    }

    private static void Fill<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
    {
        target.Clear();

        foreach (T item in items)
            target.Add(item);
    }
}
