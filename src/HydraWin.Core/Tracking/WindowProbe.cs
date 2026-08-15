using HydraWin.Core.Interop;

namespace HydraWin.Core.Tracking;

/// <summary>
/// Gathers a window's properties from Win32. This is the only impure step of the tracking
/// pipeline: it turns a handle into <see cref="WindowFacts"/>, after which
/// <see cref="WindowFilter"/> and <see cref="WindowSetDiff"/> work on plain data.
/// </summary>
internal static class WindowProbe
{
    /// <summary>Reads the properties the trackability filter needs.</summary>
    internal static WindowFacts GetFacts(nint hwnd, IHiddenWindowSet hiddenWindows)
    {
        RawWindowInfo info = NativeMethods.DescribeWindow(hwnd);
        return new WindowFacts(
            hwnd,
            info.Title,
            info.IsVisible,
            hiddenWindows.Contains(hwnd),
            info.ExtendedStyle,
            info.Owner,
            info.IsCloaked,
            info.Pid);
    }

    /// <summary>
    /// Builds the inventory entry for a window already judged trackable. The process path costs
    /// an <c>OpenProcess</c>, so it is read here rather than during filtering.
    /// </summary>
    internal static TrackedWindow CreateTrackedWindow(in WindowFacts facts) =>
        new()
        {
            Hwnd = facts.Hwnd,
            Pid = facts.Pid,
            ProcessPath = NativeMethods.GetProcessPath(facts.Pid),
            Title = facts.Title,
            IsHydraWinHidden = facts.IsHydraWinHidden,
        };
}
