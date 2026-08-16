using HydraWin.Core.Notifications;

namespace HydraWin.Core.Tests;

/// <summary>
/// Title rules: edge triggering, process scoping, and refusing to blow up on a hand-edited pattern.
/// </summary>
public class NotificationRuleTests
{
    private static NotificationRule Rule(string process = "*", string pattern = @"^\(\d+\)") => new()
    {
        ProcessFileName = process,
        TitleRegex = pattern,
        Kind = NotificationKind.Title,
        Enabled = true,
    };

    [Fact]
    public void ARuleFiresWhenTheTitleStartsMatching()
    {
        Assert.True(Rule().Matches("chrome.exe", "Inbox", "(3) Inbox"));
    }

    [Fact]
    public void ARuleIsEdgeTriggeredNotLevelTriggered()
    {
        // The transition is the event. A window sitting at a matching title must badge once, not on
        // every repaint — otherwise a chat app that repaints its title storms the badge.
        Assert.False(Rule().Matches("chrome.exe", "(3) Inbox", "(4) Inbox"));
        Assert.False(Rule().Matches("chrome.exe", "(3) Inbox", "(3) Inbox"));
    }

    [Fact]
    public void FallingOutOfMatchAndBackAgainReBadges()
    {
        NotificationRule rule = Rule();

        Assert.True(rule.Matches("chrome.exe", "Inbox", "(1) Inbox"));
        Assert.False(rule.Matches("chrome.exe", "(1) Inbox", "Inbox"));
        Assert.True(rule.Matches("chrome.exe", "Inbox", "(1) Inbox"));
    }

    [Fact]
    public void ADisabledRuleNeverFires()
    {
        NotificationRule rule = Rule();
        rule.Enabled = false;

        Assert.False(rule.Matches("chrome.exe", "Inbox", "(3) Inbox"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    public void AWildcardProcessMatchesAnyApplication(string process)
    {
        // This is what lets one rule cover applications nobody thought about.
        Assert.True(Rule(process).Matches("some-unknown-app.exe", "x", "(2) x"));
    }

    [Fact]
    public void ANamedProcessMatchesOnlyThatApplication()
    {
        NotificationRule rule = Rule("chrome.exe");

        Assert.True(rule.Matches("CHROME.EXE", "x", "(2) x"));
        Assert.False(rule.Matches("msedge.exe", "x", "(2) x"));
    }

    [Fact]
    public void AnInvalidPatternNeverMatchesAndDoesNotThrow()
    {
        // state.json is hand-edited; a bad pattern must cost its own rule and nothing else.
        NotificationRule rule = Rule(pattern: "(unclosed");

        Assert.False(rule.Matches("chrome.exe", "x", "(2) x"));
    }

    [Fact]
    public void AnEmptyPatternNeverMatches()
    {
        Assert.False(Rule(pattern: string.Empty).Matches("chrome.exe", "x", "(2) x"));
    }

    [Fact]
    public void ACatastrophicPatternTimesOutToNoMatchInsteadOfHanging()
    {
        // Classic exponential backtracking. The 100 ms timeout is what stops one hand-edited rule
        // wedging the window-tracking path it runs on.
        NotificationRule rule = Rule(pattern: "^(a+)+$");
        string evil = new string('a', 40) + "!";

        DateTime started = DateTime.UtcNow;
        bool matched = rule.Matches("chrome.exe", string.Empty, evil);
        TimeSpan took = DateTime.UtcNow - started;

        Assert.False(matched);
        Assert.True(took < TimeSpan.FromSeconds(3), $"took {took.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public void TheSeededDefaultsShipDisabled()
    {
        // Badges come from the flash channel, which needs no rules. The seeded rule is an example
        // to copy, not behaviour.
        List<NotificationRule> defaults = NotificationRule.Defaults();

        Assert.NotEmpty(defaults);
        Assert.All(defaults, r => Assert.False(r.Enabled));
    }
}
