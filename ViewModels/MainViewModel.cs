using System;
using OS_Concepts_Demonstrator.ViewModels.Memory;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.ViewModels.Dashboard;
using OS_Concepts_Demonstrator.ViewModels.Processes;

namespace OS_Concepts_Demonstrator.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    public DashboardViewModel Dashboard { get; } 

    public ProcessesViewModel Processes { get; } 

    public MemoryManagementViewModel MemoryManagement { get; }

    [ObservableProperty]
    private ViewModelBase currentViewModel;

    public MainViewModel()
    {
        Dashboard = new DashboardViewModel();
        Processes = new ProcessesViewModel();
        MemoryManagement = new MemoryManagementViewModel();

        CurrentViewModel = Dashboard;
    }

    public void ShowDashboard()
    {
        CurrentViewModel = Dashboard;
    }

    public void ShowProcesses()
    {
        CurrentViewModel = Processes;
    }

    public void ShowMemoryManagement()
{
    CurrentViewModel = MemoryManagement;
}

    public void Dispose()
    {
        Dashboard.Dispose();
        MemoryManagement.Dispose();
    }
}