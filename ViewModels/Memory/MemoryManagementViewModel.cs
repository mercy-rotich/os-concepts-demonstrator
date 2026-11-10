
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.Services;

namespace OS_Concepts_Demonstrator.ViewModels.Memory;

public partial class MemoryManagementViewModel : ViewModelBase
{
    private readonly SystemInformationService _systemInformationService;

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
}
