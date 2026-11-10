using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Dashboard;

    public partial class DashboardViewModel : ViewModelBase, IDisposable
    {
        private readonly SystemInformationService _systemInformationService;

        private readonly DispatcherTimer _memoryTimer;

        private bool _isDisposed;

        [ObservableProperty]
        private string operatingSystem = string.Empty;

        [ObservableProperty]
        private string processor = string.Empty;

        [ObservableProperty]
        private string memory = string.Empty;

        [ObservableProperty]
        private double memoryUsedPercentage;

        [ObservableProperty]
        private string memoryUsageLabel = string.Empty;

        public DashboardViewModel()
        {
            _systemInformationService = new SystemInformationService();

            OperatingSystem = _systemInformationService.GetOperatingSystem();
            Processor = _systemInformationService.GetProcessor();
            
            RefreshMemory();

            // Ticks on the UI thread; Stop() in Dispose() guarantees no later ticks.
            _memoryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            _memoryTimer.Tick += OnMemoryTimerTick;
            _memoryTimer.Start();
        }

        private void OnMemoryTimerTick(object? sender, EventArgs e)
        {
            RefreshMemory();
        }

        private void RefreshMemory()
        {
            if (_isDisposed)
                return;

            if (!_systemInformationService.TryGetMemoryInfo(out MemoryInfoModel? info, out string error)
                || info is null)
            {
                Memory = "Unavailable";
                MemoryUsedPercentage = 0;
                MemoryUsageLabel = error;
                return;
            }

            Memory = $"{info.UsedGb:F2} GB / {info.TotalGb:F2} GB";
            MemoryUsedPercentage = info.UsedPercentage;
            MemoryUsageLabel = $"{info.UsedPercentage:F2}%  Used";
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _memoryTimer.Stop();
            _memoryTimer.Tick -= OnMemoryTimerTick;
        }
    }
