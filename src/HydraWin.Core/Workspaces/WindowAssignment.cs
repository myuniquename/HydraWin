using System.Text.Json.Serialization;

namespace HydraWin.Core.Workspaces;

/// <summary>
/// One window's membership in a task: a durable <see cref="ReattachRule"/> plus, at runtime, the
/// handle it is currently bound to.
/// </summary>
/// <remarks>
/// Later tasks add to this type: task 06 marks an assignment unmanageable when a window refuses
/// <c>SW_HIDE</c> (elevated windows, per task 01's measurement), and task 10 adds the
/// global/pinned flag for windows that stay visible in every task.
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
    /// Set when the window refused to be hidden — in practice an elevated window, which UIPI
    /// stops a non-elevated HydraWin from touching (task 01 measured the signature). The window
    /// stays visible through every switch and task 10 annotates it in the UI.
    /// </summary>
    /// <remarks>
    /// Runtime-only on purpose: the verdict comes from actually trying, and persisting it would
    /// carry a stale answer across a restart where the offending app may no longer be running
    /// elevated. The engine re-tries on each switch, so it heals by itself.
    /// </remarks>
    [JsonIgnore]
    public bool Unmanageable { get; set; }
}
