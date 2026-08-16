namespace HydraWin.Core.Interop;

/// <summary>
/// Screen-level queries and the two window tricks the Spy++-style picker needs.
/// </summary>
/// <remarks>
/// A separate seam from <see cref="IWindowApi"/>, which is about manipulating a window you already
/// have a handle to. This one answers "what is under the pointer" and lets the App build the
/// highlight frame without declaring P/Invoke of its own (repo rule). Everything here is in
/// physical screen pixels, deliberately: mixing these with WPF's device-independent units is how
/// multi-monitor DPI bugs start.
/// </remarks>
public interface IScreenApi
{
    /// <summary>Where the pointer is, in physical screen pixels.</summary>
    Point GetCursorPosition();

    /// <summary>
    /// One sample of the picker's input. The picker follows this rather than a WPF mouse capture,
    /// which the gesture's own window operations keep breaking.
    /// </summary>
    PickerInput ReadPickerInput();

    /// <summary>
    /// The top-level window at a screen point, or 0. Child windows are walked up to their root, so
    /// the answer is the application window the user is pointing at.
    /// </summary>
    nint TopLevelWindowAt(Point screenPoint);

    /// <summary>The window's bounding rectangle in physical screen pixels.</summary>
    bool TryGetWindowRect(nint hwnd, out Rect rect);

    /// <summary>
    /// Drops a window to the bottom of the z-order. Applied to HydraWin's own window while
    /// picking, so whatever it was covering becomes pointable.
    /// </summary>
    void SendToBottom(nint hwnd);

    /// <summary>Puts a window back on top after <see cref="SendToBottom"/>.</summary>
    void RestoreZOrder(nint hwnd, bool topmost);

    /// <summary>Turns a window into a click-through, never-activated, always-on-top frame.</summary>
    void MakeOverlay(nint hwnd);

    /// <summary>Places an overlay over a target rectangle, in physical pixels.</summary>
    void PositionOverlay(nint hwnd, in Rect rect);
}
