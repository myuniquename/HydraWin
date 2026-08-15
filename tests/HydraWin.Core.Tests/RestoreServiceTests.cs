using HydraWin.Core.Interop;
using HydraWin.Core.Recovery;

namespace HydraWin.Core.Tests;

/// <summary>
/// Identity validation and restore ordering, against a scripted Win32 layer.
/// </summary>
public sealed class RestoreServiceTests : IDisposable
{
    private const int NotepadPid = 4321;
    private const string NotepadPath = @"C:\Windows\System32\notepad.exe";

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeWindowApi api = new();
    private readonly RecoveryJournal journal;
    private readonly RestoreService service;

    public RestoreServiceTests()
    {
        Directory.CreateDirectory(directory);
        journal = new RecoveryJournal(Path.Combine(directory, "journal.json"));
        service = new RestoreService(api);
    }

    public void Dispose()
    {
        journal.Dispose();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JournalEntry Entry(
        long hwnd,
        int pid = NotepadPid,
        string path = NotepadPath) => new()
        {
            Hwnd = hwnd,
            Pid = pid,
            ProcessPath = path,
            TitleAtHide = "Untitled - Notepad",
            HiddenAt = DateTimeOffset.UtcNow,
            Placement = new WindowPlacementDto
            {
                ShowCmd = 3,
                NormalLeft = 100,
                NormalTop = 200,
                NormalRight = 900,
                NormalBottom = 700,
            },
        };

    [Fact]
    public void AMatchingWindowIsRestoredAndItsEntryRemoved()
    {
        api.Add(0x10, NotepadPid, NotepadPath);
        journal.RecordBeforeHide([Entry(0x10)]);

        RestoreSummary summary = service.RestoreAll(journal);

        Assert.Equal(new RestoreSummary(1, 0, 0), summary);
        Assert.True(api.Get(0x10)!.Visible);
        Assert.True(journal.IsEmpty);
    }

    [Fact]
    public void PlacementIsRestoredBeforeTheWindowIsShown()
    {
        // Position first, then visibility: the window should never appear in the wrong place and
        // then jump. Task 06 will assert an equivalent ordering for journal-before-hide.
        api.Add(0x10, NotepadPid, NotepadPath);
        journal.RecordBeforeHide([Entry(0x10)]);

        service.RestoreAll(journal);

        int setPlacement = api.Calls.IndexOf("SetPlacement(0x10)");
        int show = api.Calls.IndexOf("Show(0x10)");
        Assert.True(setPlacement >= 0 && show >= 0);
        Assert.True(setPlacement < show, $"expected placement before show, got: {string.Join(", ", api.Calls)}");
    }

    [Fact]
    public void TheRecordedPlacementIsWhatGetsApplied()
    {
        api.Add(0x10, NotepadPid, NotepadPath);
        journal.RecordBeforeHide([Entry(0x10)]);

        service.RestoreAll(journal);

        WindowPlacement applied = api.Get(0x10)!.Placement;
        Assert.Equal(3, applied.ShowCmd);
        Assert.Equal(100, applied.NormalPosition.Left);
        Assert.Equal(700, applied.NormalPosition.Bottom);
    }

    [Fact]
    public void ADeadHandleIsDroppedWithoutBeingShown()
    {
        // The window was closed while hidden. Nothing to restore; just stop tracking it.
        journal.RecordBeforeHide([Entry(0x10)]);

        RestoreSummary summary = service.RestoreAll(journal);

        Assert.Equal(new RestoreSummary(0, 1, 0), summary);
        Assert.DoesNotContain("Show(0x10)", api.Calls);
        Assert.True(journal.IsEmpty);
    }

    [Fact]
    public void ARecycledHandleWithADifferentProcessIsNeverShown()
    {
        // The dangerous case: Windows handed our old handle to somebody else's window. Showing it
        // would drag an unrelated window into view and move it.
        api.Add(0x10, pid: 9999, path: @"C:\apps\other.exe");
        journal.RecordBeforeHide([Entry(0x10)]);

        RestoreSummary summary = service.RestoreAll(journal);

        Assert.Equal(new RestoreSummary(0, 1, 0), summary);
        Assert.DoesNotContain("Show(0x10)", api.Calls);
        Assert.DoesNotContain("SetPlacement(0x10)", api.Calls);
        Assert.True(journal.IsEmpty);
    }

    [Fact]
    public void ARecycledHandleWithTheSamePidButADifferentImageIsNeverShown()
    {
        // Process ids are recycled too, so the path is the second half of the check.
        api.Add(0x10, NotepadPid, @"C:\apps\something-else.exe");
        journal.RecordBeforeHide([Entry(0x10)]);

        RestoreSummary summary = service.RestoreAll(journal);

        Assert.Equal(new RestoreSummary(0, 1, 0), summary);
        Assert.DoesNotContain("Show(0x10)", api.Calls);
    }

    [Fact]
    public void ProcessPathComparisonIgnoresCase()
    {
        api.Add(0x10, NotepadPid, NotepadPath.ToUpperInvariant());
        journal.RecordBeforeHide([Entry(0x10)]);

        Assert.Equal(1, service.RestoreAll(journal).Restored);
    }

    [Fact]
    public void AWindowThatRefusesToShowStaysInTheJournal()
    {
        // It is still hidden and still ours. Dropping the entry would strand it forever, which is
        // the exact failure this whole task exists to prevent.
        api.Add(0x10, NotepadPid, NotepadPath);
        api.RefuseToShow.Add(0x10);
        journal.RecordBeforeHide([Entry(0x10)]);

        RestoreSummary summary = service.RestoreAll(journal);

        Assert.Equal(new RestoreSummary(0, 0, 1), summary);
        Assert.Single(journal.Snapshot());
    }

    [Fact]
    public void AMixedJournalIsHandledEntryByEntry()
    {
        api.Add(0x10, NotepadPid, NotepadPath);
        api.Add(0x30, pid: 9999, path: @"C:\apps\other.exe");
        journal.RecordBeforeHide([Entry(0x10), Entry(0x20), Entry(0x30)]);

        RestoreSummary summary = service.RestoreAll(journal);

        Assert.Equal(new RestoreSummary(1, 2, 0), summary);
        Assert.True(api.Get(0x10)!.Visible);
        Assert.True(journal.IsEmpty);
    }

    [Fact]
    public void RestoringAnEmptyJournalDoesNothing()
    {
        RestoreSummary summary = service.RestoreAll(journal);

        Assert.Equal(new RestoreSummary(0, 0, 0), summary);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public void TheSummaryReadsAsTheCommandLinePrintsIt()
    {
        Assert.Equal(
            "restored 2 window(s), dropped 1 stale entry",
            new RestoreSummary(2, 1, 0).ToString());
        Assert.Equal(
            "restored 0 window(s), dropped 3 stale entries",
            new RestoreSummary(0, 3, 0).ToString());
        Assert.Equal(
            "restored 1 window(s), dropped 0 stale entries, 2 could not be restored",
            new RestoreSummary(1, 0, 2).ToString());
    }
}
