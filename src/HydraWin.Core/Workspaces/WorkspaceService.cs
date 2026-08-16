using HydraWin.Core.Persistence;
using HydraWin.Core.Tracking;

namespace HydraWin.Core.Workspaces;

/// <summary>Payload for the assignment change events.</summary>
/// <param name="Task">The task involved.</param>
/// <param name="Assignment">The assignment involved.</param>
/// <param name="Window">The live window, when there is one.</param>
public sealed record AssignmentChangedEventArgs(
    HydraWinTask Task,
    WindowAssignment Assignment,
    TrackedWindow? Window);

/// <summary>Payload for the always-visible list changing.</summary>
/// <param name="Assignment">The assignment pinned or re-bound.</param>
/// <param name="Window">The live window, when there is one.</param>
/// <remarks>
/// Separate from <see cref="AssignmentChangedEventArgs"/> because a global window belongs to no
/// task, and inventing one to fill the field would put something in
/// <see cref="WorkspaceState.Tasks"/> that must never be there.
/// </remarks>
public sealed record GlobalChangedEventArgs(
    WindowAssignment Assignment,
    TrackedWindow? Window);

/// <summary>
/// Owns the task model: creating and deleting tasks, assigning windows to them, and re-binding
/// windows to their task when they reappear. UI-free, and the only writer of <c>state.json</c>.
/// </summary>
/// <remarks>
/// Events are raised on the <see cref="SynchronizationContext"/> captured at construction, the
/// same contract <see cref="WindowTracker"/> honours, so WPF can bind to them directly.
/// </remarks>
public sealed class WorkspaceService
{
    private static readonly string[] DefaultColors =
    [
        "#4C8DFF", "#FF8A4C", "#4CC38A", "#C56BFF", "#FFC24C", "#FF6B8A", "#4CD4E0", "#9AA5B1",
    ];

    private readonly WorkspaceStore store;
    private readonly Dictionary<nint, WindowAssignment> assignmentByHwnd = [];
    private readonly Dictionary<Guid, HydraWinTask> taskByAssignmentId = [];
    private readonly SynchronizationContext? context;
    private readonly Lock gate = new();

    /// <summary>Loads the persisted state and indexes it.</summary>
    public WorkspaceService(WorkspaceStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
        context = SynchronizationContext.Current;
        State = store.Load();
        Reindex();
    }

    /// <summary>A task was created, renamed, deleted or reordered.</summary>
    public event EventHandler? TasksChanged;

    /// <summary>A window was assigned to a task by the user.</summary>
    public event EventHandler<AssignmentChangedEventArgs>? WindowAssigned;

    /// <summary>A window was unassigned, or its binding dropped because the window went away.</summary>
    public event EventHandler<AssignmentChangedEventArgs>? WindowUnassigned;

    /// <summary>
    /// A reappearing window was re-bound to its task by a rule. Carries both sides so the UI can
    /// say which window rejoined which task.
    /// </summary>
    public event EventHandler<AssignmentChangedEventArgs>? WindowReattached;

    /// <summary>A window was pinned as always-visible, or re-bound to an existing pin by a rule.</summary>
    public event EventHandler<GlobalChangedEventArgs>? GlobalsChanged;

    /// <summary>The live model. Treat as read-only outside this service.</summary>
    public WorkspaceState State { get; }

    /// <summary>Tasks in display order.</summary>
    public IReadOnlyList<HydraWinTask> Tasks => [.. State.OrderedTasks];

    /// <summary>The always-visible windows, which no switch ever hides.</summary>
    public IReadOnlyList<WindowAssignment> GlobalWindows => [.. State.GlobalWindows];

    /// <summary>Whether a window is currently bound to any task. O(1) — task 07 calls it per window.</summary>
    public bool IsBound(nint hwnd)
    {
        lock (gate)
        {
            return assignmentByHwnd.ContainsKey(hwnd);
        }
    }

