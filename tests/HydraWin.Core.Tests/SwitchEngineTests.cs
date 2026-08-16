using System.Text.Json;
using HydraWin.Core.Persistence;
using HydraWin.Core.Recovery;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// The switch itself, against a scripted Win32 layer and a real journal in a temp directory.
/// </summary>
public sealed class SwitchEngineTests : IDisposable
{
    private const string AppPath = @"C:\apps\app.exe";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeWindowApi api = new();
    private readonly RecoveryJournal journal;
    private readonly WorkspaceStore store;
    private readonly WorkspaceService workspaces;
    private readonly HiddenWindowSet hidden = new();
    private readonly SwitchEngine engine;

    public SwitchEngineTests()
    {
        Directory.CreateDirectory(directory);
        journal = new RecoveryJournal(Path.Combine(directory, "journal.json"));
        store = new WorkspaceStore(Path.Combine(directory, "state.json"), TimeSpan.FromMinutes(5));
        workspaces = new WorkspaceService(store);
        engine = new SwitchEngine(workspaces, journal, new RestoreService(api), api, hidden);
    }

    public void Dispose()
    {
        journal.Dispose();
        store.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Creates a task holding one live window, registered with the fake desktop.</summary>
    private HydraWinTask TaskWith(string name, params nint[] handles)
    {
        HydraWinTask task = workspaces.CreateTask(name);
        foreach (nint hwnd in handles)
        {
            api.Add(hwnd, pid: (int)hwnd, path: AppPath, visible: true);
            workspaces.AssignWindow(task.Id, new TrackedWindow
            {
                Hwnd = hwnd,
                Pid = (int)hwnd,
                ProcessPath = AppPath,
                Title = $"window {hwnd:X}",
            });
        }

        return task;
    }

    [Fact]
    public void SwitchingHidesTheOtherTaskAndLeavesTheTargetVisible()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);

        SwitchSummary summary = engine.SwitchTo(alpha.Id);

