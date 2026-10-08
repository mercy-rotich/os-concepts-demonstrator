using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Dashboard;

    public partial class DashboardViewModel : ObservableObject
    {
        private readonly SystemInformationService _systemInformationService;

        private readonly Timer _memoryTimer;

        [ObservableProperty]
        private string operatingSystem = string.Empty;

        [ObservableProperty]
        private string processor = string.Empty;

        [ObservableProperty]
        private string memory = string.Empty;

        public DashboardViewModel()
        {
            _systemInformationService = new SystemInformationService();

            OperatingSystem = _systemInformationService.GetOperatingSystem();
            Processor = _systemInformationService.GetProcessor();
            Memory = _systemInformationService.GetMemoryInformation();

            _memoryTimer = new Timer(
                UpdateMemory, 
                null, 
                TimeSpan.FromSeconds(2), 
                TimeSpan.FromSeconds(2));
        }

        private void UpdateMemory(object? state)
        {
            Memory = _systemInformationService.GetMemoryInformation();
        }
    }