    /// <summary>
    /// The task a bound window belongs to, or <see langword="null"/> — which is also the answer
    /// for an always-visible window, because it belongs to no task by construction.
    /// </summary>
    public HydraWinTask? FindTaskOf(nint hwnd)
    {
        lock (gate)
        {
            return assignmentByHwnd.TryGetValue(hwnd, out WindowAssignment? assignment)
                && taskByAssignmentId.TryGetValue(assignment.Id, out HydraWinTask? task)
                ? task
                : null;
        }
    }

    /// <summary>Whether a window is pinned as always-visible.</summary>
    public bool IsGlobal(nint hwnd)
    {
        lock (gate)
        {
            return assignmentByHwnd.TryGetValue(hwnd, out WindowAssignment? assignment)
                && State.GlobalWindows.Contains(assignment);
        }
    }

    /// <summary>
    /// Pins a live window as always-visible, taking it out of whatever task held it.
    /// </summary>
    /// <remarks>
    /// The caller must make sure the window is actually on screen first: a hidden window pinned
    /// here would be stranded, because a global window is in no switch plan and so nothing would
    /// ever show it again. That is the same trap <see cref="UnassignWindow"/> has, and the UI
    /// applies the same guard to both.
    /// </remarks>
    public WindowAssignment PinGlobal(TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        WindowAssignment assignment;
        HydraWinTask? previousTask;
        WindowAssignment? previousAssignment;

        lock (gate)
        {
            (previousTask, previousAssignment) = RemoveAssignmentLocked(window.Hwnd);

            assignment = new WindowAssignment
            {
                Rule = ReattachRule.FromWindow(window.ProcessPath, window.Title),
                BoundHwnd = window.Hwnd,
            };

            State.GlobalWindows.Add(assignment);
            assignmentByHwnd[window.Hwnd] = assignment;
        }

        Persist();

        if (previousTask is not null && previousAssignment is not null)
        {
            Raise(
                WindowUnassigned,
                new AssignmentChangedEventArgs(previousTask, previousAssignment, null));
        }

        Raise(GlobalsChanged, new GlobalChangedEventArgs(assignment, window));
        return assignment;
    }

    /// <summary>Creates a task at the end of the list.</summary>
    public HydraWinTask CreateTask(string name)
    {
        HydraWinTask task;
        lock (gate)
        {
            int order = State.Tasks.Count == 0 ? 1 : State.Tasks.Max(t => t.Order) + 1;
            task = new HydraWinTask
            {
                Name = name,
                Order = order,
                ColorHex = DefaultColors[(order - 1) % DefaultColors.Length],
            };
            State.Tasks.Add(task);
        }

        Persist();
        Raise(TasksChanged);
        return task;
    }

    /// <summary>Renames a task. Unknown ids are ignored.</summary>
    public void RenameTask(Guid taskId, string name)
    {
        lock (gate)
        {
            HydraWinTask? task = State.Tasks.Find(t => t.Id == taskId);
            if (task is null)
            {
                return;
            }

            task.Name = name;
        }

        Persist();
        Raise(TasksChanged);
    }

    /// <summary>
    /// Deletes a task and returns its assignments so the caller can un-hide and unassign their
    /// windows. Deletion never closes a window.
    /// </summary>
    public IReadOnlyList<WindowAssignment> DeleteTask(Guid taskId)
    {
        List<WindowAssignment> orphaned;
        lock (gate)
        {
            HydraWinTask? task = State.Tasks.Find(t => t.Id == taskId);
            if (task is null)
            {
                return [];
            }

            orphaned = [.. task.Assignments];
            State.Tasks.Remove(task);

            foreach (WindowAssignment assignment in orphaned)
            {
                taskByAssignmentId.Remove(assignment.Id);
                if (assignment.BoundHwnd is nint hwnd)
                {
                    assignmentByHwnd.Remove(hwnd);
                }
            }

            if (State.ActiveTaskId == taskId)
            {
                State.ActiveTaskId = null;
            }
        }

        Persist();
        Raise(TasksChanged);
        return orphaned;
    }

