using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Persistence;

/// <summary>
/// The workspace document at <c>%APPDATA%\HydraWin\state.json</c>, with a short write debounce.
/// </summary>
/// <remarks>
/// Dragging a window between tasks can mutate the model many times a second; a debounce keeps
/// that off the disk without the caller having to think about it. <see cref="Flush"/> forces a
/// pending write out, and must be called on shutdown.
/// </remarks>
public sealed class WorkspaceStore : IDisposable
{
    private readonly JsonStore<WorkspaceState> store;
    private readonly Lock gate = new();
    private readonly TimeSpan debounce;

    private Timer? timer;
    private WorkspaceState? pending;
    private bool disposed;

    /// <summary>Creates the store.</summary>
    /// <param name="path">Document path; defaults to <see cref="HydraWinPaths.StateFile"/>.</param>
    /// <param name="debounce">Write delay; defaults to one second.</param>
    public WorkspaceStore(string? path = null, TimeSpan? debounce = null)
    {
        store = new JsonStore<WorkspaceState>(path ?? HydraWinPaths.StateFile);
        this.debounce = debounce ?? TimeSpan.FromSeconds(1);
        store.CorruptFileQuarantined += (_, quarantined) =>
            CorruptFileQuarantined?.Invoke(this, quarantined);
    }

    /// <summary>Raised when a corrupt <c>state.json</c> was set aside and defaults were used.</summary>
    public event EventHandler<string>? CorruptFileQuarantined;

    /// <summary>
    /// Raised when a write failed. The state stays pending so the next <see cref="Flush"/> or
    /// debounce retries it.
    /// </summary>
    public event EventHandler<Exception>? SaveFailed;

    /// <summary>The document's path.</summary>
    public string Path => store.Path;

    /// <summary>Reads the document, or returns a fresh one.</summary>
    public WorkspaceState Load() => store.Load();

    /// <summary>Queues a write, coalescing with any already pending.</summary>
    public void SaveDebounced(WorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            pending = state;
            timer ??= new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            timer.Change(debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes any pending state immediately. Safe to call when nothing is pending.</summary>
    public void Flush()
    {
        WorkspaceState? toWrite;
        lock (gate)
        {
            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            toWrite = pending;
            pending = null;
        }

        if (toWrite is null)
        {
            return;
        }

        try
        {
            store.Save(toWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // This runs on a timer thread, where an unhandled exception kills the process. Losing
            // the task layout is bad; taking HydraWin down with the user's windows hidden is far
            // worse. Keep the state pending so a later flush can retry.
            lock (gate)
            {
                pending ??= toWrite;
            }

            SaveFailed?.Invoke(this, ex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Flush();

        lock (gate)
        {
            timer?.Dispose();
            timer = null;
            disposed = true;
        }
    }
}
