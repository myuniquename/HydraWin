namespace HydraWin.Core.Workspaces;

/// <summary>
/// User preferences, persisted inside <c>state.json</c> alongside the tasks.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately minimal for now — task 04 is told to start small and let the tasks that need a
/// setting add it. What is coming, so nobody has to re-derive it:
/// </para>
/// <list type="bullet">
///   <item>task 08 — the hotkey map, keyed by action (<c>Ctrl+Alt+1..9</c> switch to the task with
///     that <see cref="HydraWinTask.Order"/>, plus show-all, panic-restore and toggle-window),
///     the close-to-tray toggle (default on) and launch-at-login (default off);</item>
///   <item>task 09 — the seeded <c>NotificationRule</c> list and the toasts toggle (default off);</item>
///   <item>task 10 — the settings UI over all of the above, plus rule editing.</item>
/// </list>
/// <para>
/// Tasks 08 and 09 both require <c>state.json</c> to stay hand-editable, so anything added here
/// must serialize to flat property names and string enums — no polymorphic JSON.
/// </para>
/// </remarks>
public sealed class SettingsModel
{
    /// <summary>
    /// Whether exiting HydraWin restores every hidden window first. Default on: leaving a user's
    /// windows hidden after the manager is gone is the one failure this project cannot afford.
    /// Task 08 wires the tray <i>Exit</i> paths to it and task 10 exposes the toggle.
    /// </summary>
    public bool RestoreOnExit { get; set; } = true;

    /// <summary>
    /// Whether the manager window stays above other windows. Default on, because a switch ends by
    /// focusing one of the task's windows — which would otherwise bury the very window the user
    /// clicks to switch again.
    /// </summary>
    /// <remarks>
    /// This cannot usefully be scoped to "only while switching": <c>SwitchTo</c> is synchronous, so
    /// a flag raised and lowered inside it never reaches a frame. Task 10 puts it in the settings
    /// UI alongside the rest.
    /// </remarks>
    public bool AlwaysOnTop { get; set; } = true;
}
