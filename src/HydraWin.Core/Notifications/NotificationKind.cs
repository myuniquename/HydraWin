namespace HydraWin.Core.Notifications;

/// <summary>Where a pending notification came from.</summary>
public enum NotificationKind
{
    /// <summary>
    /// The window asked for attention through the shell — its taskbar button flashed.
    /// </summary>
    /// <remarks>
    /// The application-agnostic channel, and the only one that fires by default. The shell raises
    /// it for any window, whether the app called <c>FlashWindowEx</c> itself or a console rang the
    /// terminal bell, so HydraWin never has to know which program it is looking at.
    /// </remarks>
    Attention,

    /// <summary>A <see cref="NotificationRule"/> matched a change of the window's title.</summary>
    Title,
}
