using System.Globalization;
using HydraWin.Core.Persistence;

namespace HydraWin.Core.Diagnostics;

/// <summary>
/// The activity log: one line per thing that happened to the user's windows, appended to a file
/// that is capped and rolled once.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a logging framework. What this has to do is survive being called from the
/// hotkey thread while the UI thread is busy, never grow without bound, and — above all — never be
/// the reason HydraWin fails. Every operation swallows its own exceptions: a log that cannot be
/// written is a diagnostic inconvenience, whereas an exception escaping from a status-line call
/// would take down the app whose whole job is not to lose the user's windows.
/// </para>
/// <para>
/// One rollover, not a numbered series. Two files bound the disk cost at twice
/// <see cref="MaxBytes"/> with no cleanup logic to get wrong, and nobody debugging yesterday's
/// switch needs the week before it.
/// </para>
/// </remarks>
public sealed class AppLog
{
    /// <summary>Size at which the current file is rolled aside. Two files, so twice this on disk.</summary>
    public const long MaxBytes = 1024 * 1024;

    private readonly string path;
    private readonly string rolledPath;
    private readonly Lock gate = new();

    /// <summary>Creates a log over a specific file. The directory is created on first write.</summary>
    public AppLog(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        path = filePath;
        string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);
        rolledPath = Path.Combine(directory, $"{name}.1{extension}");
    }

    /// <summary>The log the application runs with.</summary>
    public static AppLog Default { get; } = new(HydraWinPaths.LogFile);

    /// <summary>The file being written to, for the record and for tests.</summary>
    public string FilePath => path;

    /// <summary>Appends one timestamped line. Never throws.</summary>
    public void Write(string message)
    {
        WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}");
    }

    /// <summary>
    /// Appends an exception with its stack, for the crash handler.
    /// </summary>
    public void WriteException(string context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}  {context}{Environment.NewLine}{exception}"));
    }

    private void WriteLine(string line)
    {
        lock (gate)
        {
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RollIfTooLargeLocked();
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // The log is diagnostics. Losing a line is not worth an exception on a path the
                // caller only wanted to narrate what it just did.
            }
            catch (UnauthorizedAccessException)
            {
                // Same: a read-only or permission-denied profile must not stop the app.
            }
        }
    }

    private void RollIfTooLargeLocked()
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < MaxBytes)
        {
            return;
        }

        // Move, not copy: the previous roll is expendable and this leaves no window in which both
        // files are the same size.
        File.Move(path, rolledPath, overwrite: true);
    }
}
