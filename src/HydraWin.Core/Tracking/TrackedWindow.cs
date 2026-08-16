namespace HydraWin.Core.Tracking;

/// <summary>
/// One top-level window in HydraWin's live inventory. Mutable by design: the tracker updates
/// <see cref="Title"/> and <see cref="IsHydraWinHidden"/> in place so bound UI keeps the same
/// instance across changes.
/// </summary>
/// <remarks>
/// Windows HydraWin has hidden must stay in the inventory — they are still part of a task — which
/// is why <see cref="IsHydraWinHidden"/> exists and visibility alone cannot gate membership.
/// </remarks>
public sealed class TrackedWindow
{
    /// <summary>The window handle. Stable for the window's lifetime; recycled afterwards.</summary>
    public required nint Hwnd { get; init; }

    /// <summary>Owning process id.</summary>
    public required int Pid { get; init; }

    /// <summary>
    /// Full image path of the owning process, or empty when the process could not be opened or
    /// queried. Task 01 measured that plain elevation does <em>not</em> prevent this, so an empty
    /// value means a genuinely protected process — which has no durable name to write a re-attach
    /// rule against, so <see cref="Workspaces.RuleMatcher"/> refuses to claim one.
    /// </summary>
    public required string ProcessPath { get; set; }

    /// <summary>Current window title.</summary>
    public required string Title { get; set; }

    /// <summary>True while HydraWin is the reason this window is not visible.</summary>
    public bool IsHydraWinHidden { get; set; }

    /// <summary>The process image file name, for display — e.g. <c>Code.exe</c>.</summary>
    public string ProcessFileName =>
        ProcessPath.Length == 0 ? string.Empty : Path.GetFileName(ProcessPath);

    public override string ToString() =>
        $"0x{Hwnd:X} {ProcessFileName} \"{Title}\"{(IsHydraWinHidden ? " [hidden]" : string.Empty)}";
}
