namespace HydraWin.Core.Persistence;

/// <summary>
/// Where HydraWin keeps its files. The one place <c>%APPDATA%\HydraWin\</c> is spelled out.
/// </summary>
/// <remarks>
/// <see cref="JsonStore{T}"/> and <see cref="Diagnostics.AppLog"/> both stay path-agnostic so they
/// can be tested against a temp directory; these are only the defaults the app runs with.
/// </remarks>
public static class HydraWinPaths
{
    /// <summary><c>%APPDATA%\HydraWin</c>. Not created until something is saved.</summary>
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HydraWin");

    /// <summary>Preference data: tasks, assignments, settings.</summary>
    public static string StateFile { get; } = Path.Combine(AppDataDirectory, "state.json");

    /// <summary>
    /// Crash-safety data: the windows HydraWin currently has hidden. Losing
    /// <see cref="StateFile"/> costs the user their task layout; losing this could cost them
    /// their windows.
    /// </summary>
    public static string JournalFile { get; } = Path.Combine(AppDataDirectory, "journal.json");

    /// <summary>Where the rolling activity log lives. Diagnostics only; safe to delete.</summary>
    public static string LogDirectory { get; } = Path.Combine(AppDataDirectory, "logs");

    /// <summary>The current log file. Rolled to <c>hydrawin.1.log</c> when it grows too large.</summary>
    public static string LogFile { get; } = Path.Combine(LogDirectory, "hydrawin.log");
}
