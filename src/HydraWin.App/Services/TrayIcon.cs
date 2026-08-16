using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using HydraWin.App.ViewModels;

namespace HydraWin.App.Services;

/// <summary>
/// HydraWin's presence in the notification area: the thing that makes it a companion rather than
/// an app you have to keep in front of you.
/// </summary>
/// <remarks>
/// Owned by <see cref="App"/> rather than by the window, so it does not depend on the window
/// existing or being visible — which is the entire point once closing hides to tray.
/// </remarks>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon icon;
    private readonly MainViewModel viewModel;
    private bool disposed;

    internal TrayIcon(MainViewModel viewModel, Action openWindow, Action<bool> exit)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(openWindow);
        ArgumentNullException.ThrowIfNull(exit);

        this.viewModel = viewModel;
        OpenWindow = openWindow;
        Exit = exit;

        var menu = new ContextMenu();
        menu.Opened += (_, _) => Rebuild(menu);

        icon = new TaskbarIcon
        {
            ToolTipText = "HydraWin",
            // Relative on purpose: WPF resolves it against the application's pack root, and it
            // avoids writing an absolute pack:// URI into the source.
            IconSource = new BitmapImage(new Uri("Assets/hydrawin.ico", UriKind.Relative)),
            ContextMenu = menu,
        };

        icon.TrayLeftMouseUp += (_, _) => OpenWindow();
    }

    private Action OpenWindow { get; }

    /// <summary>Ends the app; the flag forces the restore even if the setting is off.</summary>
    private Action<bool> Exit { get; }

    /// <summary>Shows the number of windows waiting to be looked at in the tray tooltip.</summary>
    internal void UpdatePendingCount(int pending) =>
        icon.ToolTipText = pending == 0
            ? "HydraWin"
            : $"HydraWin — {pending} window(s) waiting";

    /// <summary>Raises a tray balloon. Only called when the user has turned toasts on.</summary>
    internal void ShowBalloon(string title, string message) =>
        icon.ShowBalloonTip(title, message, BalloonIcon.Info);

    /// <inheritdoc />
    public void Dispose()
    {
        if (!disposed)
        {
            icon.Dispose();
            disposed = true;
        }
    }

    /// <summary>
    /// Rebuilds the menu each time it opens, because the task list is most of what it shows.
    /// </summary>
    private void Rebuild(ContextMenu menu)
    {
        menu.Items.Clear();

        if (viewModel.Tasks.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No tasks yet", IsEnabled = false });
        }

        foreach (TaskViewModel task in viewModel.Tasks)
        {
            TaskViewModel captured = task;
            var item = new MenuItem
            {
                Header = $"{task.Name}  ({task.WindowCount} win)",
                IsChecked = task.IsActive,
                IsCheckable = false,
            };

            item.Click += (_, _) => viewModel.SwitchToFromTray(captured.Id);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        Add(menu, "Show all windows", () => viewModel.ShowAllCommand.Execute(null));
        Add(menu, "Open HydraWin", OpenWindow);

        menu.Items.Add(new Separator());

        var closeToTray = new MenuItem
        {
            Header = "Close to tray",
            IsCheckable = true,
            IsChecked = viewModel.CloseToTray,
        };

        closeToTray.Click += (_, _) => viewModel.CloseToTray = closeToTray.IsChecked;
        menu.Items.Add(closeToTray);

        menu.Items.Add(new Separator());

        // Two exits on purpose. The first is unconditional insurance; the second honours the
        // setting, which is the only way a user can deliberately leave windows hidden.
        Add(menu, "Restore all & exit", () => Exit(true));
        Add(menu, "Exit", () => Exit(false));
    }

    private static void Add(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }
}
