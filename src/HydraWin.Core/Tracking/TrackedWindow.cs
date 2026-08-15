namespace HydraWin.Core.Tracking;

// Temporary suppression: this type is an empty placeholder by design. Task 03 gives it its
// members (Hwnd, Pid, ProcessPath, Title, IsHydraWinHidden) and deletes this pragma pair with it.
#pragma warning disable S2094 // Classes should not be empty

/// <summary>
/// One top-level window in HydraWin's live inventory. Placeholder — task 03 fills this in and
/// builds <c>WindowTracker</c> and <c>IHiddenWindowSet</c> around it.
/// </summary>
/// <remarks>
/// Note for task 03: windows HydraWin has hidden must stay in the inventory (they are still part
/// of a task), which is why <c>IsHydraWinHidden</c> exists and visibility alone cannot gate
/// membership.
/// </remarks>
public sealed class TrackedWindow
{
}

#pragma warning restore S2094 // Classes should not be empty
