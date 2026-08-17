using System.Windows;
using System.Windows.Media;

namespace HydraWin.App.Themes;

/// <summary>
/// The theme's colours in the one form XAML cannot deliver: frozen <see cref="Brush"/> and
/// <see cref="Pen"/> objects for the drag adorners' <c>OnRender</c>.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in the app reaches the palette through <c>{DynamicResource}</c>, which needs a
/// <see cref="DependencyObject"/> and a <see cref="DependencyProperty"/> to attach to.
/// <c>DrawingContext.DrawRectangle</c> has neither — it takes a brush and a pen as plain arguments —
/// so those two have to be looked up eagerly and refreshed when the theme changes.
/// </para>
/// <para>
/// <see cref="ThemeManager"/> calls <see cref="Rebuild"/> on every apply. The adorners read these in
/// their constructors, and an adorner is created fresh at the start of each drag, so a drag always
/// draws in the current theme and no lookup or allocation happens per frame. A theme change
/// <em>during</em> a drag keeps the colours it started with, which is not worth handling.
/// </para>
/// </remarks>
internal static class ThemeBrushes
{
    /// <summary>The accent, for when the palette cannot be reached at all — see <see cref="Rebuild"/>.</summary>
    private static readonly Color FallbackAccent = Color.FromRgb(0x4C, 0x8D, 0xFF);

    static ThemeBrushes() => Rebuild();

    /// <summary>The wash over a row that will receive a dropped window.</summary>
    internal static Brush DropTargetFill { get; private set; } = Brushes.Transparent;

    /// <summary>The outline of that row.</summary>
    internal static Pen DropTargetEdge { get; private set; } = new();

    /// <summary>The line showing where a dragged task will land.</summary>
    internal static Pen InsertionLine { get; private set; } = new();

    /// <summary>
    /// Re-reads the three values from the current palette.
    /// </summary>
    /// <remarks>
    /// Static, and called only from <see cref="ThemeManager"/>, which is the single owner of theme
    /// state. The fallbacks matter more than they look: this type's static constructor can run
    /// before <see cref="Application.Current"/> exists, and an unresolved lookup would otherwise
    /// leave a null pen that throws inside a render pass.
    /// </remarks>
    internal static void Rebuild()
    {
        DropTargetFill = Find("DropTargetFillBrush", 0x28);
        DropTargetEdge = Freeze(new Pen(Find("DropTargetEdgeBrush", 0xFF), 2));
        InsertionLine = Freeze(new Pen(Find("InsertionLineBrush", 0xFF), 3));
    }

    private static Brush Find(string key, byte fallbackAlpha)
    {
        if (Application.Current?.TryFindResource(key) is Brush found)
        {
            // A palette brush is already frozen; cloning an unfrozen one keeps a later mutation
            // from reaching a pen we have handed out.
            return found.IsFrozen ? found : Freeze(found.Clone());
        }

        return Freeze(new SolidColorBrush(Color.FromArgb(
            fallbackAlpha,
            FallbackAccent.R,
            FallbackAccent.G,
            FallbackAccent.B)));
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
