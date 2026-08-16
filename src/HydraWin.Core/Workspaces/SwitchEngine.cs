using HydraWin.Core.Interop;
using HydraWin.Core.Recovery;

namespace HydraWin.Core.Workspaces;

/// <summary>What a switch did.</summary>
/// <param name="Hidden">Windows hidden.</param>
/// <param name="Shown">Windows brought back.</param>
/// <param name="Stale">Bindings dropped because the window died while hidden.</param>
/// <param name="Unmanageable">Windows that refused to hide — in practice, elevated ones.</param>
public readonly record struct SwitchSummary(int Hidden, int Shown, int Stale, int Unmanageable)
{
    /// <summary>The line the UI puts in its status bar.</summary>
    public override string ToString()
    {
        string text = $"hidden {Hidden}, shown {Shown}";
        if (Stale > 0)
        {
            text += $", {Stale} stale";
        }

        return Unmanageable > 0 ? text + $", {Unmanageable} could not be hidden" : text;
    }
}

/// <summary>
/// Switches between tasks: hides every other task's windows and restores this one's, exactly
/// where they were.
/// </summary>
/// <remarks>
/// <para>
/// The order in <see cref="SwitchTo"/> is the project's one invariant made concrete: placements
/// are captured, the journal is written and flushed, and only then does anything get hidden. A
/// crash at any point leaves a journal that describes strictly more than what is actually hidden,
/// which recovery handles safely — the reverse would lose windows.
/// </para>
/// <para>
/// Unassigned windows are never touched, because <see cref="SwitchPlan"/> is built from task
/// assignments alone.
/// </para>
/// </remarks>
public sealed class SwitchEngine
{
    private readonly WorkspaceService workspaces;
    private readonly RecoveryJournal journal;
    private readonly RestoreService restoreService;
    private readonly IWindowApi windowApi;
    private readonly HiddenWindowSet hiddenWindows;

    /// <summary>Creates the engine.</summary>
    public SwitchEngine(
        WorkspaceService workspaces,
        RecoveryJournal journal,
        RestoreService restoreService,
        IWindowApi windowApi,
        HiddenWindowSet hiddenWindows)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(restoreService);
        ArgumentNullException.ThrowIfNull(windowApi);
        ArgumentNullException.ThrowIfNull(hiddenWindows);

