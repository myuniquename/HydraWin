namespace HydraWin.Core.Notifications;

/// <summary>
/// One window that wants attention and has not been looked at yet.
/// </summary>
/// <param name="Hwnd">The window asking.</param>
/// <param name="Kind">Which channel raised it.</param>
/// <param name="Label">
/// What the tooltip says — a rule's label when a rule fired, otherwise the window's own
/// description, which is what makes a rule-less notification readable for any application.
/// </param>
/// <param name="RaisedAt">
/// When it was last raised. Refreshed rather than duplicated when a window flashes repeatedly, so
/// the count tracks windows needing attention rather than signals received.
/// </param>
public readonly record struct PendingNotification(
    nint Hwnd,
    NotificationKind Kind,
    string Label,
    DateTimeOffset RaisedAt);
