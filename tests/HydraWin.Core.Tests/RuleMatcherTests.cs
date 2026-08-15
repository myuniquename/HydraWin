using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>Which task a reappearing window re-attaches to.</summary>
public class RuleMatcherTests
{
    private static TrackedWindow Window(string process, string title, nint hwnd = 0x100) => new()
    {
        Hwnd = hwnd,
        Pid = 42,
        ProcessPath = @"C:\apps\" + process,
        Title = title,
    };

    private static HydraWinTask Task(string name, int order, params ReattachRule[] rules)
    {
        var task = new HydraWinTask { Name = name, Order = order };
        foreach (ReattachRule rule in rules)
        {
            task.Assignments.Add(new WindowAssignment { Rule = rule });
        }

        return task;
    }

    private static ReattachRule Rule(string process, string pattern) =>
        new() { ProcessFileName = process, TitlePattern = pattern };

    [Fact]
    public void AWindowMatchingNoRuleStaysUnassigned()
    {
        var state = new WorkspaceState { Tasks = [Task("Alpha", 1, Rule("Code.exe", "hydrawin"))] };

        Assert.Null(RuleMatcher.FindTask(state, Window("chrome.exe", "News")));
    }

    [Fact]
    public void TheMatchingTaskAndAssignmentAreReturned()
    {
        HydraWinTask alpha = Task("Alpha", 1, Rule("Code.exe", "hydrawin"));
        var state = new WorkspaceState { Tasks = [alpha] };

        RuleMatch? match = RuleMatcher.FindTask(
            state, Window("Code.exe", "hydrawin - Visual Studio Code"));

        Assert.NotNull(match);
        Assert.Same(alpha, match.Task);
        Assert.Same(alpha.Assignments[0], match.Assignment);
    }

    [Fact]
    public void TheLowestOrderTaskWinsRegardlessOfListPosition()
    {
        HydraWinTask beta = Task("Beta", 2, Rule("Code.exe", "hydrawin"));
        HydraWinTask alpha = Task("Alpha", 1, Rule("Code.exe", "hydrawin"));

        // Deliberately out of order in the list: Order decides, not storage order.
        var state = new WorkspaceState { Tasks = [beta, alpha] };

        Assert.Same(alpha, RuleMatcher.FindTask(state, Window("Code.exe", "hydrawin"))!.Task);
    }

    [Fact]
    public void ARuleThatAlreadyHoldsAWindowIsSkipped()
    {
        // A rule binds at most one window; the second matching window stays unassigned so the
        // user can place it deliberately instead of it displacing the first.
        HydraWinTask alpha = Task("Alpha", 1, Rule("chrome.exe", "Docs"));
        alpha.Assignments[0].BoundHwnd = 0x111;
        var state = new WorkspaceState { Tasks = [alpha] };

        Assert.Null(RuleMatcher.FindTask(state, Window("chrome.exe", "Docs", 0x222)));
    }

    [Fact]
    public void AnotherFreeRuleInTheSameTaskStillMatches()
    {
        HydraWinTask alpha = Task("Alpha", 1, Rule("chrome.exe", "Docs"), Rule("chrome.exe", "Docs"));
        alpha.Assignments[0].BoundHwnd = 0x111;
        var state = new WorkspaceState { Tasks = [alpha] };

        RuleMatch? match = RuleMatcher.FindTask(state, Window("chrome.exe", "Docs", 0x222));

        Assert.NotNull(match);
        Assert.Same(alpha.Assignments[1], match.Assignment);
    }

    [Fact]
    public void AWindowWithNoProcessPathNeverMatches()
    {
        // Protected processes report an empty path; there is nothing durable to key on, and an
        // empty-vs-empty comparison would otherwise match far too much.
        var state = new WorkspaceState { Tasks = [Task("Alpha", 1, Rule(string.Empty, "Task"))] };
        var window = new TrackedWindow
        {
            Hwnd = 0x1,
            Pid = 1,
            ProcessPath = string.Empty,
            Title = "Task Manager",
        };

        Assert.Null(RuleMatcher.FindTask(state, window));
    }

    [Fact]
    public void AnInvalidRegexInOneRuleDoesNotStopLaterRulesMatching()
    {
        HydraWinTask alpha = Task("Alpha", 1);
        alpha.Assignments.Add(new WindowAssignment
        {
            Rule = new ReattachRule
            {
                ProcessFileName = "Code.exe",
                TitlePattern = "([unclosed",
                TitleIsRegex = true,
            },
        });
        alpha.Assignments.Add(new WindowAssignment { Rule = Rule("Code.exe", "hydrawin") });
        var state = new WorkspaceState { Tasks = [alpha] };

        RuleMatch? match = RuleMatcher.FindTask(state, Window("Code.exe", "hydrawin"));

        Assert.NotNull(match);
        Assert.Same(alpha.Assignments[1], match.Assignment);
    }
}
