using System;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Dashboard;

    public partial class DashboardViewModel : ViewModelBase
    {
        private readonly SystemInformationService _systemInformationService;

        private readonly Timer _memoryTimer;

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

            _memoryTimer = new Timer(
                _ => Dispatcher.UIThread.Post(RefreshMemory),
                null,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2));
        }

        private void RefreshMemory()
        {
            MemoryInfoModel Info = _systemInformationService.GetMemoryInfo();

            Memory = $"{Info.UsedGb:F2} GB / {Info.TotalGb:F2} GB";
            MemoryUsedPercentage = Info.UsedPercentage;
            MemoryUsageLabel = $"{Info.UsedPercentage:F2}%  Used";
        }
    }
