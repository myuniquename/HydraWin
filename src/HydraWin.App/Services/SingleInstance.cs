namespace HydraWin.App.Services;

/// <summary>
/// Keeps one HydraWin running, and lets a second launch surface the one that is already there.
/// </summary>
/// <remarks>
/// <para>
/// A named event rather than a pipe or <c>WM_COPYDATA</c>: the second instance has nothing to
/// <em>say</em>. "Show yourself" is the entire payload, so a signal is the whole protocol — no
/// server loop, no message pump, no serialization.
/// </para>
/// <para>
/// <c>--restore-all</c> never comes through here. That path returns before any of this, because it
/// has to work while a wedged first instance still holds the mutex.
/// </para>
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\HydraWinSingleton";
    private const string ShowEventName = @"Local\HydraWinShowWindow";

    private readonly Mutex mutex;
    private readonly EventWaitHandle showRequested;
    private readonly ManualResetEvent stopping = new(false);

    private Thread? listener;
    private bool disposed;

    internal SingleInstance()
    {
        mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        IsFirstInstance = createdNew;
        showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    /// <summary>Whether this process is the one that should run the UI.</summary>
    internal bool IsFirstInstance { get; }

    /// <summary>Asks the instance that is already running to show its window.</summary>
    internal void AskFirstInstanceToShow() => showRequested.Set();

    /// <summary>
    /// Watches for a later launch asking us to surface. The thread is a background one, so it can
    /// never hold the process open.
    /// </summary>
    internal void ListenForShowRequests(Action show)
    {
        ArgumentNullException.ThrowIfNull(show);

        listener = new Thread(() =>
        {
            WaitHandle[] waits = [showRequested, stopping];
            while (WaitHandle.WaitAny(waits) == 0)
            {
                show();
            }
        })
        {
            IsBackground = true,
            Name = "HydraWin single-instance listener",
        };

        listener.Start();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        stopping.Set();
        listener?.Join(TimeSpan.FromSeconds(1));

        if (IsFirstInstance)
        {
            mutex.ReleaseMutex();
        }

        mutex.Dispose();
        showRequested.Dispose();
        stopping.Dispose();
    }
}
