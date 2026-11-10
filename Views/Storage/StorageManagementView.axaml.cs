using Avalonia.Controls;
using Avalonia.Input;
using OS_Concepts_Demonstrator.Models;
using OS_Concepts_Demonstrator.ViewModels.Storage;

namespace OS_Concepts_Demonstrator.Views.Storage;

public partial class StorageManagementView : UserControl
{
    public StorageManagementView()
    {
        InitializeComponent();
    }

    // A double-click is purely a UI gesture, so it is translated here into a
    // single ViewModel call; the decision of what to do stays in the ViewModel.
    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is StorageManagementViewModel viewModel &&
            EntriesList.SelectedItem is DirectoryEntryModel entry)
        {
            viewModel.OpenEntry(entry);
        }
    }

    // The button's DataContext is the MountPointModel of its row.
    private void OnBrowseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is StorageManagementViewModel viewModel &&
            sender is Button { DataContext: MountPointModel mountPoint })
        {
            viewModel.BrowseMountPoint(mountPoint);
        }
    }
}
