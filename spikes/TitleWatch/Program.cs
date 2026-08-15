using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TitleWatch;

/// <summary>
/// Spike C — title transitions (task 01, question 3).
///
/// SetWinEventHook(EVENT_OBJECT_NAMECHANGE) across all processes, filtered to real top-level
/// window titles (idObject == OBJID_WINDOW, idChild == CHILDID_SELF). A bare console app receives
/// no window messages, so this runs its own GetMessage/DispatchMessage pump — without it the
/// out-of-context hook never fires.
/// </summary>
public static class Program
{
    private const uint WM_QUIT = 0x0012;

    // The callback MUST be rooted for the hook's lifetime: a lambda handed straight to
    // SetWinEventHook gets collected and the hook dies silently. (CLAUDE.md gotcha.)
    private static Native.WinEventProc? winEventProc;
    private static IntPtr hook;
    private static StreamWriter? log;
    private static string? filter;
    private static bool includeInvisible = true;
    private static readonly Dictionary<long, string> LastTitle = [];
    private static uint pumpThreadId;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string logPath = ArgValue(args, "--log") ?? "titlewatch.log";
        filter = ArgValue(args, "--filter");
        includeInvisible = !args.Contains("--visible-only");
        int? seconds = ArgValue(args, "--seconds") is string s
            ? int.Parse(s, CultureInfo.InvariantCulture)
            : null;

        log = new StreamWriter(logPath, append: true, Encoding.UTF8) { AutoFlush = true };
        pumpThreadId = Native.GetCurrentThreadId();

        Write($"=== titlewatch start {DateTime.Now:O} "
            + (filter is null ? "(no filter)" : $"(filter: {filter})")
            + (includeInvisible ? " (incl. hidden windows)" : " (visible only)") + " ===");
        Write("cols: time | hwnd | visible | process | class | new title");

        winEventProc = OnWinEvent;
        hook = Native.SetWinEventHook(
            Native.EVENT_OBJECT_NAMECHANGE,
            Native.EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero,
            winEventProc,
            0,
            0,
            Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

        if (hook == IntPtr.Zero)
        {
            Write($"SetWinEventHook FAILED, win32={Marshal.GetLastWin32Error()}");
            return 1;
        }

        Write($"hook=0x{hook.ToInt64():X} — listening. Ctrl+C to stop.");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Native.PostThreadMessageW(pumpThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        };

        if (seconds is int limit)
        {
            var timer = new Timer(
                _ => Native.PostThreadMessageW(pumpThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero),
                null,
                TimeSpan.FromSeconds(limit),
                Timeout.InfiniteTimeSpan);
            Write($"auto-stop in {limit}s");
            GC.KeepAlive(timer);
        }

        while (Native.GetMessageW(out Native.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }

        Native.UnhookWinEvent(hook);
        Write($"=== titlewatch stop {DateTime.Now:O} ===");
        log.Dispose();
        return 0;
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Only the window's own name; controls and menu items also raise NAMECHANGE.
        if (idObject != Native.OBJID_WINDOW || idChild != Native.CHILDID_SELF || hwnd == IntPtr.Zero)
        {
            return;
        }

        string title = Native.GetWindowTitle(hwnd);
        if (title.Length == 0)
        {
            return;
        }

        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        string process = "?";
        try
        {
            using Process p = Process.GetProcessById((int)pid);
            process = p.ProcessName;
        }
        catch (ArgumentException)
        {
            // process gone between the event and the lookup
        }

        if (filter is not null
            && !title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            && !process.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool visible = Native.IsWindowVisible(hwnd);
        if (!visible && !includeInvisible)
        {
            return;
        }

        // Apps re-set the same title repeatedly; only transitions are interesting.
        long key = hwnd.ToInt64();
        if (LastTitle.TryGetValue(key, out string? previous) && previous == title)
        {
            return;
        }

        LastTitle[key] = title;

        Write($"{DateTime.Now:HH:mm:ss.fff} | 0x{key:X8} | vis={(visible ? "Y" : "n")} | "
            + $"{process,-18} | {Native.GetWindowClass(hwnd),-34} | {Native.Quote(title)}");
    }

    private static void Write(string line)
    {
        Console.WriteLine(line);
        log?.WriteLine(line);
    }
}
