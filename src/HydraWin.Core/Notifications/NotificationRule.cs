using System.Text.RegularExpressions;

namespace HydraWin.Core.Notifications;

/// <summary>
/// Turns a change of a window's title into a task badge.
/// </summary>
/// <remarks>
/// <para>
/// The <em>secondary</em> channel, and none of these fire by default. Badges normally come from
/// <see cref="NotificationKind.Attention"/> — the shell flash — which works for any application
/// without a rule, a regex or a process name. Rules exist for programs that announce something in
/// their title and never flash; task 10 added the editor on the settings dialog's
/// <i>Notifications</i> tab, and <c>state.json</c> stays hand-editable alongside it.
/// </para>
/// <para>
/// Two facts task 01 measured, both of which cost time to establish and neither of which should be
/// re-derived from the older comments in this file's history:
/// </para>
/// <list type="bullet">
///   <item><b>Teams never changes its window title</b>, in any window state, on any event — so no
///     title rule can ever badge it. It is handled entirely by the flash channel, which reaches
///     <c>SW_HIDE</c>-hidden windows.</item>
///   <item><b>Claude Code ships no title rule either.</b> Its terminal bell <em>does</em> raise a
///     flash — an earlier negative result used an invalid <c>bellStyle</c> and so tested nothing —
///     about 61 s after the session goes idle. The user accepted that latency in exchange for
///     having no per-app regexes at all. The Claude Code title is still parsed, but only to show
///     live progress in the overview (task 07 § F).</item>
/// </list>
/// <para>
/// Matching is <b>edge-triggered</b>: a rule fires when the new title matches and the previous one
/// did not. A window sitting at a matching title therefore badges once, not on every repaint.
/// </para>
/// </remarks>
public sealed class NotificationRule
{
    /// <summary>How long a user-authored regex may run before it is abandoned.</summary>
    internal static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private Regex? compiled;
    private string? compiledFor;

    /// <summary>
    /// Image file name to match, e.g. <c>chrome.exe</c>, compared case-insensitively. Empty or
    /// <c>*</c> matches any process, which is how a rule is made application-agnostic.
    /// </summary>
    public string ProcessFileName { get; set; } = string.Empty;

    /// <summary>The pattern the new title must match. An empty pattern never fires.</summary>
    public string TitleRegex { get; set; } = string.Empty;

    /// <summary>What kind of pending notification this raises.</summary>
    public NotificationKind Kind { get; set; } = NotificationKind.Title;

    /// <summary>
    /// What the badge tooltip says when this rule fires. Empty falls back to the window's own
    /// description, which is what every rule-less notification uses.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Whether the rule is live. Seeded rules ship off; the flash channel is the default.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The rules HydraWin seeds into a fresh <c>state.json</c>.
    /// </summary>
    /// <remarks>
    /// Exactly one, and it is <b>disabled</b>: a worked example to copy, because there is no rule
    /// editor before task 10 and an empty list gives a hand-editor nothing to work from. Enabling
    /// it is the user's call — a browser tab titled "(2) something" is indistinguishable from two
    /// unread messages, which is the noise the task's soak test exists to catch.
    /// </remarks>
    public static List<NotificationRule> Defaults() =>
    [
        new NotificationRule
        {
            ProcessFileName = "chrome.exe",
            TitleRegex = @"^\(\d+\)",
            Kind = NotificationKind.Title,
            Label = "Unread",
            Enabled = false,
        },
    ];

    /// <summary>
    /// Whether this rule fires for a title change on a window of the given process.
    /// </summary>
    /// <remarks>
    /// A malformed or slow hand-authored regex counts as "no match" rather than throwing. This runs
    /// on the window-tracking path, where a bad pattern must cost its own rule and nothing else.
    /// </remarks>
    public bool Matches(string processFileName, string oldTitle, string newTitle)
    {
        if (!Enabled || string.IsNullOrEmpty(TitleRegex))
        {
            return false;
        }

        // Edge-triggered: the transition is the event, not the state.
        return MatchesTitle(processFileName, newTitle)
            && !MatchesTitle(processFileName, oldTitle);
    }

    /// <summary>
    /// Whether the process and pattern match a title as it stands — no edge, and
    /// <see cref="Enabled"/> ignored.
    /// </summary>
    /// <remarks>
    /// This is what the rule editor's live preview asks, and it is also the half
    /// <see cref="Matches"/> applies twice. Sharing it is the point: a preview computed by a
    /// second implementation could tell the user their rule matches something the tracker would
    /// never fire on.
    /// </remarks>
    public bool MatchesTitle(string processFileName, string title)
    {
        if (string.IsNullOrEmpty(TitleRegex) || !MatchesProcess(processFileName))
        {
            return false;
        }

        Regex? regex = GetCompiledRegex();
        if (regex is null)
        {
            return false;
        }

        try
        {
            return regex.IsMatch(title ?? string.Empty);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private bool MatchesProcess(string processFileName) =>
        string.IsNullOrEmpty(ProcessFileName)
        || ProcessFileName == "*"
        || string.Equals(ProcessFileName, processFileName, StringComparison.OrdinalIgnoreCase);

    private Regex? GetCompiledRegex()
    {
        if (compiled is not null && string.Equals(compiledFor, TitleRegex, StringComparison.Ordinal))
        {
            return compiled;
        }

        compiledFor = TitleRegex;
        try
        {
            compiled = new Regex(TitleRegex, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (ArgumentException)
        {
            // A pattern hand-edited into state.json. The editor validates up front and saves a
            // broken rule disabled; here it simply never matches.
            compiled = null;
        }

        return compiled;
    }
}
