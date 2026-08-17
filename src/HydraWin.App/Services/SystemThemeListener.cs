using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using HydraWin.Core.Interop;

namespace HydraWin.App.Services;

/// <summary>
/// Watches for the user changing the Windows theme while HydraWin is running.
/// </summary>
/// <remarks>
/// <para>
/// A near-twin of <see cref="ShellHookListener"/>, and for the same reasons. The hook lives on the
/// main window because <c>WM_SETTINGCHANGE</c> is delivered to a window's message loop, and that
/// window is created at start-up and only ever hidden, never closed — so the subscription survives
/// closing to tray. A hidden window still pumps messages; a closed one does not.
/// </para>
/// <para>
/// <c>SystemEvents.UserPreferenceChanged</c> would be shorter, but it lives in
/// <c>Microsoft.Win32.SystemEvents.dll</c>, which only the WindowsDesktop targeting pack carries —
/// so Core cannot have it without a new package reference, and using it from here would put an OS
/// query above the Core seam. It also raises on a thread of its own, needing a marshal back, and a
/// missed unsubscribe keeps the whole object graph alive.
/// </para>
/// </remarks>
internal sealed class SystemThemeListener : IDisposable
{
    /// <summary>
    /// How long to wait for the notifications to stop arriving.
    /// </summary>
    /// <remarks>
    /// One user toggle produces several <c>WM_SETTINGCHANGE</c> messages. Each palette swap
    /// invalidates every <c>DynamicResource</c> in the tree, so coalescing them into one is worth a
    /// sixth of a second of latency. <see cref="Themes.ThemeManager.Reevaluate"/> also ignores a
    /// change that resolves to the theme already in force — both are needed, because the burst can
    /// straddle the moment the registry value settles.
    /// </remarks>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly IAppearanceApi appearance;
    private readonly Action changed;
    private readonly DispatcherTimer timer;

    private HwndSource? source;
    private bool disposed;

    internal SystemThemeListener(Window window, IAppearanceApi appearance, Action changed)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(changed);

        this.appearance = appearance;
        this.changed = changed;

        timer = new DispatcherTimer { Interval = Debounce };
        timer.Tick += OnDebounceElapsed;

        Attach(window);
    }

    /// <summary>Whether the hook is in place.</summary>
    internal bool IsListening => source is not null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        timer.Stop();
        timer.Tick -= OnDebounceElapsed;
        source?.RemoveHook(OnMessage);
        source = null;
    }

    private void Attach(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(OnMessage);
    }

    private nint OnMessage(nint windowHandle, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == (int)appearance.SettingChangeMessage
            && appearance.IsThemeChange(wParam, lParam))
        {
            // Restarting the timer is what makes this a debounce rather than a throttle: the burst
            // has to go quiet before anything is applied.
            timer.Stop();
            timer.Start();
        }

        // Never handled: other listeners, and WPF itself, still want these messages.
        return 0;
    }

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        timer.Stop();
        changed();
    }
}
