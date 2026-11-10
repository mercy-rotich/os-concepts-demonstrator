
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Memory;

public partial class MemoryManagementViewModel : ViewModelBase, IDisposable
{
    private readonly SystemInformationService _systemInformationService;

    private readonly DispatcherTimer _memoryHistoryTimer;

    private bool _isDisposed;

    public ObservableCollection<MemoryHistoryPoint> MemoryHistory { get; } = new();

    // A fresh snapshot each time, so the chart's Points property sees a new reference and redraws.
    public IEnumerable<MemoryHistoryPoint> MemoryChartPoints =>
        MemoryHistory.ToList();

    [ObservableProperty]
    private string totalMemory = string.Empty;

    [ObservableProperty]
    private string usedMemory = string.Empty;

    [ObservableProperty]
    private string availableMemory = string.Empty;

    [ObservableProperty]
    private double memoryUsagePercentage;

    [ObservableProperty]
    private string memoryUsageLabel = string.Empty;

    // Text shown above the graph. Updated every time a new reading is recorded.
    [ObservableProperty]
    private string latestReadingLabel = "Waiting for first reading...";

    // Describes the time span covered by the readings currently on the graph.
    [ObservableProperty]
    private string monitoringPeriodLabel = "No readings yet";

    // Shown on the page when memory cannot be read. Empty string = everything is fine.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = string.Empty;

    // True once at least one reading exists; the view uses it to hide the empty-state text.
    [ObservableProperty]
    private bool hasHistory;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public MemoryManagementViewModel()
    {
        _systemInformationService = new SystemInformationService();

        RefreshMemory();

        // DispatcherTimer ticks on the UI thread, so the handler can touch UI-bound
        // properties directly and Stop() guarantees no tick arrives afterwards.
        _memoryHistoryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        _memoryHistoryTimer.Tick += OnMemoryHistoryTick;
        _memoryHistoryTimer.Start();
    }

    private void OnMemoryHistoryTick(object? sender, EventArgs e)
    {
        RecordMemoryHistory();
    }

    private void RecordMemoryHistory()
    {
        if (_isDisposed)
            return;

        if (!_systemInformationService.TryGetMemoryInfo(out MemoryInfoModel? info, out string error)
            || info is null)
        {
            // Invalid reading: skip it rather than plotting a wrong point.
            ErrorMessage = error;
            return;
        }

        ErrorMessage = string.Empty;

        var point = new MemoryHistoryPoint
        {
            RecordedAt = DateTime.Now,
            UsagePercentage = info.UsedPercentage
        };

        MemoryHistory.Add(point);
        HasHistory = true;

        LatestReadingLabel =
            $"Latest: {point.UsagePercentage:F1}% at {point.RecordedAt:HH:mm:ss}";

        // Keep only the latest 20 readings.
        while (MemoryHistory.Count > 20)
        {
            MemoryHistory.RemoveAt(0);
        }

        UpdateMonitoringPeriod();

        OnPropertyChanged(nameof(MemoryChartPoints));
    }

    // The period runs from the oldest to the newest reading still kept (max 20).
    private void UpdateMonitoringPeriod()
    {
        if (MemoryHistory.Count == 0)
        {
            MonitoringPeriodLabel = "No readings yet";
            return;
        }

        DateTime start = MemoryHistory[0].RecordedAt;
        DateTime end = MemoryHistory[^1].RecordedAt;
        TimeSpan span = end - start;

        string duration = span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes} min {span.Seconds} s"
            : $"{span.Seconds} s";

        MonitoringPeriodLabel =
            $"Monitoring period: {start:HH:mm:ss} - {end:HH:mm:ss} ({duration}, {MemoryHistory.Count} {(MemoryHistory.Count == 1 ? "reading" : "readings")})";
    }

    public void RefreshMemory()
    {
        if (_isDisposed)
            return;

        if (!_systemInformationService.TryGetMemoryInfo(out MemoryInfoModel? info, out string error)
            || info is null)
        {
            ErrorMessage = error;
            TotalMemory = UsedMemory = AvailableMemory = "N/A";
            MemoryUsagePercentage = 0;
            MemoryUsageLabel = "Unavailable";
            return;
        }

        ErrorMessage = string.Empty;

        TotalMemory = $"{info.TotalGb:F2} GB";
        UsedMemory = $"{info.UsedGb:F2} GB";
        AvailableMemory = $"{info.AvailableGb:F2} GB";

        MemoryUsagePercentage = info.UsedPercentage;

        MemoryUsageLabel = $"{info.UsedPercentage:F1}% Used";
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        // Set the flag first so any late call exits immediately,
        // then stop the timer and detach the handler.
        _isDisposed = true;

        _memoryHistoryTimer.Stop();
        _memoryHistoryTimer.Tick -= OnMemoryHistoryTick;
    }
}
