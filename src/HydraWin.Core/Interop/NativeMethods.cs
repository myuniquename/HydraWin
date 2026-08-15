namespace HydraWin.Core.Interop;

/// <summary>
/// The single home for every P/Invoke declaration in HydraWin. Nothing above
/// <c>HydraWin.Core</c> may declare or call Win32 directly (see CLAUDE.md).
/// </summary>
/// <remarks>
/// Deliberately empty in the scaffold; later tasks add signatures here:
/// <list type="bullet">
///   <item>task 03 — <c>EnumWindows</c>, <c>GetWindowTextW</c>, <c>GetWindowLongPtrW</c>,
///     <c>DwmGetWindowAttribute</c>, <c>SetWinEventHook</c>/<c>UnhookWinEvent</c>,
///     <c>GetWindowThreadProcessId</c>, <c>QueryFullProcessImageNameW</c>.</item>
///   <item>task 05 — <c>ShowWindow</c>, <c>IsWindow</c>,
///     <c>GetWindowPlacement</c>/<c>SetWindowPlacement</c> and <c>WINDOWPLACEMENT</c>.</item>
///   <item>task 06 — <c>SetForegroundWindow</c>, <c>IsWindowVisible</c>,
///     <c>OpenProcessToken</c> + <c>GetTokenInformation</c>.</item>
///   <item>task 08 — <c>RegisterHotKey</c>/<c>UnregisterHotKey</c>.</item>
///   <item>task 09 — <c>RegisterShellHookWindow</c>, <c>DeregisterShellHookWindow</c>,
///     <c>RegisterWindowMessageW</c>.</item>
/// </list>
/// Callers depend on <see cref="IWindowApi"/>, never on this class directly — tasks 05 and 06
/// both require a fake Win32 layer for their tests.
/// <para>
/// Throwaway reference implementations of most of these, with observed behaviour recorded, live
/// in <c>spikes/</c> from task 01.
/// </para>
/// </remarks>
internal static partial class NativeMethods
{
}
