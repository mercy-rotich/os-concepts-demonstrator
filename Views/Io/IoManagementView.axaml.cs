using Avalonia;
using Avalonia.Controls;
using OS_Concepts_Demonstrator.ViewModels.Io;

namespace OS_Concepts_Demonstrator.Views.Io;

public partial class IoManagementView : UserControl
{
    private bool _isOnScreen;

    public IoManagementView()
    {
        InitializeComponent();
    }

    // Sampling only runs while this page is actually on screen, so leaving the page
    // stops the timer and nothing keeps reading /proc in the background.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isOnScreen = true;

        if (DataContext is IoManagementViewModel viewModel)
            viewModel.StartMonitoring();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isOnScreen = false;

        if (DataContext is IoManagementViewModel viewModel)
            viewModel.StopMonitoring();

        base.OnDetachedFromVisualTree(e);
    }

    // The DataContext can be assigned just after the view is attached; start in that case too.
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_isOnScreen && DataContext is IoManagementViewModel viewModel)
            viewModel.StartMonitoring();
    }
}
