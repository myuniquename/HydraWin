using System.Windows;
using System.Windows.Interop;
using HydraWin.Core.Interop;

namespace HydraWin.App.Services;

/// <summary>
/// Subscribes the main window to the shell's notifications and reports flashing windows.
/// </summary>
/// <remarks>
/// <para>
/// The one place the application-agnostic notification channel enters the app. The shell posts
/// <c>HSHELL_FLASH</c> for any window whose taskbar button flashes, so nothing here — and nothing
/// downstream — needs to know which program raised it.
/// </para>
/// <para>
/// The hook lives on the main window because shell notifications are delivered to a window's
/// message loop. That window is created at start-up and only ever hidden, never closed, so the
/// subscription survives closing to tray. Task 08's hotkeys are unaffected: they run on their own
/// thread and never touch this one.
/// </para>
/// </remarks>
internal sealed class ShellHookListener : IDisposable
{
    private readonly IShellHookApi shellHook;
    private readonly Action<nint> flashed;

    private HwndSource? source;
    private nint hwnd;
    private uint message;
    private bool disposed;

    internal ShellHookListener(Window window, IShellHookApi shellHook, Action<nint> flashed)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(shellHook);
        ArgumentNullException.ThrowIfNull(flashed);

        this.shellHook = shellHook;
        this.flashed = flashed;

        Attach(window);
    }

    /// <summary>Whether the shell accepted the subscription.</summary>
    internal bool IsListening => message != 0;

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        source?.RemoveHook(OnMessage);
        shellHook.Unregister(hwnd);
        source = null;
    }

    private void Attach(Window window)
    {
        hwnd = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd);

        if (source is null)
        {
            return;
        }

        // The id is not a constant — RegisterWindowMessage hands out a different one per session,
        // so it is read here and compared against, never hard-coded.
        message = shellHook.Register(hwnd);

        if (message != 0)
        {
            source.AddHook(OnMessage);
        }
    }

    private nint OnMessage(nint windowHandle, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (message != 0 && msg == (int)message && (int)wParam == shellHook.FlashMessage)
        {
            // An application may flash an owned dialog rather than its main window; resolving to
            // the root is what makes that badge the right task instead of being dropped.
            flashed(shellHook.RootWindow(lParam));
        }

        // Never handled: other listeners, and WPF itself, still want these messages.
        return 0;
    }
}
