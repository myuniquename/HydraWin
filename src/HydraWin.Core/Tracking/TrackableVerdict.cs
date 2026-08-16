namespace HydraWin.Core.Tracking;

/// <summary>
/// Why a window is or is not part of HydraWin's inventory.
/// </summary>
/// <remarks>
/// The filter returns the failing clause rather than a bare <see langword="bool"/> so that
/// "confirm these windows are absent" is answerable with evidence: the task 03 debug harness
/// lists every rejected window against its reason, and each clause gets its own unit test.
/// </remarks>
public enum TrackableVerdict
{
    /// <summary>The window belongs in the inventory.</summary>
    Trackable,

    /// <summary>No title — not something the user thinks of as a window.</summary>
    NoTitle,

    /// <summary>Invisible, and not hidden by HydraWin, so the app itself hid or closed it.</summary>
    NotVisible,

    /// <summary>Has <c>WS_EX_TOOLWINDOW</c>: a palette or popup, never a task window.</summary>
    ToolWindow,

    /// <summary>Owned by another window — a dialog or tool attached to a real window.</summary>
    Owned,

    /// <summary>DWM-cloaked, the usual signature of a UWP ghost window.</summary>
    Cloaked,

    /// <summary>One of HydraWin's own windows, which it never manages.</summary>
    OwnProcess,

    /// <summary>
    /// Owned by an elevated process while HydraWin is not elevated. UIPI stops the hide, so
    /// listing it would only offer the user something that could never be switched away.
    /// </summary>
    Elevated,
}
