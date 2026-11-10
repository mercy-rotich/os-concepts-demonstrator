using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Accounting;

public partial class ProcessAccountingViewModel : ViewModelBase, IDisposable
{
    private const int MaxRows = 30;

    private const string SortCpuNow = "CPU use right now (%)";
    private const string SortCpuTotal = "CPU time used in total";
    private const string SortMemory = "Memory in use";
    private const string SortNewest = "Newest processes first";
    private const string SortPid = "Process ID";

    private readonly ProcessAccountingService _service = new();

    // Ticks on the UI thread; only runs while the page is on screen.
    private readonly DispatcherTimer _timer;

    // The most recent reading. The next reading is compared with it to work out rates.
    private ProcessAccountingSnapshot? _current;
    private List<ProcessAccountingRowModel> _allRows = new();

    private int? _selectedPid;
    private bool _isSampling;
    private bool _isRebuilding;
    private bool _isDisposed;

    public ObservableCollection<ProcessAccountingRowModel> Rows { get; } = new();

    public ObservableCollection<DataSourceStatusModel> Sources { get; } = new();

    public ObservableCollection<DetailRow> DetailRows { get; } = new();

    public IReadOnlyList<string> SortOptions { get; } = new[] { SortCpuNow, SortCpuTotal, SortMemory, SortNewest, SortPid };

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string selectedSort = SortCpuNow;

    [ObservableProperty]
    private ProcessAccountingRowModel? selectedRow;

    [ObservableProperty]
    private bool isMeasuring = true;

    [ObservableProperty]
    private string summary = "Reading the process table...";

    [ObservableProperty]
    private string listNote = string.Empty;

    [ObservableProperty]
    private string lastUpdated = string.Empty;

    [ObservableProperty]
    private bool hasNoMatches;

    // Details of the selected process.
    [ObservableProperty]
    private string detailTitle = string.Empty;

    [ObservableProperty]
    private bool hasSelection;

