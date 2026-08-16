using HydraWin.Core.Notifications;
using HydraWin.Core.Persistence;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Which windows are waiting to be looked at. No Win32 and no UI: the hub takes the foreground
/// handle as an input, which is what makes suppression and the clearing matrix testable at all.
/// </summary>
public sealed class NotificationHubTests : IDisposable
{
    private const string AppPath = @"C:\apps\app.exe";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private readonly WorkspaceStore store;
    private readonly WorkspaceService workspaces;
    private readonly List<NotificationRule> rules = [];
    private readonly NotificationHub hub;

    public NotificationHubTests()
    {
        Directory.CreateDirectory(directory);
        store = new WorkspaceStore(Path.Combine(directory, "state.json"), TimeSpan.FromMinutes(5));
        workspaces = new WorkspaceService(store);
        hub = new NotificationHub(workspaces, () => rules);
    }

    public void Dispose()
    {
        store.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static TrackedWindow Window(nint hwnd, string title = "a window") => new()
    {
        Hwnd = hwnd,
        Pid = (int)hwnd,
        ProcessPath = AppPath,
        Title = title,
    };

    /// <summary>A task holding one window, bound.</summary>
    private (HydraWinTask Task, TrackedWindow Window) TaskWith(string name, nint hwnd)
    {
        HydraWinTask task = workspaces.CreateTask(name);
        TrackedWindow window = Window(hwnd);
        workspaces.AssignWindow(task.Id, window);
        return (task, window);
    }

    [Fact]
    public void AFlashBadgesTheWindowsTask()
    {
        (HydraWinTask task, TrackedWindow window) = TaskWith("Alpha", 0x10);

        hub.OnFlash(window);

        Assert.Equal(1, hub.CountFor(task.Id));
        Assert.True(hub.IsPending(0x10));
    }

    [Fact]
    public void AFlashNeedsNoRuleAndNoKnowledgeOfTheApplication()
    {
        // The point of the flash channel: an application nobody configured still badges, and the
        // label is built from the window itself.
        (HydraWinTask task, _) = TaskWith("Alpha", 0x10);
        var unknown = new TrackedWindow
        {
            Hwnd = 0x10,
            Pid = 42,
            ProcessPath = @"C:\vendor\never-heard-of-it.exe",
            Title = "Something happened",
        };

        Assert.Empty(rules);
        hub.OnFlash(unknown);

        PendingNotification pending = Assert.Single(hub.PendingFor(task.Id));
        Assert.Equal(NotificationKind.Attention, pending.Kind);
        Assert.Contains("never-heard-of-it.exe", pending.Label, StringComparison.Ordinal);
        Assert.Contains("Something happened", pending.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlashFromAnUnassignedWindowIsIgnored()
    {
        // Nothing to badge: it belongs to no task and is visible in all of them.
        hub.OnFlash(Window(0x99));

        Assert.False(hub.IsPending(0x99));
        Assert.Equal(0, hub.TotalPending);
    }

    [Fact]
    public void AFlashFromTheForegroundWindowIsIgnored()
    {
        (HydraWinTask task, TrackedWindow window) = TaskWith("Alpha", 0x10);
        hub.OnForegroundChanged(0x10);

        hub.OnFlash(window);

        Assert.Equal(0, hub.CountFor(task.Id));
    }

    [Fact]
    public void AFlashFromABackgroundWindowOfTheActiveTaskStillBadges()
    {
        // The user cannot see it either way, so it is worth telling them about.
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        TrackedWindow first = Window(0x10);
        TrackedWindow second = Window(0x11);
        workspaces.AssignWindow(alpha.Id, first);
        workspaces.AssignWindow(alpha.Id, second);

        workspaces.SetActiveTask(alpha.Id);
        hub.OnForegroundChanged(0x10);

        hub.OnFlash(second);

        Assert.Equal(1, hub.CountFor(alpha.Id));
    }

    [Fact]
    public void AWindowStopsBeingForegroundOnceSomethingElseTakesFocus()
    {
        // Regression. The tracker's WinEvent hook skips our own process, so HydraWin taking focus
        // is invisible to it; the App reports that separately as handle 0. Without it the hub goes
        // on believing the last window the user visited is still in front and suppresses it for
        // the rest of the session — found by flashing a window that had been focused earlier and
        // getting no badge.
        (HydraWinTask task, TrackedWindow window) = TaskWith("Alpha", 0x10);

        hub.OnForegroundChanged(0x10);
        hub.OnFlash(window);
        Assert.Equal(0, hub.CountFor(task.Id));

        // The manager window comes forward: nothing foreign is in front now.
        hub.OnForegroundChanged(0);
        hub.OnFlash(window);

        Assert.Equal(1, hub.CountFor(task.Id));
    }

    [Fact]
    public void RepeatedFlashesCoalesceIntoOnePendingWindow()
    {
        // FLASHW_TIMERNOFG flashes until the window is foregrounded. The count tracks windows
        // needing attention, not signals received.
        (HydraWinTask task, TrackedWindow window) = TaskWith("Alpha", 0x10);

        for (int i = 0; i < 25; i++)
        {
            hub.OnFlash(window);
        }

        Assert.Equal(1, hub.CountFor(task.Id));
    }

    [Fact]
    public void FocusingTheWindowClearsItsBadge()
    {
        (HydraWinTask task, TrackedWindow window) = TaskWith("Alpha", 0x10);
        hub.OnFlash(window);

        hub.OnForegroundChanged(0x10);

        Assert.Equal(0, hub.CountFor(task.Id));
    }

    [Fact]
    public void SwitchingToTheTaskDoesNotClearAnything()
    {
        // The decisive rule. Teams flashes once per unread run, so clearing on a switch would drop
        // the badge with no second flash ever coming to raise it again — and the same holds for any
        // application that signals once per event.
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        foreach (nint hwnd in new nint[] { 0x10, 0x11, 0x12 })
        {
            workspaces.AssignWindow(alpha.Id, Window(hwnd));
            hub.OnFlash(Window(hwnd));
        }

        Assert.Equal(3, hub.CountFor(alpha.Id));

        workspaces.SetActiveTask(alpha.Id);

        Assert.Equal(3, hub.CountFor(alpha.Id));

        // Only looking at them clears them, one at a time.
        hub.OnForegroundChanged(0x11);
        Assert.Equal(2, hub.CountFor(alpha.Id));
    }

    [Fact]
    public void AClosedWindowStopsWaitingForAttention()
    {
        (HydraWinTask task, TrackedWindow window) = TaskWith("Alpha", 0x10);
        hub.OnFlash(window);

        hub.OnWindowDisappeared(0x10);

        Assert.Equal(0, hub.CountFor(task.Id));
        Assert.Equal(0, hub.TotalPending);
    }

    [Fact]
    public void BadgesAreCountedPerTaskNotGlobally()
    {
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        HydraWinTask beta = workspaces.CreateTask("Beta");
        workspaces.AssignWindow(alpha.Id, Window(0x10));
        workspaces.AssignWindow(beta.Id, Window(0x20));

        hub.OnFlash(Window(0x10));
        hub.OnFlash(Window(0x20));

        Assert.Equal(1, hub.CountFor(alpha.Id));
        Assert.Equal(1, hub.CountFor(beta.Id));
        Assert.Equal(2, hub.TotalPending);
    }

    [Fact]
    public void TheBadgeChangedEventCarriesTheCountAndNewestLabel()
    {
        (HydraWinTask task, _) = TaskWith("Alpha", 0x10);
        HydraWinTask sameTask = task;
        workspaces.AssignWindow(sameTask.Id, Window(0x11));

        List<TaskBadge> raised = [];
        hub.TaskBadgeChanged += (_, badge) => raised.Add(badge);

        hub.OnFlash(Window(0x10, "first"));
        hub.OnFlash(Window(0x11, "second"));

        Assert.Equal(2, raised.Count);
        Assert.Equal(2, raised[^1].Count);
        Assert.Contains("second", raised[^1].TopLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleRuleBadgesWithItsOwnLabel()
    {
        (HydraWinTask task, _) = TaskWith("Alpha", 0x10);
        rules.Add(new NotificationRule
        {
            ProcessFileName = "*",
            TitleRegex = @"^\(\d+\)",
            Kind = NotificationKind.Title,
            Label = "Unread",
            Enabled = true,
        });

        hub.OnTitleChanged(Window(0x10, "(2) Chat"), "Chat", "(2) Chat");

        PendingNotification pending = Assert.Single(hub.PendingFor(task.Id));
        Assert.Equal(NotificationKind.Title, pending.Kind);
        Assert.StartsWith("Unread", pending.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void ATitleChangeMatchingNoRuleBadgesNothing()
    {
        // Title noise is constant — VS Code's dirty dot, browser tabs. Silence is the default.
        (HydraWinTask task, _) = TaskWith("Alpha", 0x10);

        hub.OnTitleChanged(Window(0x10, "● file.cs"), "file.cs", "● file.cs");

        Assert.Equal(0, hub.CountFor(task.Id));
    }

    [Fact]
    public void ARepeatedMatchingTitleBadgesOnlyOnce()
    {
        (HydraWinTask task, _) = TaskWith("Alpha", 0x10);
        rules.Add(new NotificationRule
        {
            ProcessFileName = "*",
            TitleRegex = @"^\(\d+\)",
            Enabled = true,
        });

        hub.OnTitleChanged(Window(0x10), "Chat", "(2) Chat");
        hub.OnTitleChanged(Window(0x10), "(2) Chat", "(3) Chat");
        hub.OnTitleChanged(Window(0x10), "(3) Chat", "(4) Chat");

        Assert.Equal(1, hub.CountFor(task.Id));
    }
}
