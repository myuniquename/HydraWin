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

/// <summary>Win32 <c>POINT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Point
{
    /// <summary>Horizontal coordinate.</summary>
    public int X;

    /// <summary>Vertical coordinate.</summary>
    public int Y;
}

/// <summary>Win32 <c>RECT</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Rect
{
    /// <summary>Left edge.</summary>
    public int Left;

    /// <summary>Top edge.</summary>
    public int Top;

    /// <summary>Right edge.</summary>
    public int Right;

    /// <summary>Bottom edge.</summary>
    public int Bottom;
}

/// <summary>
/// Win32 <c>WINDOWPLACEMENT</c>: everything needed to put a window back exactly where it was,
/// including whether it was maximized.
/// </summary>
/// <remarks>
/// <see cref="Length"/> must equal the struct size before <em>both</em> the get and the set call
/// or Windows rejects them — silently, with no error worth reading. The wrappers in
/// <c>NativeMethods</c> own that so no caller can forget it.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct WindowPlacement
{
    /// <summary>Size of this struct in bytes. Set by the wrappers.</summary>
    public int Length;

    /// <summary>WPF_* flags.</summary>
    public int Flags;

    /// <summary>The SW_* command describing the window's state.</summary>
    public int ShowCmd;

    /// <summary>Position when minimized.</summary>
    public Point MinPosition;

    /// <summary>Position when maximized.</summary>
    public Point MaxPosition;

    /// <summary>Position when restored — in workspace coordinates, so it survives monitor moves.</summary>
    public Rect NormalPosition;
}

/// <summary>
/// The outcome of a show or hide. <c>ShowWindow</c> returns the window's <em>previous</em>
/// visibility rather than success (task 01 measured this), so the wrappers verify the real state
/// afterwards and report it here.
/// </summary>
/// <param name="Succeeded">Whether the window actually ended up in the requested state.</param>
/// <param name="Win32Error">
/// <c>GetLastError</c> immediately after the call. An elevated window refuses the hide with
/// error 5 (<c>ERROR_ACCESS_DENIED</c>) under UIPI — that is task 06's <c>Unmanageable</c> case.
/// </param>
public readonly record struct ShowWindowResult(bool Succeeded, int Win32Error);

/// <summary>
/// One sample of the input the window picker follows, taken from the hardware rather than the
/// message queue.
/// </summary>
/// <param name="ButtonHeld">Whether the left mouse button is still down — the pick continues.</param>
/// <param name="CancelRequested">Whether Escape is down — the pick is abandoned.</param>
public readonly record struct PickerInput(bool ButtonHeld, bool CancelRequested);

