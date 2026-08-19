using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Per-task active time: what accrues, what does not, and what happens at the awkward edges — the
/// user walking away, a task deleted mid-segment, and a clock that misbehaves. No Win32 and no UI,
/// which is the whole point of the ledger being a separate class: the away signals arrive as
/// method calls, so every rule below can be proved without locking a real screen.
/// </summary>
public sealed class ActiveTimeLedgerTests
{
    private readonly Dictionary<Guid, HydraWinTask> tasks = [];
    private readonly FakeClock clock = new();
    private readonly ActiveTimeLedger ledger;
    private readonly HydraWinTask alpha;
    private readonly HydraWinTask beta;

    public ActiveTimeLedgerTests()
    {
        alpha = Add("Alpha");
        beta = Add("Beta");
        ledger = new ActiveTimeLedger(id => tasks.GetValueOrDefault(id), clock);
    }

    [Fact]
    public void ATaskThatWasNeverActiveHasNoTimeAtAll()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(beta.Id));
    }

    [Fact]
    public void TimeAccumulatesOnlyAgainstTheTaskThatIsActive()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(5), ledger.TotalFor(alpha.Id));
        Assert.Equal(300, alpha.ActiveSeconds);
        Assert.Equal(0, beta.ActiveSeconds);
    }

    [Fact]
    public void SwitchingBetweenTasksSplitsTheTimeAtTheMomentOfTheSwitch()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(2));
        ledger.SetActive(beta.Id);
        Run(TimeSpan.FromMinutes(3));

        Assert.Equal(TimeSpan.FromMinutes(2), ledger.TotalFor(alpha.Id));
        Assert.Equal(TimeSpan.FromMinutes(3), ledger.TotalFor(beta.Id));
    }

    [Fact]
    public void ReselectingTheAlreadyActiveTaskNeitherRestartsNorDoubleCountsItsSegment()
    {
        // SwitchTo is idempotent and re-runs its whole body, so this happens on every re-click.
        ledger.SetActive(alpha.Id);
        clock.Advance(TimeSpan.FromSeconds(30));
        ledger.SetActive(alpha.Id);
        clock.Advance(TimeSpan.FromSeconds(30));
        ledger.Sample();

        Assert.Equal(TimeSpan.FromMinutes(1), ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void SubSecondRemaindersCarryForwardRatherThanBeingTruncatedAway()
    {
        ledger.SetActive(alpha.Id);

        for (int i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(1500));
            ledger.Sample();
        }

        Assert.Equal(TimeSpan.FromSeconds(6), ledger.TotalFor(alpha.Id));
        Assert.Equal(6, alpha.ActiveSeconds);
    }

    [Fact]
    public void ShowingAllTasksStopsTheClockBecauseNoTaskIsActive()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(1));
        ledger.SetActive(null);
        Run(TimeSpan.FromMinutes(9));

        Assert.Null(ledger.RunningTaskId);
        Assert.Equal(TimeSpan.FromMinutes(1), ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void LockingTheScreenStopsTheClockAndUnlockingStartsItAgain()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(1));

        Assert.True(ledger.GoAway(AwayReason.Locked));
        Run(TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(1), ledger.TotalFor(alpha.Id));

        Assert.True(ledger.ComeBack(AwayReason.Locked));
        Run(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(3), ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void TimeSpentAwayIsCreditedToNoTaskAtAll()
    {
        ledger.SetActive(alpha.Id);
        ledger.GoAway(AwayReason.Suspended);
        Run(TimeSpan.FromHours(8));

        Assert.Equal(0, alpha.ActiveSeconds);
        Assert.Equal(0, beta.ActiveSeconds);
    }

    [Fact]
    public void RepeatedLocksDoNotDoublePauseAndOneUnlockStillResumes()
    {
        ledger.SetActive(alpha.Id);

        Assert.True(ledger.GoAway(AwayReason.Locked));
        Assert.False(ledger.GoAway(AwayReason.Locked));
        Assert.True(ledger.ComeBack(AwayReason.Locked));

        Run(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(1), ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void ASuspendOverAnAlreadyLockedSessionKeepsTheClockStoppedUntilBothEnd()
    {
        // A machine that sleeps while locked wakes on a timer with nobody there: the resume
        // arrives, but the screen is still locked. A nesting count would restart the clock here.
        ledger.SetActive(alpha.Id);
        ledger.GoAway(AwayReason.Locked);
        ledger.GoAway(AwayReason.Suspended);

        Assert.False(ledger.ComeBack(AwayReason.Suspended));
        Assert.True(ledger.IsAway);

        Run(TimeSpan.FromMinutes(20));
        Assert.Equal(0, alpha.ActiveSeconds);

        Assert.True(ledger.ComeBack(AwayReason.Locked));
        Assert.False(ledger.IsAway);
    }

    [Fact]
    public void AResumeWithNoMatchingSuspendLeavesTheClockExactlyAsItWas()
    {
        ledger.SetActive(alpha.Id);

        Assert.False(ledger.ComeBack(AwayReason.Suspended));
        Assert.False(ledger.IsAway);

        Run(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(1), ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void ASwitchWhileAwayChangesTheTaskWithoutStartingTheClock()
    {
        ledger.SetActive(alpha.Id);
        ledger.GoAway(AwayReason.Locked);
        ledger.SetActive(beta.Id);
        Run(TimeSpan.FromMinutes(10));

        Assert.Null(ledger.RunningTaskId);
        Assert.Equal(0, alpha.ActiveSeconds);
        Assert.Equal(0, beta.ActiveSeconds);
    }

    [Fact]
    public void ATaskThatDisappearsMidSegmentDropsItsTimeRatherThanCreditingItElsewhere()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(4));
        tasks.Remove(alpha.Id);
        clock.Advance(TimeSpan.FromSeconds(30));
        ledger.Sample();

        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(alpha.Id));
        Assert.Equal(0, beta.ActiveSeconds);
    }

    [Fact]
    public void AClockThatJumpsBackwardsCreditsNothingRatherThanNegativeTime()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(10));

        clock.Rewind(TimeSpan.FromMinutes(5));
        ledger.Sample();

        Assert.Equal(TimeSpan.FromMinutes(10), ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void AClockThatJumpsBackwardsResumesCountingNormallyAfterwards()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(10));
        clock.Rewind(TimeSpan.FromMinutes(5));
        ledger.Sample();

        Run(TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromMinutes(12), ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void ALongGapBetweenSamplesIsCappedSoASleptMachineCannotInventHours()
    {
        // The fallback for a suspend that was never broadcast: whatever the machine did while it
        // was gone, one sample can only ever credit MaxCreditPerSample.
        ledger.SetActive(alpha.Id);
        clock.Advance(TimeSpan.FromHours(9));
        ledger.Sample();

        Assert.Equal(ActiveTimeLedger.MaxCreditPerSample, ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void ResettingATaskZeroesItsTotalAndKeepsTheClockRunningOnIt()
    {
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromHours(3));

        ledger.Reset(alpha.Id);
        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(alpha.Id));
        Assert.Equal(0, alpha.ActiveSeconds);

        Run(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(1), ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void ResettingOneTaskLeavesEveryOtherTasksTotalUntouched()
    {
        ledger.SetActive(beta.Id);
        Run(TimeSpan.FromMinutes(7));
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromMinutes(7));

        ledger.Reset(alpha.Id);

        Assert.Equal(TimeSpan.FromMinutes(7), ledger.TotalFor(beta.Id));
    }

    [Fact]
    public void ResettingEveryTaskZeroesEveryTotalAndKeepsTheClockRunningOnTheActiveOne()
    {
        ledger.SetActive(beta.Id);
        Run(TimeSpan.FromMinutes(20));
        ledger.SetActive(alpha.Id);
        Run(TimeSpan.FromHours(3));

        ledger.ResetAll([alpha.Id, beta.Id]);

        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(alpha.Id));
        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(beta.Id));
        Assert.Equal(0, alpha.ActiveSeconds);
        Assert.Equal(0, beta.ActiveSeconds);

        Run(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(1), ledger.TotalFor(alpha.Id));
        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(beta.Id));
    }

    [Fact]
    public void ResettingEveryTaskDropsTheSubSecondRemaindersRatherThanLettingThemResurface()
    {
        // Sub-second slices live in the ledger's remainder, not in ActiveSeconds. Clearing the
        // model alone would leave them behind, and the next sample would round them up into a
        // total the user was told had gone.
        ledger.SetActive(alpha.Id);
        for (int i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(400));
            ledger.Sample();
        }

        ledger.ResetAll([alpha.Id, beta.Id]);
        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(alpha.Id));

        clock.Advance(TimeSpan.FromMilliseconds(400));
        ledger.Sample();

        Assert.Equal(0, alpha.ActiveSeconds);
    }

    [Fact]
    public void ResettingEveryTaskIsHarmlessWhenThereAreNoTasksToReset()
    {
        ledger.ResetAll([]);

        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void AHandEditedNegativeTotalReadsAsZeroRatherThanAsNegativeTime()
    {
        alpha.ActiveSeconds = -600;

        Assert.Equal(TimeSpan.Zero, ledger.TotalFor(alpha.Id));
    }

    [Fact]
    public void ATaskThatWasActiveWhenTheLedgerWasBuiltCountsFromThatMoment()
    {
        // The launch path: state.json remembers the active task, so the clock picks up without
        // waiting for the user to switch to anything.
        var fresh = new ActiveTimeLedger(id => tasks.GetValueOrDefault(id), clock);
        fresh.SetActive(alpha.Id);

        clock.Advance(TimeSpan.FromSeconds(60));
        fresh.Sample();

        Assert.Equal(TimeSpan.FromMinutes(1), fresh.TotalFor(alpha.Id));
    }

    [Fact]
    public void TheDisplayedTotalIncludesTheSegmentInFlightSoTheRowDoesNotLookFrozen()
    {
        ledger.SetActive(alpha.Id);
        clock.Advance(TimeSpan.FromSeconds(45));

        // No Sample() call: this is what a read between two ticks sees.
        Assert.Equal(TimeSpan.FromSeconds(45), ledger.TotalFor(alpha.Id));
    }

    /// <summary>
    /// Runs the clock forward the way the App layer one-minute tick does, in slices no longer
    /// than one sample apart. Advancing hours in a single jump instead would hit
    /// <see cref="ActiveTimeLedger.MaxCreditPerSample"/> and measure the safety net rather
    /// than the arithmetic.
    /// </summary>
    private void Run(TimeSpan span)
    {
        TimeSpan tick = TimeSpan.FromMinutes(1);

        for (TimeSpan spent = TimeSpan.Zero; spent < span; spent += tick)
        {
            TimeSpan remaining = span - spent;
            clock.Advance(remaining < tick ? remaining : tick);
            ledger.Sample();
        }
    }

    private HydraWinTask Add(string name)
    {
        var task = new HydraWinTask { Name = name };
        tasks[task.Id] = task;
        return task;
    }
}
