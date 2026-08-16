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

    /// <summary>The live model. Treat as read-only outside this service.</summary>
    public WorkspaceState State { get; }

    /// <summary>Tasks in display order.</summary>
    public IReadOnlyList<HydraWinTask> Tasks => [.. State.OrderedTasks];

    /// <summary>Whether a window is currently bound to any task. O(1) — task 07 calls it per window.</summary>
    public bool IsBound(nint hwnd)
    {
        lock (gate)
        {
            return assignmentByHwnd.ContainsKey(hwnd);
        }
    }

    /// <summary>The task a bound window belongs to, or <see langword="null"/>.</summary>
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
    public WindowAssignment? AssignWindow(Guid taskId, TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        WindowAssignment assignment;
        HydraWinTask task;

        lock (gate)
        {
            HydraWinTask? target = State.Tasks.Find(t => t.Id == taskId);
            if (target is null)
            {
                return null;
            }

            task = target;
            RemoveBindingLocked(window.Hwnd);

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
        Raise(WindowAssigned, new AssignmentChangedEventArgs(task, assignment, window));
        return assignment;
    }

    /// <summary>Removes a window's assignment entirely — the rule goes with it.</summary>
    public void UnassignWindow(nint hwnd)
    {
        HydraWinTask? task;
        WindowAssignment? assignment;

        lock (gate)
        {
            (task, assignment) = RemoveBindingLocked(hwnd);
            if (task is null || assignment is null)
            {
                return;
            }

            task.Assignments.Remove(assignment);
            taskByAssignmentId.Remove(assignment.Id);
        }

        Persist();
        Raise(WindowUnassigned, new AssignmentChangedEventArgs(task, assignment, null));
    }

    /// <summary>
    /// Offers a newly seen window to the rules; binds it to the first task that recognises it.
    /// </summary>
    public void OnWindowAppeared(TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        RuleMatch? match;
        lock (gate)
        {
            if (assignmentByHwnd.ContainsKey(window.Hwnd))
            {
                return;
            }

            match = RuleMatcher.FindTask(State, window);
            if (match is null)
            {
                return;
            }

            match.Assignment.BoundHwnd = window.Hwnd;
            assignmentByHwnd[window.Hwnd] = match.Assignment;
        }

        // The binding itself is runtime-only, but re-attaching does not change the document, so
        // there is nothing to persist here.
        Raise(WindowReattached, new AssignmentChangedEventArgs(match.Task, match.Assignment, window));
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

        if (task is not null && assignment is not null)
        {
            Raise(WindowUnassigned, new AssignmentChangedEventArgs(task, assignment, null));
        }
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

    /// <summary>Forces any pending write to disk. Call on shutdown.</summary>
    public void Flush() => store.Flush();

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
