namespace HydraWin.Core.Workspaces;

/// <summary>Why the user is not at the machine.</summary>
/// <remarks>
/// Named in Core so nothing above the interop seam has to know that "away" arrives as
/// <c>WTS_SESSION_LOCK</c> in a <c>wParam</c>. A screen saver is deliberately absent: Windows sends
/// no unsolicited message when one starts, so detecting it needs polling, which was offered and
/// declined. A screen saver set to show the logon screen on resume locks the session and is
/// therefore covered by <see cref="Locked"/>; one that does not, is not covered at all.
/// </remarks>
public enum AwayReason
{
    /// <summary>The session is locked.</summary>
    Locked,

    /// <summary>The machine is suspended.</summary>
    Suspended,
}

/// <summary>
/// Accumulates, per task, the time that task has spent as the active one.
/// </summary>
/// <remarks>
/// <para>
/// Driven by plain method calls and holding no Win32, no UI and no timer of its own — the same
/// shape as <see cref="Notifications.NotificationHub"/>, and for the same reason: every rule below
/// is then testable without a desktop. The App layer owns the tick that calls <see cref="Sample"/>.
/// </para>
/// <para>
/// <b>The running total lives in <see cref="HydraWinTask.ActiveSeconds"/>, not here.</b> There is
/// exactly one copy of the number, so it cannot drift from what gets written to <c>state.json</c>,
/// and a task that has been deleted resolves to <see langword="null"/> and drops its segment
/// without any bookkeeping. This class holds only the sub-second remainder and the position of the
/// segment in flight.
/// </para>
/// <para>
/// Not internally locked, matching <see cref="Notifications.NotificationHub"/>. Every call site is
/// inside <see cref="WorkspaceService"/>'s own gate, on the dispatcher thread.
/// </para>
/// </remarks>
public sealed class ActiveTimeLedger
{
    /// <summary>
    /// The most time a single <see cref="Sample"/> may credit.
    /// </summary>
    /// <remarks>
    /// This is what bounds the damage when the machine goes away without saying so — a
    /// battery-critical hibernate, a power cut, a dispatcher wedged for minutes. The next sample
    /// sees an enormous delta and credits this much instead. Comfortably above the App layer's
    /// one-minute tick so ordinary jitter is never clipped, and small enough that a night's sleep
    /// cannot invent hours.
    /// </remarks>
    public static readonly TimeSpan MaxCreditPerSample = TimeSpan.FromMinutes(2);

    private readonly Func<Guid, HydraWinTask?> findTask;
    private readonly TimeProvider clock;
    private readonly HashSet<AwayReason> away = [];
    private readonly Dictionary<Guid, long> carryMs = [];

    private long lastStamp;
    private Guid? active;

    /// <summary>Creates a ledger over the task model.</summary>
    /// <param name="findTask">
    /// Resolves a task id against the live model on each use rather than being captured, so a task
    /// deleted mid-segment simply stops resolving.
    /// </param>
    /// <param name="clock">
    /// Read through <see cref="TimeProvider.GetTimestamp"/>, which is monotonic. Wall-clock time
    /// would be wrong here: an NTP correction or a daylight-saving boundary would land straight in
    /// somebody's lifetime total.
    /// </param>
    public ActiveTimeLedger(Func<Guid, HydraWinTask?> findTask, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(findTask);
        ArgumentNullException.ThrowIfNull(clock);

        this.findTask = findTask;
        this.clock = clock;
        lastStamp = clock.GetTimestamp();
    }

    /// <summary>The task the clock is running on, or <see langword="null"/> when it is stopped.</summary>
    public Guid? RunningTaskId => away.Count == 0 ? active : null;

    /// <summary>Whether at least one away reason is in force.</summary>
    public bool IsAway => away.Count > 0;

    /// <summary>
    /// Records which task is active, or <see langword="null"/> for "everything visible".
    /// </summary>
    /// <remarks>
    /// Samples first, so the boundary is taken at this instant rather than rounded to the next
    /// tick. Re-selecting the task already running is then a no-op — which matters, because
    /// <see cref="SwitchEngine.SwitchTo"/> is idempotent and re-runs its whole body on every
    /// re-click.
    /// </remarks>
    public void SetActive(Guid? taskId)
    {
        Sample();
        active = taskId;
    }

    /// <summary>The user left. Returns whether this actually stopped the clock.</summary>
    /// <remarks>
    /// Reasons are a set rather than a count. A count goes negative on a resume that never had a
    /// suspend and sticks on a duplicate lock; a set makes both harmless, and correctly keeps the
    /// clock stopped when a machine that slept while locked wakes on a timer with nobody there.
    /// </remarks>
    public bool GoAway(AwayReason reason)
    {
        Sample();
        return away.Add(reason) && away.Count == 1;
    }

    /// <summary>The user is back. Returns whether this actually restarted the clock.</summary>
    public bool ComeBack(AwayReason reason)
    {
        Sample();
        return away.Remove(reason) && away.Count == 0;
    }

    /// <summary>
    /// Folds the time since the last sample into the running task's total, without closing the
    /// segment.
    /// </summary>
    public void Sample()
    {
        long now = clock.GetTimestamp();
        TimeSpan delta = clock.GetElapsedTime(lastStamp, now);
        lastStamp = now;

        // A clock that went backwards credits nothing rather than negative time, and the stamp has
        // already moved so the next sample is sane again.
        if (delta <= TimeSpan.Zero || RunningTaskId is not Guid id)
        {
            return;
        }

        // A task that no longer resolves has been deleted mid-segment. Its total went with it, so
        // the segment is dropped rather than parked in a remainder nobody will ever read.
        if (findTask(id) is not HydraWinTask task)
        {
            carryMs.Remove(id);
            return;
        }

        long pending = carryMs.GetValueOrDefault(id) + (long)Capped(delta).TotalMilliseconds;
        long whole = pending / 1000;

        if (whole > 0)
        {
            task.ActiveSeconds += whole;
            pending -= whole * 1000;
        }

        carryMs[id] = pending;
    }

    /// <summary>Drops a deleted task's remainder. Its total went with the task itself.</summary>
    public void Forget(Guid taskId) => carryMs.Remove(taskId);

    /// <summary>
    /// Zeroes a task's lifetime total. The clock keeps running on it if it was running.
    /// </summary>
    public void Reset(Guid taskId)
    {
        Sample();
        carryMs.Remove(taskId);

        if (findTask(taskId) is HydraWinTask task)
        {
            task.ActiveSeconds = 0;
        }
    }

    /// <summary>
    /// A task's lifetime total, including the remainder and the segment currently in flight.
    /// </summary>
    /// <remarks>
    /// A negative <c>ActiveSeconds</c> can only come from a hand-edit of <c>state.json</c>, which
    /// is an editable file by design. It reads as zero rather than as a negative duration.
    /// </remarks>
    public TimeSpan TotalFor(Guid taskId)
    {
        if (findTask(taskId) is not HydraWinTask task)
        {
            return TimeSpan.Zero;
        }

        long seconds = Math.Max(0, task.ActiveSeconds);
        long milliseconds = carryMs.GetValueOrDefault(taskId);

        if (RunningTaskId == taskId)
        {
            TimeSpan inFlight = clock.GetElapsedTime(lastStamp, clock.GetTimestamp());
            if (inFlight > TimeSpan.Zero)
            {
                milliseconds += (long)Capped(inFlight).TotalMilliseconds;
            }
        }

        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
    }

    private static TimeSpan Capped(TimeSpan elapsed) =>
        elapsed < MaxCreditPerSample ? elapsed : MaxCreditPerSample;
}
