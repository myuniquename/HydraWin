namespace HydraWin.Core.Interop;

/// <summary>
/// What theming needs from the operating system: what it is set to, when that changed, and a dark
/// title bar on a window HydraWin owns.
/// </summary>
/// <remarks>
/// A seam of its own rather than a few extra members on <see cref="IWindowApi"/>, which is about
/// manipulating <em>other</em> applications' windows and is consumed by the switch engine. This one
/// is entirely about how HydraWin presents itself, and it exists because of the repo rule that no
/// P/Invoke lives above Core — the App needs a dark title bar and cannot call <c>dwmapi</c> for one.
/// <see cref="IIconSource"/> is justified the same way.
/// </remarks>
public interface IAppearanceApi
{
    /// <summary>
    /// <c>WM_SETTINGCHANGE</c>, the message a theme change arrives as. Exposed the way
    /// <see cref="IShellHookApi.FlashMessage"/> is, so the listener above never hard-codes it.
    /// </summary>
    uint SettingChangeMessage => 0x001A;

    /// <summary>
    /// Whether the Windows <em>app</em> theme is dark.
    /// </summary>
    /// <remarks>
    /// This is <c>AppsUseLightTheme</c>, not <c>SystemUsesLightTheme</c> — the sibling value governs
    /// the taskbar and Start chrome, users routinely set the two differently, and confusing them is
    /// the classic mistake here.
    /// </remarks>
    bool IsSystemDarkMode();

    /// <summary>Whether a Windows high-contrast scheme is active.</summary>
    bool IsHighContrast();

    /// <summary>
    /// Whether a <c>WM_SETTINGCHANGE</c> is one that could have changed the theme.
    /// </summary>
    /// <remarks>
    /// Two unrelated shapes have to be accepted. An app-theme change arrives with the string
    /// <c>"ImmersiveColorSet"</c> in <paramref name="lParam"/>; a high-contrast change arrives as
    /// <c>SPI_SETHIGHCONTRAST</c> in <paramref name="wParam"/> and carries no such string. Matching
    /// only the string silently misses every contrast-theme toggle.
    /// </remarks>
    bool IsThemeChange(nint wParam, nint lParam);

    /// <summary>
    /// Asks the desktop compositor to draw a window's title bar dark, or light again.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the compositor refused, which on an older build means the
    /// attribute is unsupported. Cosmetic either way: the caller carries on.
    /// </returns>
    bool TrySetDarkTitleBar(nint hwnd, bool dark);

    /// <summary>
    /// Forces a window's non-client area to repaint, without moving or resizing it.
    /// </summary>
    /// <remarks>
    /// Needed after <see cref="TrySetDarkTitleBar"/> on an already-visible window: the attribute
    /// changes, the compositor often does not redraw the caption until the frame is invalidated.
    /// </remarks>
    void RedrawFrame(nint hwnd);
}
