namespace HydraWin.Core.Interop;

/// <summary>
/// The real <see cref="IHotkeyApi"/>: a thin adapter onto <c>NativeMethods</c>, which is the only
/// class allowed to declare P/Invoke.
/// </summary>
public sealed class Win32HotkeyApi : IHotkeyApi
{
    /// <summary>A shared instance; the type holds no state.</summary>
    public static Win32HotkeyApi Instance { get; } = new();

    /// <inheritdoc />
    public bool TryRegister(int id, uint modifiers, uint virtualKey) =>
        NativeMethods.TryRegisterHotkey(id, modifiers, virtualKey);

    /// <inheritdoc />
    public void UnregisterAll(List<int> ids) => NativeMethods.UnregisterHotkeys(ids);

    /// <inheritdoc />
    public bool WaitForHotkey(out int id) => NativeMethods.WaitForHotkey(out id);

    /// <inheritdoc />
    public uint CurrentThreadId() => NativeMethods.CurrentThreadId();

    /// <inheritdoc />
    public void StopLoop(uint threadId) => NativeMethods.StopHotkeyLoop(threadId);
}
