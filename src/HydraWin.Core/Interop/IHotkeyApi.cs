namespace HydraWin.Core.Interop;

/// <summary>
/// Global hotkey registration and the message loop that receives them.
/// </summary>
/// <remarks>
/// Every member here is thread-affine: hotkeys belong to the thread that registered them, and only
/// that thread may wait for or release them. The one exception is <see cref="StopLoop"/>, which
/// exists precisely to be called from elsewhere.
/// </remarks>
public interface IHotkeyApi
{
    /// <summary>
    /// Claims a hotkey for the calling thread. <see langword="false"/> means another application
    /// already owns the combination — expected, and not an error worth stopping for.
    /// </summary>
    bool TryRegister(int id, uint modifiers, uint virtualKey);

    /// <summary>
    /// Releases every hotkey in the list and empties it, on the thread that registered them.
    /// </summary>
    void UnregisterAll(List<int> ids);

    /// <summary>
    /// Blocks until a hotkey fires, returning its id; <see langword="false"/> once
    /// <see cref="StopLoop"/> has been called.
    /// </summary>
    bool WaitForHotkey(out int id);

    /// <summary>The calling thread's id, to hand to <see cref="StopLoop"/> later.</summary>
    uint CurrentThreadId();

    /// <summary>Ends the wait loop running on another thread.</summary>
    void StopLoop(uint threadId);
}
