using HydraWin.Core.Interop;

namespace HydraWin.Core.Tracking;

/// <summary>
/// Gathers a window's properties from Win32. This is the only impure step of the tracking
/// pipeline: it turns a handle into <see cref="WindowFacts"/>, after which
/// <see cref="WindowFilter"/> and <see cref="WindowSetDiff"/> work on plain data.
/// </summary>
internal static class WindowProbe
{
    /// <summary>
    /// How long an elevation answer is trusted. A process never changes its elevation, so this
    /// exists only to bound the damage from a recycled process id inheriting a stale verdict.
    /// </summary>
    private static readonly long ElevationTtlMs = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;

    private static readonly Dictionary<int, (bool Elevated, long Stamp)> ElevationByPid = [];
    private static readonly Lock ElevationGate = new();

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
            info.Pid,
            IsElevated(info.Pid));
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

    /// <summary>
    /// Whether a process is elevated, answered from a per-process-id cache.
    /// </summary>
    /// <remarks>
    /// The sweep asks this for every top-level window roughly every two seconds — several hundred
    /// calls — and most of them share a handful of process ids. Caching turns three syscalls per
    /// window into three per process.
    /// </remarks>
    internal static bool IsElevated(int pid)
    {
        if (pid == 0)
        {
            return false;
        }

        long now = Environment.TickCount64;

        lock (ElevationGate)
        {
            if (ElevationByPid.TryGetValue(pid, out (bool Elevated, long Stamp) cached)
                && now - cached.Stamp < ElevationTtlMs)
            {
                return cached.Elevated;
            }
        }

        // Deliberately outside the lock: the query is a syscall, and a duplicate answer for the
        // same process id is harmless.
        bool elevated = NativeMethods.IsProcessElevated(pid);

        lock (ElevationGate)
        {
            ElevationByPid[pid] = (elevated, now);
        }

        return elevated;
    }
}
