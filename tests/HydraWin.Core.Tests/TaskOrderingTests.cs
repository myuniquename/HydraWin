using HydraWin.Core.Persistence;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Task reordering and moving a window between tasks — the two model operations task 07's
/// drag-and-drop is the only caller of.
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
