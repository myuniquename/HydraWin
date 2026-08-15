using HydraWin.Core.Tracking;

namespace HydraWin.Core.Workspaces;

/// <summary>A rule that recognised a window, and the task it belongs to.</summary>
/// <param name="Task">The owning task.</param>
/// <param name="Assignment">The assignment whose rule matched.</param>
public sealed record RuleMatch(HydraWinTask Task, WindowAssignment Assignment);

/// <summary>
/// Finds the task a (re)appearing window should re-attach to. Pure: no Win32, no persistence.
/// </summary>
public static class RuleMatcher
{
    /// <summary>
    /// Returns the first task, by <see cref="HydraWinTask.Order"/>, holding an unbound rule that
    /// recognises this window — or <see langword="null"/> when the window stays unassigned.
    /// </summary>
    /// <remarks>
    /// Two binding rules, both deliberate:
    /// <list type="bullet">
    ///   <item>a rule binds at most one window at a time, so assignments that already hold a
    ///     window are skipped — a second matching window stays unassigned for the user to drag
    ///     rather than silently displacing the first;</item>
    ///   <item>a window that is already bound is never rebound, which is the caller's
    ///     responsibility to check via <see cref="WorkspaceService.IsBound"/>.</item>
    /// </list>
    /// </remarks>
    public static RuleMatch? FindTask(WorkspaceState state, TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(window);

        string processFileName = window.ProcessFileName;
        if (string.IsNullOrEmpty(processFileName))
        {
            // A protected process HydraWin cannot identify; nothing durable to match on.
            return null;
        }

        foreach (HydraWinTask task in state.OrderedTasks)
        {
            foreach (WindowAssignment assignment in task.Assignments)
            {
                if (!assignment.IsBound && assignment.Rule.Matches(processFileName, window.Title))
                {
                    return new RuleMatch(task, assignment);
                }
            }
        }

        return null;
    }
}
