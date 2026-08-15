using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Rule generation and matching. The titles here are the real ones task 01 measured, not
/// invented examples.
/// </summary>
public class ReattachRuleTests
{
    [Fact]
    public void TheRuleKeepsOnlyTheProcessFileNameNotTheFullPath()
    {
        // The full path breaks whenever the app updates into a versioned directory.
        ReattachRule rule = ReattachRule.FromWindow(
            @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe",
            "hydrawin - Visual Studio Code");

        Assert.Equal("Code.exe", rule.ProcessFileName);
    }

    [Theory]
    [InlineData("● file.cs - project - Visual Studio Code", "file.cs - project - Visual Studio Code")]
    [InlineData("*new 9 - Notepad++", "new 9 - Notepad++")]
    [InlineData("✳ Initialize git repository", "Initialize git repository")]
    [InlineData("◐ window-tracker-core-service", "window-tracker-core-service")]
    [InlineData("◑ window-tracker-core-service", "window-tracker-core-service")]
    [InlineData("◒ window-tracker-core-service", "window-tracker-core-service")]
    [InlineData("◓ window-tracker-core-service", "window-tracker-core-service")]
    public void LeadingVolatileMarkersAreStripped(string title, string expected)
    {
        // Task 01 measured Claude Code advancing the spinner about once a second; a rule that
        // captured the frame it happened to see would never match again.
        Assert.Equal(expected, ReattachRule.FromWindow("Code.exe", title).TitlePattern);
    }

    [Fact]
    public void EveryClaudeCodeSpinnerFrameCollapsesToTheSamePattern()
    {
        string[] frames =
        [
            "◐ my-session", "◑ my-session", "◒ my-session", "◓ my-session", "✳ my-session",
        ];

        string[] patterns =
            [.. frames.Select(f => ReattachRule.FromWindow("WindowsTerminal.exe", f).TitlePattern)];

        Assert.Single(patterns.Distinct(StringComparer.Ordinal));
        Assert.Equal("my-session", patterns[0]);
    }

    [Fact]
    public void TheTrailingApplicationSuffixIsKept()
    {
        // It is the stable part, and dropping it would make the rule match far too much.
        ReattachRule rule = ReattachRule.FromWindow("chrome.exe", "(2) Chat | Teams");

        Assert.Equal("(2) Chat | Teams", rule.TitlePattern);
    }

    [Fact]
    public void OnlyALeadingMarkerIsStrippedNotOneInTheMiddle()
    {
        Assert.Equal(
            "report * draft",
            ReattachRule.FromWindow("winword.exe", "report * draft").TitlePattern);
    }

    [Fact]
    public void AnEmptyProcessPathYieldsAnEmptyProcessName()
    {
        // Protected processes report no path (task 01/03); there is nothing durable to match on.
        Assert.Equal(string.Empty, ReattachRule.FromWindow(string.Empty, "Task Manager").ProcessFileName);
    }

    [Fact]
    public void SubstringMatchingIsCaseInsensitiveOnBothProcessAndTitle()
    {
        var rule = new ReattachRule { ProcessFileName = "Code.exe", TitlePattern = "HydraWin" };

        Assert.True(rule.Matches("code.exe", "hydrawin - Visual Studio Code"));
    }

    [Fact]
    public void ADifferentProcessNeverMatches()
    {
        var rule = new ReattachRule { ProcessFileName = "Code.exe", TitlePattern = "hydrawin" };

        Assert.False(rule.Matches("chrome.exe", "hydrawin - Visual Studio Code"));
    }

    [Fact]
    public void AnEmptyPatternMatchesAnyTitleOfThatProcess()
    {
        var rule = new ReattachRule { ProcessFileName = "Code.exe", TitlePattern = string.Empty };

        Assert.True(rule.Matches("Code.exe", "anything at all"));
    }

    [Fact]
    public void RegexModeIsOptIn()
    {
        var rule = new ReattachRule { ProcessFileName = "chrome.exe", TitlePattern = "^\\(\\d+\\)" };

        // Substring mode: the pattern is looked for literally, so it does not match.
        Assert.False(rule.Matches("chrome.exe", "(2) Inbox"));

        rule.TitleIsRegex = true;
        Assert.True(rule.Matches("chrome.exe", "(2) Inbox"));
    }

    [Fact]
    public void AnInvalidUserRegexNeverMatchesAndNeverThrows()
    {
        // Hand-edited state.json is a supported workflow, so a broken pattern must not take down
        // the window-tracking path that calls this.
        var rule = new ReattachRule
        {
            ProcessFileName = "chrome.exe",
            TitlePattern = "([unclosed",
            TitleIsRegex = true,
        };

        Assert.False(rule.Matches("chrome.exe", "anything"));
    }

    [Fact]
    public void AChangedPatternIsRecompiledRatherThanCached()
    {
        var rule = new ReattachRule
        {
            ProcessFileName = "chrome.exe",
            TitlePattern = "alpha",
            TitleIsRegex = true,
        };
        Assert.True(rule.Matches("chrome.exe", "alpha"));

        rule.TitlePattern = "beta";

        Assert.False(rule.Matches("chrome.exe", "alpha"));
        Assert.True(rule.Matches("chrome.exe", "beta"));
    }

    [Fact]
    public void ACatastrophicallySlowRegexTimesOutAsNoMatch()
    {
        var rule = new ReattachRule
        {
            ProcessFileName = "chrome.exe",
            TitlePattern = "^(a+)+$",
            TitleIsRegex = true,
        };

        Assert.False(rule.Matches("chrome.exe", new string('a', 40) + "!"));
    }
}
