
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

    private void OnMemoryManagementClick(
        object? sender, 
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowMemoryManagement();
        }
    }

    private void OnStorageManagementClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowStorageManagement();
        }
    }

    private void OnIoManagementClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowIoManagement();
        }
    }

    private void OnIpcManagementClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowIpcManagement();
        }
    }

    private void OnOsServicesClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowOsServices();
        }
    }
}
