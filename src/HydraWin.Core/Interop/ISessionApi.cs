using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Interop;

/// <summary>
/// The channel that says whether the user is at the machine: the session locking and unlocking,
/// and the machine suspending and resuming.
/// </summary>
/// <remarks>
/// <para>
/// A seam rather than a direct call so the App layer can subscribe its window without declaring
/// P/Invoke of its own (repo rule), and — the reason it carries <see cref="Classify"/> rather than
/// just the message ids — so that nothing above this interface ever handles a raw
/// <c>wParam</c>. The listener asks what a message <em>means</em> and gets a Core answer back.
/// </para>
/// <para>
/// <c>SystemEvents.SessionSwitch</c> and <c>SystemEvents.PowerModeChanged</c> would do this in ten
/// lines and are deliberately not used, for the reasons set out on
/// <c>HydraWin.App.Services.SystemThemeListener</c>: the assembly ships only in the WindowsDesktop
/// targeting pack, it raises on a thread of its own, and a missed unsubscribe pins the whole
/// object graph.
/// </para>
/// </remarks>
public interface ISessionApi
{
    /// <summary><c>WM_WTSSESSION_CHANGE</c>. Only arrives after <see cref="Register"/>.</summary>
    int SessionChangeMessage => 0x02B1;

    /// <summary>
    /// <c>WM_POWERBROADCAST</c>. Needs no registration for the suspend and resume events — those
    /// are broadcast to every top-level window, which is why only half of this interface has a
    /// subscription call.
    /// </summary>
    int PowerBroadcastMessage => 0x0218;

    /// <summary>
    /// Subscribes a window to this session's lock and unlock notifications.
    /// </summary>
    /// <returns><see langword="false"/> when Windows refused, which is survivable.</returns>
    bool Register(nint hwnd);

    /// <summary>Unsubscribes a window.</summary>
    void Unregister(nint hwnd);

    /// <summary>
    /// What a window message means for the user's presence, or <see langword="null"/> when it
    /// means nothing.
    /// </summary>
    /// <remarks>
    /// Both resume events count as back. Waiting for the one that means "a person did this" would
    /// leave the clock stopped forever after a wake-timer resume on an unlocked machine; and if
    /// the machine woke with nobody there, the session is still locked, so
    /// <see cref="AwayReason.Locked"/> is holding the clock anyway.
    /// </remarks>
    SessionTransition? Classify(int message, nint wParam);
}

/// <summary>What a session or power message says about the user being at the machine.</summary>
/// <param name="Away">Whether they left; otherwise they are back.</param>
/// <param name="Reason">Which of the two channels said so.</param>
public readonly record struct SessionTransition(bool Away, AwayReason Reason);
