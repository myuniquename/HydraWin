namespace HydraWin.Core.Tracking;

/// <summary>
/// Read-only view of the windows HydraWin currently has hidden. The tracker consumes this so a
/// hidden window stays in the inventory instead of looking like one the app closed.
/// </summary>
/// <remarks>
/// Task 06's switch engine implements this over the recovery journal, which is the single source
/// of truth for "windows HydraWin currently has hidden". Until then
/// <see cref="EmptyHiddenWindowSet"/> stands in.
/// </remarks>
public interface IHiddenWindowSet
{
    /// <summary>True when HydraWin hid this window.</summary>
    bool Contains(nint hwnd);
}
