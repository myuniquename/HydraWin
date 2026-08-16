using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HydraWin.App.Services;
using HydraWin.Core.Interop;
using HydraWin.Core.Persistence;
using HydraWin.Core.Recovery;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.ViewModels;

/// <summary>
/// Root view model: owns the task rows and the unassigned pane, and turns Core's events into
/// what the window shows.
/// </summary>
/// <remarks>
/// <para>
/// Structural changes — a task created, a window assigned, a switch completed — rebuild the task
/// list, which is cheap because tasks and their windows are few. Title changes deliberately do
/// <em>not</em>: task 01 measured about one per second per busy Claude Code terminal, so
/// <see cref="OnWindowTitleChanged"/> touches one row through a handle-keyed index and nothing
/// else (task 07 § F).
/// </para>
/// <para>
/// No Win32 here or anywhere above Core; every window operation goes through
/// <see cref="SwitchEngine"/> or <see cref="WorkspaceService"/>.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly WindowTracker tracker;
    private readonly HiddenWindowSet hiddenWindows;
    private readonly WorkspaceStore store;
    private readonly WorkspaceService workspaces;
    private readonly SwitchEngine switchEngine;
    private readonly WindowIconCache icons;
    private readonly Dictionary<nint, WindowViewModel> windowsByHwnd = [];
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
        : this(
            tracker,
            hiddenWindows,
            store,
            journal,
            restoreService,
            new WindowIconCache(Win32IconSource.Instance))
    {
    }

    public MainViewModel(
        WindowTracker tracker,
        HiddenWindowSet hiddenWindows,
        WorkspaceStore store,
        RecoveryJournal journal,
        RestoreService restoreService,
        WindowIconCache icons)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(hiddenWindows);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(restoreService);
        ArgumentNullException.ThrowIfNull(icons);

        this.tracker = tracker;
        this.hiddenWindows = hiddenWindows;
        this.store = store;
        this.icons = icons;

        workspaces = new WorkspaceService(store);
        switchEngine = new SwitchEngine(
            workspaces, journal, restoreService, Win32WindowApi.Instance, hiddenWindows);

        switchEngine.SwitchCompleted += OnSwitchCompleted;

        tracker.WindowAppeared += OnWindowAppeared;
        tracker.WindowDisappeared += OnWindowDisappeared;
        tracker.WindowTitleChanged += OnWindowTitleChanged;
        tracker.ForegroundChanged += OnForegroundChanged;

        workspaces.TasksChanged += (_, _) => Rebuild();
        workspaces.WindowAssigned += (_, _) => Rebuild();
        workspaces.WindowUnassigned += (_, _) => Rebuild();
        workspaces.WindowReattached += OnWindowReattached;

        store.CorruptFileQuarantined += (_, path) =>
            Say($"state.json was corrupt — set aside as {System.IO.Path.GetFileName(path)}");
        store.SaveFailed += (_, ex) => Say($"could not save: {ex.Message}");

        AlwaysOnTop = workspaces.State.Settings.AlwaysOnTop;

        Rebuild();
    }

    partial void OnAlwaysOnTopChanged(bool value) =>
        workspaces.UpdateSettings(settings => settings.AlwaysOnTop = value);

    /// <summary>The window title: <c>HydraWin — &lt;active task&gt;</c>, or just the app name.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "HydraWin";

    /// <summary>
    /// The one status line. Switch summaries, re-attach notices and recovery reports all land
    /// here, because they are all answers to "what just happened to my windows".
    /// </summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "Ready.";

    /// <summary>
    /// Whether the manager window stays above other windows. Persisted, and on by default: a
    /// switch ends by focusing one of the task's windows, which would otherwise cover the very
    /// window the user clicks to switch again.
    /// </summary>
    [ObservableProperty]
    public partial bool AlwaysOnTop { get; set; }

    /// <summary>The tasks, in display order.</summary>
    public ObservableCollection<TaskViewModel> Tasks { get; } = [];

    /// <summary>
    /// Windows belonging to no task. These stay visible through every switch by design, which is
    /// why the pane says so.
    /// </summary>
    public ObservableCollection<WindowViewModel> Unassigned { get; } = [];

    /// <summary>
    /// Asks the user to confirm deleting a task. Set by the view; the wording matters, so it lives
    /// with the view rather than here.
    /// </summary>
    public Func<TaskViewModel, bool>? ConfirmDelete { get; set; }

    /// <summary>Starts tracking. Must be called on the dispatcher thread.</summary>
    public void Start()
    {
        tracker.Start();
        foreach (TrackedWindow window in tracker.Windows)
        {
            Track(window);
        }

        Rebuild();
        Say($"{windowsByHwnd.Count} window(s) found. Drag one onto a task to begin.");
    }

    /// <summary>Reports what startup recovery put back, if anything.</summary>
    public void ShowRecoveryNotice(RestoreSummary summary) =>
        Say($"Recovered {summary.Restored} window(s) from a previous session"
            + (summary.Stale > 0 ? $", dropped {summary.Stale} stale entr(ies)" : string.Empty)
            + ".");

    /// <summary>Creates a task and opens it for naming straight away.</summary>
    [RelayCommand]
    public void CreateTask()
    {
        HydraWinTask task = workspaces.CreateTask(NextTaskName());
        Rebuild();

        TaskViewModel? row = Tasks.FirstOrDefault(t => t.Id == task.Id);
        if (row is not null)
        {
            row.IsRenaming = true;
        }
    }

    /// <summary>Commits an inline rename.</summary>
    [RelayCommand]
    public void RenameTask(TaskViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        task.IsRenaming = false;
        string name = task.Name.Trim();
        if (name.Length == 0)
        {
            // An empty name would leave an unclickable row; put the old one back.
            Rebuild();
            return;
        }

        workspaces.RenameTask(task.Id, name);
        UpdateTitle();
    }

    /// <summary>
    /// Deletes a task after confirmation, un-hiding its windows first. Never closes a window.
    /// </summary>
    [RelayCommand]
    public void DeleteTask(TaskViewModel? task)
    {
        if (task is null || ConfirmDelete?.Invoke(task) == false)
        {
            return;
        }

        IReadOnlyList<WindowAssignment> orphaned = switchEngine.DeleteTask(task.Id);
        SyncHiddenFlags();
        Rebuild();
        Say($"Deleted “{task.Name}”. {orphaned.Count} window(s) returned to Unassigned, "
            + "none closed.");
    }

    /// <summary>Switches to a task: hides every other task's windows and restores this one's.</summary>
    [RelayCommand]
    public void SwitchTo(TaskViewModel? task)
    {
        if (task is not null)
        {
            switchEngine.SwitchTo(task.Id);
        }
    }

    /// <summary>Brings every hidden window back and leaves no task active.</summary>
    [RelayCommand]
    public void ShowAll()
    {
        RestoreSummary summary = switchEngine.ShowAllTasks();
        SyncHiddenFlags();
        Rebuild();
        Say($"Showing all windows — {summary}.");
    }

    /// <summary>Switches to the window's task and focuses that window.</summary>
    [RelayCommand]
    public void FocusWindow(WindowViewModel? window)
    {
        if (window is not null)
        {
            switchEngine.SwitchToWindow(window.Hwnd);
        }
    }

    /// <summary>
    /// Puts a window into a task, moving it out of wherever it was. When the task is not the one
    /// on screen the window is hidden immediately, so it behaves as though it had always belonged
    /// there.
    /// </summary>
    public void AssignWindow(WindowViewModel? window, TaskViewModel? task)
    {
        if (window is null || task is null)
        {
            return;
        }

        AssignOutcome outcome = switchEngine.AssignWindowToTask(task.Id, window.Source);
        SyncHiddenFlags();
        Rebuild();
        Say(Describe(outcome, window.DisplayTitle, task.Name));
    }

    /// <summary>Puts a one-off note in the status line.</summary>
    public void Note(string message) => Say(message);

    /// <summary>
    /// Whether a window handle is one the picker should offer. Being in the inventory means every
    /// clause of the filter already passed, which is exactly the condition for accepting it — so
    /// the highlight and the drop can never disagree.
    /// </summary>
    public bool IsPickable(nint hwnd) => windowsByHwnd.ContainsKey(hwnd);

    /// <summary>
    /// Takes the window the crosshair was released over and puts it into the task, explaining
    /// itself when it will not.
    /// </summary>
    public void PickWindow(TaskViewModel? task, nint hwnd)
    {
        if (task is null)
        {
            return;
        }

        if (hwnd == 0)
        {
            Say("No window there.");
            return;
        }

        // Being in the inventory is the whitelist: it means every clause of the filter already
        // passed. Only when the lookup misses is the tracker asked to name the reason.
        if (!windowsByHwnd.TryGetValue(hwnd, out WindowViewModel? window))
        {
            Say(ExplainRefusal(hwnd));
            return;
        }

        AssignWindow(window, task);
    }

    private string ExplainRefusal(nint hwnd) => tracker.ExplainOne(hwnd) switch
    {
        TrackableVerdict.OwnProcess => "That is HydraWin's own window.",
        TrackableVerdict.Elevated =>
            "That window belongs to an elevated app — HydraWin cannot hide it, so it cannot join "
            + "a task.",
        TrackableVerdict.NoTitle or TrackableVerdict.ToolWindow or TrackableVerdict.Owned =>
            "That is not a window HydraWin can manage.",
        TrackableVerdict.Cloaked or TrackableVerdict.NotVisible =>
            "That window is not on screen any more.",

        // Trackable, yet absent from the index: the sweep has not caught up with it yet.
        _ => "That window is not in the list yet — try again in a moment.",
    };

    private static string Describe(AssignOutcome outcome, string window, string task) => outcome switch
    {
        AssignOutcome.AssignedAndHidden =>
            $"Added “{window}” to “{task}” and hid it with the task.",
        AssignOutcome.Assigned => $"Added “{window}” to “{task}”.",
        AssignOutcome.AssignedButRefusedToHide =>
            $"Added “{window}” to “{task}”, but it refused to hide — it is probably elevated.",
        AssignOutcome.AssignedButUnreadablePlacement =>
            $"Added “{window}” to “{task}”, but its position could not be read, so it was left "
            + "visible rather than hidden with no way back.",
        _ => $"Could not add “{window}” — that task no longer exists.",
    };

    /// <summary>Removes a window from its task; it stays visible in every task thereafter.</summary>
    [RelayCommand]
    public void UnassignWindow(WindowViewModel? window)
    {
        if (window is null)
        {
            return;
        }

        // A window that is currently hidden must come back before it is let go of, or it would be
        // stranded: unassigned windows are not in any switch plan, so nothing would ever show it.
        if (hiddenWindows.Contains(window.Hwnd))
        {
            switchEngine.ShowAllTasks();
            SyncHiddenFlags();
        }

        workspaces.UnassignWindow(window.Hwnd);
        Say($"“{window.DisplayTitle}” is unassigned and stays visible in every task.");
    }

    /// <summary>Moves a task to a new position in the list.</summary>
    public void ReorderTask(Guid taskId, int newIndex)
    {
        workspaces.ReorderTask(taskId, newIndex);
        Say("Task order updated.");
    }

    /// <summary>The task a window row currently belongs to, or <see langword="null"/>.</summary>
    public TaskViewModel? TaskOf(WindowViewModel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return Tasks.FirstOrDefault(t => t.Windows.Contains(window));
    }

    /// <summary>The row for a window handle — how a drop turns its payload back into a window.</summary>
    public WindowViewModel? FindWindow(nint hwnd) =>
        windowsByHwnd.GetValueOrDefault(hwnd);

    /// <summary>The row for a task id — how a drop turns its payload back into a task.</summary>
    public TaskViewModel? FindTask(Guid id) => Tasks.FirstOrDefault(t => t.Id == id);

    /// <summary>
    /// Commits any inline rename that is still open. Called whenever the user does something else
    /// — presses a button, starts a drag, clicks another row.
    /// </summary>
    /// <remarks>
    /// Lost focus alone is not enough: the rename box has to have <em>had</em> focus for that to
    /// fire, and a click that never reaches it (or a drag begun elsewhere) would otherwise leave
    /// the box open indefinitely.
    /// </remarks>
    public void CommitPendingRename()
    {
        foreach (TaskViewModel task in Tasks.Where(t => t.IsRenaming).ToList())
        {
            RenameTask(task);
        }
    }

    /// <summary>Abandons an inline rename, putting the stored name back.</summary>
    public void CancelRename(TaskViewModel? task)
    {
        if (task is not null)
        {
            task.IsRenaming = false;

            // Name was edited in place, so the model is the only copy of the old value.
            Rebuild();
        }
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
        switchEngine.SwitchCompleted -= OnSwitchCompleted;
        tracker.Dispose();

        // Shutdown must not lose the last edits.
        workspaces.Flush();
        store.Dispose();
        disposed = true;
    }

    /// <summary>Adds a window to the index, creating its row.</summary>
    /// <returns>Whether it was new.</returns>
    private bool Track(TrackedWindow window)
    {
        if (windowsByHwnd.ContainsKey(window.Hwnd))
        {
            return false;
        }

        windowsByHwnd[window.Hwnd] = new WindowViewModel(window, icons.GetIcon(window.ProcessPath))
        {
            IsHydraWinHidden = hiddenWindows.Contains(window.Hwnd),
        };

        return true;
    }

    /// <summary>
    /// Rebuilds the task rows and the unassigned pane from the model. Structural changes only —
    /// never on a title event.
    /// </summary>
    private void Rebuild()
    {
        // Transient row state has to survive the rebuild. Creating a task raises TasksChanged
        // through the synchronization context, so the rebuild it triggers lands *after* the
        // command has already put the new row into rename mode — without this, the rename box
        // appeared and vanished in the same instant.
        Dictionary<Guid, (bool Expanded, bool Renaming)> rowState =
            Tasks.ToDictionary(t => t.Id, t => (t.IsExpanded, t.IsRenaming));
        Guid? active = workspaces.State.ActiveTaskId;

        foreach (TaskViewModel row in Tasks)
        {
            row.Clear();
        }

        Tasks.Clear();

        foreach (HydraWinTask task in workspaces.Tasks)
        {
            bool known = rowState.TryGetValue(task.Id, out (bool Expanded, bool Renaming) previous);
            var row = new TaskViewModel(task.Id, task.Name, task.ColorHex)
            {
                IsActive = task.Id == active,
                IsExpanded = !known || previous.Expanded,
                IsRenaming = known && previous.Renaming,
            };

            foreach (WindowAssignment assignment in task.Assignments)
            {
                if (assignment.BoundHwnd is nint hwnd
                    && windowsByHwnd.TryGetValue(hwnd, out WindowViewModel? window))
                {
                    window.IsUnmanageable = assignment.Unmanageable;
                    row.Add(window);
                }
            }

            Tasks.Add(row);
        }

        Unassigned.Clear();
        foreach (WindowViewModel window in windowsByHwnd.Values
            .Where(w => !workspaces.IsBound(w.Hwnd))
            .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.DisplayTitle, StringComparer.CurrentCultureIgnoreCase))
        {
            Unassigned.Add(window);
        }

        UpdateTitle();
    }

    private void UpdateTitle()
    {
        TaskViewModel? active = Tasks.FirstOrDefault(t => t.IsActive);
        Title = active is null ? "HydraWin" : $"HydraWin — {active.Name}";
    }

    private void SyncHiddenFlags()
    {
        foreach (WindowViewModel window in windowsByHwnd.Values)
        {
            window.IsHydraWinHidden = hiddenWindows.Contains(window.Hwnd);
        }
    }

    private string NextTaskName()
    {
        int n = Tasks.Count + 1;
        while (Tasks.Any(t => string.Equals(t.Name, $"Task {n}", StringComparison.OrdinalIgnoreCase)))
        {
            n++;
        }

        return $"Task {n}";
    }

    private void OnSwitchCompleted(object? sender, SwitchSummary summary)
    {
        SyncHiddenFlags();
        Rebuild();

        TaskViewModel? active = Tasks.FirstOrDefault(t => t.IsActive);
        Say(active is null
            ? $"Switched — {summary}."
            : $"Switched to “{active.Name}” — {summary}.");
    }

    private void OnWindowAppeared(object? sender, TrackedWindow window)
    {
        if (!Track(window))
        {
            return;
        }

        // Offer it to the re-attach rules: a reopened window rejoins its task on its own. Raises
        // WindowReattached when a rule claims it, which rebuilds.
        workspaces.OnWindowAppeared(window);
        Rebuild();
    }

    private void OnWindowDisappeared(object? sender, TrackedWindow window)
    {
        windowsByHwnd.Remove(window.Hwnd);

        // Drops the binding but keeps the rule, so the window re-attaches when it comes back...
        workspaces.OnWindowDisappeared(window.Hwnd);

        // ...and forgets it in the journal, so a window that died while hidden leaves no dead
        // handle behind in journal.json or the hidden set.
        switchEngine.OnWindowDisappeared(window.Hwnd);
        Rebuild();
    }

    private void OnWindowTitleChanged(object? sender, WindowTitleChangedEventArgs e)
    {
        // The hot path. One dictionary lookup and one property set; no list is touched.
        if (windowsByHwnd.TryGetValue(e.Window.Hwnd, out WindowViewModel? window))
        {
            window.Title = e.NewTitle;
        }
    }

    private void OnForegroundChanged(object? sender, nint hwnd) =>
        switchEngine.OnForegroundChanged(hwnd);

    private void OnWindowReattached(object? sender, AssignmentChangedEventArgs e)
    {
        Rebuild();
        string window = e.Window?.Title ?? e.Assignment.Rule.TitlePattern;
        Say($"Re-attached “{window}” to “{e.Task.Name}”.");
    }

    private void Say(string message) => Status = $"{message}  ({DateTime.Now:HH:mm:ss})";
}
