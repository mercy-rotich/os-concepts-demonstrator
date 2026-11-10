using Avalonia;
using Avalonia.Controls;
using OS_Concepts_Demonstrator.ViewModels.Accounting;

namespace OS_Concepts_Demonstrator.Views.Accounting;

public partial class ProcessAccountingView : UserControl
{
    private bool _isOnScreen;

    public ProcessAccountingView()
    {
        InitializeComponent();
    }

    // Sampling only runs while this page is on screen, so leaving the page stops all reading of /proc.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isOnScreen = true;

        if (DataContext is ProcessAccountingViewModel viewModel)
            viewModel.StartMonitoring();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isOnScreen = false;

        if (DataContext is ProcessAccountingViewModel viewModel)
            viewModel.StopMonitoring();

        base.OnDetachedFromVisualTree(e);
    }

    // The DataContext can be assigned just after the view is attached; start in that case too.
    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_isOnScreen && DataContext is ProcessAccountingViewModel viewModel)
            viewModel.StartMonitoring();
    }
}
