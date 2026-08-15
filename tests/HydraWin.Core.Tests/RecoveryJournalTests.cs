using System.Text.Json;
using HydraWin.Core.Recovery;

namespace HydraWin.Core.Tests;

/// <summary>
/// The write-ahead contract. The one thing that matters here is that a record is on disk before
/// the call returns — everything downstream assumes it.
/// </summary>
public sealed class RecoveryJournalTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    public RecoveryJournalTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string JournalPath => Path.Combine(directory, "journal.json");

    private static JournalEntry Entry(long hwnd, int pid = 100, string path = @"C:\apps\notepad.exe") =>
        new()
        {
            Hwnd = hwnd,
            Pid = pid,
            ProcessPath = path,
            TitleAtHide = "Untitled - Notepad",
            HiddenAt = DateTimeOffset.UtcNow,
            Placement = new WindowPlacementDto { ShowCmd = 1, NormalLeft = 10, NormalTop = 20 },
        };

    [Fact]
    public void ANewJournalIsEmpty()
    {
        using var journal = new RecoveryJournal(JournalPath);

        Assert.Empty(journal.Snapshot());
        Assert.True(journal.IsEmpty);
    }

    [Fact]
    public void RecordBeforeHideHasAlreadyWrittenToDiskWhenItReturns()
    {
        // The invariant. If this is ever false, a crash between the call and SW_HIDE loses a
        // window with nothing on disk to recover it from.
        using var journal = new RecoveryJournal(JournalPath);

        journal.RecordBeforeHide([Entry(0x10)]);

        Assert.True(File.Exists(JournalPath));
        string json = File.ReadAllText(JournalPath);
        Assert.Contains("\"Hwnd\": 16", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEntryCarriesIdentityAndPlacement()
    {
        using var journal = new RecoveryJournal(JournalPath);
        journal.RecordBeforeHide([Entry(0x2A, pid: 4321, path: @"C:\apps\Code.exe")]);

        JournalEntry stored = Assert.Single(journal.Snapshot());

        Assert.Equal(0x2A, stored.Hwnd);
        Assert.Equal(4321, stored.Pid);
        Assert.Equal(@"C:\apps\Code.exe", stored.ProcessPath);
        Assert.Equal("Untitled - Notepad", stored.TitleAtHide);
        Assert.Equal(10, stored.Placement.NormalLeft);
        Assert.NotEqual(default, stored.HiddenAt);
    }

    [Fact]
    public void RecordingSeveralWindowsKeepsThemAll()
    {
        using var journal = new RecoveryJournal(JournalPath);

        journal.RecordBeforeHide([Entry(0x10), Entry(0x20), Entry(0x30)]);

        Assert.Equal(3, journal.Snapshot().Count);
    }

    [Fact]
    public void RecordingAppendsToWhatIsAlreadyThere()
    {
        using var journal = new RecoveryJournal(JournalPath);

        journal.RecordBeforeHide([Entry(0x10)]);
        journal.RecordBeforeHide([Entry(0x20)]);

        Assert.Equal([0x10, 0x20], journal.Snapshot().Select(e => e.Hwnd));
    }

    [Fact]
    public void RerecordingTheSameWindowReplacesItRatherThanDuplicating()
    {
        using var journal = new RecoveryJournal(JournalPath);
        journal.RecordBeforeHide([Entry(0x10)]);

        JournalEntry moved = Entry(0x10);
        moved.Placement.NormalLeft = 999;
        journal.RecordBeforeHide([moved]);

        JournalEntry stored = Assert.Single(journal.Snapshot());
        Assert.Equal(999, stored.Placement.NormalLeft);
    }

    [Fact]
    public void RecordingNothingIsHarmless()
    {
        using var journal = new RecoveryJournal(JournalPath);

        journal.RecordBeforeHide([]);

        Assert.True(journal.IsEmpty);
    }

    [Fact]
    public void ConfirmShownRemovesOnlyThatWindowAndFlushes()
    {
        using var journal = new RecoveryJournal(JournalPath);
        journal.RecordBeforeHide([Entry(0x10), Entry(0x20)]);

        journal.ConfirmShown(0x10);

        Assert.Equal([0x20], journal.Snapshot().Select(e => e.Hwnd));
        Assert.DoesNotContain("\"Hwnd\": 16", File.ReadAllText(JournalPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmingAWindowThatWasNeverRecordedIsHarmless()
    {
        using var journal = new RecoveryJournal(JournalPath);
        journal.RecordBeforeHide([Entry(0x10)]);

        journal.ConfirmShown(0x999);

        Assert.Single(journal.Snapshot());
    }

    [Fact]
    public void TheJournalSurvivesAProcessRestart()
    {
        using (var journal = new RecoveryJournal(JournalPath))
        {
            journal.RecordBeforeHide([Entry(0x10)]);
        }

        // Exactly what --restore-all does: a brand-new process reading what the dead one wrote.
        using var reopened = new RecoveryJournal(JournalPath);

        Assert.Equal(0x10, Assert.Single(reopened.Snapshot()).Hwnd);
    }

    [Fact]
    public void ConcurrentWritersDoNotLoseEntries()
    {
        // Task 01's spike hit exactly this: two writers collided and one write vanished. Here two
        // journal instances share the file, as the UI process and --restore-all would.
        using var first = new RecoveryJournal(JournalPath);
        using var second = new RecoveryJournal(JournalPath);

        Parallel.For(0, 40, i =>
        {
            RecoveryJournal target = i % 2 == 0 ? first : second;
            target.RecordBeforeHide([Entry(0x1000 + i)]);
        });

        Assert.Equal(40, first.Snapshot().Count);
    }

    [Fact]
    public void TheDocumentIsReadableJson()
    {
        // A human may have to read this file after a crash to work out what happened.
        using var journal = new RecoveryJournal(JournalPath);
        journal.RecordBeforeHide([Entry(0x10)]);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(JournalPath));

        JsonElement entry = document.RootElement[0];
        Assert.Equal(
            ["Hwnd", "Pid", "ProcessPath", "TitleAtHide", "Placement", "HiddenAt"],
            entry.EnumerateObject().Select(p => p.Name));
    }
}