        this.workspaces = workspaces;
        this.journal = journal;
        this.restoreService = restoreService;
        this.windowApi = windowApi;
        this.hiddenWindows = hiddenWindows;
    }

    /// <summary>Raised after a switch, for the status bar and the log.</summary>
    public event EventHandler<SwitchSummary>? SwitchCompleted;

    /// <summary>
    /// Test hook fired immediately after the journal flush and before the first hide. The task 06
    /// harness wires it to a hard process kill to prove the invariant survives the worst
    /// interleaving.
    /// </summary>
    public Action? AfterJournalFlush { get; set; }

    /// <summary>
    /// Hides every other task's windows and brings this task's back. Idempotent: switching to the
    /// task that is already active just re-asserts visibility and focus.
    /// </summary>
    public SwitchSummary SwitchTo(Guid taskId)
    {
        HydraWinTask? target = workspaces.FindTask(taskId);
        if (target is null)
        {
            return default;
        }

        SwitchPlan plan = SwitchPlan.Compute(workspaces.State, taskId, hiddenWindows);

        (int hiddenCount, int unmanageable) = HideAll(plan.ToHide);
        (int shown, int stale) = ShowAll(plan.ToShow);

        FocusTarget(target, plan.ToShow);

        workspaces.SetActiveTask(taskId);

        var summary = new SwitchSummary(hiddenCount, shown, stale, unmanageable);
        SwitchCompleted?.Invoke(this, summary);
        return summary;
    }

    /// <summary>
    /// Brings back everything HydraWin has hidden and clears the active task. Used by the "show
    /// all" command and before a task is deleted.
    /// </summary>
    public RestoreSummary ShowAllTasks()
    {
        RestoreSummary summary = restoreService.RestoreAll(journal);
        hiddenWindows.ResetFrom(journal);
        workspaces.SetActiveTask(null);
        return summary;
    }

    /// <summary>
    /// Deletes a task, showing any of its windows that are hidden <em>first</em>. Deletion never
    /// closes a window: the user gets them all back, unassigned.
    /// </summary>
    public IReadOnlyList<WindowAssignment> DeleteTask(Guid taskId)
    {
        HydraWinTask? task = workspaces.FindTask(taskId);
        if (task is null)
        {
            return [];
        }

        ShowAll([.. task.BoundAssignments.Where(a => a.BoundHwnd is nint h && hiddenWindows.Contains(h))]);

        return workspaces.DeleteTask(taskId);
    }

    /// <summary>
    /// Records the foreground window against its task, so a later switch back restores focus to
    /// wherever the user left it. Wire this to the tracker's foreground event.
    /// </summary>
    public void OnForegroundChanged(nint hwnd) => workspaces.NoteForegroundWindow(hwnd);

    /// <summary>
    /// Forgets a window that has closed. Wire this to the tracker's disappeared event.
    /// </summary>
    /// <remarks>
    /// A window that dies <em>while hidden</em> is unbound by the tracker before any switch can
    /// notice, so its journal entry would otherwise be orphaned: no assignment refers to it any
    /// more, and nothing short of a full <c>RestoreAll</c> would clear it. Until then the handle
    /// stays in the hidden set, and if Windows recycled it onto a new window the tracker would
    /// treat that one as hidden. <see cref="RecoveryJournal.ConfirmShown"/> only writes when it
    /// actually removed something, so the common case — an unassigned window closing — costs
    /// nothing.
    /// </remarks>
    public void OnWindowDisappeared(nint hwnd)
    {
        journal.ConfirmShown(hwnd);
        hiddenWindows.MarkShown(hwnd);
    }

    /// <summary>
    /// Journals the windows, flushes, and only then hides them.
    /// </summary>
    /// <returns>
    /// How many were actually hidden, and how many refused. The two need not add up to the input:
    /// a window whose placement could not be read is skipped entirely and counted as neither.
    /// </returns>
    private (int Hidden, int Unmanageable) HideAll(IReadOnlyList<WindowAssignment> toHide)
    {
        if (toHide.Count == 0)
        {
            return (0, 0);
        }

        List<JournalEntry> entries = [];
        List<WindowAssignment> hideable = [];

        foreach (WindowAssignment assignment in toHide)
        {
            if (assignment.BoundHwnd is not nint hwnd)
            {
                continue;
            }

            // A window whose placement cannot be read could never be put back where it was, so it
            // does not get hidden at all. Better a window that stays visible than one that
            // reappears somewhere unexpected.
            if (!windowApi.TryGetPlacement(hwnd, out WindowPlacement placement))
            {
                continue;
            }

            int pid = windowApi.GetProcessId(hwnd);
            entries.Add(new JournalEntry
            {
                Hwnd = hwnd,
                Pid = pid,
                ProcessPath = windowApi.GetProcessPath(pid),
                TitleAtHide = windowApi.GetWindowTitle(hwnd),
                Placement = WindowPlacementDto.FromPlacement(in placement),
                HiddenAt = DateTimeOffset.UtcNow,
            });
            hideable.Add(assignment);
        }

        if (entries.Count == 0)
        {
            return (0, 0);
        }

        // THE INVARIANT. This returns only once the entries are on disk.
        journal.RecordBeforeHide(entries);

        AfterJournalFlush?.Invoke();

        int hiddenCount = 0;
        int unmanageable = 0;
        foreach (WindowAssignment assignment in hideable)
        {
            nint hwnd = assignment.BoundHwnd!.Value;
            ShowWindowResult result = windowApi.Hide(hwnd);

            if (result.Succeeded)
            {
                assignment.Unmanageable = false;
                hiddenWindows.MarkHidden(hwnd);
                hiddenCount++;
                continue;
            }

            // It is still visible, so its journal entry must go: an entry for a window that is not
            // hidden would have recovery "restore" something that never moved, and would quietly
            // misrepresent the invariant.
            journal.ConfirmShown(hwnd);
            assignment.Unmanageable = true;
            unmanageable++;
        }

        return (hiddenCount, unmanageable);
    }

    /// <summary>Restores windows through the same identity-validating path as crash recovery.</summary>
    private (int Shown, int Stale) ShowAll(IReadOnlyList<WindowAssignment> toShow)
    {
        if (toShow.Count == 0)
        {
            return (0, 0);
        }

        Dictionary<long, JournalEntry> byHandle = journal.Snapshot().ToDictionary(e => e.Hwnd);
        int shown = 0;
        int stale = 0;

        foreach (WindowAssignment assignment in toShow)
        {
            if (assignment.BoundHwnd is not nint hwnd)
            {
                continue;
            }

            if (!byHandle.TryGetValue(hwnd, out JournalEntry? entry))
            {
                // Nothing on the books, so it is not actually hidden.
                hiddenWindows.MarkShown(hwnd);
                continue;
            }

            RestoreOutcome outcome = restoreService.RestoreWindow(journal, entry);
            switch (outcome)
            {
                case RestoreOutcome.Restored:
                    hiddenWindows.MarkShown(hwnd);
                    shown++;
                    break;

                case RestoreOutcome.Stale:
                    // The window died while hidden. Drop the binding but keep the rule, so it
                    // re-attaches if the user reopens it.
                    hiddenWindows.MarkShown(hwnd);
                    workspaces.OnWindowDisappeared(hwnd);
                    stale++;
                    break;

                default:
                    // Still hidden and still ours; its entry stays for a later attempt.
                    break;
            }
        }

        return (shown, stale);
    }

    /// <summary>
    /// Focuses the task's last-active window, falling back to one it just showed.
    /// </summary>
    private void FocusTarget(HydraWinTask target, IReadOnlyList<WindowAssignment> shown)
    {
        if (target.LastActiveHwnd is nint last && windowApi.TryFocus(last))
        {
            return;
        }

        foreach (WindowAssignment assignment in shown)
        {
            if (assignment.BoundHwnd is nint hwnd && windowApi.TryFocus(hwnd))
            {
                return;
            }
        }

        foreach (WindowAssignment assignment in target.BoundAssignments)
        {
            if (assignment.BoundHwnd is nint hwnd && windowApi.TryFocus(hwnd))
            {
                return;
            }
        }
    }
}
