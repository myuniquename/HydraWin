namespace HydraWin.Core.Persistence;

/// <summary>
/// Where HydraWin keeps its files. The one place <c>%APPDATA%\HydraWin\</c> is spelled out.
/// </summary>
/// <remarks>
/// Task 05 adds <c>journal.json</c> here and task 10 adds the <c>logs\</c> directory.
/// <see cref="JsonStore{T}"/> itself stays path-agnostic so both can be tested against temp
/// directories.
/// </remarks>
public static class HydraWinPaths
{
    /// <summary><c>%APPDATA%\HydraWin</c>. Not created until something is saved.</summary>
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HydraWin");

    /// <summary>Preference data: tasks, assignments, settings.</summary>
    public static string StateFile { get; } = Path.Combine(AppDataDirectory, "state.json");
}
