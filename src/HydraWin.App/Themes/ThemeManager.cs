using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Interop;
using HydraWin.Core.Interop;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.Themes;

/// <summary>
/// Owns which palette is in force: resolves the preference against the OS, swaps the palette
/// dictionary, and keeps every window's title bar in step.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not on a view model. Swapping <see cref="Application"/> resources is presentation
/// plumbing, and <c>MainViewModel</c> has no business touching a <see cref="ResourceDictionary"/>.
/// The dialog's OK reaches here the way a hotkey change already does: the view model raises an
/// event and <c>App</c> subscribes.
/// </para>
/// <para>
/// Injected into each window rather than reached through a static, so there is one owner with one
/// lifetime and no service locator.
/// </para>
/// </remarks>
internal sealed class ThemeManager
{
    private readonly IAppearanceApi appearance;
    private readonly Dictionary<EffectiveTheme, ResourceDictionary> palettes = [];
    private readonly List<Window> tracked = [];

    private ResourceDictionary? palette;
    private EffectiveTheme current = EffectiveTheme.Light;
    private Appearance requested = Appearance.System;
    private bool applied;

    internal ThemeManager(IAppearanceApi appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        this.appearance = appearance;

        // App.xaml declares the light palette, so adopt it rather than merging a second copy: that
        // is the entry Reevaluate replaces in place.
        palette = Merged.FirstOrDefault(IsPalette);
        if (palette is not null)
        {
            palettes[EffectiveTheme.Light] = palette;
        }
    }

    /// <summary>Raised after the palette changed, for anything that cannot follow a brush key.</summary>
    internal event EventHandler? Changed;

    /// <summary>Which palette is currently painted.</summary>
    internal EffectiveTheme Current => current;

    private static Collection<ResourceDictionary> Merged =>
        Application.Current.Resources.MergedDictionaries;

    /// <summary>
    /// Records the user's preference and applies whatever it resolves to.
    /// </summary>
    /// <remarks>
    /// Call this before the first <see cref="Window.Show"/>. WPF creates no window handle and paints
    /// nothing until then, so a palette applied at that point is the one the first frame is drawn
    /// with — which is the whole reason there is no light flash on a dark start.
    /// </remarks>
    internal void Apply(Appearance requestedAppearance)
    {
        requested = requestedAppearance;
        Reevaluate();
    }

    /// <summary>
    /// Re-asks the OS and applies the result. Does nothing when the answer has not changed.
    /// </summary>
    /// <remarks>
    /// The no-op is not an optimisation, it is the fix for a real defect: Windows raises
    /// <c>ImmersiveColorSet</c> several times for one toggle, and at least one of those can arrive
    /// before the registry value has settled. Without this guard the app resolves the old theme,
    /// paints it, and then flips back — visible as a strobe.
    /// </remarks>
    internal void Reevaluate()
    {
        EffectiveTheme next = AppearanceResolver.Resolve(
            requested,
            appearance.IsSystemDarkMode(),
            appearance.IsHighContrast());

        if (applied && next == current)
        {
            return;
        }

        current = next;
        applied = true;

        SwapPalette(next);
        ThemeBrushes.Rebuild();

        foreach (Window window in tracked)
        {
            ApplyTitleBar(window);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Keeps a window's title bar in the current theme, now and after every later change.
    /// </summary>
    /// <remarks>
    /// Call it from the window's constructor. The attribute needs a handle, which does not exist
    /// yet — hence <see cref="Window.SourceInitialized"/>, which fires once the handle is created
    /// and still before the first paint.
    /// </remarks>
    internal void TrackTitleBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        tracked.Add(window);
        window.SourceInitialized += OnSourceInitialized;
        window.Closed += OnClosed;

        // Handle, never EnsureHandle: forcing the handle into existence early moves a
        // WindowStartupLocation="CenterOwner" dialog to the wrong place.
        if (new WindowInteropHelper(window).Handle != 0)
        {
            ApplyTitleBar(window);
        }
    }

    private static bool IsPalette(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString.Contains("Palette.", StringComparison.Ordinal) == true;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            ApplyTitleBar(window);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.SourceInitialized -= OnSourceInitialized;
            window.Closed -= OnClosed;
            tracked.Remove(window);
        }
    }

    private void ApplyTitleBar(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0)
        {
            return;
        }

        // Under a high-contrast scheme the compositor draws the caption from that scheme and ignores
        // this, so asking for "not dark" there is the honest request rather than a special case.
        if (!appearance.TrySetDarkTitleBar(hwnd, current == EffectiveTheme.Dark))
        {
            return;
        }

        // Only worth nudging a window that is on screen: a hidden one repaints when it is shown.
        if (window.IsVisible)
        {
            appearance.RedrawFrame(hwnd);
        }
    }

    private void SwapPalette(EffectiveTheme theme)
    {
        ResourceDictionary next = Palette(theme);
        if (ReferenceEquals(next, palette))
        {
            return;
        }

        int index = palette is null ? -1 : Merged.IndexOf(palette);

        // Indexed assignment, never Clear-then-Add: two notifications would leave a moment in which
        // every key resolves to nothing, and an unresolved DynamicResource falls back to the
        // property default — a visible flash of unstyled chrome.
        if (index < 0)
        {
            Merged.Insert(0, next);
        }
        else
        {
            Merged[index] = next;
        }

        palette = next;
    }

    private ResourceDictionary Palette(EffectiveTheme theme)
    {
        if (palettes.TryGetValue(theme, out ResourceDictionary? cached))
        {
            return cached;
        }

        // Relative, resolved against the pack root, as Assets/hydrawin.ico already is in TrayIcon.
        var created = new ResourceDictionary
        {
            Source = new Uri($"Themes/Palette.{theme}.xaml", UriKind.Relative),
        };

        palettes[theme] = created;
        return created;
    }
}
