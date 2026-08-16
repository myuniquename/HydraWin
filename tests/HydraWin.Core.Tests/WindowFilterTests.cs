using HydraWin.Core.Tracking;

namespace HydraWin.Core.Tests;

/// <summary>
/// One test per trackability clause. The filter is pure, so none of this touches Win32.
/// </summary>
public class WindowFilterTests
{
    private const int OwnPid = 1000;
    private const int OtherPid = 2000;
    private const long ToolWindowStyle = 0x0000_0080L;

    private static WindowFacts Trackable() => new(
        Hwnd: 0x1234,
        Title: "A real window",
        IsVisible: true,
        IsHydraWinHidden: false,
        ExtendedStyle: 0,
        Owner: 0,
        IsCloaked: false,
        Pid: OtherPid);

    [Fact]
    public void AnOrdinaryVisibleWindowIsTrackable()
    {
        WindowFacts facts = Trackable();

        Assert.Equal(TrackableVerdict.Trackable, WindowFilter.Evaluate(in facts, OwnPid));
        Assert.True(WindowFilter.IsTrackable(in facts, OwnPid));
    }

    [Fact]
    public void HydraWinsOwnWindowsAreNeverTracked()
    {
        WindowFacts facts = Trackable() with { Pid = OwnPid };

        Assert.Equal(TrackableVerdict.OwnProcess, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void AnElevatedWindowIsNotTrackedByANonElevatedHydraWin()
    {
        // UIPI would refuse the hide, so offering it as something to put in a task would only
        // promise a switch that can never happen.
        WindowFacts facts = Trackable() with { IsElevated = true };

        Assert.Equal(
            TrackableVerdict.Elevated,
            WindowFilter.Evaluate(in facts, OwnPid, ownIsElevated: false));
    }

    [Fact]
    public void AnElevatedWindowIsOrdinaryToAnElevatedHydraWin()
    {
        WindowFacts facts = Trackable() with { IsElevated = true };

        Assert.Equal(
            TrackableVerdict.Trackable,
            WindowFilter.Evaluate(in facts, OwnPid, ownIsElevated: true));
    }

    [Fact]
    public void ElevationIsCheckedBeforeTheCosmeticClauses()
    {
        // An elevated window that is also cloaked should report the reason that actually matters,
        // so the rejection counts say something useful.
        WindowFacts facts = Trackable() with { IsElevated = true, IsCloaked = true };

        Assert.Equal(TrackableVerdict.Elevated, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void HydraWinsOwnWindowLosesToNothingElse()
    {
        // Own-process wins even over elevation: if HydraWin were elevated, its own window must
        // still never appear in its own list.
        WindowFacts facts = Trackable() with { Pid = OwnPid, IsElevated = true };

        Assert.Equal(TrackableVerdict.OwnProcess, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AWindowWithoutATitleIsNotTracked(string? title)
    {
        WindowFacts facts = Trackable() with { Title = title! };

        Assert.Equal(TrackableVerdict.NoTitle, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void AnInvisibleWindowIsNotTracked()
    {
        WindowFacts facts = Trackable() with { IsVisible = false };

        Assert.Equal(TrackableVerdict.NotVisible, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void AnInvisibleWindowHydraWinHidItselfStaysTracked()
    {
        // The whole point: a hidden window is still part of a task.
        WindowFacts facts = Trackable() with { IsVisible = false, IsHydraWinHidden = true };

        Assert.Equal(TrackableVerdict.Trackable, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void AToolWindowIsNotTracked()
    {
        WindowFacts facts = Trackable() with { ExtendedStyle = ToolWindowStyle };

        Assert.Equal(TrackableVerdict.ToolWindow, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void OtherExtendedStyleBitsDoNotExcludeAWindow()
    {
        WindowFacts facts = Trackable() with { ExtendedStyle = 0x0004_0000L };

        Assert.Equal(TrackableVerdict.Trackable, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void AnOwnedWindowIsNotTracked()
    {
        WindowFacts facts = Trackable() with { Owner = 0x99 };

        Assert.Equal(TrackableVerdict.Owned, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void ACloakedWindowIsNotTracked()
    {
        WindowFacts facts = Trackable() with { IsCloaked = true };

        Assert.Equal(TrackableVerdict.Cloaked, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void AWindowHydraWinHidIsKeptEvenWhenTheSystemReportsItCloaked()
    {
        // Packaged apps such as Teams report cloaked *because* we hid them. Dropping those would
        // lose a window the user still owns and break the switch engine's restore path.
        WindowFacts facts = Trackable() with
        {
            IsVisible = false,
            IsHydraWinHidden = true,
            IsCloaked = true,
        };

        Assert.Equal(TrackableVerdict.Trackable, WindowFilter.Evaluate(in facts, OwnPid));
    }

    [Fact]
    public void OwnProcessIsCheckedBeforeEverythingElse()
    {
        // A titleless, invisible, cloaked window of ours reports OwnProcess, not the other clauses.
        WindowFacts facts = new(
            Hwnd: 0x1,
            Title: string.Empty,
            IsVisible: false,
            IsHydraWinHidden: false,
            ExtendedStyle: ToolWindowStyle,
            Owner: 0x5,
            IsCloaked: true,
            Pid: OwnPid);

        Assert.Equal(TrackableVerdict.OwnProcess, WindowFilter.Evaluate(in facts, OwnPid));
    }
}
