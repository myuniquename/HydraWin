using System.Text.RegularExpressions;
using HydraWin.Core.Tracking;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// The durable half of an assignment: how to recognise a window again after it — or HydraWin —
/// has been restarted. HWNDs are meaningless across restarts, so this is what re-binds a
/// reopened VS Code or browser window to the task it belonged to.
/// </summary>
/// <remarks>
/// Persisted to <c>state.json</c> and meant to be hand-editable, so the property names are flat
/// and the semantics deliberately simple. Task 10 adds a UI editor.
/// </remarks>
public sealed class ReattachRule
{
    /// <summary>How long a user-authored regex may run before it is abandoned.</summary>
    internal static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Leading title decorations that change while the window is doing nothing interesting, and
    /// so must not end up baked into a pattern.
    /// </summary>
    /// <remarks>
    /// <c>●</c> and <c>*</c> are the usual "unsaved changes" markers (VS Code, Notepad++). The
    /// Claude Code markers come from <see cref="ClaudeCodeTitle"/> rather than being repeated
    /// here, so task 01's measured glyph set has exactly one home: an interactive session titles
    /// its terminal <c>&lt;marker&gt; &lt;session name&gt;</c> and advances the spinner about once
    /// a second, so a rule generated from a busy terminal would otherwise capture whichever frame
    /// happened to be showing and never match again.
    /// </remarks>
    private static readonly char[] VolatileTitleMarkers =
    [
        '●', // the unsaved-changes dot, as used by VS Code
        '*', // the unsaved-changes asterisk, as used by most other editors
        ClaudeCodeTitle.IdleMarker,
        .. ClaudeCodeTitle.SpinnerFrames,
    ];

    private Regex? compiled;
    private string? compiledFor;

    /// <summary>
    /// Image file name only, e.g. <c>Code.exe</c>, compared case-insensitively. The full path is
    /// too brittle: it changes when the app updates.
    /// </summary>
    public string ProcessFileName { get; set; } = string.Empty;

    /// <summary>
    /// Substring to look for in the window title, or a regex when <see cref="TitleIsRegex"/> is
    /// set. An empty pattern matches any title of a matching process.
    /// </summary>
    public string TitlePattern { get; set; } = string.Empty;

    /// <summary>Opt-in regex mode. Substring matching is the default because it is predictable.</summary>
    public bool TitleIsRegex { get; set; }

    /// <summary>
    /// Derives a rule from a live window: the process image file name, and the title with leading
    /// volatile decoration stripped.
    /// </summary>
    /// <remarks>
    /// A trailing <c> - &lt;app&gt;</c> suffix is deliberately <em>kept</em> — it is usually the
    /// stable part of a title, and keeping it is what stops a rule for
    /// <c>foo.cs - hydrawin - Visual Studio Code</c> matching every other editor window.
    /// </remarks>
    public static ReattachRule FromWindow(string processPath, string title) =>
        new()
        {
            ProcessFileName = string.IsNullOrEmpty(processPath)
                ? string.Empty
                : Path.GetFileName(processPath),
            TitlePattern = StripVolatileDecoration(title),
        };

    /// <summary>Removes one leading volatile marker and any whitespace after it.</summary>
    public static string StripVolatileDecoration(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> span = title.AsSpan().TrimStart();
        if (span.Length > 0 && VolatileTitleMarkers.Contains(span[0]))
        {
            span = span[1..].TrimStart();
        }

        return span.ToString();
    }

    /// <summary>
    /// Whether this rule recognises the given window. A malformed or slow user-authored regex
    /// counts as "no match" rather than throwing — this runs on the window-tracking path and a
    /// bad pattern must not take it down.
    /// </summary>
    public bool Matches(string processFileName, string title)
    {
        if (!string.Equals(ProcessFileName, processFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrEmpty(TitlePattern))
        {
            return true;
        }

        string subject = title ?? string.Empty;

        if (!TitleIsRegex)
        {
            return subject.Contains(TitlePattern, StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return GetCompiledRegex()?.IsMatch(subject) ?? false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private Regex? GetCompiledRegex()
    {
        if (compiled is not null && string.Equals(compiledFor, TitlePattern, StringComparison.Ordinal))
        {
            return compiled;
        }

        compiledFor = TitlePattern;
        try
        {
            compiled = new Regex(TitlePattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (ArgumentException)
        {
            // An invalid pattern the user hand-edited into state.json. Task 10's editor will
            // validate up front; here it simply never matches.
            compiled = null;
        }

        return compiled;
    }
}
