using HydraWin.Core.Recovery;
using HydraWin.Core.Tracking;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// The set of windows HydraWin currently has hidden, as the window tracker sees it.
/// </summary>
/// <remarks>
/// <para>
/// The recovery journal is the durable source of truth; this is its hot-path cache. It has to be:
/// <see cref="WindowTracker"/> asks <see cref="Contains"/> about <em>every</em> top-level window
/// on <em>every</em> reconciliation sweep — around 400 handles every two seconds on this desktop —
/// and answering each from <see cref="RecoveryJournal.Snapshot"/> would mean hundreds of
/// mutex-guarded file reads per second.
/// </para>
/// <para>
/// Seeded from the journal at construction, so a HydraWin that starts up with windows still hidden
/// keeps tracking them, and updated by the switch engine on every hide and show.
/// </para>
/// </remarks>
public sealed class HiddenWindowSet : IHiddenWindowSet
{
    private readonly HashSet<nint> hidden = [];
    private readonly Lock gate = new();

    /// <summary>Creates an empty set.</summary>
    public HiddenWindowSet()
    {
    }

    /// <summary>Creates a set seeded from what the journal says is currently hidden.</summary>
    public HiddenWindowSet(RecoveryJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ResetFrom(journal);
    }

    /// <summary>How many windows are currently hidden.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return hidden.Count;
            }
        }
    }

    /// <inheritdoc />
    public bool Contains(nint hwnd)
    {
        lock (gate)
        {
            return hidden.Contains(hwnd);
        }
    }

    /// <summary>Records that a window is now hidden.</summary>
    public void MarkHidden(nint hwnd)
    {
        lock (gate)
        {
            hidden.Add(hwnd);
        }
    }

    /// <summary>Records that a window is visible again.</summary>
    public void MarkShown(nint hwnd)
    {
        lock (gate)
        {
            hidden.Remove(hwnd);
        }
    }

    /// <summary>Re-seeds from the journal — used after a bulk restore.</summary>
    public void ResetFrom(RecoveryJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        List<nint> current = [.. journal.Snapshot().Select(e => (nint)e.Hwnd)];
        lock (gate)
        {
            hidden.Clear();
            foreach (nint hwnd in current)
            {
                hidden.Add(hwnd);
            }
        }
    }
}
