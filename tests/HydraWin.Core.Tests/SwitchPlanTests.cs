using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>Who gets hidden and who gets shown. Pure — no Win32, no journal.</summary>
public class SwitchPlanTests
{
    private static HydraWinTask Task(string name, int order, params nint[] boundHandles)
    {
        var task = new HydraWinTask { Name = name, Order = order };
        foreach (nint hwnd in boundHandles)
        {
            task.Assignments.Add(new WindowAssignment { BoundHwnd = hwnd });
        }

        return task;
    }

    private static HiddenWindowSet Hidden(params nint[] handles)
    {
        var set = new HiddenWindowSet();
        foreach (nint hwnd in handles)
        {
            set.MarkHidden(hwnd);
        }

        return set;
    }

    [Fact]
    public void SwitchingHidesTheOtherTasksVisibleWindows()
    {
        HydraWinTask alpha = Task("Alpha", 1, 0x10, 0x11);
        HydraWinTask beta = Task("Beta", 2, 0x20);
        var state = new WorkspaceState { Tasks = [alpha, beta] };

        SwitchPlan plan = SwitchPlan.Compute(state, alpha.Id, Hidden());

        Assert.Equal([0x20], plan.ToHide.Select(a => a.BoundHwnd));
        Assert.Empty(plan.ToShow);
    }

    [Fact]
    public void SwitchingShowsTheTargetsHiddenWindows()
    {
        HydraWinTask alpha = Task("Alpha", 1, 0x10, 0x11);
        HydraWinTask beta = Task("Beta", 2, 0x20);
        var state = new WorkspaceState { Tasks = [alpha, beta] };

        SwitchPlan plan = SwitchPlan.Compute(state, alpha.Id, Hidden(0x10, 0x11));

        Assert.Equal([0x10, 0x11], plan.ToShow.Select(a => a.BoundHwnd));
        Assert.Equal([0x20], plan.ToHide.Select(a => a.BoundHwnd));
    }

    [Fact]
    public void AlreadyHiddenWindowsAreNotHiddenAgain()
    {
        HydraWinTask alpha = Task("Alpha", 1, 0x10);
        HydraWinTask beta = Task("Beta", 2, 0x20);
        var state = new WorkspaceState { Tasks = [alpha, beta] };

        SwitchPlan plan = SwitchPlan.Compute(state, alpha.Id, Hidden(0x20));

        Assert.Empty(plan.ToHide);
    }

    [Fact]
    public void SwitchingToTheAlreadyActiveTaskIsANoOp()
    {
        // Everything of Alpha's is visible and everything else is hidden: nothing left to do.
        HydraWinTask alpha = Task("Alpha", 1, 0x10);
        HydraWinTask beta = Task("Beta", 2, 0x20);
        var state = new WorkspaceState { Tasks = [alpha, beta] };

        SwitchPlan plan = SwitchPlan.Compute(state, alpha.Id, Hidden(0x20));

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void UnboundAssignmentsAreIgnored()
    {
        // The window is closed; there is nothing to hide or show, only a rule waiting to re-attach.
        HydraWinTask alpha = Task("Alpha", 1);
        alpha.Assignments.Add(new WindowAssignment());
        HydraWinTask beta = Task("Beta", 2);
        beta.Assignments.Add(new WindowAssignment());
        var state = new WorkspaceState { Tasks = [alpha, beta] };

        SwitchPlan plan = SwitchPlan.Compute(state, alpha.Id, Hidden());

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void UnassignedWindowsCanNeverBeTouched()
    {
        // The plan is built from assignments only, which is exactly why an unassigned window
        // survives every switch. 0x99 belongs to no task and appears nowhere.
        HydraWinTask alpha = Task("Alpha", 1, 0x10);
        HydraWinTask beta = Task("Beta", 2, 0x20);
        var state = new WorkspaceState { Tasks = [alpha, beta] };

        SwitchPlan plan = SwitchPlan.Compute(state, alpha.Id, Hidden());

        Assert.DoesNotContain(plan.ToHide, a => a.BoundHwnd == 0x99);
        Assert.DoesNotContain(plan.ToShow, a => a.BoundHwnd == 0x99);
    }

    [Fact]
    public void EveryOtherTaskContributesNotJustOne()
    {
        HydraWinTask alpha = Task("Alpha", 1, 0x10);
        HydraWinTask beta = Task("Beta", 2, 0x20);
        HydraWinTask gamma = Task("Gamma", 3, 0x30, 0x31);
        var state = new WorkspaceState { Tasks = [alpha, beta, gamma] };

        SwitchPlan plan = SwitchPlan.Compute(state, alpha.Id, Hidden());

        Assert.Equal([0x20, 0x30, 0x31], plan.ToHide.Select(a => a.BoundHwnd));
    }

    [Fact]
    public void AnUnmanageableAssignmentIsStillRetried()
    {
        // The flag records that a window refused once, not that it always will — retrying is what
        // lets the app heal if that process is restarted without elevation.
        HydraWinTask alpha = Task("Alpha", 1, 0x10);
        HydraWinTask beta = Task("Beta", 2, 0x20);
        beta.Assignments[0].Unmanageable = true;
        var state = new WorkspaceState { Tasks = [alpha, beta] };

        SwitchPlan plan = SwitchPlan.Compute(state, alpha.Id, Hidden());

        Assert.Single(plan.ToHide);
    }

    [Fact]
    public void AnUnknownTaskHidesEverythingAndShowsNothing()
    {
        HydraWinTask alpha = Task("Alpha", 1, 0x10);
        var state = new WorkspaceState { Tasks = [alpha] };

        SwitchPlan plan = SwitchPlan.Compute(state, Guid.NewGuid(), Hidden());

        Assert.Single(plan.ToHide);
        Assert.Empty(plan.ToShow);
    }
}
