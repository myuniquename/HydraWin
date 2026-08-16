using CommunityToolkit.Mvvm.ComponentModel;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.ViewModels;

/// <summary>
/// One editable hotkey row in the settings dialog.
/// </summary>
/// <remarks>
/// Holds a <em>copy</em> of the binding's written form, so Cancel really cancels. Validation is
/// <see cref="HotkeyBinding.TryResolve"/>'s, not a second opinion: what the dialog accepts is
/// exactly what <c>RegisterHotKey</c> will be asked for.
/// </remarks>
public sealed partial class HotkeyBindingViewModel : ObservableObject
{
    /// <summary>Copies a binding for editing.</summary>
    public HotkeyBindingViewModel(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        Action = binding.Action;
        TaskOrder = binding.TaskOrder;
        Description = binding.DescribeAction();
        Combination = binding.ToDisplayString();
    }

    /// <summary>What the hotkey does. Not editable — the set of actions is fixed.</summary>
    public HotkeyAction Action { get; }

    /// <summary>Which task, for a switch binding.</summary>
    public int TaskOrder { get; }

    /// <summary>The action in words, for the row's label.</summary>
    public string Description { get; }

    /// <summary>The combination as written, e.g. <c>Control+Alt+1</c>.</summary>
    [ObservableProperty]
    public partial string Combination { get; set; }

    /// <summary>Why this combination cannot be used, or empty.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Error"/> has anything to say.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>The binding to persist.</summary>
    public HotkeyBinding ToBinding()
    {
        (string modifiers, string key) = HotkeyBinding.Split(Combination);
        return new HotkeyBinding
        {
            Action = Action,
            TaskOrder = TaskOrder,
            Modifiers = modifiers,
            Key = key,
        };
    }

    /// <summary>
    /// Re-checks the combination and sets <see cref="Error"/>.
    /// </summary>
    /// <returns>Whether it is usable.</returns>
    public bool Validate()
    {
        if (Combination.Trim().Length == 0)
        {
            // An empty row is a deliberate "no hotkey for this", not a mistake.
            Error = string.Empty;
            return true;
        }

        bool ok = ToBinding().TryResolve(out _, out _);
        Error = ok
            ? string.Empty
            : "Needs at least one modifier and a digit, letter or F-key.";

        return ok;
    }

    partial void OnCombinationChanged(string value) => Validate();

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
}
