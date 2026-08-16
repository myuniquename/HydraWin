using HydraWin.Core.Persistence;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Always-visible windows. The promise is absolute — no switch, to any task, ever hides one — so
/// the tests are mostly about the structure that makes that true rather than about a flag.
/// </summary>
public sealed class GlobalWindowTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private readonly WorkspaceStore store;
    private readonly WorkspaceService workspaces;

    public GlobalWindowTests()
    {
        Directory.CreateDirectory(directory);
        store = new WorkspaceStore(Path.Combine(directory, "state.json"), TimeSpan.FromMinutes(5));
        workspaces = new WorkspaceService(store);
    }

    public void Dispose()
    {
        store.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TrackedWindow Window(
        nint hwnd,
        string title = "Player",
        string path = @"C:\apps\player.exe") => new()
        {
            Hwnd = hwnd,
            Pid = (int)hwnd,
            ProcessPath = path,
            Title = title,
        };

    [Fact]
    public void APinnedWindowIsNeverInAHideSetForAnyTask()
    {
        // The headline promise, checked against every task rather than one.
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        HydraWinTask beta = workspaces.CreateTask("Beta");
        workspaces.AssignWindow(alpha.Id, Window(0x10));
        workspaces.AssignWindow(beta.Id, Window(0x20));
        workspaces.PinGlobal(Window(0x30));

        foreach (Guid target in new[] { alpha.Id, beta.Id, Guid.NewGuid() })
        {
            SwitchPlan plan = SwitchPlan.Compute(
                workspaces.State, target, EmptyHiddenWindowSet.Instance);

            Assert.DoesNotContain(plan.ToHide, a => a.BoundHwnd == 0x30);
            Assert.DoesNotContain(plan.ToShow, a => a.BoundHwnd == 0x30);
        }
    }

    [Fact]
    public void PinningTakesTheWindowOutOfItsTask()
    {
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        workspaces.AssignWindow(alpha.Id, Window(0x10));

        workspaces.PinGlobal(Window(0x10));

        Assert.Empty(alpha.Assignments);
        Assert.True(workspaces.IsGlobal(0x10));
        Assert.True(workspaces.IsBound(0x10));
        Assert.Null(workspaces.FindTaskOf(0x10));
    }

    [Fact]
    public void UnpinningRemovesTheRuleAsWellAsTheBinding()
    {
        // A pin left behind as a rule would silently re-pin the window next time it appeared,
        // which is the bug the task-04 "orphaned rule" fix taught us to test for.
        workspaces.PinGlobal(Window(0x10));

        workspaces.UnassignWindow(0x10);

        Assert.Empty(workspaces.GlobalWindows);
        Assert.False(workspaces.IsBound(0x10));
    }

    [Fact]
    public void AssigningAPinnedWindowToATaskUnpinsIt()
    {
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        workspaces.PinGlobal(Window(0x10));

        workspaces.AssignWindow(alpha.Id, Window(0x10));

        Assert.Empty(workspaces.GlobalWindows);
        Assert.False(workspaces.IsGlobal(0x10));
        Assert.Equal(alpha.Id, workspaces.FindTaskOf(0x10)?.Id);
    }

    [Fact]
    public void APinSurvivesARestartAndReclaimsItsWindow()
    {
        workspaces.PinGlobal(Window(0x10, "Player — album"));
        workspaces.Flush();

        using var reopened = new WorkspaceStore(
            Path.Combine(directory, "state.json"), TimeSpan.FromMinutes(5));
        var restarted = new WorkspaceService(reopened);

        Assert.Single(restarted.GlobalWindows);
        Assert.False(restarted.IsBound(0x10));

        // A new session, so a new handle for the same window.
        restarted.OnWindowAppeared(Window(0x99, "Player — album"));

        Assert.True(restarted.IsGlobal(0x99));
    }

    [Fact]
    public void APinClaimsAReappearingWindowBeforeATaskRuleCan()
    {
        // Otherwise the task would hide, at the next switch, the very window pinning exists to
        // keep on screen.
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        workspaces.AssignWindow(alpha.Id, Window(0x10, "Player"));
        workspaces.PinGlobal(Window(0x20, "Player"));

        workspaces.OnWindowDisappeared(0x10);
        workspaces.OnWindowDisappeared(0x20);

        workspaces.OnWindowAppeared(Window(0x30, "Player"));

        Assert.True(workspaces.IsGlobal(0x30));
        Assert.Null(workspaces.FindTaskOf(0x30));
    }

    [Fact]
    public void ClosingAPinnedWindowKeepsThePinAndDropsTheBinding()
    {
        workspaces.PinGlobal(Window(0x10));

        workspaces.OnWindowDisappeared(0x10);

        Assert.Single(workspaces.GlobalWindows);
        Assert.False(workspaces.IsBound(0x10));
    }

    [Fact]
    public void PinningRaisesGlobalsChangedRatherThanAnAssignmentEvent()
    {
        // The UI listens for one or the other; a pin arriving as an assignment would need a task
        // to name, and there is none.
        List<GlobalChangedEventArgs> globals = [];
        List<AssignmentChangedEventArgs> assigned = [];
        workspaces.GlobalsChanged += (_, e) => globals.Add(e);
        workspaces.WindowAssigned += (_, e) => assigned.Add(e);

        workspaces.PinGlobal(Window(0x10));

        Assert.Single(globals);
        Assert.Empty(assigned);
    }
}
