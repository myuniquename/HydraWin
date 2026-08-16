using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HydraWin.Core.Tracking;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.ViewModels;

/// <summary>
/// One re-attach rule being edited, with a live answer to "what does this match right now?".
/// </summary>
/// <remarks>
/// The preview is the point of the dialog. A re-attach rule only proves itself the next time the
/// app is restarted, which is far too late to discover that a pattern matches nothing — or matches
/// every window of the process. Showing the current answer as the user types turns that into
/// something they can check on the spot.
/// </remarks>
public sealed partial class RuleEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<TrackedWindow> inventory;
    private readonly nint editingHwnd;

    /// <summary>
    /// Whether construction has finished. Generated property setters raise their change hooks
    /// immediately, so without this the first assignment in the constructor would preview against
    /// fields that have not been assigned yet.
    /// </summary>
    private readonly bool ready;

    /// <summary>Creates the editor over an existing rule.</summary>
    /// <param name="rule">The rule to start from; it is not modified until the dialog is saved.</param>
    /// <param name="inventory">Every window currently tracked, to preview against.</param>
    /// <param name="editingHwnd">The window this rule already owns, left out of the preview.</param>
    public RuleEditorViewModel(
        ReattachRule rule,
        IReadOnlyList<TrackedWindow> inventory,
        nint editingHwnd)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(inventory);

        this.inventory = inventory;
        this.editingHwnd = editingHwnd;

        ProcessFileName = rule.ProcessFileName;
        TitlePattern = rule.TitlePattern;
        TitleIsRegex = rule.TitleIsRegex;

        ready = true;
        RefreshPreview();
    }

    /// <summary>Image file name the rule matches, e.g. <c>Code.exe</c>.</summary>
    [ObservableProperty]
    public partial string ProcessFileName { get; set; }

    /// <summary>Substring, or regex when <see cref="TitleIsRegex"/> is set.</summary>
    [ObservableProperty]
    public partial string TitlePattern { get; set; }

    /// <summary>Whether the pattern is a regular expression.</summary>
    [ObservableProperty]
    public partial bool TitleIsRegex { get; set; }

    /// <summary>Why the pattern cannot be used, or empty.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Error"/> has anything to say.</summary>
    public bool HasError => Error.Length > 0;

    /// <summary>Whether the rule can be saved as it stands.</summary>
    public bool CanSave => !HasError;

    /// <summary>The heading over the preview list, which doubles as the count.</summary>
    [ObservableProperty]
    public partial string PreviewCaption { get; set; } = string.Empty;

    /// <summary>The other open windows this rule currently recognises.</summary>
    public ObservableCollection<string> Preview { get; } = [];

    /// <summary>Copies the edited values onto a rule. Only called once the dialog is accepted.</summary>
    public void ApplyTo(ReattachRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        rule.ProcessFileName = ProcessFileName.Trim();
        rule.TitlePattern = TitlePattern;
        rule.TitleIsRegex = TitleIsRegex;
    }

    partial void OnProcessFileNameChanged(string value) => RefreshPreview();

    partial void OnTitlePatternChanged(string value) => RefreshPreview();

    partial void OnTitleIsRegexChanged(bool value) => RefreshPreview();

    partial void OnErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanSave));
    }

    private void RefreshPreview()
    {
        if (!ready)
        {
            return;
        }

        Preview.Clear();

        if (TitleIsRegex && !RulePreview.IsValidRegex(TitlePattern, out string error))
        {
            Error = error;
            PreviewCaption = "Matches nothing while the pattern is broken";
            return;
        }

        Error = string.Empty;

        var candidate = new ReattachRule
        {
            ProcessFileName = ProcessFileName.Trim(),
            TitlePattern = TitlePattern,
            TitleIsRegex = TitleIsRegex,
        };

        if (candidate.ProcessFileName.Length == 0)
        {
            // A rule with no process matches nothing, which is worth saying rather than showing an
            // empty list that looks like a bad pattern.
            PreviewCaption = "Enter a process name — a rule without one never matches";
            return;
        }

        IReadOnlyList<TrackedWindow> matched = RulePreview.Match(candidate, inventory, editingHwnd);

        foreach (TrackedWindow window in matched)
        {
            Preview.Add($"{window.ProcessFileName} — {window.Title}");
        }

        PreviewCaption = matched.Count switch
        {
            0 => "No other open window matches — only this one would re-attach",
            1 => "1 other open window also matches",
            _ => $"{matched.Count} other open windows also match",
        };
    }
}
