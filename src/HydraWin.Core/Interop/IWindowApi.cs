namespace HydraWin.Core.Interop;

/// <summary>
/// The seam between HydraWin's logic and Win32. Every service that manipulates or inspects a
/// foreign window takes this interface, never <c>NativeMethods</c> directly.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the dangerous parts are testable. Task 05 verifies <c>RestoreService</c>'s
/// identity checks against a fake — a real one would need a recycled window handle to exist on
/// demand — and task 06 must prove the project's one invariant, that the journal is flushed
/// <em>before</em> any <c>SW_HIDE</c>, by asserting call order on a scripted fake.
/// </para>
/// <para>
/// Two behaviours task 01 measured are baked into the shape rather than left to callers:
/// <c>ShowWindow</c> reports the window's previous visibility instead of success, so
/// <see cref="Hide"/> and <see cref="Show"/> return a verified
/// <see cref="ShowWindowResult"/>; and an elevated window refuses to hide with
/// <c>ERROR_ACCESS_DENIED</c>, which that result carries.
/// </para>
/// </remarks>
public interface IWindowApi
{
    /// <summary>Whether the handle still refers to a live window.</summary>
    bool IsWindow(nint hwnd);

    /// <summary>Whether the window is currently visible.</summary>
    bool IsWindowVisible(nint hwnd);

    /// <summary>The owning process id, or 0.</summary>
    int GetProcessId(nint hwnd);

    /// <summary>The process image path, or empty for a protected process.</summary>
    string GetProcessPath(int pid);

    /// <summary>The window's title, or empty.</summary>
    string GetWindowTitle(nint hwnd);

    /// <summary>Reads position and maximized state.</summary>
    bool TryGetPlacement(nint hwnd, out WindowPlacement placement);

    /// <summary>Restores position and maximized state.</summary>
    bool TrySetPlacement(nint hwnd, in WindowPlacement placement);

    /// <summary>Hides a window, reporting whether it actually went away.</summary>
    ShowWindowResult Hide(nint hwnd);

    /// <summary>Shows a window, reporting whether it actually came back.</summary>
    ShowWindowResult Show(nint hwnd);

    /// <summary>
    /// Brings a window to the foreground, reporting whether focus actually landed. Only valid
    /// while HydraWin is itself the foreground process — see the implementation's remarks.
    /// </summary>
    bool TryFocus(nint hwnd);

    /// <summary>
    /// Brings a window to the front of the z-order without activating it, so the keyboard stays
    /// where it is. Used when the switch was driven from HydraWin's own UI.
    /// </summary>
    void Raise(nint hwnd);
}
