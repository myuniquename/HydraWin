using System.Globalization;
using HydraWin.Core.Diagnostics;

namespace HydraWin.Core.Tests;

/// <summary>
/// The activity log. Its contract is short: keep the lines, stay bounded, and never throw at the
/// caller — a log that takes the app down defeats the reason for having one.
/// </summary>
public sealed class AppLogTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private readonly string path;

    public AppLogTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "logs", "hydrawin.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WritingCreatesTheDirectoryAndAppends()
    {
        var log = new AppLog(path);

        log.Write("first");
        log.Write("second");

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("first", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("second", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLineIsTimestamped()
    {
        var log = new AppLog(path);

        log.Write("switched");

        string line = File.ReadAllLines(path)[0];
        Assert.True(
            DateTime.TryParse(line[..23], CultureInfo.InvariantCulture, out _),
            $"expected a leading timestamp, got \"{line}\"");
    }

    [Fact]
    public void AnExceptionIsRecordedWithItsStack()
    {
        var log = new AppLog(path);
        Exception caught;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        log.WriteException("unhandled on the dispatcher", caught);

        string text = File.ReadAllText(path);
        Assert.Contains("unhandled on the dispatcher", text, StringComparison.Ordinal);
        Assert.Contains("boom", text, StringComparison.Ordinal);
        Assert.Contains(nameof(AnExceptionIsRecordedWithItsStack), text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFileIsRolledOnceItPassesTheCap()
    {
        var log = new AppLog(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', (int)AppLog.MaxBytes + 1));

        log.Write("after the roll");

        string rolled = Path.Combine(Path.GetDirectoryName(path)!, "hydrawin.1.log");
        Assert.True(File.Exists(rolled), "the oversized file should have been moved aside");
        Assert.Single(File.ReadAllLines(path));
        Assert.True(new FileInfo(path).Length < AppLog.MaxBytes);
    }

    [Fact]
    public void OnlyOneRolledFileIsKept()
    {
        var log = new AppLog(path);
        string rolled = Path.Combine(Path.GetDirectoryName(path)!, "hydrawin.1.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        for (int i = 0; i < 3; i++)
        {
            File.WriteAllText(path, new string('x', (int)AppLog.MaxBytes + 1));
            log.Write($"roll {i}");
        }

        Assert.True(File.Exists(rolled));
        Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(path)!).Length);
    }

    [Fact]
    public void AnUnwritablePathIsSwallowed()
    {
        // A directory where the file should be: every write fails, and none of them may escape.
        string blocked = Path.Combine(directory, "blocked.log");
        Directory.CreateDirectory(blocked);
        var log = new AppLog(blocked);

        Assert.Null(Record.Exception(() =>
        {
            log.Write("this cannot be written");
            log.WriteException("nor this", new InvalidOperationException("boom"));
        }));
    }

    [Fact]
    public void ConcurrentWritersDoNotLoseLines()
    {
        var log = new AppLog(path);

        Parallel.For(0, 200, i => log.Write($"line {i}"));

        Assert.Equal(200, File.ReadAllLines(path).Length);
    }
}
