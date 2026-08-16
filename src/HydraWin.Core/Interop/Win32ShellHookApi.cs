namespace HydraWin.Core.Interop;

/// <summary>
/// The real <see cref="IShellHookApi"/>: a thin adapter onto <c>NativeMethods</c>, which is the
/// only class allowed to declare P/Invoke.
/// </summary>
public sealed class Win32ShellHookApi : IShellHookApi
{
    /// <summary>A shared instance; the type holds no state.</summary>
    public static Win32ShellHookApi Instance { get; } = new();

    /// <inheritdoc />
    public uint Register(nint hwnd) => NativeMethods.RegisterShellHook(hwnd);

    /// <inheritdoc />
    public void Unregister(nint hwnd) => NativeMethods.UnregisterShellHook(hwnd);

    /// <inheritdoc />
    public nint RootWindow(nint hwnd) => NativeMethods.RootWindow(hwnd);
}
