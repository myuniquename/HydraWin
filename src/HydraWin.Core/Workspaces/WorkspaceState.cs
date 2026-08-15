namespace HydraWin.Core.Workspaces;

// Temporary suppression: this type is an empty placeholder by design. Task 04 gives it its
// members (Tasks, ActiveTaskId, Settings) and deletes this pragma pair with it.
#pragma warning disable S2094 // Classes should not be empty

/// <summary>
/// The persisted root: every task, the active task, and settings. Placeholder — task 04 fills
/// this in alongside <c>HydraWinTask</c>, <c>WindowAssignment</c>, <c>ReattachRule</c>,
/// <c>SettingsModel</c>, <c>RuleMatcher</c> and <c>WorkspaceService</c>; task 06 adds
/// <c>SwitchEngine</c> here.
/// </summary>
/// <remarks>
/// This is preference data, saved to <c>%APPDATA%\HydraWin\state.json</c>. It is deliberately
/// separate from the crash-safety data in <c>Recovery/</c> — see <see cref="Recovery.JournalEntry"/>.
/// </remarks>
public sealed class WorkspaceState
{
}

#pragma warning restore S2094 // Classes should not be empty
