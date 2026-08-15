using HydraWin.Core.Persistence;

namespace HydraWin.Core.Recovery;

/// <summary>
/// The write-ahead record of every window HydraWin currently has hidden.
/// </summary>
/// <remarks>
/// <para>
/// Writes are synchronous and never debounced: <see cref="RecordBeforeHide"/> returns only once
/// the bytes are on disk, because the caller hides windows the moment it does. That ordering is
/// the project's one invariant.
/// </para>
/// <para>
/// <b>Cross-process safe on purpose.</b> Task 01's spike hit exactly this: two processes writing
/// the journal at the same instant collided and one write was lost. <c>--restore-all</c> can
/// legitimately run while the UI process is live, so every read-modify-write here is serialised
/// by a named mutex. <c>state.json</c> needs none of this — it has a single writer.
/// </para>
/// </remarks>
public sealed class RecoveryJournal : IDisposable
{
    private const string MutexName = @"Local\HydraWinRecoveryJournal";

    private readonly JsonStore<List<JournalEntry>> store;
    private readonly Mutex mutex;
    private bool disposed;

    /// <summary>Creates the journal.</summary>
    /// <param name="path">Document path; defaults to <see cref="HydraWinPaths.JournalFile"/>.</param>
    public RecoveryJournal(string? path = null)
    {
        store = new JsonStore<List<JournalEntry>>(path ?? HydraWinPaths.JournalFile);
        mutex = new Mutex(initiallyOwned: false, MutexName);
    }

    /// <summary>The journal's path.</summary>
    public string Path => store.Path;

    /// <summary>Whether anything is currently recorded as hidden.</summary>
    public bool IsEmpty => Snapshot().Count == 0;

    /// <summary>
    /// Records windows about to be hidden and flushes to disk before returning. Call this, let it
    /// return, and only then hide them — never the other way round.
    /// </summary>
    public void RecordBeforeHide(IEnumerable<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        List<JournalEntry> incoming = [.. entries];
        if (incoming.Count == 0)
        {
            return;
        }

        Locked(() =>
        {
            List<JournalEntry> current = store.Load();

            // Re-hiding a window that is somehow already journaled replaces its entry rather than
            // duplicating it; the newest placement is the one worth restoring.
            foreach (JournalEntry entry in incoming)
            {
                current.RemoveAll(e => e.Hwnd == entry.Hwnd);
                current.Add(entry);
            }

            store.Save(current);
        });
    }

    /// <summary>Removes a window's entry after it has been successfully shown again.</summary>
    public void ConfirmShown(long hwnd) => Locked(() =>
    {
        List<JournalEntry> current = store.Load();
        if (current.RemoveAll(e => e.Hwnd == hwnd) > 0)
        {
            store.Save(current);
        }
    });

    /// <summary>Everything currently recorded as hidden.</summary>
    public IReadOnlyList<JournalEntry> Snapshot()
    {
        List<JournalEntry> entries = [];
        Locked(() => entries = store.Load());
        return entries;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        mutex.Dispose();
        disposed = true;
    }

    private void Locked(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        bool held;
        try
        {
            held = mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died without releasing — which is precisely the crash this
            // journal exists for. We now own the mutex; the file itself is written atomically, so
            // whatever it holds is a complete document.
            held = true;
        }

        try
        {
            action();
        }
        finally
        {
            if (held)
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