    /// <summary>
    /// Assigns a live window to a task, creating its re-attach rule. A window already assigned
    /// elsewhere is moved rather than duplicated.
    /// </summary>
    /// <remarks>
    /// "Moved" has to mean the old assignment is <em>removed</em>, not merely unbound. Unbinding
    /// alone would leave the previous task holding a rule that still recognises this window, and
    /// after the next restart both tasks would claim it — whichever re-attached first would win.
    /// This is the <see cref="UnassignWindow"/> path, deliberately not the
    /// <see cref="OnWindowDisappeared"/> one, which keeps the rule on purpose.
    /// </remarks>
    public WindowAssignment? AssignWindow(Guid taskId, TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        WindowAssignment assignment;
        HydraWinTask task;
        HydraWinTask? previousTask;
        WindowAssignment? previousAssignment;

        lock (gate)
        {
            HydraWinTask? target = State.Tasks.Find(t => t.Id == taskId);
            if (target is null)
            {
                return null;
            }

            task = target;
            (previousTask, previousAssignment) = RemoveAssignmentLocked(window.Hwnd);

            assignment = new WindowAssignment
            {
                Rule = ReattachRule.FromWindow(window.ProcessPath, window.Title),
                BoundHwnd = window.Hwnd,
            };

            task.Assignments.Add(assignment);
            taskByAssignmentId[assignment.Id] = task;
            assignmentByHwnd[window.Hwnd] = assignment;
        }

        Persist();

        // The move's two halves are reported separately so the UI can drop the old row and add the
        // new one without needing to know that a move happened.
        if (previousAssignment is not null)
        {
            if (previousTask is null)
            {
                // It was pinned always-visible; the pin is gone now.
                Raise(GlobalsChanged, new GlobalChangedEventArgs(previousAssignment, null));
            }
            else if (previousTask != task)
            {
                Raise(
                    WindowUnassigned,
                    new AssignmentChangedEventArgs(previousTask, previousAssignment, null));
            }
        }

        Raise(WindowAssigned, new AssignmentChangedEventArgs(task, assignment, window));
        return assignment;
    }

    /// <summary>
    /// Moves a task to a new position and renumbers every <see cref="HydraWinTask.Order"/> from 1.
    /// Positions outside the list are clamped; an unknown id is ignored.
    /// </summary>
    /// <remarks>
    /// <see cref="HydraWinTask.Order"/> is load-bearing beyond display — task 08 binds
    /// <c>Ctrl+Alt+1..9</c> to it — so this renumbers the whole list rather than leaving gaps.
    /// </remarks>
    public void ReorderTask(Guid taskId, int newIndex)
    {
        lock (gate)
        {
            List<HydraWinTask> ordered = [.. State.OrderedTasks];
            int current = ordered.FindIndex(t => t.Id == taskId);
            if (current < 0)
            {
                return;
            }

            int target = Math.Clamp(newIndex, 0, ordered.Count - 1);
            if (target == current)
            {
                return;
            }

            HydraWinTask task = ordered[current];
            ordered.RemoveAt(current);
            ordered.Insert(target, task);

            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i + 1;
            }
        }

