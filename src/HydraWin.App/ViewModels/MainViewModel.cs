using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HydraWin.App.Services;
using HydraWin.Core.Diagnostics;
using HydraWin.Core.Interop;
using HydraWin.Core.Notifications;
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
    private readonly NotificationHub notifications;
    private readonly WindowIconCache icons;
    private readonly AppLog log = AppLog.Default;
    private readonly Dictionary<nint, WindowViewModel> windowsByHwnd = [];
    private Guid? pendingRenameTaskId;
    private bool rebuildDeferred;
    private int lastAnnouncedTotal;
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

        notifications = new NotificationHub(
            workspaces,
            () => workspaces.State.Settings.NotificationRules);
        notifications.TaskBadgeChanged += (_, _) => RefreshBadges();

        switchEngine.SwitchCompleted += OnSwitchCompleted;

        tracker.WindowAppeared += OnWindowAppeared;
        tracker.WindowDisappeared += OnWindowDisappeared;
        tracker.WindowTitleChanged += OnWindowTitleChanged;
        tracker.ForegroundChanged += OnForegroundChanged;

        workspaces.TasksChanged += (_, _) => Rebuild();
        workspaces.WindowAssigned += (_, _) => Rebuild();
        workspaces.WindowUnassigned += (_, _) => Rebuild();
        workspaces.WindowReattached += OnWindowReattached;
        workspaces.GlobalsChanged += OnGlobalsChanged;

        store.CorruptFileQuarantined += (_, path) =>
            Say($"state.json was corrupt — set aside as {System.IO.Path.GetFileName(path)}");
        store.SaveFailed += (_, ex) => Say($"could not save: {ex.Message}");

        AlwaysOnTop = workspaces.State.Settings.AlwaysOnTop;
        EnsureHotkeyDefaults();
        EnsureNotificationDefaults();

        Rebuild();
    }

    /// <summary>The global hotkeys to register, seeded on first run and hand-editable after.</summary>
    public IReadOnlyList<HotkeyBinding> Hotkeys => workspaces.State.Settings.Hotkeys;

    /// <summary>Whether a clean exit restores every hidden window first.</summary>
    public bool RestoreOnExit => workspaces.State.Settings.RestoreOnExit;

    /// <summary>Whether a new notification also raises a tray balloon. Default off.</summary>
    public bool NotificationToasts => workspaces.State.Settings.NotificationToasts;

    /// <summary>The title-watching notification rules, for the settings dialog to edit.</summary>
    public IReadOnlyList<NotificationRule> NotificationRules =>
        workspaces.State.Settings.NotificationRules;

    /// <summary>
    /// Every window currently in the inventory, for the rule editors to preview against.
    /// </summary>
    public IReadOnlyList<TrackedWindow> Inventory =>
        [.. windowsByHwnd.Values.Select(w => w.Source)];

    /// <summary>
    /// Raised when the hotkey list has been replaced, so the App can re-register them. The
    /// bindings live on the Win32 thread that claimed them, so nothing here can rebind in place.
    /// </summary>
    public event EventHandler? HotkeysChanged;

    /// <summary>
    /// Writes the settings dialog's result through in one go, and reports what needs re-doing
    /// because of it.
    /// </summary>
    /// <remarks>
    /// One call rather than a property per setting: the dialog is modal and its OK is a single
    /// decision, so persisting once keeps <c>state.json</c> from being rewritten five times and
    /// keeps the hotkey re-registration to one pass.
    /// </remarks>
    public void ApplySettings(
        bool restoreOnExit,
        bool closeToTray,
        bool alwaysOnTop,
        bool notificationToasts,
        IReadOnlyList<HotkeyBinding> hotkeys,
        IReadOnlyList<NotificationRule> notificationRules)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(notificationRules);

        bool hotkeysDiffer = !Hotkeys
            .Select(b => (b.Action, b.TaskOrder, b.Modifiers, b.Key))
            .SequenceEqual(hotkeys.Select(b => (b.Action, b.TaskOrder, b.Modifiers, b.Key)));

        workspaces.UpdateSettings(settings =>
        {
            settings.RestoreOnExit = restoreOnExit;
            settings.CloseToTray = closeToTray;
            settings.AlwaysOnTop = alwaysOnTop;
            settings.NotificationToasts = notificationToasts;
            settings.Hotkeys = [.. hotkeys];
            settings.NotificationRules = [.. notificationRules];
        });

        // Goes through the observable property so the toolbar checkbox and the window's Topmost
        // binding follow, rather than only the file.
        AlwaysOnTop = alwaysOnTop;

        OnPropertyChanged(nameof(RestoreOnExit));
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(NotificationToasts));

        Say("Settings saved.");

        if (hotkeysDiffer)
        {
            HotkeysChanged?.Invoke(this, EventArgs.Empty);
        }

        // A changed rule can make a pending badge wrong, and the cheapest honest answer is to let
        // the next event decide rather than leave a stale one showing.
        RefreshBadges();
    }

    /// <summary>
    /// Rewrites the re-attach rule of the assignment a window is bound to.
    /// </summary>
    /// <returns>Whether the window still had an assignment to edit.</returns>
    public bool UpdateReattachRule(
        WindowViewModel window,
        string processFileName,
        string titlePattern,
        bool titleIsRegex)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (workspaces.FindAssignmentOf(window.Hwnd) is not WindowAssignment assignment)
        {
            Say($"“{window.DisplayTitle}” no longer belongs to a task, so it has no rule.");
            return false;
        }

        workspaces.UpdateRule(assignment.Id, rule =>
        {
            rule.ProcessFileName = processFileName;
            rule.TitlePattern = titlePattern;
            rule.TitleIsRegex = titleIsRegex;
        });

        Say($"Re-attach rule updated for “{window.DisplayTitle}”.");
        return true;
    }

    /// <summary>The assignment a window is bound to, for the rule editor to read.</summary>
    public WindowAssignment? AssignmentOf(WindowViewModel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return workspaces.FindAssignmentOf(window.Hwnd);
    }

    /// <summary>Whether closing the manager window hides it to the tray instead of exiting.</summary>
    public bool CloseToTray
    {
        get => workspaces.State.Settings.CloseToTray;
        set
        {
            workspaces.UpdateSettings(settings => settings.CloseToTray = value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Switches to the task at a 1-based position, which is what the hotkeys and the tray menu
    /// both address tasks by.
    /// </summary>
    /// <remarks>
    /// Focus goes to the task's window here, unlike the click-in-the-panel path: a hotkey or a tray
    /// click means the user is somewhere else entirely and wants to land in the task.
    /// </remarks>
    public void SwitchToOrder(int order)
    {
        HydraWinTask? task = workspaces.Tasks.FirstOrDefault(t => t.Order == order);
        if (task is null)
        {
            Say($"No task at position {order}.");
            return;
        }

        switchEngine.SwitchTo(task.Id);
    }

    /// <summary>Switches to a task from the tray, landing in it.</summary>
    public void SwitchToFromTray(Guid taskId) => switchEngine.SwitchTo(taskId);

    private void EnsureHotkeyDefaults()
    {
        if (workspaces.State.Settings.Hotkeys.Count == 0)
        {
            workspaces.UpdateSettings(settings => settings.Hotkeys = HotkeyBinding.Defaults());
        }
    }

    /// <summary>
    /// Seeds the example rule on first run. All seeded rules are disabled: badges come from the
    /// flash channel, which works for any application without a rule at all.
    /// </summary>
    private void EnsureNotificationDefaults()
    {
        if (workspaces.State.Settings.NotificationRules.Count == 0)
        {
            workspaces.UpdateSettings(
                settings => settings.NotificationRules = NotificationRule.Defaults());
        }
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
    /// Windows the user pinned to stay on screen in every task — a player, a clock, a chat window.
    /// </summary>
    /// <remarks>
    /// Unassigned windows are also never hidden, so on the surface these look the same. The
    /// difference is intent, and it survives a restart: a pin carries a re-attach rule, so the
    /// window is claimed again when it reopens instead of drifting back into the unassigned list
    /// where some task's rule might take it.
    /// </remarks>
    public ObservableCollection<WindowViewModel> Globals { get; } = [];

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
    /// <remarks>
    /// The new task's id is remembered rather than its row: creating a task raises
    /// <c>TasksChanged</c>, so a rebuild has already replaced every row by the time this returns.
    /// <see cref="Rebuild"/> applies the flag by construction, which holds however many rebuilds
    /// happen in between.
    /// </remarks>
    [RelayCommand]
    public void CreateTask()
    {
        pendingRenameTaskId = null;
        HydraWinTask task = workspaces.CreateTask(NextTaskName());

        pendingRenameTaskId = task.Id;
        Rebuild();
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
        pendingRenameTaskId = null;

        string name = task.Name.Trim();
        if (name.Length == 0)
        {
            // An empty name would leave an unclickable row; put the old one back.
            Rebuild();
            return;
        }

        workspaces.RenameTask(task.Id, name);
        UpdateTitle();
        Rebuild();
    }

    /// <summary>
    /// Deletes a task, un-hiding its windows first. Never closes a window.
    /// </summary>
    /// <remarks>
    /// Confirmation is asked for only when the task actually holds windows. The dialog exists to
    /// say what becomes of them; with none open it has nothing to tell the user and only costs
    /// them a keystroke.
    /// </remarks>
    [RelayCommand]
    public void DeleteTask(TaskViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        if (task.WindowCount > 0 && ConfirmDelete?.Invoke(task) == false)
        {
            return;
        }

        IReadOnlyList<WindowAssignment> orphaned = switchEngine.DeleteTask(task.Id);
        SyncHiddenFlags();
        Rebuild();

        Say(orphaned.Count == 0
            ? $"Deleted “{task.Name}”."
            : $"Deleted “{task.Name}”. {orphaned.Count} window(s) returned to Unassigned, "
                + "none closed.");
    }

    /// <summary>
    /// Deletes whichever task is currently switched to. Bound to the Del key.
    /// </summary>
    /// <remarks>
    /// The list has no selection — clicking a row switches to it — so the active task, which the
    /// accent border already makes unmistakable, is the one the key acts on.
    /// </remarks>
    [RelayCommand]
    public void DeleteActiveTask()
    {
        TaskViewModel? active = Tasks.FirstOrDefault(t => t.IsActive);
        if (active is null)
        {
            Say("No task is active — click one first, or use its right-click menu.");
            return;
        }

        DeleteTask(active);
    }

    /// <summary>Switches to a task: hides every other task's windows and restores this one's.</summary>
    /// <remarks>
    /// The keyboard deliberately stays with HydraWin. This is the click-in-the-panel path, so the
    /// user is still driving the panel — handing focus to the task's window would send their next
    /// key press, Del above all, to that app instead. The windows are raised, not activated. Task
    /// 08's hotkeys will switch <em>with</em> focus, because there the intent is the opposite.
    /// </remarks>
    [RelayCommand]
    public void SwitchTo(TaskViewModel? task)
    {
        if (task is not null)
        {
            switchEngine.SwitchTo(task.Id, focusTarget: false);
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

    /// <summary>Removes a window from its task or its pin; it stays visible in every task after.</summary>
    [RelayCommand]
    public void UnassignWindow(WindowViewModel? window)
    {
        if (window is null)
        {
            return;
        }

        bool wasPinned = workspaces.IsGlobal(window.Hwnd);
        Unhide(window);
        workspaces.UnassignWindow(window.Hwnd);

        Say(wasPinned
            ? $"“{window.DisplayTitle}” is no longer pinned."
            : $"“{window.DisplayTitle}” is unassigned and stays visible in every task.");
    }

    /// <summary>
    /// Pins a window to stay visible in every task, taking it out of whatever task held it.
    /// </summary>
    [RelayCommand]
    public void PinGlobal(WindowViewModel? window)
    {
        if (window is null || workspaces.IsGlobal(window.Hwnd))
        {
            return;
        }

        Unhide(window);
        workspaces.PinGlobal(window.Source);
        Say($"“{window.DisplayTitle}” is pinned and stays visible in every task.");
    }

    /// <summary>Whether a window is pinned always-visible.</summary>
    public bool IsPinned(WindowViewModel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return workspaces.IsGlobal(window.Hwnd);
    }

    /// <summary>Whether a window belongs to a task or a pin, rather than being loose.</summary>
    public bool IsAssigned(WindowViewModel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return workspaces.IsBound(window.Hwnd);
    }

    /// <summary>
    /// Brings a window back on screen before it leaves the switch machinery's care.
    /// </summary>
    /// <remarks>
    /// Both unassigning and pinning move a window somewhere no switch plan reaches. Doing that to
    /// a window that is currently hidden would strand it: nothing left would ever show it again.
    /// </remarks>
    private void Unhide(WindowViewModel window)
    {
        if (!hiddenWindows.Contains(window.Hwnd))
        {
            return;
        }

        switchEngine.ShowAllTasks();
        SyncHiddenFlags();
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

        FlushDeferredRebuild();
    }

    /// <summary>
    /// Runs the rebuild that was held back while a name was being typed.
    /// </summary>
    private void FlushDeferredRebuild()
    {
        if (rebuildDeferred && !Tasks.Any(t => t.IsRenaming))
        {
            Rebuild();
        }
    }

    /// <summary>Abandons an inline rename, putting the stored name back.</summary>
    public void CancelRename(TaskViewModel? task)
    {
        if (task is not null)
        {
            task.IsRenaming = false;
            pendingRenameTaskId = null;

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
        // A rebuild clears and re-adds every row, which regenerates the item containers and
        // destroys the open rename box — focus, caret and half-typed name with it. Windows appear
        // and disappear constantly, so without this a name could not be typed in peace. The
        // deferred rebuild runs the moment the rename is committed or abandoned.
        if (Tasks.Any(t => t.IsRenaming))
        {
            rebuildDeferred = true;
            return;
        }

        rebuildDeferred = false;

        // Transient row state has to survive the rebuild.
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
                IsRenaming = (known && previous.Renaming) || task.Id == pendingRenameTaskId,
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

        Globals.Clear();
        foreach (WindowAssignment pin in workspaces.GlobalWindows)
        {
            if (pin.BoundHwnd is nint hwnd
                && windowsByHwnd.TryGetValue(hwnd, out WindowViewModel? pinned))
            {
                pinned.IsUnmanageable = pin.Unmanageable;
                Globals.Add(pinned);
            }
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

        // The rows are new objects, so the badges have to be put back onto them.
        RefreshBadges();
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

        if (summary.Unmanageable > 0)
        {
            // The count alone leaves the user hunting for which window stayed put. Since task 07
            // keeps elevated windows out of the inventory entirely, anything landing here is a
            // surprise worth naming.
            Say("Still on screen because it refused to hide: " + NameUnmanageable() + ".");
        }
    }

    /// <summary>The windows currently marked unmanageable, for the status line.</summary>
    private string NameUnmanageable()
    {
        IEnumerable<string> names = Tasks
            .SelectMany(t => t.Windows)
            .Where(w => w.IsUnmanageable)
            .Select(w => $"“{w.DisplayTitle}”");

        string joined = string.Join(", ", names);
        return joined.Length == 0 ? "a window that has since closed" : joined;
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

        // A closed window can hardly still be waiting for attention.
        notifications.OnWindowDisappeared(window.Hwnd);
        Rebuild();
    }

    private void OnWindowTitleChanged(object? sender, WindowTitleChangedEventArgs e)
    {
        // The hot path. One dictionary lookup and one property set; no list is touched.
        if (windowsByHwnd.TryGetValue(e.Window.Hwnd, out WindowViewModel? window))
        {
            window.Title = e.NewTitle;
        }

        // Rules are edge-triggered and none ship enabled, so this is normally a no-op.
        notifications.OnTitleChanged(e.Window, e.OldTitle, e.NewTitle);
    }

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        switchEngine.OnForegroundChanged(hwnd);

        // Looking at a window is the only thing that clears its badge.
        notifications.OnForegroundChanged(hwnd);
    }

    /// <summary>
    /// A window's taskbar button flashed. Fed from the shell hook in the App layer.
    /// </summary>
    public void OnWindowFlashed(nint hwnd) =>
        notifications.OnFlash(windowsByHwnd.GetValueOrDefault(hwnd)?.Source);

    /// <summary>
    /// HydraWin's own window came to the front, so no foreign window is in front any more.
    /// </summary>
    /// <remarks>
    /// The tracker's WinEvent hook is registered with <c>WINEVENT_SKIPOWNPROCESS</c>, so it never
    /// reports HydraWin taking focus. Without this the hub would go on believing the last window
    /// the user visited was still foreground and would suppress its notifications for the rest of
    /// the session — focus a task's window once, come back here, and that window could never badge
    /// again.
    /// </remarks>
    public void OnManagerActivated() => notifications.OnForegroundChanged(0);

    /// <summary>How many windows are waiting to be looked at, across every task.</summary>
    public int TotalPendingNotifications => notifications.TotalPending;

    /// <summary>Raised when the total changes, for the tray tooltip and the optional balloon.</summary>
    public event EventHandler<string>? NotificationRaised;

    /// <summary>
    /// Switches to the task and focuses whichever of its windows most recently asked for attention.
    /// </summary>
    public void OpenNewestNotification(TaskViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        IReadOnlyList<PendingNotification> pending = notifications.PendingFor(task.Id);
        if (pending.Count == 0)
        {
            SwitchToCommand.Execute(task);
            return;
        }

        // User-initiated, so taking focus is legitimate — and it is also what clears the badge.
        switchEngine.SwitchToWindow(pending[0].Hwnd);
    }

    /// <summary>Pushes the hub's current state onto the rows.</summary>
    private void RefreshBadges()
    {
        int previousTotal = lastAnnouncedTotal;

        foreach (TaskViewModel task in Tasks)
        {
            IReadOnlyList<PendingNotification> pending = notifications.PendingFor(task.Id);
            task.NotificationCount = pending.Count;
            task.NotificationTooltip = pending.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, pending.Select(p => p.Label));

            foreach (WindowViewModel window in task.Windows)
            {
                window.NotificationLabel = notifications.LabelFor(window.Hwnd);
            }
        }

        lastAnnouncedTotal = notifications.TotalPending;

        if (lastAnnouncedTotal > previousTotal)
        {
            NotificationRaised?.Invoke(this, NewestLabel());
        }
    }

    private string NewestLabel()
    {
        return Tasks
            .Select(t => notifications.PendingFor(t.Id))
            .Where(p => p.Count > 0)
            .OrderByDescending(p => p[0].RaisedAt)
            .Select(p => p[0].Label)
            .FirstOrDefault() ?? string.Empty;
    }

    private void OnWindowReattached(object? sender, AssignmentChangedEventArgs e)
    {
        Rebuild();
        string window = e.Window?.Title ?? e.Assignment.Rule.TitlePattern;
        Say($"Re-attached “{window}” to “{e.Task.Name}”.");
    }

    /// <summary>
    /// The always-visible list changed. Only a rule re-claiming a reopened window is worth saying
    /// out loud; pinning and unpinning already say so from the command that did it.
    /// </summary>
    private void OnGlobalsChanged(object? sender, GlobalChangedEventArgs e)
    {
        Rebuild();

        if (e.Window is not null)
        {
            Say($"“{e.Window.Title}” is pinned — it stays visible in every task.");
        }
    }

    /// <summary>
    /// Puts a line in the status bar and in the log. One funnel, so anything worth telling the
    /// user is also worth having in the file when they come to ask what happened yesterday.
    /// </summary>
    private void Say(string message)
    {
        Status = $"{message}  ({DateTime.Now:HH:mm:ss})";
        log.Write(message);
    }
}
