using HydraWin.Core.Interop;

namespace HydraWin.Core.Recovery;

/// <summary>
/// A serializable mirror of <see cref="WindowPlacement"/>: everything needed to put a window back
/// exactly where it was, in a form that reads sensibly in <c>journal.json</c>.
/// </summary>
/// <remarks>
/// Kept separate from the native struct on purpose. The struct's <c>Length</c> field is a
/// marshalling detail that must be recomputed at call time, never trusted from a file, and flat
/// integers survive a hand-edit far better than a nested native layout would.
/// </remarks>
public sealed class WindowPlacementDto
{
    /// <summary>The SW_* command describing the window's state — this is what restores maximized.</summary>
    public int ShowCmd { get; set; }

    /// <summary>WPF_* flags.</summary>
    public int Flags { get; set; }

    /// <summary>Minimized position, X.</summary>
    public int MinX { get; set; }

    /// <summary>Minimized position, Y.</summary>
    public int MinY { get; set; }

    /// <summary>Maximized position, X.</summary>
    public int MaxX { get; set; }

    /// <summary>Maximized position, Y.</summary>
    public int MaxY { get; set; }

    /// <summary>Restored position, left edge.</summary>
    public int NormalLeft { get; set; }

    /// <summary>Restored position, top edge.</summary>
    public int NormalTop { get; set; }

    /// <summary>Restored position, right edge.</summary>
    public int NormalRight { get; set; }

    /// <summary>Restored position, bottom edge.</summary>
    public int NormalBottom { get; set; }

    /// <summary>Captures a native placement.</summary>
    public static WindowPlacementDto FromPlacement(in WindowPlacement placement) =>
        new()
        {
            ShowCmd = placement.ShowCmd,
            Flags = placement.Flags,
            MinX = placement.MinPosition.X,
            MinY = placement.MinPosition.Y,
            MaxX = placement.MaxPosition.X,
            MaxY = placement.MaxPosition.Y,
            NormalLeft = placement.NormalPosition.Left,
            NormalTop = placement.NormalPosition.Top,
            NormalRight = placement.NormalPosition.Right,
            NormalBottom = placement.NormalPosition.Bottom,
        };

    /// <summary>
    /// Rebuilds the native placement. <c>Length</c> is deliberately left at zero — the interop
    /// wrapper sets it from the real struct size at call time.
    /// </summary>
    public WindowPlacement ToPlacement() =>
        new()
        {
            ShowCmd = ShowCmd,
            Flags = Flags,
            MinPosition = new Point { X = MinX, Y = MinY },
            MaxPosition = new Point { X = MaxX, Y = MaxY },
            NormalPosition = new Rect
            {
                Left = NormalLeft,
                Top = NormalTop,
                Right = NormalRight,
                Bottom = NormalBottom,
            },
        };
}
