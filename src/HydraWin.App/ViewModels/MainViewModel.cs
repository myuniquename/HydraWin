using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HydraWin.Core.Tracking;

namespace HydraWin.App.ViewModels;

/// <summary>
/// Root view model for the main window.
/// </summary>
/// <remarks>
/// Everything below the title is task 03's throwaway debug harness — a live view of the tracker
/// plus the rejected windows and their reasons, which is what makes "these windows are absent"
/// a verifiable claim. Task 07 replaces all of it with the real task table.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly WindowTracker tracker;
    private bool disposed;

    public MainViewModel()
        : this(new WindowTracker(EmptyHiddenWindowSet.Instance))
    {
    }

    public MainViewModel(WindowTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        this.tracker = tracker;

        tracker.WindowAppeared += OnWindowAppeared;
        tracker.WindowDisappeared += OnWindowDisappeared;
        tracker.WindowTitleChanged += OnWindowTitleChanged;
        tracker.ForegroundChanged += OnForegroundChanged;
    }

    /// <summary>Window title; task 07 makes it <c>HydraWin — &lt;active task&gt;</c>.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "HydraWin";

    /// <summary>Status line under the lists.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "not started";

    /// <summary>
    /// Count of rejections per clause. This is what makes "tool windows and UWP ghosts are
    /// absent" checkable: every clause should be visibly firing, and HydraWin's own windows
    /// should show up under <see cref="TrackableVerdict.OwnProcess"/>.
    /// </summary>
    [ObservableProperty]
    public partial string RejectionSummary { get; set; } = string.Empty;

    /// <summary>Whether the ~2 s reconciliation sweep runs; off proves the hooks work alone.</summary>
    [ObservableProperty]
    public partial bool ReconciliationEnabled { get; set; } = true;

    /// <summary>The live inventory, in the order the tracker reported it.</summary>
    public ObservableCollection<TrackedWindow> Windows { get; } = [];

    /// <summary>Windows that failed the filter, with the clause that rejected them.</summary>
    public ObservableCollection<RejectedWindow> Rejected { get; } = [];

    /// <summary>Starts tracking. Must be called on the dispatcher thread.</summary>
    public void Start()
    {
        tracker.Start();
        foreach (TrackedWindow window in tracker.Windows)
        {
            Windows.Add(window);
        }

        RefreshRejected();
        UpdateStatus("started");
    }

    /// <summary>Re-runs the explain pass that feeds the rejection pane.</summary>
    [RelayCommand]
    public void RefreshRejected()
    {
        Rejected.Clear();

        List<RejectedWindow> rejected =
        [
            .. tracker.Explain()
                .Where(entry => entry.Verdict != TrackableVerdict.Trackable)
                .Select(entry => new RejectedWindow(entry.Hwnd, entry.Title, entry.Verdict))
                // Noisiest clauses last so the interesting ones are visible without scrolling.
                .OrderBy(r => r.Verdict == TrackableVerdict.NoTitle ? 1 : 0)
                .ThenBy(r => r.Verdict),
        ];

        foreach (RejectedWindow window in rejected)
        {
            Rejected.Add(window);
        }

        RejectionSummary = string.Join(
            "   ",
            rejected.GroupBy(r => r.Verdict)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}={group.Count()}"));

        UpdateStatus("rejections refreshed");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        tracker.WindowAppeared -= OnWindowAppeared;
        tracker.WindowDisappeared -= OnWindowDisappeared;
        tracker.WindowTitleChanged -= OnWindowTitleChanged;
        tracker.ForegroundChanged -= OnForegroundChanged;
        tracker.Dispose();
        disposed = true;
    }

    partial void OnReconciliationEnabledChanged(bool value)
    {
        tracker.ReconciliationEnabled = value;
        UpdateStatus(value ? "sweep on" : "sweep OFF (hooks only)");
    }

    private void OnWindowAppeared(object? sender, TrackedWindow window)
    {
        Windows.Add(window);
        UpdateStatus($"+ {window.ProcessFileName}");
    }

    private void OnWindowDisappeared(object? sender, TrackedWindow window)
    {
        Windows.Remove(window);
        UpdateStatus($"- {window.ProcessFileName}");
    }

    private void OnWindowTitleChanged(object? sender, WindowTitleChangedEventArgs e) =>
        UpdateStatus($"~ {e.Window.ProcessFileName}: {e.NewTitle}");

    private void OnForegroundChanged(object? sender, nint hwnd) =>
        UpdateStatus($"foreground 0x{hwnd:X}");

    private void UpdateStatus(string detail) =>
        Status = $"{Windows.Count} tracked, {Rejected.Count} rejected — {detail} "
            + $"({DateTime.Now:HH:mm:ss})";
}

/// <summary>A window the filter excluded, and why.</summary>
/// <param name="Hwnd">The window handle.</param>
/// <param name="Title">Its title, which may be empty.</param>
/// <param name="Verdict">The clause that rejected it.</param>
public sealed record RejectedWindow(nint Hwnd, string Title, TrackableVerdict Verdict)
{
    /// <summary>Display form for the harness list.</summary>
    public string Display => $"[{Verdict}] 0x{Hwnd:X} {Title}";
}
