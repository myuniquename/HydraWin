using System.Runtime.InteropServices;
using System.Text;

namespace HideShow;

/// <summary>
/// Throwaway spike interop. The durable versions of these live in
/// src/HydraWin.Core/Interop/ once task 02 creates it.
/// </summary>
public static class Native
{
    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWMINIMIZED = 2;
    public const int SW_SHOWMAXIMIZED = 3;
    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const long WS_VISIBLE = 0x1000_0000L;
    public const long WS_EX_TOOLWINDOW = 0x0000_0080L;
    public const long WS_EX_APPWINDOW = 0x0004_0000L;

    public const uint GW_OWNER = 4;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly override string ToString() =>
            $"({Left},{Top})-({Right},{Bottom}) {Right - Left}x{Bottom - Top}";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public readonly override string ToString() => $"({X},{Y})";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;

        public static WINDOWPLACEMENT Create() =>
            new() { length = Marshal.SizeOf<WINDOWPLACEMENT>() };

        public readonly override string ToString() =>
            $"showCmd={ShowCmdName(showCmd)} flags=0x{flags:X} " +
            $"min={ptMinPosition} max={ptMaxPosition} normal={rcNormalPosition}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public static string ShowCmdName(int showCmd) => showCmd switch
    {
        0 => "SW_HIDE(0)",
        1 => "SW_SHOWNORMAL(1)",
        2 => "SW_SHOWMINIMIZED(2)",
        3 => "SW_SHOWMAXIMIZED(3)",
        4 => "SW_SHOWNOACTIVATE(4)",
        5 => "SW_SHOW(5)",
        6 => "SW_MINIMIZE(6)",
        7 => "SW_SHOWMINNOACTIVE(7)",
        9 => "SW_RESTORE(9)",
        _ => $"showCmd({showCmd})",
    };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        out int tokenInformation, int tokenInformationLength, out int returnLength);

    public delegate bool ConsoleCtrlHandler(uint ctrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handler,
        [MarshalAs(UnmanagedType.Bool)] bool add);

    public static string GetWindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLengthW(hWnd);
        if (len <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(len + 2);
        int copied = GetWindowTextW(hWnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }

    public static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        int copied = GetClassNameW(hWnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }

    public static string GetMonitorName(IntPtr hWnd)
    {
        IntPtr mon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        if (mon == IntPtr.Zero)
        {
            return "<none>";
        }

        var mi = new MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<MONITORINFOEX>(),
            szDevice = string.Empty,
        };
        return GetMonitorInfoW(mon, ref mi) ? mi.szDevice : "<unknown>";
    }

    /// <summary>
    /// "Elevated" from a non-elevated caller's point of view: either the token cannot be read at
    /// all (the usual case — UIPI/ACL blocks it) or it reports an elevated token. This is the
    /// signal task 10 needs; UIPI will also make ShowWindow(SW_HIDE) a no-op for such windows.
    /// </summary>
    public static bool LooksElevated(uint pid)
    {
        IntPtr proc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (proc == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            if (!OpenProcessToken(proc, TOKEN_QUERY, out IntPtr token))
            {
                return true;
            }

            try
            {
                return GetTokenInformation(token, TokenElevation, out int elevated,
                    sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(proc);
        }
    }

    /// <summary>Escapes non-ASCII so markers such as U+2733 survive a copy into Markdown.</summary>
    public static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            if (c == '"' || c == '\\')
            {
                sb.Append('\\').Append(c);
            }
            else if (c is >= ' ' and <= '~')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append("\\u").Append(((int)c).ToString("X4"));
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
