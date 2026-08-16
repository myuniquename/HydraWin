namespace HydraWin.Core.Tracking;

/// <summary>What a window's title says it is doing right now.</summary>
public enum TitleActivity
{
    /// <summary>No recognised marker; the title is just a title.</summary>
    None = 0,

    /// <summary>A Claude Code session is working — the spinner is turning.</summary>
    Working,

    /// <summary>A Claude Code session is idle, waiting for input.</summary>
    Idle,
}

/// <summary>
/// Reads the activity marker off a Claude Code terminal title.
/// </summary>
/// <remarks>
/// <para>
/// Task 01 measured the format: an interactive session titles its terminal
/// <c>&lt;marker&gt; &lt;session or activity name&gt;</c>, where the marker is one of the rotating
/// spinner frames <c>◐ ◑ ◒ ◓</c> (<c>U+25D0</c>–<c>U+25D3</c>) while it is working — advancing
/// about once a second — or <c>✳</c> (<c>U+2733</c>) once it goes idle, after which the title
/// stops changing.
/// </para>
/// <para>
/// This is for <em>display</em> only. The badge for "Claude finished" comes from the flash channel
/// in task 09, deliberately: <c>✳</c> also appears momentarily at the start of an activity, so
/// treating it as a notification would fire on starting work as well as finishing it.
/// </para>
/// </remarks>
public static class ClaudeCodeTitle
{
    /// <summary>The spinner frames shown while a session is working.</summary>
    public const string SpinnerFrames = "◐◑◒◓";

    /// <summary>The marker shown when a session is idle and waiting for input.</summary>
    public const char IdleMarker = '✳';

    /// <summary>
    /// Splits a title into its activity marker and the text after it. A title with no marker comes
    /// back unchanged as <see cref="TitleActivity.None"/>.
    /// </summary>
    /// <remarks>
    /// The marker only counts at the very start. A window whose title merely contains one of these
    /// glyphs — a browser tab showing this documentation, say — is not a working Claude session.
    /// </remarks>
    public static (TitleActivity Activity, string Text) Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (TitleActivity.None, string.Empty);
        }

        ReadOnlySpan<char> span = title.AsSpan().TrimStart();
        TitleActivity activity = span[0] switch
        {
            IdleMarker => TitleActivity.Idle,
            char c when SpinnerFrames.Contains(c) => TitleActivity.Working,
            _ => TitleActivity.None,
        };

        return activity == TitleActivity.None
            ? (activity, title)
            : (activity, span[1..].TrimStart().ToString());
    }
}
