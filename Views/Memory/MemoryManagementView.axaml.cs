
using Avalonia.Controls;
using Avalonia.Interactivity;
using OS_Concepts_Demonstrator.ViewModels.Memory;

namespace OS_Concepts_Demonstrator.Views.Memory;

public partial class MemoryManagementView : UserControl
{
    public MemoryManagementView()
    {
        InitializeComponent();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MemoryManagementViewModel viewModel)
        {
            viewModel.RefreshMemory();
        }
    }
}
