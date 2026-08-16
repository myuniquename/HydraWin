using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HydraWin.Core.Notifications;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.ViewModels;

/// <summary>
/// The settings dialog: general behaviour, the hotkey map, and the notification rules.
/// </summary>
/// <remarks>
/// Everything here is a copy taken at construction and written back only by <see cref="Save"/>.
/// That is what makes Cancel mean something — the alternative, binding straight at the live
/// settings, would persist every keystroke and leave a half-edited hotkey registered.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel main;

    /// <summary>Takes a snapshot of the current settings to edit.</summary>
    public SettingsViewModel(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);
        this.main = main;

        RestoreOnExit = main.RestoreOnExit;
        CloseToTray = main.CloseToTray;
        AlwaysOnTop = main.AlwaysOnTop;
        NotificationToasts = main.NotificationToasts;

        foreach (HotkeyBinding binding in main.Hotkeys)
        {
            Hotkeys.Add(new HotkeyBindingViewModel(binding));
        }

        foreach (NotificationRule rule in main.NotificationRules)
        {
            NotificationRules.Add(new NotificationRuleViewModel(rule, main.Inventory));
        }
    }

    /// <summary>Whether a clean exit restores every hidden window first.</summary>
    [ObservableProperty]
    public partial bool RestoreOnExit { get; set; }

    /// <summary>Whether closing the manager window hides it to the tray instead of exiting.</summary>
    [ObservableProperty]
    public partial bool CloseToTray { get; set; }

    /// <summary>Whether the manager window stays above other windows.</summary>
    [ObservableProperty]
    public partial bool AlwaysOnTop { get; set; }

    /// <summary>Whether a new notification also raises a tray balloon.</summary>
    [ObservableProperty]
    public partial bool NotificationToasts { get; set; }

    /// <summary>The editable hotkey rows.</summary>
    public ObservableCollection<HotkeyBindingViewModel> Hotkeys { get; } = [];

    /// <summary>The editable notification rules.</summary>
    public ObservableCollection<NotificationRuleViewModel> NotificationRules { get; } = [];

    /// <summary>The currently selected rule, for the delete command.</summary>
    [ObservableProperty]
    public partial NotificationRuleViewModel? SelectedRule { get; set; }

    /// <summary>Adds a blank rule, switched off, for the user to fill in.</summary>
    [RelayCommand]
    public void AddRule()
    {
        var rule = new NotificationRuleViewModel(
            new NotificationRule { ProcessFileName = "*" },
            main.Inventory);

        NotificationRules.Add(rule);
        SelectedRule = rule;
    }

    /// <summary>Removes the selected rule.</summary>
    [RelayCommand]
    public void DeleteRule()
    {
        if (SelectedRule is NotificationRuleViewModel rule)
        {
            NotificationRules.Remove(rule);
            SelectedRule = NotificationRules.FirstOrDefault();
        }
    }

    /// <summary>
    /// Whether every hotkey row is usable. A combination the dialog cannot resolve would simply
    /// fail to register later, with the failure reported long after the dialog closed.
    /// </summary>
    public bool Validate()
    {
        bool ok = true;
        foreach (HotkeyBindingViewModel hotkey in Hotkeys)
        {
            ok &= hotkey.Validate();
        }

        return ok;
    }

    /// <summary>Writes everything back in one pass.</summary>
    public void Save() => main.ApplySettings(
        RestoreOnExit,
        CloseToTray,
        AlwaysOnTop,
        NotificationToasts,
        [.. Hotkeys.Select(h => h.ToBinding())],
        [.. NotificationRules.Select(r => r.ToRule())]);
}
