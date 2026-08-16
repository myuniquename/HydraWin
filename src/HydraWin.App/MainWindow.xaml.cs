using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using HydraWin.App.ViewModels;
using HydraWin.Core.Recovery;

namespace HydraWin.App;

/// <summary>
/// The main window: task table, unassigned pane, and the mouse handling that keeps "click to
/// switch" and "drag to rearrange" apart.
/// </summary>
/// <remarks>
/// Task 08 needs this window to survive while hidden and to be reachable through an
/// <c>HwndSource</c> hook, since <c>WM_HOTKEY</c> is delivered to its message loop.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly DropIndicator indicator = new();

    private Point dragOrigin;
    private object? pressedItem;
    private bool dragging;
    private DragGhostAdorner? ghost;
    private WindowViewModel? menuWindow;
    private TaskViewModel? menuTask;

    public MainWindow(RecoveryJournal journal, RestoreService restoreService)
    {
        InitializeComponent();
        viewModel = new MainViewModel(journal, restoreService)
        {
            ConfirmDelete = ConfirmDelete,
        };

        DataContext = viewModel;

        // The tracker's WinEvent hooks need a message pump, which the dispatcher thread has once
        // the window is loaded.
        Loaded += (_, _) => viewModel.Start();
        Closed += (_, _) => viewModel.Dispose();
    }

    /// <summary>Reports what startup recovery put back, without interrupting the user.</summary>
    public void ShowRecoveryNotice(RestoreSummary summary) => viewModel.ShowRecoveryNotice(summary);

    /// <summary>
    /// Any press anywhere outside an open rename box commits it — a toolbar button, the start of a
    /// drag, another task row. Handled at the window so nothing has to remember to do it, and in
    /// the tunnelling phase so the rename is settled before the click is acted on.
    /// </summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!DragDropSupport.IsWithin<TextBox>(e.OriginalSource))
        {
            viewModel.CommitPendingRename();
        }

        base.OnPreviewMouseDown(e);
    }

    /// <summary>
    /// Asks before deleting a task. The wording is deliberate: it states what happens to the
    /// windows, because that is the safety model the whole app rests on.
    /// </summary>
    private bool ConfirmDelete(TaskViewModel task) =>
        MessageBox.Show(
            this,
            $"Delete “{task.Name}”?\n\n"
                + $"Its {task.WindowCount} window(s) will be un-hidden and returned to "
                + "Unassigned. No window is closed.",
            "Delete task",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    // ------------------------------------------------------------------ dragging

    private void OnPaneMouseDown(object sender, MouseButtonEventArgs e)
    {
        dragging = false;
        pressedItem = null;

        // The expand toggle and the rename box own their clicks; a press there is neither a drag
        // nor a switch.
        if (DragDropSupport.IsInteractiveControl(e.OriginalSource))
        {
            return;
        }

        dragOrigin = e.GetPosition(this);
        pressedItem = DragDropSupport.FindDataContext<WindowViewModel>(e.OriginalSource)
            ?? (object?)DragDropSupport.FindDataContext<TaskViewModel>(e.OriginalSource);

        // A window row has no click action to protect, so its drag begins on the press itself —
        // which is the moment the user expects to see that they have picked something up. A task
        // row cannot: a click there switches tasks, so it has to wait and see whether the pointer
        // moves.
        if (pressedItem is WindowViewModel)
        {
            BeginDrag((DependencyObject)sender, e.OriginalSource);
        }
    }

    private void OnPaneMouseMove(object sender, MouseEventArgs e)
    {
        if (!dragging
            && pressedItem is TaskViewModel
            && e.LeftButton == MouseButtonState.Pressed
            && DragDropSupport.PastDragThreshold(dragOrigin, e.GetPosition(this)))
        {
            BeginDrag((DependencyObject)sender, e.OriginalSource);
        }
    }

    /// <summary>
    /// Runs one drag: builds the payload, raises the ghost, and blocks in WPF's modal drag loop
    /// until the drop.
    /// </summary>
    private void BeginDrag(DependencyObject source, object? originalSource)
    {
        DataObject data = new();
        string rowTag;

        switch (pressedItem)
        {
            case WindowViewModel window:
                data.SetData(DragDropSupport.WindowFormat, (long)window.Hwnd);
                rowTag = DragDropSupport.WindowRowTag;
                break;

            case TaskViewModel task:
                data.SetData(DragDropSupport.TaskFormat, task.Id);
                rowTag = DragDropSupport.TaskRowTag;
                break;

            default:
                return;
        }

        dragging = true;
        ShowGhost(DragDropSupport.FindRow(originalSource, rowTag));

        try
        {
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move);
        }
        finally
        {
            HideGhost();
            indicator.Clear();
            pressedItem = null;
        }
    }

    private void ShowGhost(FrameworkElement? row)
    {
        if (row is null)
        {
            return;
        }

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(RootPanel);
        if (layer is null)
        {
            return;
        }

        ghost = new DragGhostAdorner(RootPanel, row);
        ghost.MoveTo(Mouse.GetPosition(RootPanel));
        layer.Add(ghost);
    }

    private void HideGhost()
    {
        if (ghost is not null)
        {
            AdornerLayer.GetAdornerLayer(RootPanel)?.Remove(ghost);
            ghost = null;
        }
    }

    /// <summary>
    /// Keeps the ghost under the pointer. Handled at the window rather than on the panes so it
    /// still tracks over the toolbar, the splitter and the gaps between drop targets.
    /// </summary>
    private void OnWindowDragOver(object sender, DragEventArgs e) =>
        ghost?.MoveTo(e.GetPosition(RootPanel));

    /// <summary>
    /// A press that did not turn into a drag, on a task row rather than one of its windows, is the
    /// core interaction: switch to that task.
    /// </summary>
    private void OnTaskPaneMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging && pressedItem is TaskViewModel task)
        {
            viewModel.SwitchToCommand.Execute(task);
        }

        dragging = false;
        pressedItem = null;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => indicator.Clear();

    private void OnTaskPaneDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;

        if (e.Data.GetDataPresent(DragDropSupport.WindowFormat))
        {
            FrameworkElement? row =
                DragDropSupport.FindRow(e.OriginalSource, DragDropSupport.TaskRowTag);
            if (row is null)
            {
                indicator.Clear();
                return;
            }

            e.Effects = DragDropEffects.Move;
            indicator.Highlight(row);
            return;
        }

        if (e.Data.GetDataPresent(DragDropSupport.TaskFormat))
        {
            ShowTaskInsertion(e);
        }
        else
        {
            indicator.Clear();
        }
    }

    private void ShowTaskInsertion(DragEventArgs e)
    {
        // Dropping a task onto a window is meaningless; say so with the cursor rather than
        // guessing at an intent (§ C).
        if (DragDropSupport.FindRow(e.OriginalSource, DragDropSupport.WindowRowTag) is not null)
        {
            indicator.Clear();
            return;
        }

        FrameworkElement? row =
            DragDropSupport.FindRow(e.OriginalSource, DragDropSupport.TaskRowTag);

        if (row is null)
        {
            // Past the last row: the drop appends.
            FrameworkElement? last = RowFor(viewModel.Tasks.LastOrDefault());
            if (last is null)
            {
                indicator.Clear();
                return;
            }

            e.Effects = DragDropEffects.Move;
            indicator.Insertion(last, below: true);
            return;
        }

        e.Effects = DragDropEffects.Move;
        indicator.Insertion(row, below: e.GetPosition(row).Y > row.ActualHeight / 2);
    }

    private void OnTaskPaneDrop(object sender, DragEventArgs e)
    {
        indicator.Clear();
        e.Handled = true;

        if (e.Data.GetDataPresent(DragDropSupport.WindowFormat))
        {
            FrameworkElement? row =
                DragDropSupport.FindRow(e.OriginalSource, DragDropSupport.TaskRowTag);
            viewModel.AssignWindow(WindowFrom(e.Data), row?.DataContext as TaskViewModel);
            return;
        }

        if (e.Data.GetDataPresent(DragDropSupport.TaskFormat))
        {
            DropTask(e);
        }
    }

    private void DropTask(DragEventArgs e)
    {
        if (e.Data.GetData(DragDropSupport.TaskFormat) is not Guid id
            || viewModel.FindTask(id) is not TaskViewModel dragged
            || DragDropSupport.FindRow(e.OriginalSource, DragDropSupport.WindowRowTag) is not null)
        {
            return;
        }

        FrameworkElement? row =
            DragDropSupport.FindRow(e.OriginalSource, DragDropSupport.TaskRowTag);

        int current = viewModel.Tasks.IndexOf(dragged);
        int target;

        if (row?.DataContext is TaskViewModel over)
        {
            target = viewModel.Tasks.IndexOf(over);
            if (e.GetPosition(row).Y > row.ActualHeight / 2)
            {
                target++;
            }
        }
        else
        {
            target = viewModel.Tasks.Count;
        }

        // The insertion point was measured with the dragged row still in place; removing it first
        // shifts everything after it up by one.
        if (current < target)
        {
            target--;
        }

        viewModel.ReorderTask(id, target);
    }

    private void OnUnassignedDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;

        // Only a window that belongs to a task has anything to gain from being dropped here.
        if (WindowFrom(e.Data) is WindowViewModel window && viewModel.TaskOf(window) is not null)
        {
            e.Effects = DragDropEffects.Move;
            indicator.Highlight(UnassignedPane);
        }
        else
        {
            indicator.Clear();
        }
    }

    private void OnUnassignedDrop(object sender, DragEventArgs e)
    {
        indicator.Clear();
        e.Handled = true;

        if (WindowFrom(e.Data) is WindowViewModel window && viewModel.TaskOf(window) is not null)
        {
            viewModel.UnassignWindowCommand.Execute(window);
        }
    }

    private WindowViewModel? WindowFrom(IDataObject data) =>
        data.GetDataPresent(DragDropSupport.WindowFormat)
            && data.GetData(DragDropSupport.WindowFormat) is long handle
            ? viewModel.FindWindow((nint)handle)
            : null;

    /// <summary>The row element for a task, reached through its generated item container.</summary>
    private FrameworkElement? RowFor(TaskViewModel? task)
    {
        if (task is null)
        {
            return null;
        }

        return TaskList.ItemContainerGenerator.ContainerFromItem(task) is DependencyObject container
            ? DragDropSupport.FindDescendantRow(container, DragDropSupport.TaskRowTag)
            : null;
    }

    // ------------------------------------------------------------------ menus

    private void OnWindowMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu
            || menu.PlacementTarget is not FrameworkElement target
            || target.DataContext is not WindowViewModel window)
        {
            return;
        }

        menuWindow = window;
        TaskViewModel? owner = viewModel.TaskOf(window);

        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Tag)
            {
                case "Unassign":
                    // Nothing to unassign it from.
                    item.Visibility = owner is null ? Visibility.Collapsed : Visibility.Visible;
                    break;

                case "MoveTo":
                    BuildMoveToMenu(item, window, owner);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Rebuilds the "move to" submenu on every open, because the task list is what it lists.
    /// </summary>
    private void BuildMoveToMenu(MenuItem item, WindowViewModel window, TaskViewModel? owner)
    {
        item.Items.Clear();
        item.Header = owner is null ? "_Assign to" : "_Move to";

        foreach (TaskViewModel task in viewModel.Tasks.Where(t => t != owner))
        {
            var entry = new MenuItem { Header = task.Name };
            entry.Click += (_, _) => viewModel.AssignWindow(window, task);
            item.Items.Add(entry);
        }

        item.IsEnabled = item.Items.Count > 0;
    }

    private void OnTaskMenuOpened(object sender, RoutedEventArgs e) =>
        menuTask = sender is ContextMenu { PlacementTarget: FrameworkElement target }
            ? target.DataContext as TaskViewModel
            : null;

    private void OnFocusWindowClick(object sender, RoutedEventArgs e) =>
        viewModel.FocusWindowCommand.Execute(menuWindow);

    private void OnUnassignWindowClick(object sender, RoutedEventArgs e) =>
        viewModel.UnassignWindowCommand.Execute(menuWindow);

    private void OnSwitchToTaskClick(object sender, RoutedEventArgs e) =>
        viewModel.SwitchToCommand.Execute(menuTask);

    private void OnDeleteTaskClick(object sender, RoutedEventArgs e) =>
        viewModel.DeleteTaskCommand.Execute(menuTask);

    private void OnRenameTaskClick(object sender, RoutedEventArgs e)
    {
        if (menuTask is not null)
        {
            menuTask.IsRenaming = true;
        }
    }

    // ------------------------------------------------------------------ renaming

    private void OnTaskNameClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || DragDropSupport.FindDataContext<TaskViewModel>(sender) is not
            TaskViewModel task)
        {
            return;
        }

        task.IsRenaming = true;

        // The first click of the double-click already armed a switch; renaming cancels it.
        pressedItem = null;
        e.Handled = true;
    }

    private void OnRenameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box)
        {
            // The box is only just becoming visible, so focus has to wait for layout.
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }

    private void OnRenameBoxKeyDown(object sender, KeyEventArgs e)
    {
        TaskViewModel? task = DragDropSupport.FindDataContext<TaskViewModel>(sender);

        switch (e.Key)
        {
            case Key.Enter:
                viewModel.RenameTaskCommand.Execute(task);
                e.Handled = true;
                break;

            case Key.Escape:
                viewModel.CancelRename(task);
                e.Handled = true;
                break;

            default:
                break;
        }
    }

    private void OnRenameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (DragDropSupport.FindDataContext<TaskViewModel>(sender) is
            { IsRenaming: true } task)
        {
            viewModel.RenameTaskCommand.Execute(task);
        }
    }
}
