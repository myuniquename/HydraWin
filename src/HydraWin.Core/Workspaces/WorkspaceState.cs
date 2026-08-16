using System.Text.Json.Serialization;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// The persisted root: every task, which one is active, and the user's settings.
/// </summary>
/// <remarks>
/// This is preference data, saved to <c>%APPDATA%\HydraWin\state.json</c>. It is deliberately
/// separate from the crash-safety data in <c>Recovery/</c> — see
/// <see cref="Recovery.JournalEntry"/>. Losing <c>state.json</c> costs the user their task
/// layout; losing <c>journal.json</c> could cost them their windows.
/// </remarks>
public sealed class WorkspaceState
{
    /// <summary>All tasks, in no guaranteed order — read <see cref="HydraWinTask.Order"/>.</summary>
    public List<HydraWinTask> Tasks { get; set; } = [];

    /// <summary>
    /// The task currently switched to, or <see langword="null"/> when everything is shown. Task 06
    /// writes it on <c>SwitchTo</c> and clears it on <c>ShowAllTasks</c>; task 09 reads it to
    /// suppress badges for the active task.
    /// </summary>
    public Guid? ActiveTaskId { get; set; }

    /// <summary>User preferences.</summary>
    public SettingsModel Settings { get; set; } = new();

    /// <summary>Tasks in display order.</summary>
    /// <remarks>
    /// Derived, so never written: serializing it would duplicate the whole task list in
    /// <c>state.json</c>, and because it is get-only, anything a user hand-edited in that second
    /// copy would be silently discarded on load.
    /// </remarks>
    [JsonIgnore]
    public IEnumerable<HydraWinTask> OrderedTasks => Tasks.OrderBy(t => t.Order);
}
