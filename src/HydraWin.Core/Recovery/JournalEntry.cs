namespace HydraWin.Core.Recovery;

/// <summary>
/// One window HydraWin currently has hidden, recorded so a crash can never lose it. Placeholder —
/// task 05 fills this in with <c>WindowPlacementDto</c>, <c>RecoveryJournal</c> and
/// <c>RestoreService</c>.
/// </summary>
/// <remarks>
/// <para>
/// The project's one invariant: no foreign window is ever hidden before its entry here is flushed
/// to <c>%APPDATA%\HydraWin\journal.json</c>. The journal therefore always equals "windows
/// HydraWin currently has hidden", and <c>hydrawin.exe --restore-all</c> must work from it even
/// when the UI cannot start.
/// </para>
/// <para>
/// An entry needs enough identity to survive HWND recycling — PID plus process image path,
/// validated at restore time — because a recycled handle must never be shown as if it were the
/// original window.
/// </para>
/// <para>
/// Task 01's spike hit a concurrency trap worth repeating here: two processes appending to the
/// journal at the same instant collided on the file, and the second write was lost. Since
/// <c>--restore-all</c> can legitimately run while the UI process is live, task 05 must decide
/// this deliberately — a named mutex, or a share mode that permits it plus defined reader
/// behaviour for a partially written file.
/// </para>
/// </remarks>
public sealed class JournalEntry
{
}
