using Avalonia;
using Avalonia.Controls;
using OS_Concepts_Demonstrator.ViewModels.Ipc;

namespace OS_Concepts_Demonstrator.Views.Ipc;

public partial class IpcManagementView : UserControl
{
    public IpcManagementView()
    {
        InitializeComponent();
    }

    // Leaving the page stops the child process and removes the shared-memory file,
    // so nothing from the demos is left running or lying around.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is IpcManagementViewModel viewModel)
            viewModel.ReleaseResources();

        base.OnDetachedFromVisualTree(e);
    }
}
