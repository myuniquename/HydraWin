using HydraWin.Core.Tracking;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// Which windows a switch must hide and which it must bring back.
/// </summary>
/// <param name="ToHide">Bound windows of every other task that are still visible.</param>
/// <param name="ToShow">Bound windows of the target task that HydraWin currently has hidden.</param>
public sealed record SwitchPlan(
    IReadOnlyList<WindowAssignment> ToHide,
    IReadOnlyList<WindowAssignment> ToShow)
{
    /// <summary>True when the switch has nothing to do.</summary>
    public bool IsEmpty => ToHide.Count == 0 && ToShow.Count == 0;

    /// <summary>
    /// Works out the two sets. Pure: no Win32, no journal, no persistence — the whole point is
    /// that "who gets hidden" is decidable and testable on its own.
    /// </summary>
    /// <param name="state">The task model.</param>
    /// <param name="targetTaskId">The task being switched to.</param>
    /// <param name="hiddenWindows">Which handles HydraWin currently has hidden.</param>
    /// <remarks>
    /// <para>
    /// Unassigned windows can never appear here — the plan is built from task assignments only,
    /// which is exactly why they stay visible through every switch.
    /// </para>
    /// <para>
    /// Assignments previously marked <see cref="WindowAssignment.Unmanageable"/> are still
    /// included: the flag records that a window refused once, not that it always will. Retrying
    /// costs one failed call per switch and means the app heals itself if the window's process is
    /// restarted without elevation.
    /// </para>
    /// </remarks>
    public static SwitchPlan Compute(
        WorkspaceState state,
        Guid targetTaskId,
        IHiddenWindowSet hiddenWindows)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(hiddenWindows);

        List<WindowAssignment> toHide = [];
        List<WindowAssignment> toShow = [];

        foreach (HydraWinTask task in state.Tasks)
        {
            bool isTarget = task.Id == targetTaskId;

            foreach (WindowAssignment assignment in task.Assignments)
            {
                if (assignment.BoundHwnd is not nint hwnd)
                {
                    continue;
                }

                bool hidden = hiddenWindows.Contains(hwnd);

                if (isTarget)
                {
                    if (hidden)
                    {
                        toShow.Add(assignment);
                    }
                }
                else if (!hidden)
                {
                    toHide.Add(assignment);
                }
            }
        }

        return new SwitchPlan(toHide, toShow);
    }
}
