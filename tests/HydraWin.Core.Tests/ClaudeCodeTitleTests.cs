using HydraWin.Core.Tracking;

namespace HydraWin.Core.Tests;

/// <summary>
/// Reading the activity marker off a Claude Code terminal title, per task 01's measurement.
/// </summary>
public class ClaudeCodeTitleTests
{
    [Theory]
    [InlineData('◐')]
    [InlineData('◑')]
    [InlineData('◒')]
    [InlineData('◓')]
    public void EverySpinnerFrameMeansWorking(char frame)
    {
        // The spinner advances about once a second, so any of the four may be showing when the
        // title event arrives; treating only one of them as "working" would flicker the indicator.
        (TitleActivity activity, string text) = ClaudeCodeTitle.Parse($"{frame} git_submit");

        Assert.Equal(TitleActivity.Working, activity);
        Assert.Equal("git_submit", text);
    }

    [Fact]
    public void TheAsteriskMarkerMeansIdle()
    {
        (TitleActivity activity, string text) = ClaudeCodeTitle.Parse("✳ prod");

        Assert.Equal(TitleActivity.Idle, activity);
        Assert.Equal("prod", text);
    }

    [Fact]
    public void AnUnmarkedTitleIsReturnedUntouched()
    {
        (TitleActivity activity, string text) =
            ClaudeCodeTitle.Parse("foo.cs - hydrawin - Visual Studio Code");

        Assert.Equal(TitleActivity.None, activity);
        Assert.Equal("foo.cs - hydrawin - Visual Studio Code", text);
    }

    [Fact]
    public void AMarkerOnlyCountsAtTheStart()
    {
        // A browser tab showing this very documentation is not a working Claude session.
        (TitleActivity activity, string text) =
            ClaudeCodeTitle.Parse("Unicode ◐ U+25D0 — Wikipedia");

        Assert.Equal(TitleActivity.None, activity);
        Assert.Equal("Unicode ◐ U+25D0 — Wikipedia", text);
    }

    [Fact]
    public void LeadingWhitespaceDoesNotHideTheMarker()
    {
        (TitleActivity activity, string text) = ClaudeCodeTitle.Parse("  ◒   long build ");

        Assert.Equal(TitleActivity.Working, activity);
        Assert.Equal("long build ", text);
    }

    [Fact]
    public void AMarkerWithNothingAfterItStillReadsAsActivity()
    {
        (TitleActivity activity, string text) = ClaudeCodeTitle.Parse("✳");

        Assert.Equal(TitleActivity.Idle, activity);
        Assert.Equal(string.Empty, text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTitlesAreNotAnActivity(string? title)
    {
        (TitleActivity activity, string text) = ClaudeCodeTitle.Parse(title);

        Assert.Equal(TitleActivity.None, activity);
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void TheStrippedTextIsWhatAReattachRuleWouldStore()
    {
        // The two must agree: the row shows "prod" and the rule matches on "prod". If they drifted,
        // a busy terminal would display one thing and re-attach by another.
        const string Title = "◓ prod";

        Assert.Equal(
            HydraWin.Core.Workspaces.ReattachRule.StripVolatileDecoration(Title),
            ClaudeCodeTitle.Parse(Title).Text);
    }
}
