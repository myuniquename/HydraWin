using System.Windows;
using System.Windows.Input;
using HydraWin.App.ViewModels;
using HydraWin.Core.Recovery;

namespace HydraWin.App;

/// <summary>
/// The main window. Task 08 needs this to survive while hidden and to be reachable through an
/// <c>HwndSource</c> hook, since <c>WM_HOTKEY</c> is delivered to its message loop.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow(RecoveryJournal journal, RestoreService restoreService)
    {
        InitializeComponent();
        viewModel = new MainViewModel(journal, restoreService);
        DataContext = viewModel;

        // The tracker's WinEvent hooks need a message pump, which the dispatcher thread has once
        // the window is loaded.
        Loaded += (_, _) => viewModel.Start();
        Closed += (_, _) => viewModel.Dispose();

        // Task 06 harness: number keys switch tasks by position. Task 07 replaces this.
        KeyDown += OnKeyDown;
    }

    /// <summary>Reports what startup recovery put back, without interrupting the user.</summary>
    public void ShowRecoveryNotice(RestoreSummary summary) => viewModel.ShowRecoveryNotice(summary);

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        int order = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1 + 1,
            _ => 0,
        };

        if (order > 0)
        {
            viewModel.SwitchToOrderCommand.Execute(order);
            e.Handled = true;
        }
    }
}
