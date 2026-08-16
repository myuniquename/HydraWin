using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace HydraWin.App;

/// <summary>
/// The in-app drag-and-drop vocabulary: what a drag carries, and how to find the row under the
/// pointer.
/// </summary>
/// <remarks>
/// Hand-rolled rather than taken from a library because a task row carries two mouse semantics on
/// one element — a click switches to the task, a drag reorders it — and owning the threshold and
/// the mouse-up decision is what keeps those two apart.
/// </remarks>
internal static class DragDropSupport
{
    /// <summary>Drag payload: a window handle, boxed as a <see cref="long"/>.</summary>
    internal const string WindowFormat = "HydraWin.Window";

    /// <summary>Drag payload: a task id.</summary>
    internal const string TaskFormat = "HydraWin.Task";

    /// <summary>Marks the element that represents a whole task row, for adorning and hit-testing.</summary>
    internal const string TaskRowTag = "TaskRow";

    /// <summary>Marks the element that represents one window row.</summary>
    internal const string WindowRowTag = "WindowRow";

    /// <summary>Whether the pointer has moved far enough to mean "drag" rather than "click".</summary>
    internal static bool PastDragThreshold(Point origin, Point current) =>
        Math.Abs(current.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(current.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;

    /// <summary>The nearest ancestor element carrying the given tag, or <see langword="null"/>.</summary>
    internal static FrameworkElement? FindRow(object? source, string tag)
    {
        for (DependencyObject? node = source as DependencyObject;
            node is not null;
            node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement element && Equals(element.Tag, tag))
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>The nearest ancestor's data context of the given type, or <see langword="null"/>.</summary>
    internal static T? FindDataContext<T>(object? source)
        where T : class
    {
        for (DependencyObject? node = source as DependencyObject;
            node is not null;
            node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { DataContext: T context })
            {
                return context;
            }
        }

        return null;
    }

    /// <summary>
    /// The first descendant carrying the given tag. Used to reach the row element inside an
    /// item container, which is a <c>ContentPresenter</c> the template sits under.
    /// </summary>
    internal static FrameworkElement? FindDescendantRow(DependencyObject root, string tag)
    {
        if (root is FrameworkElement element && Equals(element.Tag, tag))
        {
            return element;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            FrameworkElement? found = FindDescendantRow(VisualTreeHelper.GetChild(root, i), tag);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the click landed on a control that handles its own clicks — the expand toggle, the
    /// rename box, a menu — which must never also switch tasks.
    /// </summary>
    internal static bool IsInteractiveControl(object? source)
    {
        for (DependencyObject? node = source as DependencyObject;
            node is not null;
            node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or System.Windows.Controls.TextBox)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The single drop indicator on screen: a highlight over a drop target, or an insertion line
/// between two task rows.
/// </summary>
internal sealed class DropIndicator
{
    private Adorner? current;

    /// <summary>Outlines an element as the thing that will receive the drop.</summary>
    internal void Highlight(UIElement target) => Attach(target, new HighlightAdorner(target));

    /// <summary>Draws a line at the edge a dragged task would be inserted at.</summary>
    internal void Insertion(UIElement target, bool below) =>
        Attach(target, new InsertionAdorner(target) { Below = below });

    /// <summary>Removes whatever is showing.</summary>
    internal void Clear()
    {
        if (current is null)
        {
            return;
        }

        AdornerLayer.GetAdornerLayer(current.AdornedElement)?.Remove(current);
        current = null;
    }

    private void Attach(UIElement target, Adorner adorner)
    {
        // Re-attaching the same kind of adorner to the same element every DragOver would flicker.
        if (current?.AdornedElement == target && current.GetType() == adorner.GetType()
            && current is not InsertionAdorner)
        {
            return;
        }

        Clear();

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null)
        {
            return;
        }

        layer.Add(adorner);
        current = adorner;
    }
}

/// <summary>An accent outline over the row that will receive the drop.</summary>
internal sealed class HighlightAdorner : Adorner
{
    private static readonly Brush Fill = Freeze(new SolidColorBrush(Color.FromArgb(40, 76, 141, 255)));
    private static readonly Pen Edge = Freeze(new Pen(
        Freeze(new SolidColorBrush(Color.FromRgb(76, 141, 255))), 2));

    internal HighlightAdorner(UIElement adornedElement)
        : base(adornedElement) => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var bounds = new Rect(AdornedElement.RenderSize);
        drawingContext.DrawRoundedRectangle(Fill, Edge, Rect.Inflate(bounds, -1, -1), 4, 4);
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>A line showing where a dragged task will land.</summary>
internal sealed class InsertionAdorner : Adorner
{
    private static readonly Pen Line = CreatePen();

    internal InsertionAdorner(UIElement adornedElement)
        : base(adornedElement) => IsHitTestVisible = false;

    /// <summary>Whether the line sits at the bottom edge rather than the top.</summary>
    internal bool Below { get; init; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        double y = Below ? AdornedElement.RenderSize.Height : 0;
        drawingContext.DrawLine(Line, new Point(0, y), new Point(AdornedElement.RenderSize.Width, y));
    }

    private static Pen CreatePen()
    {
        var brush = new SolidColorBrush(Color.FromRgb(76, 141, 255));
        brush.Freeze();
        var pen = new Pen(brush, 3);
        pen.Freeze();
        return pen;
    }
}
