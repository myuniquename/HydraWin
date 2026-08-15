using HydraWin.Core.Interop;

namespace HydraWin.Core.Tracking;

/// <summary>
/// Decides whether a window belongs in HydraWin's inventory. Pure: it takes
/// <see cref="WindowFacts"/> and touches no Win32, so every clause is unit-testable.
/// </summary>
public static class WindowFilter
{
    /// <summary>
    /// Evaluates the trackability clauses in order and returns the first one that fails, or
    /// <see cref="TrackableVerdict.Trackable"/>.
    /// </summary>
    /// <param name="facts">The window's gathered properties.</param>
    /// <param name="ownProcessId">HydraWin's own process id; its windows are never tracked.</param>
    public static TrackableVerdict Evaluate(in WindowFacts facts, int ownProcessId)
    {
        if (facts.Pid == ownProcessId)
        {
            return TrackableVerdict.OwnProcess;
        }

        if (string.IsNullOrEmpty(facts.Title))
        {
            return TrackableVerdict.NoTitle;
        }

        // Visibility alone cannot gate membership: a window HydraWin hid is still part of a task.
        if (!facts.IsVisible && !facts.IsHydraWinHidden)
        {
            return TrackableVerdict.NotVisible;
        }

        if ((facts.ExtendedStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return TrackableVerdict.ToolWindow;
        }

        if (facts.Owner != 0)
        {
            return TrackableVerdict.Owned;
        }

        // Cloaking normally marks a UWP ghost, but some packaged apps (Teams among them) report
        // cloaked precisely *because* HydraWin hid them. Dropping those would lose a window the
        // user still owns, so the hidden set exempts them here exactly as it does for visibility.
        if (facts.IsCloaked && !facts.IsHydraWinHidden)
        {
            return TrackableVerdict.Cloaked;
        }

        return TrackableVerdict.Trackable;
    }

    /// <summary>Convenience over <see cref="Evaluate"/> for callers that only need yes/no.</summary>
    public static bool IsTrackable(in WindowFacts facts, int ownProcessId) =>
        Evaluate(in facts, ownProcessId) == TrackableVerdict.Trackable;
}
