namespace HydraWin.Core.Interop;

/// <summary>
/// The real <see cref="IWindowApi"/>: a thin adapter onto <c>NativeMethods</c>, which is the only
/// class allowed to declare P/Invoke.
/// </summary>
public sealed class Win32WindowApi : IWindowApi
{
    /// <summary>A shared instance; the type holds no state.</summary>
    public static Win32WindowApi Instance { get; } = new();

    /// <inheritdoc />
    public bool IsWindow(nint hwnd) => NativeMethods.TryGetIdentity(hwnd, out _, out _);

    /// <inheritdoc />
    public bool IsWindowVisible(nint hwnd) =>
        NativeMethods.TryGetIdentity(hwnd, out _, out bool visible) && visible;

    /// <inheritdoc />
    public int GetProcessId(nint hwnd) =>
        NativeMethods.TryGetIdentity(hwnd, out int pid, out _) ? pid : 0;

    /// <inheritdoc />
    public string GetProcessPath(int pid) => NativeMethods.GetProcessPath(pid);

    /// <inheritdoc />
    public string GetWindowTitle(nint hwnd) => NativeMethods.GetWindowTitle(hwnd);

    /// <inheritdoc />
    public bool TryGetPlacement(nint hwnd, out WindowPlacement placement) =>
        NativeMethods.TryGetPlacement(hwnd, out placement);

    /// <inheritdoc />
    public bool TrySetPlacement(nint hwnd, in WindowPlacement placement) =>
        NativeMethods.TrySetPlacement(hwnd, in placement);

    /// <inheritdoc />
    public ShowWindowResult Hide(nint hwnd) => NativeMethods.Hide(hwnd);

    /// <inheritdoc />
    public ShowWindowResult Show(nint hwnd) => NativeMethods.Show(hwnd);

    /// <inheritdoc />
    public bool RequestClose(nint hwnd) => NativeMethods.RequestClose(hwnd);

    /// <inheritdoc />
    public bool TryFocus(nint hwnd) => NativeMethods.TryFocus(hwnd);

    /// <inheritdoc />
    public void Raise(nint hwnd) => NativeMethods.Raise(hwnd);
}
