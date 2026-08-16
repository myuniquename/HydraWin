using CommunityToolkit.Mvvm.ComponentModel;
using HydraWin.Core.Notifications;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.ViewModels;

/// <summary>
/// One editable notification rule, with a live count of what its pattern currently picks out.
/// </summary>
/// <remarks>
/// A broken pattern is <em>saved disabled</em> rather than rejected: these rules are the secondary
/// channel, the flash covers every application without them, and losing a half-written rule
/// because the user tabbed away mid-regex would be worse than keeping it switched off with the
/// reason shown.
/// </remarks>
public sealed partial class NotificationRuleViewModel : ObservableObject
{
    private readonly IReadOnlyList<TrackedWindow> inventory;

    /// <summary>
    /// Whether construction has finished. Generated property setters raise their change hooks
    /// immediately, so without this the first assignment in the constructor would refresh against
    /// the fields that have not been assigned yet — which is a null pattern, not an empty one.
    /// </summary>
    private readonly bool ready;

    /// <summary>Copies a rule for editing.</summary>
    public NotificationRuleViewModel(NotificationRule rule, IReadOnlyList<TrackedWindow> inventory)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(inventory);

        this.inventory = inventory;

        ProcessFileName = rule.ProcessFileName;
        TitleRegex = rule.TitleRegex;
        Label = rule.Label;
        Enabled = rule.Enabled;

        ready = true;
        Refresh();
    }

    /// <summary>Process to match, or <c>*</c> / empty for any.</summary>
    [ObservableProperty]
    public partial string ProcessFileName { get; set; }

    /// <summary>The title pattern. Always a regex — that is what the model stores.</summary>
    [ObservableProperty]
    public partial string TitleRegex { get; set; }

    /// <summary>What the badge tooltip says; empty falls back to the window's own description.</summary>
    [ObservableProperty]
    public partial string Label { get; set; }

    /// <summary>Whether the rule is live.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>Why the pattern cannot be used, or empty.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>What the pattern currently matches, in words.</summary>
    [ObservableProperty]
    public partial string Preview { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Error"/> has anything to say.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>The rule to persist. A broken pattern comes back disabled.</summary>
    public NotificationRule ToRule() => new()
    {
        ProcessFileName = ProcessFileName.Trim(),
        TitleRegex = TitleRegex,
        Kind = NotificationKind.Title,
        Label = Label.Trim(),
        Enabled = Enabled && !HasError,
    };

    partial void OnProcessFileNameChanged(string value) => Refresh();

    partial void OnTitleRegexChanged(string value) => Refresh();

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    private void Refresh()
    {
        if (!ready)
        {
            return;
        }

        if (!RulePreview.IsValidRegex(TitleRegex, out string error))
        {
            Error = error;
            Preview = "Will be saved switched off until the pattern is valid.";
            return;
        }

        Error = string.Empty;

        if (TitleRegex.Length == 0)
        {
            Preview = "An empty pattern never fires.";
            return;
        }

        IReadOnlyList<TrackedWindow> matched = RulePreview.Match(ToRule(), inventory);

        if (matched.Count == 0)
        {
            Preview = "No open window's title matches right now.";
            return;
        }

        string named = string.Join(", ", matched.Take(3).Select(w => w.Title));
        string more = matched.Count > 3 ? $" (+{matched.Count - 3} more)" : string.Empty;
        Preview = $"Matches now: {named}{more}";
    }
}
