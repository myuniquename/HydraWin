namespace HydraWin.Core.Recovery;

/// <summary>
/// One window HydraWin currently has hidden, recorded so a crash can never lose it.
/// </summary>
/// <remarks>
/// <para>
/// The project's one invariant: no foreign window is ever hidden before its entry here is flushed
/// to <c>%APPDATA%\HydraWin\journal.json</c>. The journal therefore always equals "windows
/// HydraWin currently has hidden", and both <c>hydrawin.exe --restore-all</c> and an ordinary
/// launch can put them back.
/// </para>
/// <para>
/// Windows recycles window handles, so <see cref="Hwnd"/> alone is not identity: an entry also
/// carries <see cref="Pid"/> and <see cref="ProcessPath"/>, and
/// <see cref="RestoreService"/> refuses to show a handle whose current owner does not match. The
/// window that inherited a recycled handle could be anything.
/// </para>
/// </remarks>
public sealed class JournalEntry
{
    /// <summary>The window handle at the time it was hidden.</summary>
    public long Hwnd { get; set; }

    /// <summary>Owning process id, half of the identity check.</summary>
    public int Pid { get; set; }

    /// <summary>Owning process image path, the other half.</summary>
    public string ProcessPath { get; set; } = string.Empty;

    /// <summary>The title when it was hidden, for reporting rather than matching.</summary>
    public string TitleAtHide { get; set; } = string.Empty;

    /// <summary>Where the window was, so it comes back exactly there.</summary>
    public WindowPlacementDto Placement { get; set; } = new();

    /// <summary>When it was hidden.</summary>
    public DateTimeOffset HiddenAt { get; set; }
}