/// <summary>Win32 <c>MSG</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Msg
{
    public nint Hwnd;
    public uint Message;
    public nint WParam;
    public nint LParam;
    public uint Time;
    public Point Point;
}

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

    internal const uint TOKEN_QUERY = 0x0008;

    /// <summary><c>GetAncestor(GA_ROOT)</c>: the top-level window a child belongs to.</summary>
    private const uint GA_ROOT = 2;

    private const long WS_EX_TRANSPARENT = 0x0000_0020L;
    private const long WS_EX_NOACTIVATE = 0x0800_0000L;

    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_NOOWNERZORDER = 0x0200;

    private const nint HWND_TOP = 0;
    private const nint HWND_BOTTOM = 1;
    private const nint HWND_TOPMOST = -1;

    /// <summary><c>TOKEN_INFORMATION_CLASS.TokenElevation</c>.</summary>
    internal const int TokenElevation = 20;

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_HIDE = 0x8003;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;

    internal const uint WINEVENT_OUTOFCONTEXT = 0;
    internal const uint WINEVENT_SKIPOWNPROCESS = 2;

    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;

    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const int SW_SHOWNA = 8;

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

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowCore(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowCore(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindowCore(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static partial nint GetForegroundWindowCore();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowPlacement", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowPlacementCore(nint hWnd, ref WindowPlacement lpwndpl);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPlacement", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPlacementCore(nint hWnd, ref WindowPlacement lpwndpl);

    [LibraryImport(
        "shell32.dll",
        EntryPoint = "ExtractIconExW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    private static partial uint ExtractIconExCore(
        string lpszFile,
        int nIconIndex,
        out nint phiconLarge,
        out nint phiconSmall,
        uint nIcons);

    [LibraryImport("user32.dll", EntryPoint = "DestroyIcon", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIconCore(nint hIcon);

    [LibraryImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static partial nint WindowFromPointCore(Point point);

    [LibraryImport("user32.dll", EntryPoint = "GetAncestor")]
    private static partial nint GetAncestorCore(nint hWnd, uint gaFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static partial short GetAsyncKeyStateCore(int vKey);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPosCore(out Point lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRectCore(nint hWnd, out Rect lpRect);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static partial nint SetWindowLongPtrCore(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPosCore(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [LibraryImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKeyCore(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKeyCore(nint hWnd, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static partial int GetMessageCore(out Msg lpMsg, nint hWnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageCore(uint threadId, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    private static partial uint GetCurrentThreadIdCore();

    [LibraryImport("advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessTokenCore(
        nint processHandle,
        uint desiredAccess,
        out nint tokenHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformationCore(
        nint tokenHandle,
        int tokenInformationClass,
        out uint tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

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

    /// <summary>
    /// Whether the handle still refers to a live window and, if so, who owns it and whether it is
    /// visible.
    /// </summary>
    /// <remarks>
    /// One call rather than three because every caller that asks whether a handle is still a
    /// window also needs to know whose it is: handles get recycled, so existence alone proves
    /// nothing about identity.
    /// </remarks>
    internal static bool TryGetIdentity(nint hwnd, out int pid, out bool visible)
    {
        pid = 0;
        visible = false;

        if (hwnd == 0 || !IsWindowCore(hwnd))
        {
            return false;
        }

        GetWindowThreadProcessIdCore(hwnd, out uint processId);
        pid = (int)processId;
        visible = IsWindowVisibleCore(hwnd);
        return true;
    }

    /// <summary>
    /// Reads the window's placement, setting the size field the call requires.
    /// </summary>
    internal static bool TryGetPlacement(nint hwnd, out WindowPlacement placement)
    {
        placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        return hwnd != 0 && GetWindowPlacementCore(hwnd, ref placement);
    }

    /// <summary>
    /// Restores a window's placement — position and maximized state — setting the size field the
    /// call requires. Task 01 measured this as pixel-exact, including across monitors.
    /// </summary>
    internal static bool TrySetPlacement(nint hwnd, in WindowPlacement placement)
    {
        if (hwnd == 0)
        {
            return false;
        }

        WindowPlacement copy = placement;
        copy.Length = Marshal.SizeOf<WindowPlacement>();
        return SetWindowPlacementCore(hwnd, ref copy);
    }

    /// <summary>
    /// Brings a window to the foreground, reporting whether focus actually landed there.
    /// </summary>
    /// <remarks>
    /// <c>SetForegroundWindow</c> silently does nothing unless the calling process is already the
    /// foreground one, so its return value is not to be trusted — this checks
    /// <c>GetForegroundWindow</c> afterwards. HydraWin only ever calls this during a
    /// user-initiated switch, when it *is* foreground; there are deliberately no
    /// <c>AttachThreadInput</c> tricks for any other path (repo gotcha).
    /// </remarks>
    internal static bool TryFocus(nint hwnd)
    {
        if (!TryGetIdentity(hwnd, out _, out bool visible) || !visible)
        {
            return false;
        }

        SetForegroundWindowCore(hwnd);
        return GetForegroundWindowCore() == hwnd;
    }

    /// <summary>
    /// Brings a window to the front of the z-order <em>without</em> activating it.
    /// </summary>
    /// <remarks>
    /// The difference from <see cref="TryFocus"/> is the whole point: a switch started by clicking
    /// inside HydraWin must leave the keyboard with HydraWin, or the very next key press goes to
    /// the app that was just raised. The windows still come to the front, they just do not steal
    /// focus.
    /// </remarks>
    internal static void Raise(nint hwnd) =>
        SetWindowPosCore(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Hides a window and reports whether it actually went away.</summary>
    internal static ShowWindowResult Hide(nint hwnd) => Apply(hwnd, SW_HIDE, wantVisible: false);

    /// <summary>Shows a window and reports whether it actually came back.</summary>
    internal static ShowWindowResult Show(nint hwnd) => Apply(hwnd, SW_SHOW, wantVisible: true);

    /// <summary>
    /// Issues a show command and checks the result against reality rather than trusting the
    /// return value, which is the window's previous visibility.
    /// </summary>
    private static ShowWindowResult Apply(nint hwnd, int command, bool wantVisible)
    {
        if (hwnd == 0)
        {
            return new ShowWindowResult(false, 0);
        }

        ShowWindowCore(hwnd, command);

        // Read the error before anything else can overwrite it.
        int error = Marshal.GetLastWin32Error();

        return new ShowWindowResult(IsWindowVisibleCore(hwnd) == wantVisible, error);
    }

    /// <summary>
    /// Pulls the small icon out of an executable, for the window rows in the UI.
    /// </summary>
    /// <remarks>
    /// <c>ExtractIconExW</c> is used rather than <c>SHGetFileInfo</c> because its signature is
    /// blittable and so works with <c>[LibraryImport]</c>; <c>SHFILEINFOW</c>'s inline character
    /// buffers would have forced runtime marshalling. The caller owns the returned handle and must
    /// pass it to <see cref="DestroyIcon"/>.
    /// </remarks>
    /// <returns>
    /// <see langword="false"/> for an empty path — a protected process, per task 01 — or any file
    /// with no icon, leaving the UI to fall back to a generic glyph.
    /// </returns>
    internal static bool TryExtractSmallIcon(string processPath, out nint hIcon)
    {
        hIcon = 0;
        if (string.IsNullOrEmpty(processPath))
        {
            return false;
        }

        ExtractIconExCore(processPath, 0, out nint large, out nint small, 1);

        // Only the small icon is wanted; the large one is still allocated and has to go back.
        if (large != 0)
        {
            DestroyIconCore(large);
        }

        hIcon = small;
        return small != 0;
    }

    /// <summary>
    /// Whether a process is running elevated. A non-elevated HydraWin cannot hide such a window
    /// (UIPI, measured in task 01), so the tracker leaves them out of the inventory entirely.
    /// </summary>
    /// <remarks>
    /// Being unable to ask counts as elevated. Task 01 established that plain elevation does not
    /// stop <c>OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)</c> for a same-user process, so a
    /// failure here means something stronger than elevation — a protected process — which is
    /// even further beyond reach. Guessing "not elevated" would put a window in the list that the
    /// user could assign and HydraWin could never hide.
    /// </remarks>
    internal static bool IsProcessElevated(int pid)
    {
        nint process = OpenProcessCore(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == 0)
        {
            return true;
        }

        try
        {
            if (!OpenProcessTokenCore(process, TOKEN_QUERY, out nint token))
            {
                return true;
            }

            try
            {
                return GetTokenInformationCore(
                        token, TokenElevation, out uint elevated, sizeof(uint), out _)
                    && elevated != 0;
            }
            finally
            {
                CloseHandleCore(token);
            }
        }
        finally
        {
            CloseHandleCore(process);
        }
    }

    /// <summary>
    /// Whether a mouse button or key is physically down right now, asked of the hardware rather
    /// than of the message queue.
    /// </summary>
    /// <remarks>
    /// The picker runs on this instead of a WPF mouse capture. Capture is fragile in exactly the
    /// situation the picker creates: any window operation during the gesture — and dropping the
    /// main window down the z-order is one — makes WPF release it, which ended the pick on the
    /// first movement. Polling the hardware state cares about none of that, and is what Spy++ has
    /// always done.
    /// </remarks>
    internal static PickerInput ReadPickerInput()
    {
        const int VK_LBUTTON = 0x01;
        const int VK_ESCAPE = 0x1B;

        // The high bit is "currently down". The low bit is "pressed since last asked" and would
        // report a stale press, so it is masked away.
        const int DownMask = 0x8000;

        bool held = (GetAsyncKeyStateCore(VK_LBUTTON) & DownMask) != 0;
        bool cancel = (GetAsyncKeyStateCore(VK_ESCAPE) & DownMask) != 0;

        return new PickerInput(held, cancel);
    }

    /// <summary>Where the pointer is, in physical screen pixels.</summary>
    /// <remarks>
    /// Read from Win32 rather than converted out of WPF: <c>PointToScreen</c> depends on the
    /// process's DPI awareness and gets subtly wrong answers across monitors of different scale,
    /// whereas this and <see cref="TopLevelWindowAt"/> are in the same coordinate space by
    /// construction.
    /// </remarks>
    internal static Point GetCursorPosition() =>
        GetCursorPosCore(out Point point) ? point : default;

    /// <summary>
    /// The top-level window at a screen point. <c>WindowFromPoint</c> returns the deepest child,
    /// so the result is walked up to its root — the user is pointing at an application window,
    /// not at a button inside one.
    /// </summary>
    internal static nint TopLevelWindowAt(Point screenPoint)
    {
        nint hit = WindowFromPointCore(screenPoint);
        if (hit == 0)
        {
            return 0;
        }

        nint root = GetAncestorCore(hit, GA_ROOT);
        return root == 0 ? hit : root;
    }

    /// <summary>
    /// The window's bounding rectangle in physical screen pixels, rejecting degenerate results.
    /// </summary>
    /// <remarks>
    /// A window that is closing, or one of the 0×0 message-only windows that litter the desktop,
    /// reports an empty or inverted rectangle. Callers want this to draw a highlight around, so an
    /// unusable rectangle is reported as no rectangle rather than passed on.
    /// </remarks>
    internal static bool TryGetWindowRect(nint hwnd, out Rect rect)
    {
        if (!GetWindowRectCore(hwnd, out rect))
        {
            rect = default;
            return false;
        }

        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            rect = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Drops a window to the bottom of the z-order, so anything it was covering becomes the
    /// topmost window at those coordinates — and therefore pointable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is how the picker gets HydraWin out of its own way. The obvious alternative — setting
    /// <c>WS_EX_TRANSPARENT</c> on our own window to make it click-through — was tried and
    /// abandoned: WPF owns <c>WS_EX_LAYERED</c> on a window whose <c>AllowsTransparency</c> is
    /// false and strips it back out, which left the window click-through but fully opaque. A
    /// half-applied ghost is the worst of both, and if a pick then ended abnormally the whole app
    /// stayed invisible to the mouse.
    /// </para>
    /// <para>
    /// Z-order carries no such risk: nothing can leave the window in a state that swallows input.
    /// </para>
    /// </remarks>
    internal static void SendToBottom(nint hwnd) =>
        SetWindowPosCore(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Puts a window back on top after <see cref="SendToBottom"/>.</summary>
    internal static void RestoreZOrder(nint hwnd, bool topmost) =>
        SetWindowPosCore(
            hwnd,
            topmost ? HWND_TOPMOST : HWND_TOP,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>
    /// Turns a window into the picker's highlight frame: always on top, never activated, invisible
    /// to hit-testing, and absent from Alt-Tab.
    /// </summary>
    /// <remarks>
    /// <c>WS_EX_TRANSPARENT</c> keeps it out of <see cref="TopLevelWindowAt"/>, so the highlight
    /// can never be mistaken for the target. <c>WS_EX_TOOLWINDOW</c> is belt and braces: HydraWin's
    /// own filter rejects tool windows, so even if it were enumerated it could not be tracked.
    /// </remarks>
    internal static void MakeOverlay(nint hwnd)
    {
        long style = GetWindowLongPtrCore(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrCore(
            hwnd,
            GWL_EXSTYLE,
            (nint)(style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    /// <summary>
    /// Places the highlight over a target rectangle, in physical pixels.
    /// </summary>
    /// <remarks>
    /// Positioned through Win32 rather than by setting WPF's <c>Left</c>/<c>Top</c>, which are
    /// device-independent units and would need DPI arithmetic that goes wrong the moment the
    /// target sits on a monitor with a different scale factor.
    /// </remarks>
    internal static void PositionOverlay(nint hwnd, in Rect rect) =>
        SetWindowPosCore(
            hwnd,
            HWND_TOPMOST,
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top,
            SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    /// <summary>Releases an icon handle from <see cref="TryExtractSmallIcon"/>.</summary>
    internal static void DestroyIcon(nint hIcon)
    {
        if (hIcon != 0)
        {
            DestroyIconCore(hIcon);
        }
    }

    /// <summary>
    /// Claims a global hotkey for the <em>calling thread</em>.
    /// </summary>
    /// <remarks>
    /// The null window handle is the point. <c>RegisterHotKey</c> with no window posts
    /// <c>WM_HOTKEY</c> to the calling thread's message queue instead of to a window, so the owner
    /// needs no window class, no <c>WndProc</c> and no <c>HwndSource</c> — just a thread running
    /// <see cref="WaitForHotkey"/>. That is what lets the hotkeys live off the UI thread cheaply,
    /// and it is why the panic restore still works when that thread is wedged.
    /// </remarks>
    /// <returns><see langword="false"/> when another application already owns the combination.</returns>
    internal static bool TryRegisterHotkey(int id, uint modifiers, uint virtualKey)
    {
        // MOD_NOREPEAT, always: every action behind a hotkey here is a command, and holding the
        // combination down must not fire it forty times a second. Enforced at the boundary rather
        // than trusted from callers.
        const uint MOD_NOREPEAT = 0x4000;

        if (virtualKey == 0 || (modifiers & ~MOD_NOREPEAT) == 0)
        {
            return false;
        }

        return RegisterHotKeyCore(0, id, modifiers | MOD_NOREPEAT, virtualKey);
    }

    /// <summary>
    /// Releases every hotkey in the list and empties it. Must run on the registering thread.
    /// </summary>
    internal static void UnregisterHotkeys(List<int> ids)
    {
        foreach (int id in ids)
        {
            UnregisterHotKeyCore(0, id);
        }

        ids.Clear();
    }

    /// <summary>
    /// Blocks until a hotkey fires or the loop is asked to stop.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> with the hotkey's id, or <see langword="false"/> when
    /// <see cref="StopHotkeyLoop"/> has posted the quit message.
    /// </returns>
    internal static bool WaitForHotkey(out int id)
    {
        const uint WM_HOTKEY = 0x0312;

        id = 0;

        // GetMessage returns 0 for WM_QUIT and -1 for an error; either ends the loop.
        while (GetMessageCore(out Msg message, 0, 0, 0) > 0)
        {
            if (message.Message == WM_HOTKEY)
            {
                id = (int)message.WParam;
                return true;
            }
        }

        return false;
    }

    /// <summary>The calling thread's id, so its message loop can be stopped later.</summary>
    internal static uint CurrentThreadId() => GetCurrentThreadIdCore();

    /// <summary>Ends a <see cref="WaitForHotkey"/> loop running on another thread.</summary>
    internal static void StopHotkeyLoop(uint threadId)
    {
        const uint WM_QUIT = 0x0012;
        PostThreadMessageCore(threadId, WM_QUIT, 0, 0);
    }

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
