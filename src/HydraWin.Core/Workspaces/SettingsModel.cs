namespace HydraWin.Core.Workspaces;

/// <summary>
/// User preferences, persisted inside <c>state.json</c> alongside the tasks.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately minimal: task 04 started it small and each later task added only the setting it
/// needed — task 08 the hotkey map and close-to-tray, task 09 the notification rules and the toasts
/// toggle, task 10 the dialog over all of them. <b>Launch at login was dropped from the project on
/// the user's instruction</b>, so nothing here writes to the registry.
/// </para>
/// <para>
/// <c>state.json</c> stays hand-editable, so anything added here must serialize to flat property
/// names and string enums — no polymorphic JSON.
/// </para>
/// </remarks>
public sealed class SettingsModel
{
    /// <summary>
    /// Whether exiting HydraWin restores every hidden window first. Default on: leaving a user's
    /// windows hidden after the manager is gone is the one failure this project cannot afford.
    /// The tray <i>Exit</i> paths honour it, and the settings dialog's <i>General</i> tab exposes
    /// it.
    /// </summary>
    public bool RestoreOnExit { get; set; } = true;

    /// <summary>
    /// Whether the manager window stays above other windows. Default on, because a switch ends by
    /// focusing one of the task's windows — which would otherwise bury the very window the user
    /// clicks to switch again.
    /// </summary>
    /// <remarks>
    /// This cannot usefully be scoped to "only while switching": <c>SwitchTo</c> is synchronous, so
    /// a flag raised and lowered inside it never reaches a frame. The toolbar checkbox and the
    /// settings dialog both write it.
    /// </remarks>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// Whether closing the manager window hides it to the tray instead of exiting. Default on:
    /// HydraWin is a companion, and closing its window is not a request to stop managing windows.
    /// The tray menu's <i>Exit</i> is how it actually stops.
    /// </summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>
    /// The global hotkeys. Seeded with <see cref="HotkeyBinding.Defaults"/> the first time, and
    /// hand-editable thereafter; an entry that cannot be understood is skipped, not fatal.
    /// </summary>
    public List<HotkeyBinding> Hotkeys { get; set; } = [];

    /// <summary>
    /// Title-watching notification rules. Seeded with
    /// <see cref="Notifications.NotificationRule.Defaults"/>, all of them disabled: badges come
    /// from the shell flash, which needs no rule and works for any application. The settings
    /// dialog's <i>Notifications</i> tab edits them.
    /// </summary>
    public List<Notifications.NotificationRule> NotificationRules { get; set; } = [];

    /// <summary>
    /// Whether a new notification also raises a tray balloon. Default off — the badge is the
    /// product, and toasts are noise until asked for.
    /// </summary>
    public bool NotificationToasts { get; set; }

    /// <summary>
    /// Which palette to paint with. Default <see cref="Workspaces.Appearance.System"/>: a companion
    /// window that does not match the desktop it sits on looks broken, and following the OS is the
    /// only default that is right without being asked.
    /// </summary>
    /// <remarks>
    /// The first setting here that is not a <see cref="bool"/>. It serializes as a name, so the
    /// hand-editable promise above still holds; <see cref="AppearanceResolver"/> treats anything it
    /// does not recognise as <see cref="Workspaces.Appearance.System"/>.
    /// </remarks>
    public Appearance Appearance { get; set; } = Appearance.System;
}
