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