    [ObservableProperty]
    private bool selectedProcessEnded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotices))]
    private string notices = string.Empty;

    public bool HasNotices => !string.IsNullOrEmpty(Notices);

    public ProcessAccountingViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += OnTimerTick;
    }

    partial void OnSearchTextChanged(string value) => RebuildList();

    partial void OnSelectedSortChanged(string value) => RebuildList();

    partial void OnSelectedRowChanged(ProcessAccountingRowModel? value)
    {
        // Rebuilding the list briefly deselects everything; that is not a choice by the user.
        if (_isRebuilding)
            return;

        _selectedPid = value?.Pid;
        UpdateDetails();
    }

    /// <summary>Called when the page appears: take a baseline reading, then sample every two seconds.</summary>
    public void StartMonitoring()
    {
        if (_isDisposed || _timer.IsEnabled)
            return;

        // After time away from the page, an old reading would give meaningless rates.
        _current = null;
        IsMeasuring = true;

        _ = SampleAsync();
        _timer.Start();
    }

    /// <summary>Called when the page disappears, so nothing keeps reading /proc in the background.</summary>
    public void StopMonitoring() => _timer.Stop();

    private void OnTimerTick(object? sender, EventArgs e) => _ = SampleAsync();

    private async Task SampleAsync()
    {
        // If reading takes longer than the timer interval, skip rather than pile up.
        if (_isSampling || _isDisposed)
            return;

        _isSampling = true;

        try
        {
            ProcessAccountingSnapshot? previous = _current;

            // Reading hundreds of /proc entries and formatting them is done off the UI thread.
            (ProcessAccountingSnapshot snapshot, List<ProcessAccountingRowModel> rows, double? seconds) =
                await Task.Run(() =>
                {
                    ProcessAccountingSnapshot current = _service.ReadSnapshot();
                    double? interval = ProcessAccountingCalculator.IntervalSeconds(previous, current);

                    List<ProcessAccountingRowModel> built = current.Processes.Values
                        .Select(p =>
                        {
                            ProcessAccountingSample? before = null;
                            previous?.Processes.TryGetValue(p.Pid, out before);
                            return ProcessAccountingCalculator.BuildRow(p, before, interval, _service.BootTime, _service.Users);
                        })
                        .ToList();

                    return (current, built, interval);
                });

            if (_isDisposed)
                return;

            // The new reading becomes the one the next sample is compared with.
            _current = snapshot;
            _allRows = rows;
            IsMeasuring = seconds is null;

            UpdateSources(snapshot);
            UpdateSummary(snapshot);
            RebuildList();
            UpdateDetails();

            Notices = string.Join("  ", snapshot.Warnings);
            LastUpdated = $"Updated {DateTime.Now:HH:mm:ss}, refreshes every 2 seconds";
        }
        finally
        {
            _isSampling = false;
        }
    }

    private void UpdateSources(ProcessAccountingSnapshot snapshot)
    {
        Sources.Clear();

        foreach (DataSourceStatusModel source in _service.DescribeSources(snapshot))
            Sources.Add(source);
    }

    private void UpdateSummary(ProcessAccountingSnapshot snapshot)
    {
        int total = snapshot.Processes.Count;

        Summary =
            $"{total} processes counted ({snapshot.KernelThreadCount} are kernel threads)   |   " +
            $"I/O counters readable for {snapshot.IoReadableCount} of {total}   |   " +
            $"system booted {_service.BootTime:ddd dd MMM yyyy, HH:mm}";
    }

    private void RebuildList()
    {
        string query = SearchText.Trim();

        IEnumerable<ProcessAccountingRowModel> matches = _allRows.Where(r =>
            query.Length == 0
            || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || r.User.Contains(query, StringComparison.OrdinalIgnoreCase)
            || r.Pid.ToString().Contains(query, StringComparison.Ordinal));

        List<ProcessAccountingRowModel> matching = matches.ToList();

        IEnumerable<ProcessAccountingRowModel> sorted = SelectedSort switch
        {
            SortCpuTotal => matching.OrderByDescending(r => r.CpuTotalSeconds),
            SortMemory => matching.OrderByDescending(r => r.ResidentBytes),
            SortNewest => matching.OrderByDescending(r => r.StartTime),
            SortPid => matching.OrderBy(r => r.Pid),
            // Without a rate yet (first reading), fall back to total CPU time so the list is still meaningful.
            _ => matching.OrderByDescending(r => r.CpuPercent ?? -1).ThenByDescending(r => r.CpuTotalSeconds)
        };

        List<ProcessAccountingRowModel> shown = sorted.Take(MaxRows).ToList();

        _isRebuilding = true;

        try
        {
            Rows.Clear();

            foreach (ProcessAccountingRowModel row in shown)
                Rows.Add(row);

            SelectedRow = _selectedPid is int pid ? shown.FirstOrDefault(r => r.Pid == pid) : null;
        }
        finally
        {
            _isRebuilding = false;
        }

        HasNoMatches = matching.Count == 0 && _allRows.Count > 0;
        ListNote = matching.Count > MaxRows
            ? $"Showing the first {MaxRows} of {matching.Count} matching processes. Use the search box to find others."
            : $"Showing {matching.Count} process(es).";
    }

    private void UpdateDetails()
    {
        DetailRows.Clear();

        if (_selectedPid is not int pid || _current is null)
        {
            HasSelection = false;
            SelectedProcessEnded = false;
            DetailTitle = string.Empty;
            return;
        }

        HasSelection = true;

        if (!_current.Processes.TryGetValue(pid, out ProcessAccountingSample? sample))
        {
            // The process finished since it was selected: Linux keeps no accounting record of it.
            SelectedProcessEnded = true;
            DetailTitle = $"Process {pid}";
            return;
        }

        SelectedProcessEnded = false;
        DetailTitle = $"{sample.Name} (PID {sample.Pid})";

        foreach (DetailRow row in ProcessAccountingCalculator.BuildDetails(sample, _current, _service.BootTime, _service.Users))
            DetailRows.Add(row);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
