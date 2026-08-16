using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HydraWin.Core.Interop;
using HydraWin.Core.Persistence;
using HydraWin.Core.Recovery;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.ViewModels;

/// <summary>
/// Root view model for the main window.
/// </summary>
/// <remarks>
/// Everything below the title is task 03's throwaway debug harness — a live view of the tracker
/// plus the rejected windows and their reasons, which is what makes "these windows are absent"
/// a verifiable claim. Task 07 replaces all of it with the real task table.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly WindowTracker tracker;
    private readonly HiddenWindowSet hiddenWindows;
    private readonly WorkspaceStore store;
    private readonly WorkspaceService workspaces;
    private readonly RecoveryJournal journal;
    private readonly SwitchEngine switchEngine;
    private readonly IWindowApi windowApi = Win32WindowApi.Instance;
    private bool disposed;

    public MainViewModel(RecoveryJournal journal, RestoreService restoreService)
        : this(new HiddenWindowSet(journal), new WorkspaceStore(), journal, restoreService)
    {
    }

    private MainViewModel(
        HiddenWindowSet hiddenWindows,
        WorkspaceStore store,
        RecoveryJournal journal,
        RestoreService restoreService)
        : this(new WindowTracker(hiddenWindows), hiddenWindows, store, journal, restoreService)
    {
    }

    public MainViewModel(
        WindowTracker tracker,
        HiddenWindowSet hiddenWindows,
        WorkspaceStore store,
        RecoveryJournal journal,
        RestoreService restoreService)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(hiddenWindows);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(restoreService);
        this.tracker = tracker;
        this.hiddenWindows = hiddenWindows;
        this.store = store;
        this.journal = journal;
        workspaces = new WorkspaceService(store);
        switchEngine = new SwitchEngine(
            workspaces, journal, restoreService, windowApi, hiddenWindows);
        switchEngine.SwitchCompleted += (_, summary) =>
        {
            RefreshTasks();
            UpdateStatus($"switch: {summary}");
        };

        tracker.WindowAppeared += OnWindowAppeared;
        tracker.WindowDisappeared += OnWindowDisappeared;
        tracker.WindowTitleChanged += OnWindowTitleChanged;
        tracker.ForegroundChanged += OnForegroundChanged;

        workspaces.TasksChanged += (_, _) => RefreshTasks();
        workspaces.WindowAssigned += (_, e) => OnAssignmentChanged("assigned", e);
        workspaces.WindowUnassigned += (_, e) => OnAssignmentChanged("unassigned", e);
        workspaces.WindowReattached += (_, e) => OnAssignmentChanged("re-attached", e);
        store.CorruptFileQuarantined += (_, path) =>
            UpdateStatus($"state.json was corrupt — set aside as {System.IO.Path.GetFileName(path)}");
        store.SaveFailed += (_, ex) => UpdateStatus($"save failed: {ex.Message}");

        RefreshTasks();
    }

    /// <summary>Window title; task 07 makes it <c>HydraWin — &lt;active task&gt;</c>.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "HydraWin";

    /// <summary>Status line under the lists.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "not started";

    /// <summary>
    /// Count of rejections per clause. This is what makes "tool windows and UWP ghosts are
    /// absent" checkable: every clause should be visibly firing, and HydraWin's own windows
    /// should show up under <see cref="TrackableVerdict.OwnProcess"/>.
    /// </summary>
    [ObservableProperty]
    public partial string RejectionSummary { get; set; } = string.Empty;

    /// <summary>Whether the ~2 s reconciliation sweep runs; off proves the hooks work alone.</summary>
    [ObservableProperty]
    public partial bool ReconciliationEnabled { get; set; } = true;

    /// <summary>The live inventory, in the order the tracker reported it.</summary>
    public ObservableCollection<TrackedWindow> Windows { get; } = [];

    /// <summary>Windows that failed the filter, with the clause that rejected them.</summary>
    public ObservableCollection<RejectedWindow> Rejected { get; } = [];

    /// <summary>Task 04 harness: the persisted tasks and what is bound to them.</summary>
    public ObservableCollection<string> TaskLines { get; } = [];

    /// <summary>Where <c>state.json</c> lives, shown so the manual check knows where to look.</summary>
    public string StatePath => store.Path;

    /// <summary>The tracked window the drill commands act on.</summary>
    [ObservableProperty]
    public partial TrackedWindow? SelectedWindow { get; set; }

    /// <summary>
    /// What startup recovery put back, if anything. Kept apart from <see cref="Status"/> because
    /// that line churns with every window event and would bury the notice within seconds.
    /// </summary>
    [ObservableProperty]
    public partial string RecoveryNotice { get; set; } = string.Empty;

    /// <summary>Reports what startup recovery put back.</summary>
    public void ShowRecoveryNotice(RestoreSummary summary) =>
        RecoveryNotice = $"Recovered {summary.Restored} window(s) from a previous session"
            + (summary.Stale > 0 ? $", dropped {summary.Stale} stale entr(ies)" : string.Empty)
            + $" — {DateTime.Now:HH:mm:ss}";

    /// <summary>
    /// Task 05 drill: hides the selected window <em>through the journal</em>, so the crash drill
    /// can be run before task 06's switch engine exists. Task 07 deletes this.
    /// </summary>
    /// <remarks>
    /// The ordering below is the project's one invariant and is deliberately explicit: capture
    /// the placement, write and flush the journal entry, and only then call
    /// <see cref="IWindowApi.Hide"/>. If the entry cannot be written, nothing is hidden.
    /// </remarks>
    [RelayCommand]
    public void HideSelected()
    {
        if (SelectedWindow is not TrackedWindow window)
        {
            UpdateStatus("select a window first");
            return;
        }

        nint hwnd = window.Hwnd;
        if (!windowApi.TryGetPlacement(hwnd, out WindowPlacement placement))
        {
            UpdateStatus($"could not read placement for 0x{hwnd:X}; not hiding it");
            return;
        }

        journal.RecordBeforeHide([
            new JournalEntry
            {
                Hwnd = hwnd,
                Pid = window.Pid,
                ProcessPath = window.ProcessPath,
                TitleAtHide = window.Title,
                Placement = WindowPlacementDto.FromPlacement(in placement),
                HiddenAt = DateTimeOffset.UtcNow,
            },
        ]);

        // Only now, with the entry on disk.
        ShowWindowResult result = windowApi.Hide(hwnd);
        if (result.Succeeded)
        {
            window.IsHydraWinHidden = true;
            UpdateStatus($"hid 0x{hwnd:X} \"{window.Title}\" — journaled first");
        }
        else
        {
            // It refused, so it is not hidden and must not stay on the books.
            journal.ConfirmShown(hwnd);
            UpdateStatus($"0x{hwnd:X} refused to hide (win32={result.Win32Error}) — entry removed");
        }
    }

    /// <summary>Task 06 harness: brings every hidden window back and clears the active task.</summary>
    [RelayCommand]
    public void RestoreAllWindows()
    {
        RestoreSummary summary = switchEngine.ShowAllTasks();
        foreach (TrackedWindow window in Windows)
        {
            window.IsHydraWinHidden = false;
        }

        RefreshTasks();
        UpdateStatus(summary.ToString());
    }

    /// <summary>
    /// Task 06 harness: switches to the task at the given 1-based position, which is what the
    /// number keys and the per-task buttons both call. Task 07 replaces this with the task table.
    /// </summary>
    [RelayCommand]
    public void SwitchToOrder(int order)
    {
        HydraWinTask? task = workspaces.Tasks.FirstOrDefault(t => t.Order == order);
        if (task is null)
        {
            UpdateStatus($"no task at position {order}");
            return;
        }

        switchEngine.SwitchTo(task.Id);
        SyncHiddenFlags();
    }

    /// <summary>Task 06 harness: deletes the active task, showing its windows first.</summary>
    [RelayCommand]
    public void DeleteActiveTask()
    {
        if (workspaces.State.ActiveTaskId is not Guid active)
        {
            UpdateStatus("no active task to delete");
            return;
        }

        IReadOnlyList<WindowAssignment> orphaned = switchEngine.DeleteTask(active);
        SyncHiddenFlags();
        RefreshTasks();
        UpdateStatus($"deleted the active task; {orphaned.Count} window(s) unassigned, none closed");
    }

    /// <summary>
    /// When the crash hook is armed, the next switch kills the process between the journal flush
    /// and the first hide — the worst interleaving the invariant has to survive.
    /// </summary>
    [ObservableProperty]
    public partial bool CrashAfterJournalFlush { get; set; }

    private void SyncHiddenFlags()
    {
        foreach (TrackedWindow window in Windows)
        {
            window.IsHydraWinHidden = hiddenWindows.Contains(window.Hwnd);
        }
    }

    partial void OnCrashAfterJournalFlushChanged(bool value)
    {
        switchEngine.AfterJournalFlush = value
            ? () => Environment.FailFast("HydraWin task 06 crash drill: dying between journal flush and hide.")
            : null;
        UpdateStatus(value ? "CRASH HOOK ARMED — the next switch will kill the process" : "crash hook off");
    }

    /// <summary>Starts tracking. Must be called on the dispatcher thread.</summary>
    public void Start()
    {
        tracker.Start();
        foreach (TrackedWindow window in tracker.Windows)
        {
            AddWindow(window);
        }

        RefreshRejected();
        UpdateStatus("started");
    }

    /// <summary>Re-runs the explain pass that feeds the rejection pane.</summary>
    [RelayCommand]
    public void RefreshRejected()
    {
        Rejected.Clear();

        List<RejectedWindow> rejected =
        [
            .. tracker.Explain()
                .Where(entry => entry.Verdict != TrackableVerdict.Trackable)
                .Select(entry => new RejectedWindow(entry.Hwnd, entry.Title, entry.Verdict))
                // Noisiest clauses last so the interesting ones are visible without scrolling.
                .OrderBy(r => r.Verdict == TrackableVerdict.NoTitle ? 1 : 0)
                .ThenBy(r => r.Verdict),
        ];

        foreach (RejectedWindow window in rejected)
        {
            Rejected.Add(window);
        }

        RejectionSummary = string.Join(
            "   ",
            rejected.GroupBy(r => r.Verdict)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}={group.Count()}"));

        UpdateStatus("rejections refreshed");
    }

    /// <summary>
    /// Task 04 harness: creates two demo tasks and assigns the first tracked windows to them, so
    /// the manual check has something to persist. Task 07 replaces this with real drag-and-drop.
    /// </summary>
    [RelayCommand]
    public void SeedDemoTasks()
    {
        HydraWinTask alpha = workspaces.CreateTask("Alpha");
        HydraWinTask beta = workspaces.CreateTask("Beta");

        List<TrackedWindow> windows = [.. Windows.Take(4)];
        for (int i = 0; i < windows.Count; i++)
        {
            workspaces.AssignWindow(i % 2 == 0 ? alpha.Id : beta.Id, windows[i]);
        }

        RefreshTasks();
    }

    /// <summary>
    /// Task 06 harness: builds three tasks from windows titled <c>HW-TASK1-…</c> and so on, so
    /// the switch drill has a known layout. Task 07 replaces all of this with drag-and-drop.
    /// </summary>
    [RelayCommand]
    public void SeedSwitchDrill()
    {
        for (int n = 1; n <= 3; n++)
        {
            string marker = $"HW-TASK{n}-";
            List<TrackedWindow> members =
                [.. Windows.Where(w => w.Title.Contains(marker, StringComparison.OrdinalIgnoreCase))];

            if (members.Count == 0)
            {
                continue;
            }

            HydraWinTask task = workspaces.CreateTask($"Task {n}");
            foreach (TrackedWindow window in members)
            {
                workspaces.AssignWindow(task.Id, window);
            }
        }

        workspaces.Flush();
        RefreshTasks();
        UpdateStatus($"seeded {workspaces.Tasks.Count} task(s) for the switch drill");
    }

    /// <summary>Task 04 harness: writes any pending state immediately.</summary>
    [RelayCommand]
    public void FlushState()
    {
        workspaces.Flush();
        UpdateStatus($"flushed to {StatePath}");
    }

    /// <summary>Task 04 harness: deletes every task, returning their windows to unassigned.</summary>
    [RelayCommand]
    public void ClearTasks()
    {
        foreach (HydraWinTask task in workspaces.Tasks.ToList())
        {
            workspaces.DeleteTask(task.Id);
        }

        workspaces.Flush();
        RefreshTasks();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        tracker.WindowAppeared -= OnWindowAppeared;
        tracker.WindowDisappeared -= OnWindowDisappeared;
        tracker.WindowTitleChanged -= OnWindowTitleChanged;
        tracker.ForegroundChanged -= OnForegroundChanged;
        tracker.Dispose();

        // Shutdown must not lose the last edits.
        workspaces.Flush();
        store.Dispose();
        disposed = true;
    }

    private void RefreshTasks()
    {
        TaskLines.Clear();
        foreach (HydraWinTask task in workspaces.Tasks)
        {
            TaskLines.Add($"{task.Order}. {task.Name}  [{task.ColorHex}]  "
                + $"{task.Assignments.Count} assignment(s)");

            foreach (WindowAssignment assignment in task.Assignments)
            {
                string bound = assignment.BoundHwnd is nint hwnd
                    ? $"0x{hwnd:X}"
                    : "unbound";
                TaskLines.Add($"      {assignment.Rule.ProcessFileName} "
                    + $"\"{assignment.Rule.TitlePattern}\" — {bound}");
            }
        }
    }

    private void OnAssignmentChanged(string what, AssignmentChangedEventArgs e)
    {
        RefreshTasks();
        string window = e.Window?.Title ?? e.Assignment.Rule.TitlePattern;
        UpdateStatus($"{what}: {window} ↔ {e.Task.Name}");
    }

    partial void OnReconciliationEnabledChanged(bool value)
    {
        tracker.ReconciliationEnabled = value;
        UpdateStatus(value ? "sweep on" : "sweep OFF (hooks only)");
    }

    private void OnWindowAppeared(object? sender, TrackedWindow window)
    {
        if (!AddWindow(window))
        {
            return;
        }

        // Offer it to the re-attach rules: a reopened window rejoins its task without the user
        // touching anything. Raises WindowReattached when a rule claims it.
        workspaces.OnWindowAppeared(window);
        UpdateStatus($"+ {window.ProcessFileName}");
    }

    /// <summary>
    /// Adds a window unless its handle is already listed, and reports whether it was new.
    /// </summary>
    /// <remarks>
    /// Keyed on the handle rather than the instance: <see cref="Start"/> copies the tracker's
    /// initial sweep while the same sweep is also delivering <c>WindowAppeared</c> events, and a
    /// window that flickers away and back — packaged Notepad does exactly this — can arrive as a
    /// second instance for the same handle. Listing it twice would then be permanent.
    /// </remarks>
    private bool AddWindow(TrackedWindow window)
    {
        if (Windows.Any(w => w.Hwnd == window.Hwnd))
        {
            return false;
        }

        Windows.Add(window);
        return true;
    }

    private void OnWindowDisappeared(object? sender, TrackedWindow window)
    {
        Windows.Remove(window);

        // Drops the binding but keeps the rule, so the window re-attaches when it comes back...
        workspaces.OnWindowDisappeared(window.Hwnd);

        // ...and forgets it in the journal, so a window that died while hidden does not leave a
        // dead handle behind in journal.json and the hidden set.
        switchEngine.OnWindowDisappeared(window.Hwnd);
        UpdateStatus($"- {window.ProcessFileName}");
    }

    private void OnWindowTitleChanged(object? sender, WindowTitleChangedEventArgs e) =>
        UpdateStatus($"~ {e.Window.ProcessFileName}: {e.NewTitle}");

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        // Remembered per task so switching back restores focus where the user left it.
        switchEngine.OnForegroundChanged(hwnd);
        UpdateStatus($"foreground 0x{hwnd:X}");
    }

    private void UpdateStatus(string detail) =>
        Status = $"{Windows.Count} tracked, {Rejected.Count} rejected — {detail} "
            + $"({DateTime.Now:HH:mm:ss})";
}

/// <summary>A window the filter excluded, and why.</summary>
/// <param name="Hwnd">The window handle.</param>
/// <param name="Title">Its title, which may be empty.</param>
/// <param name="Verdict">The clause that rejected it.</param>
public sealed record RejectedWindow(nint Hwnd, string Title, TrackableVerdict Verdict)
{
    /// <summary>Display form for the harness list.</summary>
    public string Display => $"[{Verdict}] 0x{Hwnd:X} {Title}";
}
