namespace HydraWin.Core.Workspaces;

/// <summary>
/// Which palette the user asked for.
/// </summary>
/// <remarks>
/// Persisted by name inside <c>state.json</c> — <c>"System"</c>, <c>"Light"</c>, <c>"Dark"</c> —
/// because that file is hand-editable and a number would tell a reader nothing.
/// </remarks>
public enum Appearance
{
    /// <summary>Follow the Windows app theme, and keep following it while HydraWin runs.</summary>
    System,

    /// <summary>Always light, whatever Windows is set to.</summary>
    Light,

    /// <summary>Always dark, whatever Windows is set to.</summary>
    Dark,
}

/// <summary>
/// Which palette HydraWin actually paints with, once the preference and the OS have both been
/// consulted.
/// </summary>
public enum EffectiveTheme
{
    /// <summary>The light palette.</summary>
    Light,

    /// <summary>The dark palette.</summary>
    Dark,

    /// <summary>
    /// The user's Windows high-contrast scheme, deferred to rather than replaced.
    /// </summary>
    HighContrast,
}

/// <summary>
/// Turns the stored preference plus the current OS state into the one value the UI needs.
/// </summary>
/// <remarks>
/// Pure on purpose, and in Core rather than in the App: it is the only part of theming that has a
/// right answer worth a unit test. Everything else is brushes and Win32.
/// </remarks>
public static class AppearanceResolver
{
    /// <summary>
    /// Which palette to paint with.
    /// </summary>
    /// <param name="requested">The user's stored preference.</param>
    /// <param name="systemIsDark">
    /// Whether the Windows <em>app</em> theme is dark. Only consulted for
    /// <see cref="Appearance.System"/>.
    /// </param>
    /// <param name="highContrast">Whether a Windows high-contrast scheme is active.</param>
    /// <remarks>
    /// <para>
    /// <b>High contrast beats an explicit override.</b> A user who turned on a contrast theme asked
    /// the operating system for particular colours for a reason, and no preference inside HydraWin
    /// is a good enough argument to paint over them. This is a judgement call, and the only one
    /// here — it is a one-line change if it turns out to be the wrong one.
    /// </para>
    /// <para>
    /// A <paramref name="requested"/> value outside the enum — which a cast or a future schema can
    /// produce — falls back to following the OS rather than throwing. Refusing to start over a
    /// colour preference would be absurd.
    /// </para>
    /// </remarks>
    public static EffectiveTheme Resolve(
        Appearance requested,
        bool systemIsDark,
        bool highContrast)
    {
        if (highContrast)
        {
            return EffectiveTheme.HighContrast;
        }

        return requested switch
        {
            Appearance.Light => EffectiveTheme.Light,
            Appearance.Dark => EffectiveTheme.Dark,
            _ => systemIsDark ? EffectiveTheme.Dark : EffectiveTheme.Light,
        };
    }
}
