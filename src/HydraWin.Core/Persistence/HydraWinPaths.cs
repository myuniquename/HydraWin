namespace HydraWin.Core.Persistence;

/// <summary>
/// Where HydraWin keeps its files. The one place <c>%APPDATA%\HydraWin\</c> is spelled out.
/// </summary>
/// <remarks>
/// Task 10 adds the <c>logs\</c> directory. <see cref="JsonStore{T}"/> itself stays path-agnostic
/// so every store can be tested against a temp directory.
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
}
