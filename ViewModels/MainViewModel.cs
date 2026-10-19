using CommunityToolkit.Mvvm.ComponentModel;
using OS_Concepts_Demonstrator.ViewModels.Dashboard;
using OS_Concepts_Demonstrator.ViewModels.Processes;

namespace OS_Concepts_Demonstrator.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public DashboardViewModel Dashboard { get; } 

    public ProcessesViewModel Processes { get; } 

    [ObservableProperty]
    private ViewModelBase currentViewModel;

    public MainViewModel()
    {
        Dashboard = new DashboardViewModel();
        Processes = new ProcessesViewModel();

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
}