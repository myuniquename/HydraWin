using System.Diagnostics;
using HydraWin.Core.Interop;

namespace HydraWin.Core.Recovery;

/// <summary>What a restore pass did.</summary>
/// <param name="Restored">Windows put back and removed from the journal.</param>
/// <param name="Stale">
/// Entries dropped because the window was gone or the handle now belongs to something else.
/// </param>
/// <param name="Failed">
/// Entries whose window is still there and still matches but refused to come back. These stay in
/// the journal so a later attempt can retry — dropping them would strand a hidden window.
/// </param>
public readonly record struct RestoreSummary(int Restored, int Stale, int Failed)
{
    /// <summary>The line the CLI prints.</summary>
    public override string ToString()
    {
        string text = $"restored {Restored} window(s), dropped {Stale} stale entr"
            + (Stale == 1 ? "y" : "ies");
        return Failed > 0 ? text + $", {Failed} could not be restored" : text;
    }
}

/// <summary>What happened to one journal entry.</summary>
public enum RestoreOutcome
{
    /// <summary>The window was verified, put back where it was, and removed from the journal.</summary>
    Restored,

    /// <summary>
    /// The window was gone, or its handle now belongs to something else. The entry was dropped
    /// and nothing was shown.
    /// </summary>
    Stale,

    /// <summary>
    /// The window is still there and still ours but refused to come back. The entry stays so a
    /// later attempt can retry.
    /// </summary>
    Failed,
}

/// <summary>
/// Puts hidden windows back. This is the other half of the crash-safety contract: whatever the
/// journal says is hidden, this makes visible again.
/// </summary>
public sealed class RestoreService
{
    private readonly IWindowApi windowApi;

    /// <summary>Creates the service over a window API.</summary>
    public RestoreService(IWindowApi windowApi)
    {
        ArgumentNullException.ThrowIfNull(windowApi);
        this.windowApi = windowApi;
    }

    /// <summary>
    /// Restores every window the journal lists, and empties it of everything resolved.
    /// </summary>
    /// <remarks>
    /// An entry is only acted on when the handle is still a window <em>and</em> its current
    /// process id and image path match what was recorded. Windows recycles handles, so showing an
    /// unverified one could yank an unrelated window into view — or worse, move it. Anything that
    /// fails the check is simply dropped.
    /// </remarks>
    public RestoreSummary RestoreAll(RecoveryJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        int restored = 0;
        int stale = 0;
        int failed = 0;

        foreach (JournalEntry entry in journal.Snapshot())
        {
            switch (RestoreWindow(journal, entry))
            {
                case RestoreOutcome.Restored:
                    restored++;
                    break;
                case RestoreOutcome.Stale:
                    stale++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return new RestoreSummary(restored, stale, failed);
    }

    /// <summary>
    /// Restores one journaled window, applying the same identity check as
    /// <see cref="RestoreAll"/>. The switch engine calls this for the task it is switching to, so
    /// the rule that an unverified handle is never shown lives in exactly one place.
    /// </summary>
    public RestoreOutcome RestoreWindow(RecoveryJournal journal, JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(entry);

        var hwnd = (nint)entry.Hwnd;

        if (!MatchesIdentity(hwnd, entry))
        {
            Debug.WriteLine(
                $"RestoreService: dropping stale entry 0x{entry.Hwnd:X} \"{entry.TitleAtHide}\".");
            journal.ConfirmShown(entry.Hwnd);
            return RestoreOutcome.Stale;
        }

        // Placement first: it carries the maximized state, and setting it on a hidden window is
        // what makes the window reappear exactly where it was rather than merely visible.
        WindowPlacement placement = entry.Placement.ToPlacement();
        windowApi.TrySetPlacement(hwnd, in placement);

        ShowWindowResult result = windowApi.Show(hwnd);
        if (result.Succeeded)
        {
            journal.ConfirmShown(entry.Hwnd);
            return RestoreOutcome.Restored;
        }

        // Keep the entry. A window that is still hidden and still ours must stay on the books, or
        // nothing will ever bring it back.
        Debug.WriteLine(
            $"RestoreService: 0x{entry.Hwnd:X} refused to show, win32={result.Win32Error}.");
        return RestoreOutcome.Failed;
    }

    private bool MatchesIdentity(nint hwnd, JournalEntry entry)
    {
        if (!windowApi.IsWindow(hwnd))
        {
            return false;
        }

        int pid = windowApi.GetProcessId(hwnd);
        if (pid != entry.Pid)
        {
            return false;
        }

        // A protected process reports no path; if it had one when hidden, the mismatch is real.
        string path = windowApi.GetProcessPath(pid);
        return string.Equals(path, entry.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }
}
