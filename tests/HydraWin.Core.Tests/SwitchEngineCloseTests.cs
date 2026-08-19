using HydraWin.Core.Persistence;
using HydraWin.Core.Recovery;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Asking a task's windows to close, against a scripted Win32 layer and a real journal in a temp
/// directory. The ordering assertions are the point: showing a window clears its journal entry,
/// and a window closed while still hidden would leave one behind.
/// </summary>
public sealed class SwitchEngineCloseTests : IDisposable
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

    public SwitchEngineCloseTests()
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

    [Fact]
    public void EveryHiddenWindowIsShownBeforeAnythingIsAskedToClose()
    {
        // A save prompt owned by an invisible window is a prompt the user cannot answer.
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        HydraWinTask beta = TaskWith("Beta", 0x20, 0x21);
        engine.SwitchTo(alpha.Id);
        api.Calls.Clear();

        engine.RequestCloseTask(beta.Id);

        int lastShow = api.Calls.FindLastIndex(c => c.StartsWith("Show(", StringComparison.Ordinal));
        int firstClose =
            api.Calls.FindIndex(c => c.StartsWith("RequestClose(", StringComparison.Ordinal));
        Assert.True(lastShow >= 0 && firstClose >= 0);
        Assert.True(lastShow < firstClose);
    }

    [Fact]
    public void TheJournalIsEmptyAfterwards()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        HydraWinTask beta = TaskWith("Beta", 0x20);
        engine.SwitchTo(alpha.Id);
        Assert.False(journal.IsEmpty);

        engine.RequestCloseTask(beta.Id);

        Assert.True(journal.IsEmpty);
    }

    [Fact]
    public void EveryBoundWindowIsAskedToClose()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10, 0x11);

        IReadOnlyList<nint> asked = engine.RequestCloseTask(alpha.Id);

        Assert.Equal([0x10, 0x11], asked);
        Assert.Contains("RequestClose(0x10)", api.Calls);
        Assert.Contains("RequestClose(0x11)", api.Calls);
    }

    [Fact]
    public void NothingIsDeleted()
    {
        // Deletion is the caller's decision, taken only once the windows have actually gone.
        HydraWinTask alpha = TaskWith("Alpha", 0x10);

        engine.RequestCloseTask(alpha.Id);

        Assert.NotNull(workspaces.FindTask(alpha.Id));
        Assert.Single(workspaces.Tasks);
    }

    [Fact]
    public void AnUnboundAssignmentIsNotAskedToClose()
    {
        // Its rule is waiting for a window that is not open; there is nothing to close.
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        workspaces.AssignWindow(alpha.Id, new TrackedWindow
        {
            Hwnd = 0x99,
            Pid = 99,
            ProcessPath = AppPath,
            Title = "ghost",
        });
        workspaces.OnWindowDisappeared(0x99);

        IReadOnlyList<nint> asked = engine.RequestCloseTask(alpha.Id);

        Assert.Empty(asked);
        Assert.DoesNotContain("RequestClose(0x99)", api.Calls);
    }

    [Fact]
    public void AnUnknownTaskAsksNothing()
    {
        IReadOnlyList<nint> asked = engine.RequestCloseTask(Guid.NewGuid());

        Assert.Empty(asked);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void StillOpenReportsNothingWhenEveryWindowClosed()
    {
        HydraWinTask alpha = TaskWith("Alpha", 0x10, 0x11);

        IReadOnlyList<nint> asked = engine.RequestCloseTask(alpha.Id);

        Assert.Empty(engine.StillOpen(asked));
    }

    [Fact]
    public void StillOpenReportsExactlyTheWindowsThatRefused()
    {
        // The real case: one application is holding a "Save changes?" prompt open.
        HydraWinTask alpha = TaskWith("Alpha", 0x10, 0x11, 0x12);
        api.RefuseToClose.Add(0x11);

        IReadOnlyList<nint> asked = engine.RequestCloseTask(alpha.Id);

        Assert.Equal([0x11], engine.StillOpen(asked));
    }

    [Fact]
    public void AVisibleWindowIsClosedWithoutBeingShownFirst()
    {
        // Nothing is on the books for it, so there is nothing to restore.
        HydraWinTask alpha = TaskWith("Alpha", 0x10);
        api.Calls.Clear();

        engine.RequestCloseTask(alpha.Id);

        Assert.DoesNotContain(api.Calls, c => c.StartsWith("Show(", StringComparison.Ordinal));
        Assert.Contains("RequestClose(0x10)", api.Calls);
    }

    /// <summary>Creates a task holding live windows, registered with the fake desktop.</summary>
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
}
