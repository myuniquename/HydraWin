using System.Runtime.InteropServices;
using System.Text;

namespace HydraWin.Core.Interop;

/// <summary>Raw Win32 properties of one top-level window, read in a single pass.</summary>
/// <param name="Title">Window title, empty when it has none.</param>
/// <param name="IsVisible">Result of <c>IsWindowVisible</c>.</param>
/// <param name="ExtendedStyle">Result of <c>GetWindowLongPtr(GWL_EXSTYLE)</c>.</param>
/// <param name="Owner">Result of <c>GetWindow(GW_OWNER)</c>; non-zero means owned.</param>
/// <param name="IsCloaked">True when DWM reports the window cloaked.</param>
/// <param name="Pid">Owning process id.</param>
internal readonly record struct RawWindowInfo(
    string Title,
    bool IsVisible,
    long ExtendedStyle,
    nint Owner,
    bool IsCloaked,
    int Pid);

/// <summary>
/// The single home for every P/Invoke declaration in HydraWin. Nothing above
/// <c>HydraWin.Core</c> may declare or call Win32 directly (see CLAUDE.md).
/// </summary>
/// <remarks>
/// <para>
/// Every <c>extern</c> here is <see langword="private"/> and reached through an
/// <see langword="internal"/> wrapper that does something worth doing. That is what Sonar's S4200
/// asks for, and it pushes the boundary in a good direction: callers get a handful of coarse
/// operations such as <see cref="DescribeWindow"/> rather than a 1:1 mirror of user32. Win32
/// constant names are kept verbatim so they can be grepped against the Microsoft documentation.
/// </para>
/// <para>
/// <c>[LibraryImport]</c> is used wherever the source generator can handle the signature. Four
/// declarations stay on <c>[DllImport]</c>: <c>EnumWindows</c> and <c>SetWinEventHook</c> take
/// delegates, which the generator cannot marshal, and the two string-buffer calls would otherwise
/// force either assembly-wide <c>DisableRuntimeMarshalling</c> or an <c>unsafe</c> block. The
/// resulting SYSLIB1054 is a suggestion, not a warning, so warnings-as-errors is unaffected.
/// </para>
/// <para>
/// Throwaway reference implementations of most of these, with observed behaviour recorded, live
/// in <c>spikes/</c> from task 01.
/// </para>
/// </remarks>
internal static partial class NativeMethods
{
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TOOLWINDOW = 0x0000_0080L;

    internal const uint GW_OWNER = 4;

    internal const uint DWMWA_CLOAKED = 14;

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_HIDE = 0x8003;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;

    internal const uint WINEVENT_OUTOFCONTEXT = 0;
    internal const uint WINEVENT_SKIPOWNPROCESS = 2;

    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;

    private const int MaxProcessPathLength = 1024;

    /// <summary>
    /// Out-of-context WinEvent callback. The caller <b>must</b> keep its instance alive for the
    /// hook's lifetime — a collected delegate kills the hook silently (repo gotcha).
    /// </summary>
    internal delegate void WinEventProc(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    // ---------------------------------------------------------------- externs

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        nint hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize);

    [LibraryImport("user32.dll", EntryPoint = "UnhookWinEvent", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEventCore(nint hWinEventHook);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true)]
    private static partial int GetWindowTextLengthCore(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisibleCore(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static partial nint GetWindowLongPtrCore(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "GetWindow", SetLastError = true)]
    private static partial nint GetWindowCore(nint hWnd, uint uCmd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static partial uint GetWindowThreadProcessIdCore(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static partial int DwmGetWindowAttributeCore(
        nint hwnd,
        uint dwAttribute,
        out int pvAttribute,
        int cbAttribute);

    [LibraryImport("kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
    private static partial nint OpenProcessCore(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        uint dwProcessId);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandleCore(nint hObject);

    // ---------------------------------------------------------------- wrappers

    /// <summary>Every top-level window on the desktop, in z-order.</summary>
    internal static List<nint> EnumerateTopLevelWindows()
    {
        List<nint> handles = [];

        bool Collect(nint hwnd, nint _)
        {
            handles.Add(hwnd);
            return true;
        }

        EnumWindowsProc callback = Collect;
        EnumWindows(callback, 0);

        // The callback must outlive the call itself, not just the assignment.
        GC.KeepAlive(callback);
        return handles;
    }

    /// <summary>
    /// Reads everything the trackability filter needs in one pass, so callers cross the Win32
    /// boundary once per window rather than six times.
    /// </summary>
    internal static RawWindowInfo DescribeWindow(nint hwnd)
    {
        GetWindowThreadProcessIdCore(hwnd, out uint pid);

        // A failed DWM call is treated as "not cloaked": a hiccup there must not silently empty
        // the inventory.
        int hr = DwmGetWindowAttributeCore(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int));

        return new RawWindowInfo(
            GetWindowTitle(hwnd),
            hwnd != 0 && IsWindowVisibleCore(hwnd),
            GetWindowLongPtrCore(hwnd, GWL_EXSTYLE).ToInt64(),
            GetWindowCore(hwnd, GW_OWNER),
            hr == 0 && cloaked != 0,
            (int)pid);
    }

    /// <summary>The window's title, or an empty string when it has none.</summary>
    internal static string GetWindowTitle(nint hwnd)
    {
        int length = GetWindowTextLengthCore(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        StringBuilder buffer = new(length + 1);
        int copied = GetWindowTextW(hwnd, buffer, buffer.Capacity);
        return copied > 0 ? buffer.ToString() : string.Empty;
    }

    /// <summary>
    /// The full image path of the window's process, or an empty string when the process cannot be
    /// opened or queried (a genuinely protected process — note that plain elevation does
    /// <em>not</em> block this, as task 01 measured).
    /// </summary>
    internal static string GetProcessPath(int pid)
    {
        nint handle = OpenProcessCore(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (handle == 0)
        {
            return string.Empty;
        }

        try
        {
            StringBuilder buffer = new(MaxProcessPathLength);
            uint size = (uint)buffer.Capacity;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : string.Empty;
        }
        finally
        {
            CloseHandleCore(handle);
        }
    }

    /// <summary>
    /// Registers an out-of-context WinEvent hook for a single event across all processes.
    /// Returns 0 on failure. The caller keeps <paramref name="callback"/> alive.
    /// </summary>
    internal static nint HookWinEvent(uint eventId, WinEventProc callback) =>
        SetWinEventHook(
            eventId,
            eventId,
            0,
            callback,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

    /// <summary>Releases every hook in the list and empties it. Zero handles are skipped.</summary>
    internal static void UnhookAll(List<nint> hooks)
    {
        foreach (nint hook in hooks.Where(hook => hook != 0))
        {
            UnhookWinEventCore(hook);
        }

        hooks.Clear();
    }
}
