using HydraWin.Core.Notifications;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// What the rule editors show while the user types. The value of these is that the preview and the
/// real matching path are the same code — so what is tested here is that the wiring did not grow a
/// second opinion.
/// </summary>
public sealed class RulePreviewTests
{
    private static readonly TrackedWindow Editor = Make(0x10, @"C:\apps\Code.exe", "app.cs - proj");
    private static readonly TrackedWindow Chat = Make(0x20, @"C:\apps\chat.exe", "(3) Team");
    private static readonly TrackedWindow Browser = Make(0x30, @"C:\apps\chrome.exe", "Docs");

    private static readonly TrackedWindow[] Inventory = [Editor, Chat, Browser];

    private static TrackedWindow Make(nint hwnd, string path, string title) => new()
    {
        Hwnd = hwnd,
        Pid = (int)hwnd,
        ProcessPath = path,
        Title = title,
    };

    [Fact]
    public void AReattachPreviewListsExactlyWhatTheRuleWouldClaim()
    {
        var rule = new ReattachRule { ProcessFileName = "Code.exe", TitlePattern = "proj" };

        IReadOnlyList<TrackedWindow> matched = RulePreview.Match(rule, Inventory);

        TrackedWindow only = Assert.Single(matched);
        Assert.Equal(Editor.Hwnd, only.Hwnd);
    }

    [Fact]
    public void AReattachPreviewCanLeaveOutTheWindowTheRuleAlreadyOwns()
    {
        // Showing the window you are editing the rule for tells the user nothing.
        var rule = new ReattachRule { ProcessFileName = "Code.exe" };

        Assert.Empty(RulePreview.Match(rule, Inventory, ignoreHwnd: Editor.Hwnd));
    }

    [Fact]
    public void AReattachPreviewAgreesWithTheRuleOnAMalformedPattern()
    {
        // "(" is not a regex. The rule swallows it to "no match"; the preview must say the same
        // rather than throwing in the user's face as they type.
        var rule = new ReattachRule
        {
            ProcessFileName = "chat.exe",
            TitlePattern = "(",
            TitleIsRegex = true,
        };

        Assert.Empty(RulePreview.Match(rule, Inventory));
        Assert.False(rule.Matches("chat.exe", "(3) Team"));
    }

    [Fact]
    public void ANotificationPreviewIgnoresTheEnabledFlagAndTheEdge()
    {
        // The question being asked is "does this pattern pick out what I mean", not "would it have
        // fired just now" — so a disabled rule still previews.
        var rule = new NotificationRule
        {
            ProcessFileName = "*",
            TitleRegex = @"^\(\d+\)",
            Enabled = false,
        };

        TrackedWindow only = Assert.Single(RulePreview.Match(rule, Inventory));

        Assert.Equal(Chat.Hwnd, only.Hwnd);
        Assert.False(rule.Matches("chat.exe", "Team", "(3) Team"));
    }

    [Fact]
    public void ANotificationPreviewNarrowsByProcess()
    {
        var rule = new NotificationRule { ProcessFileName = "chrome.exe", TitleRegex = "." };

        TrackedWindow only = Assert.Single(RulePreview.Match(rule, Inventory));

        Assert.Equal(Browser.Hwnd, only.Hwnd);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"^\(\d+\)")]
    [InlineData(".*")]
    public void AValidPatternReportsNoError(string pattern)
    {
        Assert.True(RulePreview.IsValidRegex(pattern, out string error));
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    [InlineData("*")]
    public void ABrokenPatternReportsWhyRatherThanThrowing(string pattern)
    {
        Assert.False(RulePreview.IsValidRegex(pattern, out string error));
        Assert.NotEmpty(error);
    }
}
