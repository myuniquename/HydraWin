using HydraWin.Core.Persistence;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>Assignment flows, against a real store in a temp directory.</summary>
public sealed class WorkspaceServiceTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private readonly WorkspaceStore store;
    private readonly WorkspaceService service;

    public WorkspaceServiceTests()
    {
        Directory.CreateDirectory(directory);

        // A long debounce so nothing writes spontaneously mid-test: the tests that care about
        // disk call Flush() explicitly, which is the same path shutdown uses.
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

    [Fact]
    public void CreatingTasksNumbersThemFromOneAndGivesThemDistinctColours()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        HydraWinTask beta = service.CreateTask("Beta");

        Assert.Equal(1, alpha.Order);
        Assert.Equal(2, beta.Order);
        Assert.NotEqual(alpha.ColorHex, beta.ColorHex);
        Assert.Equal(["Alpha", "Beta"], service.Tasks.Select(t => t.Name));
    }

    [Fact]
    public void AssigningAWindowCreatesARuleAndBindsIt()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        TrackedWindow window = Window("Code.exe", "● hydrawin - Visual Studio Code", 0x10);

        WindowAssignment? assignment = service.AssignWindow(alpha.Id, window);

        Assert.NotNull(assignment);
        Assert.Equal("Code.exe", assignment.Rule.ProcessFileName);
        Assert.Equal("hydrawin - Visual Studio Code", assignment.Rule.TitlePattern);
        Assert.Equal(0x10, assignment.BoundHwnd);
        Assert.True(service.IsBound(0x10));
        Assert.Same(alpha, service.FindTaskOf(0x10));
    }

    [Fact]
    public void AssigningAnAlreadyAssignedWindowMovesItRatherThanDuplicatingIt()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        HydraWinTask beta = service.CreateTask("Beta");
        TrackedWindow window = Window("Code.exe", "hydrawin", 0x10);

        service.AssignWindow(alpha.Id, window);
        service.AssignWindow(beta.Id, window);

        Assert.DoesNotContain(alpha.Assignments, a => a.IsBound);
        Assert.Single(beta.Assignments);
        Assert.Same(beta, service.FindTaskOf(0x10));
    }

    [Fact]
    public void AssigningToAnUnknownTaskDoesNothing()
    {
        Assert.Null(service.AssignWindow(Guid.NewGuid(), Window("Code.exe", "x", 0x10)));
        Assert.False(service.IsBound(0x10));
    }

    [Fact]
    public void UnassigningRemovesTheRuleEntirely()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.AssignWindow(alpha.Id, Window("Code.exe", "hydrawin", 0x10));

        service.UnassignWindow(0x10);

        Assert.Empty(alpha.Assignments);
        Assert.False(service.IsBound(0x10));
    }

    [Fact]
    public void AClosedWindowKeepsItsRuleSoItCanComeBack()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.AssignWindow(alpha.Id, Window("Code.exe", "hydrawin", 0x10));

        service.OnWindowDisappeared(0x10);

        WindowAssignment assignment = Assert.Single(alpha.Assignments);
        Assert.False(assignment.IsBound);
        Assert.Equal("hydrawin", assignment.Rule.TitlePattern);
        Assert.False(service.IsBound(0x10));
    }

    [Fact]
    public void AReopenedWindowReattachesToItsTaskOnANewHandle()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.AssignWindow(alpha.Id, Window("Code.exe", "hydrawin - Visual Studio Code", 0x10));
        service.OnWindowDisappeared(0x10);

        AssignmentChangedEventArgs? reattached = null;
        service.WindowReattached += (_, e) => reattached = e;

        // Same app, same folder, brand-new handle - the point of the whole rule mechanism.
        service.OnWindowAppeared(Window("Code.exe", "hydrawin - Visual Studio Code", 0x99));

        Assert.True(service.IsBound(0x99));
        Assert.Same(alpha, service.FindTaskOf(0x99));
        Assert.NotNull(reattached);
        Assert.Same(alpha, reattached.Task);
        Assert.Equal("hydrawin - Visual Studio Code", reattached.Window!.Title);
    }

    [Fact]
    public void AReopenedClaudeCodeTerminalReattachesDespiteADifferentSpinnerFrame()
    {
        // The marker rotates about once a second; the rule stores the session name alone.
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.AssignWindow(alpha.Id, Window("WindowsTerminal.exe", "◐ my-session", 0x10));
        service.OnWindowDisappeared(0x10);

        service.OnWindowAppeared(Window("WindowsTerminal.exe", "✳ my-session", 0x99));

        Assert.Same(alpha, service.FindTaskOf(0x99));
    }

    [Fact]
    public void AnAlreadyBoundWindowIsNotRebound()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.AssignWindow(alpha.Id, Window("Code.exe", "hydrawin", 0x10));
        int reattachCount = 0;
        service.WindowReattached += (_, _) => reattachCount++;

        service.OnWindowAppeared(Window("Code.exe", "hydrawin", 0x10));

        Assert.Equal(0, reattachCount);
        Assert.Single(alpha.Assignments);
    }

    [Fact]
    public void DeletingATaskReturnsItsAssignmentsSoTheCallerCanUnhideThem()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.AssignWindow(alpha.Id, Window("Code.exe", "hydrawin", 0x10));
        service.AssignWindow(alpha.Id, Window("chrome.exe", "Docs", 0x20));

        IReadOnlyList<WindowAssignment> orphaned = service.DeleteTask(alpha.Id);

        Assert.Equal(2, orphaned.Count);
        Assert.Empty(service.Tasks);
        Assert.False(service.IsBound(0x10));
        Assert.False(service.IsBound(0x20));
    }

    [Fact]
    public void DeletingTheActiveTaskClearsTheActiveId()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.State.ActiveTaskId = alpha.Id;

        service.DeleteTask(alpha.Id);

        Assert.Null(service.State.ActiveTaskId);
    }

    [Fact]
    public void RenamingATaskKeepsItsIdentityAndAssignments()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.AssignWindow(alpha.Id, Window("Code.exe", "hydrawin", 0x10));

        service.RenameTask(alpha.Id, "Renamed");

        Assert.Equal("Renamed", Assert.Single(service.Tasks).Name);
        Assert.True(service.IsBound(0x10));
    }

    [Fact]
    public void TheModelSurvivesARestartAndComesBackUnbound()
    {
        HydraWinTask alpha = service.CreateTask("Alpha");
        service.AssignWindow(alpha.Id, Window("Code.exe", "hydrawin - Visual Studio Code", 0x10));
        service.Flush();

        using var reopenedStore = new WorkspaceStore(store.Path, TimeSpan.FromMinutes(5));
        var reopened = new WorkspaceService(reopenedStore);

        HydraWinTask loaded = Assert.Single(reopened.Tasks);
        Assert.Equal("Alpha", loaded.Name);
        WindowAssignment assignment = Assert.Single(loaded.Assignments);
        Assert.Equal("hydrawin - Visual Studio Code", assignment.Rule.TitlePattern);

        // Handles do not survive a restart; the rule is what re-binds the window.
        Assert.False(assignment.IsBound);
        Assert.False(reopened.IsBound(0x10));
    }

    [Fact]
    public void StayOnTopDefaultsToOn()
    {
        // A switch ends by focusing one of the task's windows, so without this the manager is
        // buried by the very act of using it.
        Assert.True(service.State.Settings.AlwaysOnTop);
    }

    [Fact]
    public void ChangingASettingSurvivesARestart()
    {
        service.UpdateSettings(settings => settings.AlwaysOnTop = false);
        service.Flush();

        using var reopenedStore = new WorkspaceStore(store.Path, TimeSpan.FromMinutes(5));
        var reopened = new WorkspaceService(reopenedStore);

        Assert.False(reopened.State.Settings.AlwaysOnTop);
        Assert.True(reopened.State.Settings.RestoreOnExit);
    }
}
