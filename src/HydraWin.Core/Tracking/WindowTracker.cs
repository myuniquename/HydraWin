using System.Diagnostics;
using HydraWin.Core.Interop;

namespace HydraWin.Core.Tracking;

/// <summary>
/// Maintains the live inventory of trackable top-level windows and raises change events.
/// </summary>
/// <remarks>
/// <para>
/// WinEvent hooks are the fast path and the ~2 s reconciliation sweep is the truth: anything the
/// hooks miss or mis-order is corrected by the next sweep.
/// </para>
/// <para>
/// <see cref="Start"/> <b>must</b> be called on a thread with a running message pump — the WPF
/// dispatcher thread — because <c>WINEVENT_OUTOFCONTEXT</c> callbacks are delivered through the
/// message queue and never fire otherwise.
/// </para>
/// </remarks>
public sealed class WindowTracker : IDisposable
{
    private static readonly uint[] HookedEvents =
    [
        NativeMethods.EVENT_SYSTEM_FOREGROUND,
        NativeMethods.EVENT_OBJECT_DESTROY,
        NativeMethods.EVENT_OBJECT_SHOW,
        NativeMethods.EVENT_OBJECT_HIDE,
        NativeMethods.EVENT_OBJECT_NAMECHANGE,
    ];

    private readonly IHiddenWindowSet hiddenWindows;
    private readonly Dictionary<nint, TrackedWindow> windows = [];
    private readonly List<nint> hooks = [];
    private readonly Lock gate = new();
    private readonly int ownProcessId = Environment.ProcessId;

    /// <summary>
    /// The WinEvent callback, rooted for as long as this tracker lives. A delegate handed
    /// straight to <c>SetWinEventHook</c> — or held in a local — gets collected and the hook dies
    /// silently (repo gotcha). It is a readonly field assigned in the constructor precisely so
    /// that neither the GC nor a well-meaning refactor can shorten its lifetime.
    /// </summary>
    private readonly NativeMethods.WinEventProc winEventProc;

    private SynchronizationContext? context;
    private Timer? reconcileTimer;
    private int sweeping;
    private bool started;
    private bool disposed;

    /// <summary>Creates a tracker over the given hidden-window view.</summary>
    public WindowTracker(IHiddenWindowSet hiddenWindows)
    {
        ArgumentNullException.ThrowIfNull(hiddenWindows);
        this.hiddenWindows = hiddenWindows;
        winEventProc = OnWinEvent;
    }

    /// <summary>A window entered the inventory.</summary>
    public event EventHandler<TrackedWindow>? WindowAppeared;

    /// <summary>A window left the inventory.</summary>
    public event EventHandler<TrackedWindow>? WindowDisappeared;

    /// <summary>A tracked window's title changed.</summary>
    public event EventHandler<WindowTitleChangedEventArgs>? WindowTitleChanged;

    /// <summary>The foreground window changed.</summary>
    public event EventHandler<nint>? ForegroundChanged;

    /// <summary>The most recent foreground window, consumed by task 06's focus restore.</summary>
    public nint LastForegroundWindow { get; private set; }

    /// <summary>
    /// Whether the reconciliation sweep runs. Exposed so the task 03 harness can prove the hooks
    /// track windows on their own; leave it on everywhere else.
    /// </summary>
    public bool ReconciliationEnabled { get; set; } = true;

    /// <summary>How often the reconciliation sweep runs.</summary>
    public TimeSpan ReconciliationInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>A snapshot of the current inventory.</summary>
    public IReadOnlyCollection<TrackedWindow> Windows
    {
        get
        {
            lock (gate)
            {
                return [.. windows.Values];
            }
        }
    }

    /// <summary>
    /// Runs the initial sweep and registers the WinEvent hooks. Must be called on a thread with a
    /// message pump; see the remarks on this type.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }

        started = true;
        context = SynchronizationContext.Current;

        Reconcile();

        foreach (uint eventId in HookedEvents)
        {
            nint hook = NativeMethods.HookWinEvent(eventId, winEventProc);
            if (hook != 0)
            {
                hooks.Add(hook);
            }
        }

