using Microsoft.Win32;

namespace HydraWin.Core.Interop;

/// <summary>
/// The real <see cref="IAppearanceApi"/>: a thin adapter onto <c>NativeMethods</c>, which is the only
/// class allowed to declare P/Invoke, plus the one registry value that carries the app theme.
/// </summary>
/// <remarks>
/// The registry read lives here rather than in <c>NativeMethods</c> because it is not P/Invoke —
/// <c>Microsoft.Win32.Registry</c> is in the base <c>net10.0-windows</c> targeting pack and needs no
/// package reference. It is <b>read-only, under HKCU</b>: <see cref="Workspaces.SettingsModel"/>'s
/// promise that HydraWin writes nothing to the registry still holds.
/// </remarks>
public sealed class Win32AppearanceApi : IAppearanceApi
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsUseLightTheme = "AppsUseLightTheme";

    private const string ImmersiveColorSet = "ImmersiveColorSet";

    /// <summary><c>SPI_SETHIGHCONTRAST</c>, as it arrives in <c>WM_SETTINGCHANGE</c>'s wParam.</summary>
    private const nint SPI_SETHIGHCONTRAST = 0x0043;

    /// <summary>A shared instance; the type holds no state.</summary>
    public static Win32AppearanceApi Instance { get; } = new();

    /// <inheritdoc />
    public bool IsSystemDarkMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightTheme) is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
            or UnauthorizedAccessException
            or IOException)
        {
            // Light, for every failure mode, for the same reason: it is what Windows itself shows
            // when the value was never written, and it is how HydraWin looked before this existed.
            // A registry hiccup should degrade to the appearance the user already knows.
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsHighContrast() => NativeMethods.IsHighContrast();

    /// <inheritdoc />
    public bool IsThemeChange(nint wParam, nint lParam) =>
        wParam == SPI_SETHIGHCONTRAST || NativeMethods.IsSettingName(lParam, ImmersiveColorSet);

    /// <inheritdoc />
    public bool TrySetDarkTitleBar(nint hwnd, bool dark) =>
        NativeMethods.TrySetDarkTitleBar(hwnd, dark);

    /// <inheritdoc />
    public void RedrawFrame(nint hwnd) => NativeMethods.RedrawFrame(hwnd);
}
