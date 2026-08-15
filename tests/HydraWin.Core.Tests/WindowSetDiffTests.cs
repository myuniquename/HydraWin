using HydraWin.Core.Tracking;

namespace HydraWin.Core.Tests;

/// <summary>
/// The reconciliation diff. Pure, so the sweep's logic is tested without enumerating a desktop.
/// </summary>
public class WindowSetDiffTests
{
    private static TrackedWindow Window(nint hwnd, string title) => new()
    {
        Hwnd = hwnd,
        Pid = 42,
        ProcessPath = @"C:\Windows\System32\notepad.exe",
        Title = title,
    };

    private static Dictionary<nint, TrackedWindow> Inventory(params TrackedWindow[] windows) =>
        windows.ToDictionary(w => w.Hwnd);

    [Fact]
    public void AnUnchangedSetReportsNothing()
    {
        TrackedWindow existing = Window(1, "Untitled - Notepad");

        WindowSetChanges changes = WindowSetDiff.Compute(
            Inventory(existing),
            [Window(1, "Untitled - Notepad")]);

        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void ANewWindowIsReportedAsAdded()
    {
        WindowSetChanges changes = WindowSetDiff.Compute(
            Inventory(Window(1, "First")),
            [Window(1, "First"), Window(2, "Second")]);

        TrackedWindow added = Assert.Single(changes.Added);
        Assert.Equal(2, added.Hwnd);
        Assert.Empty(changes.Removed);
        Assert.Empty(changes.TitleChanged);
    }

    [Fact]
    public void AVanishedWindowIsReportedAsRemoved()
    {
        WindowSetChanges changes = WindowSetDiff.Compute(
            Inventory(Window(1, "First"), Window(2, "Second")),
            [Window(1, "First")]);

        TrackedWindow removed = Assert.Single(changes.Removed);
        Assert.Equal(2, removed.Hwnd);
        Assert.Empty(changes.Added);
    }

    [Fact]
    public void ARetitledWindowReportsTheExistingEntryAndTheNewTitle()
    {
        TrackedWindow existing = Window(1, "Untitled - Notepad");

        WindowSetChanges changes = WindowSetDiff.Compute(
            Inventory(existing),
            [Window(1, "*Untitled - Notepad")]);

        (TrackedWindow entry, string newTitle) = Assert.Single(changes.TitleChanged);

        // The entry still carries the old title so the caller can raise (old, new) before applying.
        Assert.Same(existing, entry);
        Assert.Equal("Untitled - Notepad", entry.Title);
        Assert.Equal("*Untitled - Notepad", newTitle);
        Assert.Empty(changes.Added);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public void TitleComparisonIsCaseSensitive()
    {
        WindowSetChanges changes = WindowSetDiff.Compute(
            Inventory(Window(1, "notepad")),
            [Window(1, "Notepad")]);

        Assert.Single(changes.TitleChanged);
    }

    [Fact]
    public void AddedRemovedAndRetitledAreReportedTogether()
    {
        WindowSetChanges changes = WindowSetDiff.Compute(
            Inventory(Window(1, "Stays"), Window(2, "Goes"), Window(3, "Old")),
            [Window(1, "Stays"), Window(3, "New"), Window(4, "Arrives")]);

        Assert.Equal(4, Assert.Single(changes.Added).Hwnd);
        Assert.Equal(2, Assert.Single(changes.Removed).Hwnd);
        Assert.Equal(3, Assert.Single(changes.TitleChanged).Existing.Hwnd);
        Assert.False(changes.IsEmpty);
    }

    [Fact]
    public void AnEmptyInventoryReportsEverythingAsAdded()
    {
        WindowSetChanges changes = WindowSetDiff.Compute(
            new Dictionary<nint, TrackedWindow>(),
            [Window(1, "First"), Window(2, "Second")]);

        Assert.Equal(2, changes.Added.Count);
        Assert.Empty(changes.Removed);
    }

    [Fact]
    public void AnEmptyDesktopReportsEverythingAsRemoved()
    {
        WindowSetChanges changes = WindowSetDiff.Compute(
            Inventory(Window(1, "First"), Window(2, "Second")),
            []);

        Assert.Equal(2, changes.Removed.Count);
        Assert.Empty(changes.Added);
    }
}
