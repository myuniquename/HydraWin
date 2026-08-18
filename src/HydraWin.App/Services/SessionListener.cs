using System.Windows;
using System.Windows.Interop;
using HydraWin.Core.Interop;

namespace HydraWin.App.Services;

/// <summary>
/// Watches for the user leaving the machine and coming back, so a task's timer is not still
/// running against a locked screen.
/// </summary>
/// <remarks>
/// <para>
/// A third sibling of <see cref="ShellHookListener"/> and <see cref="SystemThemeListener"/>, and
/// for the same reasons. The hook lives on the main window because both
/// <c>WM_WTSSESSION_CHANGE</c> and <c>WM_POWERBROADCAST</c> are delivered to a window's message
/// loop, and that window is created at start-up and only ever hidden, never closed — so the
/// subscription survives closing to tray. A hidden window still pumps messages; a closed one does
/// not.
/// </para>
/// <para>
/// Unlike its two siblings this one also has an unsubscribe to make: lock notifications are a
/// registration, whereas the power broadcasts arrive at every top-level window whether or not
/// anybody asked.
/// </para>
/// <para>
/// No debounce, unlike <see cref="SystemThemeListener"/>. Each of these messages is a single
/// discrete event rather than a burst, and the ledger it feeds treats a repeat as a no-op anyway.
/// </para>
/// </remarks>
internal sealed class SessionListener : IDisposable
{
    private readonly ISessionApi session;
    private readonly Action<SessionTransition> transition;

    private HwndSource? source;
    private nint hwnd;
    private bool disposed;

    internal SessionListener(Window window, ISessionApi session, Action<SessionTransition> transition)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transition);

        this.session = session;
        this.transition = transition;

        Attach(window);
    }

    /// <summary>
    /// Whether the lock and unlock subscription was accepted. The suspend and resume half works
    /// either way, so this being <see langword="false"/> costs the lock leg and nothing else.
    /// </summary>
    internal bool IsListening { get; private set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        source?.RemoveHook(OnMessage);
        source = null;

        if (IsListening)
        {
            session.Unregister(hwnd);
            IsListening = false;
        }
    }

    private void Attach(Window window)
    {
        hwnd = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd);

        if (source is null)
        {
            return;
        }

        source.AddHook(OnMessage);
        IsListening = session.Register(hwnd);
    }

    private nint OnMessage(nint windowHandle, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (session.Classify(msg, wParam) is SessionTransition change)
        {
            transition(change);
        }

        // Never handled: other listeners, and WPF itself, still want these messages. WM_POWERBROADCAST
        // is documented to answer TRUE, and letting DefWindowProc supply that is exactly right —
        // hard-coding a 1 here would break the convention the other two listeners keep for no gain.
        return 0;
    }
}
