
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Memory;

public partial class MemoryManagementViewModel : ViewModelBase, IDisposable
{
    private readonly SystemInformationService _systemInformationService;

    private readonly Timer _memoryHistoryTimer;

    private bool _isDisposed;

    public ObservableCollection<MemoryHistoryPoint> MemoryHistory { get; } = new();

    public IEnumerable<double> MemoryChartValues =>
    MemoryHistory.Select(point => point.UsagePercentage).ToList();

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

    public MemoryManagementViewModel()
    {
        _systemInformationService = new SystemInformationService();

        RefreshMemory();

        _memoryHistoryTimer = new Timer(
            _ => Dispatcher.UIThread.Post(RecordMemoryHistory),
            null,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5));
    }

    private void RecordMemoryHistory()
    {
        if (_isDisposed)
            return;

        MemoryInfoModel info = _systemInformationService.GetMemoryInfo();

        MemoryHistory.Add(new MemoryHistoryPoint
        {
            RecordedAt = DateTime.Now,
            UsagePercentage = info.UsedPercentage
        });

        // Keep only the latest 20 readings.
        while (MemoryHistory.Count > 20)
        {
            MemoryHistory.RemoveAt(0);
        }

        OnPropertyChanged(nameof(MemoryChartValues));
    }

    public void RefreshMemory()
    {
        MemoryInfoModel info = _systemInformationService.GetMemoryInfo();

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

    _isDisposed = true;

    _memoryHistoryTimer.Dispose();
}
}
