
using Avalonia.Controls;
using OS_Concepts_Demonstrator.ViewModels;

namespace OS_Concepts_Demonstrator.Views;

public partial class NavigationView : UserControl
{
    public NavigationView()
    {
        InitializeComponent();
    }

    private void OnDashboardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowDashboard();
        }
    }

    private void OnProcessesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowProcesses();
        }
    }
}
