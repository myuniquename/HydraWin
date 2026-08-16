using System.Text.Json.Serialization;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// One window's membership in a task: a durable <see cref="ReattachRule"/> plus, at runtime, the
/// handle it is currently bound to.
/// </summary>
/// <remarks>
/// An assignment only ever lives inside a <see cref="HydraWinTask"/>. A window that belongs to no
/// task has no assignment at all, which is precisely why <see cref="SwitchPlan"/> — built from
/// task assignments — leaves unassigned windows visible in every task.
/// </remarks>
public sealed class WindowAssignment
{
    /// <summary>Stable identity, also used as the drag payload by task 07.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>How to recognise this window again after a restart.</summary>
    public ReattachRule Rule { get; set; } = new();

    /// <summary>
    /// The handle this assignment is currently bound to, or <see langword="null"/> when its
    /// window is not open. Runtime-only: handles mean nothing across restarts.
    /// </summary>
    [JsonIgnore]
    public nint? BoundHwnd { get; set; }

    /// <summary>Whether a live window is currently bound.</summary>
    [JsonIgnore]
    public bool IsBound => BoundHwnd is not null;

    /// <summary>
    /// Set when the window refused to be hidden. Since task 07 elevated windows never reach the
    /// inventory at all, so this is the residue: a window that looked ordinary and refused anyway
    /// — a protected process, or one that became elevated after HydraWin first saw it. It stays
    /// visible through every switch, and the UI marks its row.
    /// </summary>
    /// <remarks>
    /// Runtime-only on purpose: the verdict comes from actually trying, and persisting it would
    /// carry a stale answer across a restart where the offending app may no longer be running
    /// elevated. The engine re-tries on each switch, so it heals by itself.
    /// </remarks>
    [JsonIgnore]
    public bool Unmanageable { get; set; }
}
