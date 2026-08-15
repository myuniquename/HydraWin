namespace HydraWin.Core.Notifications;

/// <summary>
/// A rule that turns a window signal into a task badge. Placeholder — task 09 fills this in
/// alongside <c>NotificationKind</c> and <c>NotificationHub</c>.
/// </summary>
/// <remarks>
/// <para>
/// Task 01 measured the two signal channels and found them <em>disjoint</em>, not
/// primary/fallback, so task 09 implements both:
/// </para>
/// <list type="bullet">
///   <item>Claude Code in Windows Terminal is <b>title-only</b> — its terminal bell never
///     flashed. The title is <c>&lt;marker&gt; &lt;session name&gt;</c>, cycling spinner frames
///     <c>U+25D0</c>–<c>U+25D3</c> while busy and settling on <c>U+2733</c> when it finishes or
///     waits for input.</item>
///   <item>Teams is <b>flash-only</b> — it never changes its window title at all, so no title
///     rule can work for it.</item>
/// </list>
/// <para>
/// Both channels reach windows hidden with <c>SW_HIDE</c>. One caveat for the hub: Teams flashes
/// only once per unread run, so a Teams badge must be cleared solely by the window gaining focus —
/// clear it on a task switch and no further flash will ever re-raise it.
/// </para>
/// </remarks>
public sealed class NotificationRule
{
}
