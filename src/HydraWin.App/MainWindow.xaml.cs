using System.Windows;
using HydraWin.App.ViewModels;

namespace HydraWin.App;

/// <summary>
/// The main window. Task 08 needs this to survive while hidden and to be reachable through an
/// <c>HwndSource</c> hook, since <c>WM_HOTKEY</c> is delivered to its message loop.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainViewModel();
        DataContext = viewModel;

        // The tracker's WinEvent hooks need a message pump, which the dispatcher thread has once
        // the window is loaded.
        Loaded += (_, _) => viewModel.Start();
        Closed += (_, _) => viewModel.Dispose();
    }
}
