using System.Text.Json.Serialization;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// A named workspace: the group of windows the user switches to and from as a unit.
/// </summary>
/// <remarks>
/// Task 06 adds <c>LastActiveHwnd</c> here — runtime-only, like
/// <see cref="WindowAssignment.BoundHwnd"/> — so a switch can restore focus to whichever window
/// of the task the user was last using.
/// </remarks>
public sealed class HydraWinTask
{
    /// <summary>Stable identity across restarts.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, unique only by convention.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Row accent colour, <c>#RRGGBB</c>.</summary>
    public string ColorHex { get; set; } = string.Empty;

    /// <summary>
    /// Position in the task list, 1-based. Load-bearing: task 08 binds <c>Ctrl+Alt+1..9</c> to
    /// tasks by this value, and task 07 lets the user reorder rows by drag.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Total whole seconds this task has been the active one, over its whole life. It never rolls
    /// over on a new day; the row's <i>Reset time</i> menu item is the only thing that clears it.
    /// </summary>
    /// <remarks>
    /// Seconds rather than a <see cref="TimeSpan"/> because <c>state.json</c> is a hand-editable
    /// document by design: <c>8073</c> is unambiguous, whereas a <c>TimeSpan</c> is written as
    /// <c>"02:14:33"</c> and switches to <c>"1.02:14:33"</c> past a day — a day separator that a
    /// hand-edit is very likely to get wrong. Every other scalar in this file is a bool, an int, a
    /// string or an enum name.
    /// <para>
    /// The segment currently in flight is <b>not</b> here. It lives in
    /// <see cref="ActiveTimeLedger"/> until a switch, an away edge or the one-minute tick folds it
    /// in.
    /// </para>
    /// </remarks>
    public long ActiveSeconds { get; set; }

    /// <summary>The windows that belong to this task.</summary>
    public List<WindowAssignment> Assignments { get; set; } = [];

    /// <summary>Assignments with a live window bound, which is what the switch engine acts on.</summary>
    [JsonIgnore]
    public IEnumerable<WindowAssignment> BoundAssignments => Assignments.Where(a => a.IsBound);

    /// <summary>
    /// The window of this task the user was last working in, so switching back restores focus
    /// where they left it. Runtime-only: handles mean nothing across restarts.
    /// </summary>
    [JsonIgnore]
    public nint? LastActiveHwnd { get; set; }
}