        // The sweep runs on the timer thread, not the UI thread: it probes every top-level window
        // on the desktop, and only the resulting events are marshalled back. `sweeping` guards
        // against a slow sweep overlapping the next tick.
        reconcileTimer = new Timer(
            _ =>
            {
                if (!ReconciliationEnabled || Interlocked.Exchange(ref sweeping, 1) == 1)
                {
                    return;
                }

                try
                {
                    Reconcile();
                }
                finally
                {
                    Interlocked.Exchange(ref sweeping, 0);
                }
            },
            null,
            ReconciliationInterval,
            ReconciliationInterval);
    }

    /// <summary>Unhooks everything and stops the sweep.</summary>
    public void Stop()
    {
        if (!started)
        {
            return;
        }

        started = false;

        reconcileTimer?.Dispose();
        reconcileTimer = null;

        NativeMethods.UnhookAll(hooks);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        disposed = true;
    }

    /// <summary>
    /// Enumerates every top-level window and reports why each was accepted or rejected. Used by
    /// the task 03 harness to make "these windows are absent" a verifiable claim rather than an
    /// eyeball check.
    /// </summary>
    public IReadOnlyList<(nint Hwnd, string Title, TrackableVerdict Verdict)> Explain()
    {
        List<(nint, string, TrackableVerdict)> result = [];
        foreach (nint hwnd in NativeMethods.EnumerateTopLevelWindows())
        {
            WindowFacts facts = WindowProbe.GetFacts(hwnd, hiddenWindows);
            result.Add((hwnd, facts.Title, WindowFilter.Evaluate(in facts, ownProcessId)));
        }

        return result;
    }

    /// <summary>Full re-enumeration diffed against the inventory. The sweep is the truth.</summary>
    private void Reconcile()
    {
        List<TrackedWindow> current = [];
        foreach (nint hwnd in NativeMethods.EnumerateTopLevelWindows())
        {
            WindowFacts facts = WindowProbe.GetFacts(hwnd, hiddenWindows);
            if (WindowFilter.IsTrackable(in facts, ownProcessId))
            {
                current.Add(WindowProbe.CreateTrackedWindow(in facts));
            }
        }

        WindowSetChanges changes;
        lock (gate)
        {
            changes = WindowSetDiff.Compute(windows, current);

            foreach (TrackedWindow added in changes.Added)
            {
                windows.TryAdd(added.Hwnd, added);
            }

            foreach (TrackedWindow removed in changes.Removed)
            {
                windows.Remove(removed.Hwnd);
            }
        }

        foreach (TrackedWindow added in changes.Added)
        {
            Raise(WindowAppeared, added);
        }

        foreach (TrackedWindow removed in changes.Removed)
        {
            Raise(WindowDisappeared, removed);
        }

        foreach ((TrackedWindow existing, string newTitle) in changes.TitleChanged)
        {
            ApplyTitle(existing, newTitle);
        }
    }

    private void OnWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        // Controls and menu items raise these too; only the window's own object is interesting.
        if (hwnd == 0 || idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
        {
            return;
        }

        if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            LastForegroundWindow = hwnd;
            Raise(ForegroundChanged, hwnd);
            return;
        }

        if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
        {
            RemoveIfPresent(hwnd);
            return;
        }

        // SHOW, HIDE and NAMECHANGE all reduce to "re-evaluate this window against the filter".
        // A HIDE for a window that is not in the HydraWin-hidden set means the app hid or closed
        // it, and re-evaluation drops it for exactly that reason.
        ReEvaluate(hwnd);
    }

    private void ReEvaluate(nint hwnd)
    {
        WindowFacts facts = WindowProbe.GetFacts(hwnd, hiddenWindows);
        bool trackable = WindowFilter.IsTrackable(in facts, ownProcessId);

        if (!trackable)
        {
            RemoveIfPresent(hwnd);
            return;
        }

        TrackedWindow? existing;
        lock (gate)
        {
            windows.TryGetValue(hwnd, out existing);
        }

        if (existing is null)
        {
            TrackedWindow candidate = WindowProbe.CreateTrackedWindow(in facts);

            // TryAdd, not an indexer assignment: the hook thread and the sweep thread can both
            // reach here for the same window, and only the one that actually inserted may raise
            // WindowAppeared — otherwise the UI gets the same window twice.
            bool added;
            lock (gate)
            {
                added = windows.TryAdd(hwnd, candidate);
            }

            if (added)
            {
                Raise(WindowAppeared, candidate);
            }

            return;
        }

        // Windows commonly gain their real title shortly after creation.
        existing.IsHydraWinHidden = facts.IsHydraWinHidden;
        ApplyTitle(existing, facts.Title);
    }

    private void ApplyTitle(TrackedWindow window, string newTitle)
    {
        string oldTitle = window.Title;
        if (string.Equals(oldTitle, newTitle, StringComparison.Ordinal))
        {
            return;
        }

        window.Title = newTitle;
        Raise(WindowTitleChanged, new WindowTitleChangedEventArgs(window, oldTitle, newTitle));
    }

    private void RemoveIfPresent(nint hwnd)
    {
        TrackedWindow? removed;
        lock (gate)
        {
            if (!windows.Remove(hwnd, out removed))
            {
                return;
            }
        }

        Raise(WindowDisappeared, removed);
    }

    private void Raise<T>(EventHandler<T>? handler, T payload)
    {
        if (handler is not null)
        {
            Post(() => handler(this, payload));
        }
    }

    /// <summary>Marshals onto the context captured at <see cref="Start"/> so WPF can bind directly.</summary>
    private void Post(Action action)
    {
        if (context is null || context == SynchronizationContext.Current)
        {
            action();
            return;
        }

        context.Post(
            state =>
            {
                try
                {
                    ((Action)state!)();
                }
                catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
                {
                    // The UI went away between posting and running; nothing useful to do.
                    Debug.WriteLine($"WindowTracker: dropped a posted update ({ex.GetType().Name}).");
                }
            },
            action);
    }
}

/// <summary>Payload for <see cref="WindowTracker.WindowTitleChanged"/>.</summary>
/// <param name="Window">The window whose title changed; it already carries the new title.</param>
/// <param name="OldTitle">The title before the change.</param>
/// <param name="NewTitle">The title after the change.</param>
public sealed record WindowTitleChangedEventArgs(TrackedWindow Window, string OldTitle, string NewTitle);
