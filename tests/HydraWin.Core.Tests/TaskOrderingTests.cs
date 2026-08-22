using HydraWin.Core.Persistence;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Reordering tasks, reordering the windows inside one, and moving a window between tasks — the
/// model operations the drag-and-drop is the only caller of.
/// </summary>
public sealed class TaskOrderingTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private readonly WorkspaceStore store;
    private readonly WorkspaceService service;

    public TaskOrderingTests()
    {
        Directory.CreateDirectory(directory);
        store = new WorkspaceStore(Path.Combine(directory, "state.json"), TimeSpan.FromMinutes(5));
        service = new WorkspaceService(store);
    }

    public void Dispose()
    {
        store.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TrackedWindow Window(string process, string title, nint hwnd) => new()
    {
        Hwnd = hwnd,
        Pid = 42,
        ProcessPath = @"C:\apps\" + process,
        Title = title,
    };

    private (HydraWinTask Alpha, HydraWinTask Beta, HydraWinTask Gamma) ThreeTasks() =>
        (service.CreateTask("Alpha"), service.CreateTask("Beta"), service.CreateTask("Gamma"));

    [Fact]
    public void DraggingATaskDownPutsItAtThatPosition()
    {
        (HydraWinTask alpha, _, _) = ThreeTasks();

        service.ReorderTask(alpha.Id, 2);

        Assert.Equal(["Beta", "Gamma", "Alpha"], service.Tasks.Select(t => t.Name));
    }

    [Fact]
    public void DraggingATaskUpPutsItAtThatPosition()
    {
        (_, _, HydraWinTask gamma) = ThreeTasks();

        service.ReorderTask(gamma.Id, 0);

        Assert.Equal(["Gamma", "Alpha", "Beta"], service.Tasks.Select(t => t.Name));
    }

    [Fact]
    public void ReorderingRenumbersOrderContiguouslyFromOne()
    {
        // Order is not merely display: task 08 binds Ctrl+Alt+1..9 to it, so a gap would leave a
        // hotkey pointing at nothing.
        (HydraWinTask alpha, _, _) = ThreeTasks();

        service.ReorderTask(alpha.Id, 1);

        Assert.Equal([1, 2, 3], service.Tasks.Select(t => t.Order));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(99)]
    public void AnOutOfRangePositionIsClampedRatherThanThrowing(int index)
    {
        // A drop below the last row reports an index past the end; that means "last", not a crash.
        (HydraWinTask alpha, _, _) = ThreeTasks();

        service.ReorderTask(alpha.Id, index);

        Assert.Equal([1, 2, 3], service.Tasks.Select(t => t.Order));
        Assert.Equal(3, service.Tasks.Count);
    }

    [Fact]
    public void ReorderingAnUnknownTaskChangesNothing()
    {
        ThreeTasks();

        service.ReorderTask(Guid.NewGuid(), 0);

        Assert.Equal(["Alpha", "Beta", "Gamma"], service.Tasks.Select(t => t.Name));
    }

    [Fact]
    public void ReorderingSurvivesARestart()
    {
        (HydraWinTask alpha, _, _) = ThreeTasks();
        service.ReorderTask(alpha.Id, 2);
        service.Flush();

        using var reopened = new WorkspaceStore(
            Path.Combine(directory, "state.json"), TimeSpan.FromMinutes(5));
        var reloaded = new WorkspaceService(reopened);

        Assert.Equal(["Beta", "Gamma", "Alpha"], reloaded.Tasks.Select(t => t.Name));
    }

    /// <summary>
    /// A task holding three windows, bound to handles 0x10, 0x11 and 0x12 in that order.
    /// </summary>
    private (HydraWinTask Task, WindowAssignment[] Assignments) TaskOfThreeWindows()
    {
        HydraWinTask task = service.CreateTask("Alpha");
        WindowAssignment[] assignments =
        [
            .. new nint[] { 0x10, 0x11, 0x12 }.Select(hwnd =>
                service.AssignWindow(task.Id, Window("Code.exe", $"window {hwnd:X}", hwnd))!),
        ];

        return (task, assignments);
    }

    private static IEnumerable<nint?> Order(HydraWinTask task) =>
        task.Assignments.Select(a => a.BoundHwnd);

    [Fact]
    public void DraggingAWindowUpPutsItAboveTheOneItLandedOn()
    {
        (HydraWinTask task, WindowAssignment[] windows) = TaskOfThreeWindows();

        Assert.True(service.ReorderAssignment(windows[2].Id, windows[0].Id));

        Assert.Equal<nint?>([0x12, 0x10, 0x11], Order(task));
    }

    [Fact]
    public void DraggingAWindowDownPutsItAboveTheOneItLandedOn()
    {
        (HydraWinTask task, WindowAssignment[] windows) = TaskOfThreeWindows();

        service.ReorderAssignment(windows[0].Id, windows[2].Id);

        Assert.Equal<nint?>([0x11, 0x10, 0x12], Order(task));
    }

    [Fact]
    public void ANullAnchorPutsTheWindowAtTheBottomOfTheTask()
    {
        // How the lower half of the last row reads: there is no window to land above.
        (HydraWinTask task, WindowAssignment[] windows) = TaskOfThreeWindows();

        service.ReorderAssignment(windows[0].Id, null);

        Assert.Equal<nint?>([0x11, 0x12, 0x10], Order(task));
    }

    [Fact]
    public void MovingAWindowAboveItselfChangesNothingAndSaysSo()
    {
        (HydraWinTask task, WindowAssignment[] windows) = TaskOfThreeWindows();

        Assert.False(service.ReorderAssignment(windows[1].Id, windows[1].Id));
        Assert.False(service.ReorderAssignment(windows[1].Id, windows[2].Id));

        Assert.Equal<nint?>([0x10, 0x11, 0x12], Order(task));
    }

    [Fact]
    public void MovingTheBottomWindowToTheBottomChangesNothing()
    {
        (HydraWinTask task, WindowAssignment[] windows) = TaskOfThreeWindows();

        Assert.False(service.ReorderAssignment(windows[2].Id, null));
        Assert.Equal<nint?>([0x10, 0x11, 0x12], Order(task));
    }

    [Fact]
    public void ReorderingAnUnknownAssignmentChangesNothing()
    {
        (HydraWinTask task, WindowAssignment[] windows) = TaskOfThreeWindows();

        Assert.False(service.ReorderAssignment(Guid.NewGuid(), windows[0].Id));
        Assert.Equal<nint?>([0x10, 0x11, 0x12], Order(task));
    }

    [Fact]
    public void AnAnchorFromAnotherTaskIsRefused()
    {
        // It says nothing about where in *this* task the window goes, so guessing would be worse
        // than doing nothing.
        (HydraWinTask task, WindowAssignment[] windows) = TaskOfThreeWindows();
        HydraWinTask beta = service.CreateTask("Beta");
        WindowAssignment elsewhere = service.AssignWindow(beta.Id, Window("Code.exe", "b", 0x20))!;

        Assert.False(service.ReorderAssignment(windows[2].Id, elsewhere.Id));
        Assert.Equal<nint?>([0x10, 0x11, 0x12], Order(task));
    }

    [Fact]
    public void AClosedWindowKeepsItsPlaceWhileTheVisibleOnesMoveAroundIt()
    {
        // The rows the user drags are only the bound assignments, which is why the destination is
        // named by the window it lands above rather than by a row index. A task holding a rule for
        // a window that is currently closed has more assignments than rows.
        (HydraWinTask task, WindowAssignment[] windows) = TaskOfThreeWindows();
        service.OnWindowDisappeared(0x11);

        service.ReorderAssignment(windows[2].Id, windows[0].Id);

        Assert.Equal<nint?>([0x12, 0x10, null], Order(task));
        Assert.Same(windows[1], task.Assignments[2]);
    }

    [Fact]
    public void ReorderingAWindowAnnouncesItSoTheRowsFollow()
    {
        (_, WindowAssignment[] windows) = TaskOfThreeWindows();
        int announced = 0;
        service.TasksChanged += (_, _) => announced++;

        service.ReorderAssignment(windows[2].Id, windows[0].Id);
        service.ReorderAssignment(windows[2].Id, windows[2].Id);

        Assert.Equal(1, announced);
    }

    [Fact]
    public void WorkingInAWindowDoesNotReorderItsTask()
    {
        // The order is the user's arrangement, not a trace of how they have been working. Nothing
        // but an explicit reorder is allowed to move a row.
        (HydraWinTask task, _) = TaskOfThreeWindows();

        service.NoteForegroundWindow(0x12);
        service.NoteForegroundWindow(0x10);

        Assert.Equal<nint?>([0x10, 0x11, 0x12], Order(task));
        Assert.Equal<nint?>(0x10, task.LastActiveHwnd);
    }

    [Fact]
    public void AReopenedWindowRejoinsInTheSlotItAlreadyHeld()
    {
        (HydraWinTask task, _) = TaskOfThreeWindows();
        service.OnWindowDisappeared(0x10);

        service.OnWindowAppeared(Window("Code.exe", "window 10", 0x20));

        Assert.Equal<nint?>([0x20, 0x11, 0x12], Order(task));
    }

    [Fact]
    public void WindowOrderSurvivesARestart()
    {
        (_, WindowAssignment[] windows) = TaskOfThreeWindows();
        service.ReorderAssignment(windows[2].Id, windows[0].Id);
        service.Flush();

        using var reopened = new WorkspaceStore(
            Path.Combine(directory, "state.json"), TimeSpan.FromMinutes(5));
        var reloaded = new WorkspaceService(reopened);

        Assert.Equal(
            ["window 12", "window 10", "window 11"],
            reloaded.Tasks[0].Assignments.Select(a => a.Rule.TitlePattern));
    }

    [Fact]
    public void MovingAWindowLeavesNoRuleBehindInTheOldTask()
    {
        // The bug this guards: unbinding alone left the old task holding a rule that still
        // recognised the window, so after a restart both tasks claimed it.
        HydraWinTask alpha = service.CreateTask("Alpha");
        HydraWinTask beta = service.CreateTask("Beta");
        TrackedWindow window = Window("Code.exe", "hydrawin - Visual Studio Code", 0x10);

        service.AssignWindow(alpha.Id, window);
        service.AssignWindow(beta.Id, window);

        Assert.Empty(alpha.Assignments);
        Assert.Single(beta.Assignments);
        Assert.Same(beta, service.FindTaskOf(0x10));
    }

    [Fact]
    public void AMovedWindowReattachesOnlyToItsNewTask()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        HydraWinTask beta = service.CreateTask("Beta");
        TrackedWindow window = Window("Code.exe", "hydrawin - Visual Studio Code", 0x10);

        service.AssignWindow(alpha.Id, window);
        service.AssignWindow(beta.Id, window);

        // The window closes and comes back on a fresh handle, which is what re-attach is for.
        service.OnWindowDisappeared(0x10);
        TrackedWindow reopened = Window("Code.exe", "hydrawin - Visual Studio Code", 0x20);
        service.OnWindowAppeared(reopened);

        Assert.Same(beta, service.FindTaskOf(0x20));
        Assert.Empty(alpha.Assignments);
    }

    [Fact]
    public void MovingAWindowReportsBothHalvesSoTheUiCanFollow()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        HydraWinTask beta = service.CreateTask("Beta");
        TrackedWindow window = Window("Code.exe", "hydrawin", 0x10);
        service.AssignWindow(alpha.Id, window);

        List<string> events = [];
        service.WindowUnassigned += (_, e) => events.Add($"-{e.Task.Name}");
        service.WindowAssigned += (_, e) => events.Add($"+{e.Task.Name}");

        service.AssignWindow(beta.Id, window);

        Assert.Equal(["-Alpha", "+Beta"], events);
    }

    [Fact]
    public void ReassigningToTheSameTaskDoesNotReportAnUnassign()
    {
        // Dropping a window back onto the task it already belongs to is a no-op to the user; it
        // must not flash the row out and back in.
        HydraWinTask alpha = service.CreateTask("Alpha");
        TrackedWindow window = Window("Code.exe", "hydrawin", 0x10);
        service.AssignWindow(alpha.Id, window);

        List<string> events = [];
        service.WindowUnassigned += (_, e) => events.Add($"-{e.Task.Name}");

        service.AssignWindow(alpha.Id, window);

        Assert.Empty(events);
        Assert.Single(alpha.Assignments);
    }
}
