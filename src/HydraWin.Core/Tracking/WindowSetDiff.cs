namespace HydraWin.Core.Tracking;

/// <summary>What a reconciliation sweep found to have changed since the last one.</summary>
/// <param name="Added">Windows now trackable that were not in the previous set.</param>
/// <param name="Removed">Windows in the previous set that are no longer trackable.</param>
/// <param name="TitleChanged">
/// Windows present in both sets whose title differs, as (existing inventory entry, new title)
/// pairs. The entry still carries the <em>old</em> title so the caller can raise
/// <c>WindowTitleChanged(old, new)</c> before applying <c>NewTitle</c> to it.
/// </param>
public readonly record struct WindowSetChanges(
    IReadOnlyList<TrackedWindow> Added,
    IReadOnlyList<TrackedWindow> Removed,
    IReadOnlyList<(TrackedWindow Existing, string NewTitle)> TitleChanged)
{
    /// <summary>True when the sweep found nothing to report.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && TitleChanged.Count == 0;
}

/// <summary>
/// Diffs a freshly enumerated window set against the current inventory. Pure: no Win32, so the
/// reconciliation logic is unit-testable on its own.
/// </summary>
public static class WindowSetDiff
{
    /// <summary>
    /// Compares <paramref name="current"/> against <paramref name="previous"/> by window handle.
    /// </summary>
    /// <param name="previous">The inventory as it stands, keyed by handle.</param>
    /// <param name="current">The freshly enumerated trackable windows.</param>
    public static WindowSetChanges Compute(
        IReadOnlyDictionary<nint, TrackedWindow> previous,
        IReadOnlyCollection<TrackedWindow> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        List<TrackedWindow> added = [];
        List<(TrackedWindow, string)> titleChanged = [];
        HashSet<nint> seen = [];

        foreach (TrackedWindow window in current)
        {
            seen.Add(window.Hwnd);

            if (!previous.TryGetValue(window.Hwnd, out TrackedWindow? existing))
            {
                added.Add(window);
            }
            else if (!string.Equals(existing.Title, window.Title, StringComparison.Ordinal))
            {
                titleChanged.Add((existing, window.Title));
            }
        }

        List<TrackedWindow> removed =
            [.. previous.Where(entry => !seen.Contains(entry.Key)).Select(entry => entry.Value)];

        return new WindowSetChanges(added, removed, titleChanged);
    }
}
