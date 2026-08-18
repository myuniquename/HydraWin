using System.Globalization;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// Turns a task's accumulated active time into the strings the task row shows.
/// </summary>
/// <remarks>
/// In Core rather than in a WPF converter because the App project has no automated coverage at all,
/// and the boundaries this has to get right — zero, the roll from minutes into hours, and the fact
/// that a day does not roll over into days — are exactly what regresses in silence.
/// <c>SwitchSummary.ToString()</c> is the precedent: a string built for the user, built in Core,
/// because it is worth testing.
/// </remarks>
public static class ActiveTimeFormat
{
    /// <summary>
    /// The row's running clock, <c>HH:mm:ss</c> — <c>00:00:00</c>, <c>00:14:07</c>,
    /// <c>02:14:07</c>, <c>137:05:00</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seconds are shown, and shown on every row including the ones at zero, because the point of
    /// the cell is not only <em>how much</em> but <b>whether it is moving at all</b>. A figure
    /// rounded to minutes is indistinguishable from a stopped clock for a minute at a time, which
    /// is exactly long enough to make the user doubt it.
    /// </para>
    /// <para>
    /// Hours accumulate rather than rolling into days: <c>25:00:00</c>, not <c>1.01:00:00</c>. The
    /// hours field grows past two digits and everything else stays padded, so the column only ever
    /// changes width when a task passes a hundred hours.
    /// </para>
    /// </remarks>
    public static string Clock(TimeSpan total)
    {
        // Only a hand-edit of state.json can make this negative, and a negative clock would be
        // worse than a wrong one.
        if (total < TimeSpan.Zero)
        {
            total = TimeSpan.Zero;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00}",
            (int)total.TotalHours,
            total.Minutes,
            total.Seconds);
    }

    /// <summary>
    /// The tooltip: the same total in words, and whether the clock is running right now.
    /// </summary>
    /// <param name="total">The accumulated time.</param>
    /// <param name="counting">
    /// Whether this task is the active one and the user is at the machine. Said out loud because
    /// "is it actually counting?" is the question the cell exists to answer, and a task that is
    /// merely not selected looks identical to one whose clock has stopped because the screen is
    /// locked.
    /// </param>
    public static string Tooltip(TimeSpan total, bool counting)
    {
        if (total <= TimeSpan.Zero && !counting)
        {
            return "Never switched to";
        }

        int hours = (int)total.TotalHours;
        string exact = hours == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}m {1:00}s", total.Minutes, total.Seconds)
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0}h {1:00}m {2:00}s",
                hours,
                total.Minutes,
                total.Seconds);

        return counting
            ? "Active for " + exact + " in total — counting now"
            : "Active for " + exact + " in total";
    }
}
