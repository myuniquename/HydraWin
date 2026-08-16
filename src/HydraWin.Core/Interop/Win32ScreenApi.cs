namespace HydraWin.Core.Interop;

/// <summary>
/// The real <see cref="IScreenApi"/>: a thin adapter onto <c>NativeMethods</c>, which is the only
/// class allowed to declare P/Invoke.
/// </summary>
public sealed class Win32ScreenApi : IScreenApi
{
    /// <summary>A shared instance; the type holds no state.</summary>
    public static Win32ScreenApi Instance { get; } = new();

    /// <inheritdoc />
    public Point GetCursorPosition() => NativeMethods.GetCursorPosition();

    /// <inheritdoc />
    public PickerInput ReadPickerInput() => NativeMethods.ReadPickerInput();

    /// <inheritdoc />
    public nint TopLevelWindowAt(Point screenPoint) => NativeMethods.TopLevelWindowAt(screenPoint);

    /// <inheritdoc />
    public bool TryGetWindowRect(nint hwnd, out Rect rect) =>
        NativeMethods.TryGetWindowRect(hwnd, out rect);

    /// <inheritdoc />
    public void SendToBottom(nint hwnd) => NativeMethods.SendToBottom(hwnd);

    /// <inheritdoc />
    public void RestoreZOrder(nint hwnd, bool topmost) =>
        NativeMethods.RestoreZOrder(hwnd, topmost);

    /// <inheritdoc />
    public void MakeOverlay(nint hwnd) => NativeMethods.MakeOverlay(hwnd);

    /// <inheritdoc />
    public void PositionOverlay(nint hwnd, in Rect rect) =>
        NativeMethods.PositionOverlay(hwnd, in rect);
}
