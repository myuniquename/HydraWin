using System.Text.RegularExpressions;
using HydraWin.Core.Tracking;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// Answers "what would this rule match right now?" for the rule editors, and says whether a
/// hand-typed pattern is even a valid regex.
/// </summary>
/// <remarks>
/// Matching deliberately goes through the same <see cref="ReattachRule.Matches"/> the tracker
/// uses rather than reimplementing it: a preview that agreed with a second implementation instead
/// of the real one would be worse than no preview at all.
/// </remarks>
public static class RulePreview
{
    /// <summary>
    /// The windows a rule currently recognises, in the order given.
    /// </summary>
    /// <param name="rule">The rule being edited.</param>
    /// <param name="windows">Every window in the live inventory.</param>
    /// <param name="ignoreHwnd">
    /// A window to leave out — the one the rule already belongs to, which would otherwise always
    /// appear and tell the user nothing.
    /// </param>
    public static IReadOnlyList<TrackedWindow> Match(
        ReattachRule rule,
        IEnumerable<TrackedWindow> windows,
        nint ignoreHwnd = 0)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(windows);

        return
        [
            .. windows.Where(w => w.Hwnd != ignoreHwnd
                && rule.Matches(w.ProcessFileName, w.Title)),
        ];
    }

    /// <summary>
    /// The windows whose current title a notification rule's pattern matches.
    /// </summary>
    /// <remarks>
    /// Ignores <see cref="Notifications.NotificationRule.Enabled"/> and the edge trigger, because
    /// the question the editor is answering is "does this pattern pick out what I mean", not
    /// "would it have fired just now".
    /// </remarks>
    public static IReadOnlyList<TrackedWindow> Match(
        Notifications.NotificationRule rule,
        IEnumerable<TrackedWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(windows);

        return [.. windows.Where(w => rule.MatchesTitle(w.ProcessFileName, w.Title))];
    }

    /// <summary>
    /// Whether a pattern compiles as a regex, and the message to show when it does not.
    /// </summary>
    /// <remarks>
    /// Only meaningful in regex mode — a substring pattern is always valid. Both rule types
    /// treat a broken pattern as "never matches" rather than throwing, so this exists to tell the
    /// user *why* their rule went quiet rather than to protect the matching path.
    /// </remarks>
    public static bool IsValidRegex(string pattern, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase, ReattachRule.RegexTimeout);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