        Persist();
        Raise(TasksChanged);
    }

    /// <summary>
    /// Removes a window's assignment entirely — the rule goes with it. Unpins an always-visible
    /// window too; from the user's side both are "this window is no longer part of anything".
    /// </summary>
    public void UnassignWindow(nint hwnd)
    {
        HydraWinTask? task;
        WindowAssignment? assignment;

        lock (gate)
        {
            (task, assignment) = RemoveAssignmentLocked(hwnd);
            if (assignment is null)
            {
                return;
            }
        }

        Persist();

        if (task is null)
        {
            Raise(GlobalsChanged, new GlobalChangedEventArgs(assignment, null));
            return;
        }

        Raise(WindowUnassigned, new AssignmentChangedEventArgs(task, assignment, null));
    }

    /// <summary>
    /// Offers a newly seen window to the rules; binds it to the always-visible list, or failing
    /// that to the first task that recognises it.
    /// </summary>
    /// <remarks>
    /// The always-visible list is consulted first on purpose. A window the user pinned to stay on
    /// screen must not be claimed after a restart by some task whose rule happens to match it too
    /// — that would hide it at the next switch, which is the exact opposite of what pinning means.
    /// </remarks>
    public void OnWindowAppeared(TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        RuleMatch? match;
        WindowAssignment? global;

        lock (gate)
        {
            if (assignmentByHwnd.ContainsKey(window.Hwnd))
            {
                return;
            }

            global = RuleMatcher.FindGlobal(State, window);
            if (global is not null)
            {
                global.BoundHwnd = window.Hwnd;
                assignmentByHwnd[window.Hwnd] = global;
                match = null;
            }
            else
            {
                match = RuleMatcher.FindTask(State, window);
                if (match is null)
                {
                    return;
                }

                match.Assignment.BoundHwnd = window.Hwnd;
                assignmentByHwnd[window.Hwnd] = match.Assignment;
            }
        }

        // The binding itself is runtime-only, but re-attaching does not change the document, so
        // there is nothing to persist here.
        if (global is not null)
        {
            Raise(GlobalsChanged, new GlobalChangedEventArgs(global, window));
            return;
        }

        Raise(WindowReattached, new AssignmentChangedEventArgs(match!.Task, match.Assignment, window));
    }

    /// <summary>
    /// Drops a window's binding while keeping its rule, so it re-attaches when it comes back.
    /// Task 06 also calls this for a window that died while hidden.
    /// </summary>
    public void OnWindowDisappeared(nint hwnd)
    {
        HydraWinTask? task;
        WindowAssignment? assignment;

        lock (gate)
        {
            (task, assignment) = RemoveBindingLocked(hwnd);
        }

        if (assignment is null)
        {
            return;
        }

        if (task is null)
        {
            Raise(GlobalsChanged, new GlobalChangedEventArgs(assignment, null));
            return;
        }

        Raise(WindowUnassigned, new AssignmentChangedEventArgs(task, assignment, null));
    }

    /// <summary>The task with this id, or <see langword="null"/>.</summary>
    public HydraWinTask? FindTask(Guid taskId)
    {
        lock (gate)
        {
            return State.Tasks.Find(t => t.Id == taskId);
        }
    }

    /// <summary>
    /// Records which task is switched to, or <see langword="null"/> for "everything visible".
    /// Written by the switch engine.
    /// </summary>
    public void SetActiveTask(Guid? taskId)
    {
        lock (gate)
        {
            State.ActiveTaskId = taskId;
        }

        Persist();
        Raise(TasksChanged);
    }

    /// <summary>
    /// Remembers the window the user was last working in, so switching back can restore focus
    /// there. Ignores windows that belong to no task.
    /// </summary>
    public void NoteForegroundWindow(nint hwnd)
    {
        HydraWinTask? task = FindTaskOf(hwnd);
        if (task is not null)
        {
            task.LastActiveHwnd = hwnd;
        }
    }

    /// <summary>
    /// Edits an assignment's re-attach rule in place. The live binding is untouched: the rule says
    /// how to recognise the window <em>next</em> time, and re-deciding the current one would rip a
    /// window out of the task the user is looking at.
    /// </summary>
    /// <returns>Whether an assignment with that id was found.</returns>
    public bool UpdateRule(Guid assignmentId, Action<ReattachRule> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (gate)
        {
            WindowAssignment? assignment = FindAssignmentLocked(assignmentId);
            if (assignment is null)
            {
                return false;
            }

            change(assignment.Rule);
        }

        Persist();
        return true;
    }

    /// <summary>The assignment with this id, wherever it lives.</summary>
    public WindowAssignment? FindAssignment(Guid assignmentId)
    {
        lock (gate)
        {
            return FindAssignmentLocked(assignmentId);
        }
    }

    /// <summary>The assignment a live window is bound to, in a task or in the always-visible list.</summary>
    public WindowAssignment? FindAssignmentOf(nint hwnd)
    {
        lock (gate)
        {
            return assignmentByHwnd.GetValueOrDefault(hwnd);
        }
    }

    /// <summary>Applies a change to the user preferences and persists it.</summary>
    public void UpdateSettings(Action<SettingsModel> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        lock (gate)
        {
            change(State.Settings);
        }

        Persist();
    }

    /// <summary>Forces any pending write to disk. Call on shutdown.</summary>
    public void Flush() => store.Flush();

    /// <summary>
    /// Removes a window's assignment outright — binding, rule and all — so nothing is left to
    /// re-claim it later.
    /// </summary>
    /// <returns>
    /// The assignment that was removed, or <see langword="null"/> when the window held none. A
    /// non-null assignment with a <see langword="null"/> task is an always-visible pin, which by
    /// construction belongs to no task.
    /// </returns>
    private (HydraWinTask? Task, WindowAssignment? Assignment) RemoveAssignmentLocked(nint hwnd)
    {
        (HydraWinTask? task, WindowAssignment? assignment) = RemoveBindingLocked(hwnd);
        if (assignment is null)
        {
            return (null, null);
        }

        if (task is null)
        {
            // The pin itself has to go, not just its binding: leaving the rule behind would have
            // the window silently re-pin itself the next time it appears.
            State.GlobalWindows.Remove(assignment);
            return (null, assignment);
        }

        task.Assignments.Remove(assignment);
        taskByAssignmentId.Remove(assignment.Id);
        return (task, assignment);
    }

    private WindowAssignment? FindAssignmentLocked(Guid assignmentId)
    {
        foreach (HydraWinTask task in State.Tasks)
        {
            WindowAssignment? found = task.Assignments.Find(a => a.Id == assignmentId);
            if (found is not null)
            {
                return found;
            }
        }

        return State.GlobalWindows.Find(a => a.Id == assignmentId);
    }

    /// <summary>Unbinds a window, leaving its assignment and rule in place.</summary>
    private (HydraWinTask? Task, WindowAssignment? Assignment) RemoveBindingLocked(nint hwnd)
    {
        if (!assignmentByHwnd.Remove(hwnd, out WindowAssignment? assignment))
        {
            return (null, null);
        }

        assignment.BoundHwnd = null;
        taskByAssignmentId.TryGetValue(assignment.Id, out HydraWinTask? task);
        return (task, assignment);
    }

    private void Reindex()
    {
        taskByAssignmentId.Clear();
        assignmentByHwnd.Clear();

        foreach (HydraWinTask task in State.Tasks)
        {
            foreach (WindowAssignment assignment in task.Assignments)
            {
                taskByAssignmentId[assignment.Id] = task;

                // BoundHwnd is [JsonIgnore], so nothing is bound immediately after a load.
                assignment.BoundHwnd = null;
            }
        }

        // Global pins deliberately get no taskByAssignmentId entry: "belongs to no task" is what
        // FindTaskOf reports for them, and what keeps them out of every switch plan.
        foreach (WindowAssignment assignment in State.GlobalWindows)
        {
            assignment.BoundHwnd = null;
        }
    }

    private void Persist() => store.SaveDebounced(State);

    private void Raise(EventHandler? handler)
    {
        if (handler is not null)
        {
            Post(() => handler(this, EventArgs.Empty));
        }
    }

    private void Raise<T>(EventHandler<T>? handler, T payload)
    {
        if (handler is not null)
        {
            Post(() => handler(this, payload));
        }
    }

    private void Post(Action action)
    {
        if (context is null || context == SynchronizationContext.Current)
        {
            action();
            return;
        }

        context.Post(state => ((Action)state!)(), action);
    }
}
