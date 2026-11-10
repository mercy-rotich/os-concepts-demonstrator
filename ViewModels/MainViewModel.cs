using System;
using OS_Concepts_Demonstrator.ViewModels.Memory;
using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.ViewModels.Dashboard;
using OS_Concepts_Demonstrator.ViewModels.Processes;
using OS_Concepts_Demonstrator.ViewModels.Storage;

namespace OS_Concepts_Demonstrator.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    public DashboardViewModel Dashboard { get; } 

    public ProcessesViewModel Processes { get; } 

    public MemoryManagementViewModel MemoryManagement { get; }

    // Created the first time the page is opened, then reused so the user
    // returns to the same folder.
    private StorageManagementViewModel? _storageManagement;

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

    public void ShowStorageManagement()
    {
        _storageManagement ??= new StorageManagementViewModel();
        CurrentViewModel = _storageManagement;
    }

    public void Dispose()
    {
        Dashboard.Dispose();
        MemoryManagement.Dispose();
    }
}