        Assert.Equal(1, summary.Hidden);
        Assert.False(api.Get(0x20)!.Visible);
        Assert.True(api.Get(0x10)!.Visible);
        Assert.Equal(alpha.Id, workspaces.State.ActiveTaskId);
    }

    [Fact]
    public void TheJournalIsOnDiskBeforeAnyWindowIsHidden()
    {
        // The project's one invariant, checked against the real file rather than a mock's call
        // order: at the moment Hide runs, the entry must already be readable from disk.
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);

        bool journalOnDisk = false;
        api.OnHide = _ =>
        {
            string json = File.ReadAllText(journal.Path);
            journalOnDisk = json.Contains("\"Hwnd\": 32", StringComparison.Ordinal);
        };

        engine.SwitchTo(alpha.Id);

        Assert.True(journalOnDisk, "the journal entry must be flushed before the window is hidden");
    }

    [Fact]
    public void TheJournalCarriesThePlacementNeededToPutTheWindowBack()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);
        api.Get(0x20)!.Placement = new HydraWin.Core.Interop.WindowPlacement
        {
            ShowCmd = 3,
            NormalPosition = new HydraWin.Core.Interop.Rect { Left = 5, Top = 6, Right = 7, Bottom = 8 },
        };

        api.OnHide = null;
        engine.SwitchTo(alpha.Id);

        JournalEntry entry = Assert.Single(journal.Snapshot());
        Assert.Equal(0x20, entry.Hwnd);
        Assert.Equal(3, entry.Placement.ShowCmd);
        Assert.Equal(5, entry.Placement.NormalLeft);
        Assert.Equal(AppPath, entry.ProcessPath);
    }

    [Fact]
    public void SwitchingBackShowsTheWindowsAgainAndEmptiesTheJournal()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        HydraWinTask beta = TaskWith("Beta", 0x20);

        engine.SwitchTo(alpha.Id);
        SwitchSummary summary = engine.SwitchTo(beta.Id);

        Assert.Equal(1, summary.Shown);
        Assert.True(api.Get(0x20)!.Visible);
        Assert.False(api.Get(0x10)!.Visible);
        Assert.Equal([0x10], journal.Snapshot().Select(e => e.Hwnd));
    }

    [Fact]
    public void SwitchingToTheActiveTaskAgainChangesNothing()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);
        engine.SwitchTo(alpha.Id);

        SwitchSummary again = engine.SwitchTo(alpha.Id);

        Assert.Equal(new SwitchSummary(0, 0, 0, 0), again);
        Assert.True(api.Get(0x10)!.Visible);
        Assert.False(api.Get(0x20)!.Visible);
    }

    [Fact]
    public void AWindowThatRefusesToHideStaysVisibleAndLeavesNoJournalEntry()
    {
        // An elevated window under UIPI. A journal entry for a window that is not hidden would
        // make recovery "restore" something that never moved.
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);
        api.RefuseToHide.Add(0x20);

        SwitchSummary summary = engine.SwitchTo(alpha.Id);

        Assert.Equal(1, summary.Unmanageable);
        Assert.Equal(0, summary.Hidden);
        Assert.True(api.Get(0x20)!.Visible);
        Assert.Empty(journal.Snapshot());
        Assert.False(hidden.Contains(0x20));
    }

    [Fact]
    public void ARefusedWindowIsMarkedUnmanageableOnItsAssignment()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        HydraWinTask beta = TaskWith("Beta", 0x20);
        api.RefuseToHide.Add(0x20);

        engine.SwitchTo(alpha.Id);

        Assert.True(beta.Assignments[0].Unmanageable);
    }

    [Fact]
    public void AWindowThatDiesWhileHiddenIsReportedStaleAndItsBindingDropped()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        HydraWinTask beta = TaskWith("Beta", 0x20);
        engine.SwitchTo(alpha.Id);

        // Killed from Task Manager while it was hidden.
        api.Remove(0x20);

        SwitchSummary summary = engine.SwitchTo(beta.Id);

        Assert.Equal(1, summary.Stale);
        Assert.Equal(0, summary.Shown);
        Assert.False(beta.Assignments[0].IsBound);

        // The rule survives, so reopening the window re-attaches it.
        Assert.NotNull(beta.Assignments[0].Rule);

        // The dead window's entry is gone; Alpha's window is legitimately journaled now, since
        // switching to Beta hid it.
        Assert.Equal([0x10], journal.Snapshot().Select(e => e.Hwnd));
    }

    [Fact]
    public void AWindowThatClosesWhileHiddenLeavesNothingBehindInTheJournal()
    {
        // The tracker unbinds a closed window before any switch can notice, so without this the
        // entry would be orphaned - no assignment refers to it, and only a full RestoreAll would
        // ever clear it, while the dead handle stayed in the hidden set.
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);
        engine.SwitchTo(alpha.Id);
        Assert.Single(journal.Snapshot());
        Assert.True(hidden.Contains(0x20));

        api.Remove(0x20);
        engine.OnWindowDisappeared(0x20);

        Assert.True(journal.IsEmpty);
        Assert.False(hidden.Contains(0x20));
    }

    [Fact]
    public void ForgettingAWindowThatWasNeverHiddenCostsNothing()
    {
        engine.OnWindowDisappeared(0x999);

        Assert.True(journal.IsEmpty);
    }

    [Fact]
    public void TwoWindowsOfOneProcessInDifferentTasksAreHandledIndependently()
    {
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        HydraWinTask beta = workspaces.CreateTask("Beta");
        foreach ((nint hwnd, HydraWinTask task) in new[] { (0x10, alpha), (0x11, beta) })
        {
            api.Add(hwnd, pid: 777, path: AppPath, visible: true);
            workspaces.AssignWindow(task.Id, new TrackedWindow
            {
                Hwnd = hwnd,
                Pid = 777,
                ProcessPath = AppPath,
                Title = $"window {hwnd:X}",
            });
        }

        engine.SwitchTo(alpha.Id);

        Assert.True(api.Get(0x10)!.Visible);
        Assert.False(api.Get(0x11)!.Visible);
    }

    [Fact]
    public void FocusGoesToTheTasksLastActiveWindow()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10, 0x11);
        TaskWith("Beta", 0x20);
        engine.OnForegroundChanged(0x11);

        engine.SwitchTo(alpha.Id);

        Assert.Equal(0x11, api.FocusedWindow);
    }

    [Fact]
    public void FocusFallsBackToAWindowOfTheTaskWhenThereIsNoLastActive()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);

        engine.SwitchTo(alpha.Id);

        Assert.Equal(0x10, api.FocusedWindow);
    }

    [Fact]
    public void ShowAllBringsEverythingBackAndClearsTheActiveTask()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);
        engine.SwitchTo(alpha.Id);

        RestoreSummary summary = engine.ShowAllTasks();

        Assert.Equal(1, summary.Restored);
        Assert.True(api.Get(0x20)!.Visible);
        Assert.True(journal.IsEmpty);
        Assert.Equal(0, hidden.Count);
        Assert.Null(workspaces.State.ActiveTaskId);
    }

    [Fact]
    public void DeletingATaskShowsItsHiddenWindowsFirstAndNeverClosesThem()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        HydraWinTask beta = TaskWith("Beta", 0x20);
        engine.SwitchTo(alpha.Id);
        Assert.False(api.Get(0x20)!.Visible);

        IReadOnlyList<WindowAssignment> orphaned = engine.DeleteTask(beta.Id);

        Assert.Single(orphaned);
        Assert.True(api.Get(0x20)!.Visible);
        Assert.NotNull(api.Get(0x20));
        Assert.True(journal.IsEmpty);
        Assert.Single(workspaces.Tasks);
    }

    [Fact]
    public void AWindowWhosePlacementCannotBeReadIsNotHidden()
    {
        // Hiding it would mean never being able to put it back where it was.
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        workspaces.AssignWindow(
            workspaces.CreateTask("Beta").Id,
            new TrackedWindow { Hwnd = 0x99, Pid = 99, ProcessPath = AppPath, Title = "ghost" });

        SwitchSummary summary = engine.SwitchTo(alpha.Id);

        Assert.Equal(0, summary.Hidden);
        Assert.Empty(journal.Snapshot());
    }

    [Fact]
    public void TheCrashHookRunsAfterTheFlushAndBeforeAnyHide()
    {
        // Models the worst interleaving: the process dies between the two. Whatever the hook sees
        // on disk is what recovery would have to work with.
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        TaskWith("Beta", 0x20);

        string? journalAtHook = null;
        engine.AfterJournalFlush = () => journalAtHook = File.ReadAllText(journal.Path);

        bool hidAnythingBeforeHook = false;
        api.OnHide = _ => hidAnythingBeforeHook = journalAtHook is null;

        engine.SwitchTo(alpha.Id);

        Assert.NotNull(journalAtHook);
        Assert.False(hidAnythingBeforeHook);
        using JsonDocument document = JsonDocument.Parse(journalAtHook);
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }
}
