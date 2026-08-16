namespace HydraWin.Core.Tracking;

/// <summary>
/// Everything the trackability filter needs to know about one window, gathered once so the
/// decision itself stays pure and unit-testable with no Win32 involved.
/// </summary>
/// <param name="Hwnd">The window handle.</param>
/// <param name="Title">
/// The window title, empty when it has none. Carried in full rather than as a length because the
/// inventory needs it anyway, and fetching it once saves a second Win32 round trip per window per
/// sweep.
/// </param>
/// <param name="IsVisible">Result of <c>IsWindowVisible</c>.</param>
/// <param name="IsHydraWinHidden">
/// True when HydraWin itself hid this window, which keeps it tracked despite being invisible.
/// </param>
/// <param name="ExtendedStyle">Result of <c>GetWindowLongPtr(GWL_EXSTYLE)</c>.</param>
/// <param name="Owner">Result of <c>GetWindow(GW_OWNER)</c>; non-zero means an owned window.</param>
/// <param name="IsCloaked">True when DWM reports the window cloaked (UWP ghost).</param>
/// <param name="Pid">Owning process id.</param>
/// <param name="IsElevated">
/// True when the owning process runs elevated — or is otherwise beyond this process's reach. A
/// non-elevated HydraWin cannot hide such a window, so it is kept out of the inventory rather than
/// offered to the user as something they can put in a task.
/// </param>
public readonly record struct WindowFacts(
    nint Hwnd,
    string Title,
    bool IsVisible,
    bool IsHydraWinHidden,
    long ExtendedStyle,
    nint Owner,
    bool IsCloaked,
    int Pid,
    bool IsElevated = false);
