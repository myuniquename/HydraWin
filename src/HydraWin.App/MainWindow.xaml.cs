using System.Windows;
using HydraWin.App.ViewModels;

namespace HydraWin.App;

/// <summary>
/// The main window. Task 08 needs this to survive while hidden and to be reachable through an
/// <c>HwndSource</c> hook, since <c>WM_HOTKEY</c> is delivered to its message loop.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
