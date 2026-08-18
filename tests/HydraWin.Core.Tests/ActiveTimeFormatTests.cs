using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// How an accumulated total reads on a task row. In Core rather than in a WPF converter precisely
/// so that these boundaries — zero, the roll from minutes into hours, and the fact that a day does
/// not become a day — have coverage at all; the App project has none.
/// </summary>
public sealed class ActiveTimeFormatTests
{
    [Fact]
    public void ATaskThatWasNeverActiveStillShowsAClockRatherThanNothing() =>
        Assert.Equal("00:00:00", ActiveTimeFormat.Clock(TimeSpan.Zero));

    [Fact]
    public void ANegativeTotalReadsAsZeroRatherThanAsANegativeClock() =>
        Assert.Equal("00:00:00", ActiveTimeFormat.Clock(TimeSpan.FromMinutes(-5)));

    [Fact]
    public void TheFirstSecondIsVisibleRatherThanRoundedAway() =>
        Assert.Equal("00:00:01", ActiveTimeFormat.Clock(TimeSpan.FromSeconds(1)));

    [Theory]
    [InlineData(9, "00:00:09")]
    [InlineData(59, "00:00:59")]
    [InlineData(60, "00:01:00")]
    [InlineData(847, "00:14:07")]
    [InlineData(3599, "00:59:59")]
    public void EverySectionIsZeroPaddedSoTheColumnNeverJitters(int seconds, string expected) =>
        Assert.Equal(expected, ActiveTimeFormat.Clock(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void AnHourRollsIntoTheHoursFieldAndNotAnywhereElse() =>
        Assert.Equal("01:00:00", ActiveTimeFormat.Clock(TimeSpan.FromHours(1)));

    [Fact]
    public void HoursAndMinutesAndSecondsAreShownTogether() =>
        Assert.Equal("02:14:07", ActiveTimeFormat.Clock(new TimeSpan(2, 14, 7)));

    [Fact]
    public void ADayAccumulatesIntoTheHoursFieldRatherThanBecomingADay() =>
        Assert.Equal("25:00:00", ActiveTimeFormat.Clock(TimeSpan.FromHours(25)));

    [Fact]
    public void PastAHundredHoursTheFieldSimplyGrows() =>
        Assert.Equal("137:05:00", ActiveTimeFormat.Clock(new TimeSpan(137, 5, 0)));

    [Fact]
    public void SubSecondTimeIsTruncatedRatherThanRounded() =>
        Assert.Equal("00:00:00", ActiveTimeFormat.Clock(TimeSpan.FromMilliseconds(999)));

    [Fact]
    public void TheTooltipSaysSoWhenATaskHasNeverBeenSwitchedTo() =>
        Assert.Equal("Never switched to", ActiveTimeFormat.Tooltip(TimeSpan.Zero, counting: false));

    [Fact]
    public void TheTooltipSpellsOutMinutesAndSecondsUnderAnHour() =>
        Assert.Equal(
            "Active for 14m 07s in total",
            ActiveTimeFormat.Tooltip(new TimeSpan(0, 14, 7), counting: false));

    [Fact]
    public void TheTooltipSpellsOutHoursMinutesAndSeconds() =>
        Assert.Equal(
            "Active for 2h 14m 07s in total",
            ActiveTimeFormat.Tooltip(new TimeSpan(2, 14, 7), counting: false));

    [Fact]
    public void TheTooltipSaysWhenTheClockIsActuallyRunning() =>
        Assert.Equal(
            "Active for 2h 14m 07s in total — counting now",
            ActiveTimeFormat.Tooltip(new TimeSpan(2, 14, 7), counting: true));

    [Fact]
    public void ATaskJustSwitchedToReadsAsCountingRatherThanAsNeverUsed() =>
        Assert.Equal(
            "Active for 0m 00s in total — counting now",
            ActiveTimeFormat.Tooltip(TimeSpan.Zero, counting: true));
}
