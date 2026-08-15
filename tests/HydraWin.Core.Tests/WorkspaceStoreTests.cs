using HydraWin.Core.Persistence;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>The debounce wrapper around <see cref="JsonStore{T}"/>.</summary>
public sealed class WorkspaceStoreTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    public WorkspaceStoreTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string StatePath => Path.Combine(directory, "state.json");

    [Fact]
    public void ADebouncedSaveDoesNotHitTheDiskImmediately()
    {
        using var store = new WorkspaceStore(StatePath, TimeSpan.FromMinutes(5));

        store.SaveDebounced(new WorkspaceState());

        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public void FlushWritesThePendingState()
    {
        using var store = new WorkspaceStore(StatePath, TimeSpan.FromMinutes(5));
        store.SaveDebounced(new WorkspaceState
        {
            Tasks = [new HydraWinTask { Name = "Alpha", Order = 1 }],
        });

        store.Flush();

        Assert.Equal("Alpha", Assert.Single(store.Load().Tasks).Name);
    }

    [Fact]
    public void RepeatedSavesCoalesceIntoTheLastOne()
    {
        using var store = new WorkspaceStore(StatePath, TimeSpan.FromMinutes(5));

        for (int i = 1; i <= 20; i++)
        {
            store.SaveDebounced(new WorkspaceState
            {
                Tasks = [new HydraWinTask { Name = $"Task {i}", Order = 1 }],
            });
        }

        store.Flush();

        Assert.Equal("Task 20", Assert.Single(store.Load().Tasks).Name);
    }

    [Fact]
    public void FlushingWithNothingPendingIsHarmless()
    {
        using var store = new WorkspaceStore(StatePath, TimeSpan.FromMinutes(5));

        store.Flush();

        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public void DisposingWritesAnythingStillPending()
    {
        using (var store = new WorkspaceStore(StatePath, TimeSpan.FromMinutes(5)))
        {
            store.SaveDebounced(new WorkspaceState
            {
                Tasks = [new HydraWinTask { Name = "Alpha", Order = 1 }],
            });
        }

        using var reopened = new WorkspaceStore(StatePath, TimeSpan.FromMinutes(5));
        Assert.Equal("Alpha", Assert.Single(reopened.Load().Tasks).Name);
    }

    [Fact]
    public void AFailedWriteIsReportedRatherThanThrownAndTheStateStaysPending()
    {
        // The debounced write runs on a timer thread, where an unhandled exception would kill the
        // process - with the user's windows possibly hidden. Losing preferences is the lesser evil.
        string missing = Path.Combine(directory, "gone", "state.json");
        using var store = new WorkspaceStore(missing, TimeSpan.FromMinutes(5));
        Directory.CreateDirectory(Path.Combine(directory, "gone"));

        Exception? reported = null;
        store.SaveFailed += (_, ex) => reported = ex;
        store.SaveDebounced(new WorkspaceState
        {
            Tasks = [new HydraWinTask { Name = "Alpha", Order = 1 }],
        });

        // Remove the directory out from under it, then flush.
        Directory.Delete(Path.Combine(directory, "gone"), recursive: true);
        File.WriteAllText(Path.Combine(directory, "gone"), "now a file, so the directory cannot exist");

        store.Flush();

        Assert.NotNull(reported);

        // Still pending: once the obstruction is gone, a retry succeeds.
        File.Delete(Path.Combine(directory, "gone"));
        store.Flush();
        Assert.Equal("Alpha", Assert.Single(store.Load().Tasks).Name);
    }
}
