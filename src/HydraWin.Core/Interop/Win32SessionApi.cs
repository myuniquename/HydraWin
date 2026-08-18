using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Interop;

/// <summary>
/// The real <see cref="ISessionApi"/>: a thin adapter onto <c>NativeMethods</c>, which is the only
/// class allowed to declare P/Invoke.
/// </summary>
public sealed class Win32SessionApi : ISessionApi
{
    /// <summary><c>WTS_SESSION_LOCK</c>.</summary>
    private const int SessionLock = 0x7;

    /// <summary><c>WTS_SESSION_UNLOCK</c>.</summary>
    private const int SessionUnlock = 0x8;

    /// <summary><c>PBT_APMSUSPEND</c>.</summary>
    private const int PowerSuspend = 0x4;

    /// <summary><c>PBT_APMRESUMESUSPEND</c>: resumed because somebody did something.</summary>
    private const int PowerResumeSuspend = 0x7;

    /// <summary><c>PBT_APMRESUMEAUTOMATIC</c>: resumed, possibly with nobody there.</summary>
    private const int PowerResumeAutomatic = 0x12;

    /// <summary>A shared instance; the type holds no state.</summary>
    public static Win32SessionApi Instance { get; } = new();

    /// <inheritdoc />
    public bool Register(nint hwnd) => NativeMethods.RegisterSessionNotifications(hwnd);

    /// <inheritdoc />
    public void Unregister(nint hwnd) => NativeMethods.UnregisterSessionNotifications(hwnd);

    /// <inheritdoc />
    public SessionTransition? Classify(int message, nint wParam)
    {
        int code = (int)wParam;

        if (message == ((ISessionApi)this).SessionChangeMessage)
        {
            return code switch
            {
                SessionLock => new SessionTransition(true, AwayReason.Locked),
                SessionUnlock => new SessionTransition(false, AwayReason.Locked),
                _ => null,
            };
        }

        if (message == ((ISessionApi)this).PowerBroadcastMessage)
        {
            return code switch
            {
                PowerSuspend => new SessionTransition(true, AwayReason.Suspended),
                PowerResumeSuspend or PowerResumeAutomatic =>
                    new SessionTransition(false, AwayReason.Suspended),
                _ => null,
            };
        }

        return null;
    }
}
