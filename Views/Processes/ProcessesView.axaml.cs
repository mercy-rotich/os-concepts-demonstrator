using Avalonia.Controls;
using Avalonia.Interactivity;
using OS_Concepts_Demonstrator.ViewModels.Processes;

namespace OS_Concepts_Demonstrator.Views.Processes;

public partial class ProcessesView : UserControl
{
    public ProcessesView()
    {
        InitializeComponent();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if(DataContext is ProcessesViewModel viewModel)
        {
            viewModel.RefreshProcesses();
        }
    }
}