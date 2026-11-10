using System;
using Avalonia.Controls;
using OS_Concepts_Demonstrator.ViewModels;

namespace OS_Concepts_Demonstrator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}