namespace HydraWin.Core.Interop;

/// <summary>
/// The seam between HydraWin's logic and Win32. Every service that manipulates or inspects a
/// foreign window takes this interface, never <see cref="NativeMethods"/> directly.
/// </summary>
/// <remarks>
/// This exists from the scaffold on purpose. Task 05 verifies <c>RestoreService</c> against
/// "fakes of the Win32 layer", and task 06 must prove the project's one invariant — that the
/// journal is flushed <em>before</em> any <c>SW_HIDE</c> — with a "scripted fake interop layer"
/// that asserts call order. Neither is possible if callers bind to static P/Invoke.
/// <para>
/// Members are added by the tasks that need them: hide/show and placement (task 05), visibility,
/// foreground and elevation checks (task 06). Task 01 recorded two behaviours the implementation
/// must honour: <c>ShowWindow</c> returns the window's <em>previous</em> visibility rather than
/// success, so <c>IsWindowVisible</c> is the authority afterwards; and an elevated window refuses
/// the hide with <c>GetLastError() == 5</c> (<c>ERROR_ACCESS_DENIED</c>).
/// </para>
/// </remarks>
public interface IWindowApi
{
}
