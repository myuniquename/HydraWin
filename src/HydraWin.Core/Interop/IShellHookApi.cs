namespace HydraWin.Core.Interop;

/// <summary>
/// The shell's "this window wants attention" channel.
/// </summary>
/// <remarks>
/// A seam rather than a direct call so the App layer can subscribe its window without declaring
/// P/Invoke of its own (repo rule), and so a test can drive the hub without a desktop.
/// </remarks>
public interface IShellHookApi
{
    /// <summary>
    /// <c>HSHELL_FLASH</c>: a window's taskbar button began flashing. The handle is in the
    /// message's <c>lParam</c>.
    /// </summary>
    /// <remarks>
    /// The value carries <c>HSHELL_HIGHBIT</c>, which is why it is 0x8006 and not 6.
    /// <c>HSHELL_RUDEAPPACTIVATED</c> is deliberately absent: task 01 saw it zero times in 1311
    /// captured shell-hook messages.
    /// </remarks>
    int FlashMessage => 0x8006;

    /// <summary>
    /// Subscribes a window to shell notifications and returns the message id they arrive as, or 0
    /// if the shell refused. The id varies per session and must never be hard-coded.
    /// </summary>
    uint Register(nint hwnd);

    /// <summary>Unsubscribes a window.</summary>
    void Unregister(nint hwnd);

    /// <summary>The top-level window a handle belongs to, for apps that flash an owned dialog.</summary>
    nint RootWindow(nint hwnd);
}
