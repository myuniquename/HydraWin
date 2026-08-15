namespace HydraWin.Core.Tracking;

/// <summary>
/// Stand-in for <see cref="IHiddenWindowSet"/> until task 06's switch engine provides the real
/// one. HydraWin has hidden nothing, so nothing is exempt from the visibility clause.
/// </summary>
public sealed class EmptyHiddenWindowSet : IHiddenWindowSet
{
    /// <summary>The shared instance.</summary>
    public static EmptyHiddenWindowSet Instance { get; } = new();

    private EmptyHiddenWindowSet()
    {
    }

    /// <inheritdoc />
    public bool Contains(nint hwnd) => false;
}
