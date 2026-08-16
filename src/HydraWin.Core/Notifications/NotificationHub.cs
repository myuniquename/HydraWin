using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Notifications;

/// <summary>A task's badge changed.</summary>
/// <param name="TaskId">The task.</param>
/// <param name="Count">How many of its windows are waiting to be looked at.</param>
/// <param name="TopLabel">The most recent one's label, for the tooltip. Empty when the count is 0.</param>
public readonly record struct TaskBadge(Guid TaskId, int Count, string TopLabel);

/// <summary>
/// Decides which windows are waiting for attention, and therefore which task rows show a badge.
/// </summary>
/// <remarks>
/// <para>
/// Driven by plain method calls rather than by subscribing to anything, and it holds no Win32 and
/// no UI. That is deliberate: every rule below — suppression, coalescing, the clearing matrix — is
/// then testable without a desktop. The App layer feeds it from <c>WindowTracker</c> and the shell
/// hook.
/// </para>
/// <para>
/// <b>The badge means "you have not looked at this window yet."</b> It clears when the window gains
/// focus, and at no other time — in particular <em>not</em> when the user switches to the task.
/// Task 09's § C says otherwise, but the warning at the top of that same file overrules it: Teams
/// flashes once per unread run, so clearing on a switch would drop the badge with no second flash
/// ever coming to raise it again. Applying that uniformly to every kind is also what keeps the
/// behaviour the same for applications nobody has tested against.
/// </para>
/// </remarks>
public sealed class NotificationHub
{
    private readonly WorkspaceService workspaces;
    private readonly Func<IReadOnlyList<NotificationRule>> rules;
    private readonly Dictionary<nint, PendingNotification> pending = [];

    /// <summary>Creates a hub over the task model.</summary>
    /// <param name="workspaces">Used to map a window to the task that owns it.</param>
    /// <param name="rules">
    /// Read on each title change rather than captured, so edits to <c>state.json</c> take effect
    /// without restarting.
    /// </param>
    public NotificationHub(
        WorkspaceService workspaces,
        Func<IReadOnlyList<NotificationRule>> rules)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(rules);

        this.workspaces = workspaces;
        this.rules = rules;
    }

    /// <summary>Raised whenever a task's badge count changes.</summary>
    public event EventHandler<TaskBadge>? TaskBadgeChanged;

    /// <summary>The window currently in the foreground, as last reported.</summary>
    /// <remarks>
    /// Tracked here rather than queried from Win32 so suppression can be tested without a desktop.
    /// </remarks>
    public nint ForegroundWindow { get; private set; }

    /// <summary>How many windows are waiting to be looked at, across every task.</summary>
    public int TotalPending => pending.Count;

    /// <summary>
    /// A window asked for attention — its taskbar button flashed.
    /// </summary>
    /// <remarks>
    /// The application-agnostic path: no rule, no process name, no regex. The label is built from
    /// the window itself, which is why an application nobody has ever configured still produces a
    /// readable badge.
    /// </remarks>
    public void OnFlash(TrackedWindow? window)
    {
        if (window is not null)
        {
            Raise(window, NotificationKind.Attention, Describe(window));
        }
    }

    /// <summary>A tracked window's title changed; offer it to the rules.</summary>
    public void OnTitleChanged(TrackedWindow window, string oldTitle, string newTitle)
    {
        ArgumentNullException.ThrowIfNull(window);

        // First match wins; order in state.json is the user's tie-break.
        NotificationRule? rule = rules()
            .FirstOrDefault(r => r.Matches(window.ProcessFileName, oldTitle, newTitle));

        if (rule is null)
        {
            return;
        }

        string label = string.IsNullOrEmpty(rule.Label)
            ? Describe(window)
            : $"{rule.Label} — {Describe(window)}";

        Raise(window, rule.Kind, label);
    }

    /// <summary>
    /// The foreground window changed. This is the only thing that clears a badge.
    /// </summary>
    public void OnForegroundChanged(nint hwnd)
    {
        ForegroundWindow = hwnd;
        Clear(hwnd);
    }

    /// <summary>A window closed; it can hardly still want attention.</summary>
    public void OnWindowDisappeared(nint hwnd) => Clear(hwnd);

    /// <summary>Everything waiting in a task, most recent first.</summary>
    public IReadOnlyList<PendingNotification> PendingFor(Guid taskId) =>
        [.. pending.Values
            .Where(p => workspaces.FindTaskOf(p.Hwnd)?.Id == taskId)
            .OrderByDescending(p => p.RaisedAt)];

    /// <summary>How many windows of a task are waiting.</summary>
    public int CountFor(Guid taskId) => PendingFor(taskId).Count;

    /// <summary>Whether this particular window is waiting to be looked at.</summary>
    public bool IsPending(nint hwnd) => pending.ContainsKey(hwnd);

    /// <summary>The label for a waiting window, or empty.</summary>
    public string LabelFor(nint hwnd) =>
        pending.TryGetValue(hwnd, out PendingNotification p) ? p.Label : string.Empty;

    private void Raise(TrackedWindow window, NotificationKind kind, string label)
    {
        nint hwnd = window.Hwnd;

        // Nothing to badge: an unassigned window belongs to no task, and is visible in all of them
        // anyway.
        HydraWinTask? task = workspaces.FindTaskOf(hwnd);
        if (task is null)
        {
            return;
        }

        // The user is looking straight at it.
        if (hwnd == ForegroundWindow)
        {
            return;
        }

        // Coalesced, not accumulated: a window flashing every second under FLASHW_TIMERNOFG must
        // count once, with its timestamp moved forward.
        pending[hwnd] = new PendingNotification(hwnd, kind, label, DateTimeOffset.UtcNow);
        Announce(task.Id);
    }

    private void Clear(nint hwnd)
    {
        if (!pending.Remove(hwnd))
        {
            return;
        }

        // Its task is looked up after removal, which still works: the binding is unchanged by
        // clearing. A window that has already been unbound announces nothing, which is correct —
        // its badge went with the binding.
        if (workspaces.FindTaskOf(hwnd) is HydraWinTask task)
        {
            Announce(task.Id);
        }
        else
        {
            AnnounceAll();
        }
    }

    private void Announce(Guid taskId)
    {
        IReadOnlyList<PendingNotification> forTask = PendingFor(taskId);
        TaskBadgeChanged?.Invoke(
            this,
            new TaskBadge(taskId, forTask.Count, forTask.Count == 0 ? string.Empty : forTask[0].Label));
    }

    /// <summary>
    /// Re-announces every task. Used when a cleared window can no longer be mapped to one, so the
    /// task that used to own it still gets its count corrected.
    /// </summary>
    private void AnnounceAll()
    {
        foreach (HydraWinTask task in workspaces.Tasks)
        {
            Announce(task.Id);
        }
    }

    /// <summary>
    /// How a window is described when no rule named it — process and title, which every window has.
    /// </summary>
    private static string Describe(TrackedWindow window)
    {
        string title = window.Title.Trim();
        string process = window.ProcessFileName;

        if (title.Length == 0)
        {
            return process.Length == 0 ? "A window" : process;
        }

        return process.Length == 0 ? title : $"{process} — {title}";
    }
}
