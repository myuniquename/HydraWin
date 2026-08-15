using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace HideShow;

/// <summary>
/// Spike A — hide/show round-trip fidelity (task 01, question 2).
///
/// Safety contract: every hide is journaled and flushed to disk first, and the window is re-shown
/// on every exit path — normal, Ctrl+C, console close, unhandled exception, and a watchdog cap.
/// `hideshow rescue` re-shows anything a killed run left behind.
/// </summary>
public static class Program
{
    private static readonly Lock RestoreLock = new();

    // Native callbacks must outlive the registration or the GC collects them and the hook dies.
    private static Native.ConsoleCtrlHandler? consoleHandler;
    private static Timer? watchdog;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0)
        {
            Usage();
            return 1;
        }

        InstallGuards();

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "list" => CmdList(args),
                "cycle" => CmdCycle(args),
                "hold" => CmdHold(args),
                "rescue" => CmdRescue(),
                _ => UsageWith($"unknown command '{args[0]}'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAILED: {ex}");
            return 3;
        }
        finally
        {
            RestoreAll("exit");
        }
    }

    private static int UsageWith(string message)
    {
        Console.Error.WriteLine(message);
        Usage();
        return 1;
    }

    private static void Usage()
    {
        Console.WriteLine("""
            hideshow — task 01 spike A (hide/show round-trip fidelity)

              list [substring] [--all] [--hidden]
                    List top-level windows. Default shows app-like windows with a title.
                    --all     include tool windows / owned windows / untitled ones
                    --hidden  only windows where IsWindowVisible == false (baseline snapshot)

              cycle (<substring> | --hwnd 0xABC) [--seconds N]
                    Journal, hide, wait N seconds (default 10), re-show + SetWindowPlacement,
                    then report the before/after placement, rect and monitor.

              hold (<substring> | --hwnd 0xABC) [--max-seconds N]
                    Journal, hide, wait for Enter (watchdog force-restores after N, default 120).
                    Use this while triggering an event in the hidden window (spikes B and C).

              rescue
                    Re-show every window still listed in the journal, then clear it.
                    Journal: %APPDATA%\\HydraWin\\spike-hidden.jsonl
            """);
    }

    // ---------------------------------------------------------------- guards

    private static void InstallGuards()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Ctrl+C — restoring before exit.");
            RestoreAll("Ctrl+C");
            Environment.Exit(130);
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreAll("ProcessExit");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"UNHANDLED: {e.ExceptionObject}");
            RestoreAll("UnhandledException");
        };

        consoleHandler = ctrlType =>
        {
            // CTRL_CLOSE_EVENT(2), CTRL_LOGOFF_EVENT(5), CTRL_SHUTDOWN_EVENT(6)
            Console.Error.WriteLine($"console ctrl {ctrlType} — restoring.");
            RestoreAll($"console ctrl {ctrlType}");
            return false;
        };
        Native.SetConsoleCtrlHandler(consoleHandler, true);
    }

    private static void ArmWatchdog(int seconds)
    {
        watchdog = new Timer(
            _ =>
            {
                Console.Error.WriteLine($"WATCHDOG: {seconds}s elapsed — force-restoring.");
                RestoreAll("watchdog");
                Environment.Exit(2);
            },
            null,
            TimeSpan.FromSeconds(seconds),
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Idempotent. Exit paths pass <paramref name="onlyOwn"/> = true so that a `list` (or a
    /// second spike run) never yanks back a window another process is deliberately holding
    /// hidden; `rescue` passes false to sweep up after a crashed run.
    /// </summary>
    private static void RestoreAll(string reason, bool onlyOwn = true)
    {
        lock (RestoreLock)
        {
            List<HiddenEntry> all = Journal.ReadAll();
            if (all.Count == 0)
            {
                return;
            }

            List<HiddenEntry> entries = onlyOwn
                ? [.. all.Where(e => e.OwnerPid == Environment.ProcessId)]
                : all;
            List<HiddenEntry> untouched = onlyOwn
                ? [.. all.Where(e => e.OwnerPid != Environment.ProcessId)]
                : [];

            if (entries.Count == 0)
            {
                return;
            }

            Console.WriteLine($"restore ({reason}): {entries.Count} journal entr"
                + (entries.Count == 1 ? "y" : "ies"));

            var unresolved = new List<HiddenEntry>(untouched);
            foreach (HiddenEntry e in entries)
            {
                var hwnd = new IntPtr(e.Hwnd);
                if (!Native.IsWindow(hwnd))
                {
                    Console.WriteLine($"  0x{e.Hwnd:X} gone (window closed while hidden) — dropped");
                    continue;
                }

                Native.ShowWindow(hwnd, Native.SW_SHOW);
                Native.WINDOWPLACEMENT wp = e.ToPlacement();
                bool placed = Native.SetWindowPlacement(hwnd, ref wp);
                bool visible = Native.IsWindowVisible(hwnd);

                Console.WriteLine($"  0x{e.Hwnd:X} {Native.Quote(e.Title)} "
                    + $"-> visible={visible} setPlacement={placed}");

                if (!visible)
                {
                    unresolved.Add(e);
                }
            }

            Journal.Rewrite(unresolved);
            int stuck = unresolved.Count - untouched.Count;
            if (stuck > 0)
            {
                Console.Error.WriteLine(
                    $"  WARNING: {stuck} window(s) still not visible — journal kept.");
            }
        }
    }

    // ---------------------------------------------------------------- model

    private sealed record WindowInfo(
        IntPtr Hwnd,
        string Title,
        string ClassName,
        uint Pid,
        string Process,
        bool Visible,
        bool Owned,
        bool ToolWindow,
        bool Elevated);

    private static List<WindowInfo> Enumerate()
    {
        var list = new List<WindowInfo>();
        Native.EnumWindowsProc callback = (hwnd, _) =>
        {
            string title = Native.GetWindowTitle(hwnd);
            string cls = Native.GetWindowClass(hwnd);
            Native.GetWindowThreadProcessId(hwnd, out uint pid);

            string process = "?";
            try
            {
                using Process p = Process.GetProcessById((int)pid);
                process = p.ProcessName;
            }
            catch (ArgumentException)
            {
                // process exited between enumeration and lookup
            }
            catch (InvalidOperationException)
            {
                // ditto
            }

            long exStyle = Native.GetWindowLongPtrW(hwnd, Native.GWL_EXSTYLE).ToInt64();

            list.Add(new WindowInfo(
                hwnd,
                title,
                cls,
                pid,
                process,
                Native.IsWindowVisible(hwnd),
                Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero,
                (exStyle & Native.WS_EX_TOOLWINDOW) != 0,
                Native.LooksElevated(pid)));
            return true;
        };

        Native.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return list;
    }

    private static void PrintWindow(WindowInfo w)
    {
        var wp = Native.WINDOWPLACEMENT.Create();
        Native.GetWindowPlacement(w.Hwnd, ref wp);
        Native.GetWindowRect(w.Hwnd, out Native.RECT rect);

        Console.WriteLine(
            $"0x{w.Hwnd.ToInt64():X8}  pid={w.Pid,-6} {w.Process,-20} "
            + $"vis={(w.Visible ? "Y" : "n")} {(w.Elevated ? "EL" : "  ")} "
            + $"{Native.ShowCmdName(wp.showCmd),-20} {rect} {Native.GetMonitorName(w.Hwnd),-14} "
            + $"{Native.Quote(w.Title)}  [{w.ClassName}]");
    }

    // ---------------------------------------------------------------- commands

    private static int CmdList(string[] args)
    {
        bool all = args.Contains("--all");
        bool hiddenOnly = args.Contains("--hidden");
        string? filter = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        List<WindowInfo> windows = Enumerate();
        IEnumerable<WindowInfo> q = windows;

        if (!all)
        {
            q = q.Where(w => w.Title.Length > 0 && !w.ToolWindow && !w.Owned);
        }

        if (hiddenOnly)
        {
            q = q.Where(w => !w.Visible);
        }

        if (filter is not null)
        {
            q = q.Where(w => w.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || w.Process.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        List<WindowInfo> results = [.. q.OrderBy(w => w.Process, StringComparer.OrdinalIgnoreCase)
                                        .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)];

        Console.WriteLine($"# {DateTime.Now:O}  {results.Count} window(s)"
            + (hiddenOnly ? "  [hidden only]" : string.Empty)
            + (all ? "  [all]" : string.Empty)
            + (filter is not null ? $"  [filter: {filter}]" : string.Empty));
        Console.WriteLine("# EL = elevated / token unreadable (UIPI will block SW_HIDE)");
        foreach (WindowInfo w in results)
        {
            PrintWindow(w);
        }

        return 0;
    }

    private static WindowInfo? Resolve(string[] args)
    {
        List<WindowInfo> windows = Enumerate();

        int hwndIdx = Array.IndexOf(args, "--hwnd");
        if (hwndIdx >= 0 && hwndIdx + 1 < args.Length)
        {
            string raw = args[hwndIdx + 1];
            bool hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            long value = hex
                ? long.Parse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : long.Parse(raw, CultureInfo.InvariantCulture);
            WindowInfo? byHandle = windows.FirstOrDefault(w => w.Hwnd.ToInt64() == value);
            if (byHandle is null)
            {
                Console.Error.WriteLine($"no top-level window with handle 0x{value:X}");
            }

            return byHandle;
        }

        string? needle = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (needle is null)
        {
            Console.Error.WriteLine("give a title substring or --hwnd 0xABC");
            return null;
        }

        List<WindowInfo> matches = [.. windows.Where(w =>
            w.Title.Length > 0 && !w.ToolWindow && !w.Owned
            && w.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))];

        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"no window title contains '{needle}'");
            return null;
        }

        if (matches.Count > 1)
        {
            Console.Error.WriteLine($"'{needle}' is ambiguous ({matches.Count} matches) — "
                + "re-run with --hwnd:");
            foreach (WindowInfo m in matches)
            {
                PrintWindow(m);
            }

            return null;
        }

        return matches[0];
    }

    private static int IntArg(string[] args, string name, int fallback)
    {
        int idx = Array.IndexOf(args, name);
        return idx >= 0 && idx + 1 < args.Length
            ? int.Parse(args[idx + 1], CultureInfo.InvariantCulture)
            : fallback;
    }

    /// <summary>Journals (flushed) and then hides. Returns the captured placement.</summary>
    private static Native.WINDOWPLACEMENT? Hide(WindowInfo w)
    {
        var before = Native.WINDOWPLACEMENT.Create();
        if (!Native.GetWindowPlacement(w.Hwnd, ref before))
        {
            Console.Error.WriteLine(
                $"GetWindowPlacement failed, win32={Marshal.GetLastWin32Error()}");
            return null;
        }

        Native.GetWindowRect(w.Hwnd, out Native.RECT rectBefore);
        string monBefore = Native.GetMonitorName(w.Hwnd);

        Console.WriteLine($"target   0x{w.Hwnd.ToInt64():X8} {Native.Quote(w.Title)} "
            + $"[{w.Process} pid={w.Pid}{(w.Elevated ? ", ELEVATED" : string.Empty)}]");
        Console.WriteLine($"before   {before}");
        Console.WriteLine($"before   rect={rectBefore} monitor={monBefore} "
            + $"visible={Native.IsWindowVisible(w.Hwnd)} zoomed={Native.IsZoomed(w.Hwnd)} "
            + $"iconic={Native.IsIconic(w.Hwnd)}");

        // WRITE-AHEAD: the journal entry must be on disk before the window disappears.
        Journal.Append(HiddenEntry.From(w.Hwnd, w.Title, w.Process, w.Pid, before));
        Console.WriteLine($"journal  flushed -> {Journal.Path}");

        bool wasVisible = Native.ShowWindow(w.Hwnd, Native.SW_HIDE);
        int err = Marshal.GetLastWin32Error();
        Thread.Sleep(250);
        bool nowVisible = Native.IsWindowVisible(w.Hwnd);

        // ShowWindow returns the PREVIOUS visibility, not success — IsWindowVisible is the check.
        Console.WriteLine($"hide     ShowWindow(SW_HIDE) returned {wasVisible} (previous visibility), "
            + $"win32={err}");
        Console.WriteLine(nowVisible
            ? "hide     *** REFUSED: window is still visible (task 06 'Unmanageable' case) ***"
            : "hide     confirmed: IsWindowVisible == false");

        return before;
    }

    private static void ReportRestore(WindowInfo w, Native.WINDOWPLACEMENT before)
    {
        var after = Native.WINDOWPLACEMENT.Create();
        Native.GetWindowPlacement(w.Hwnd, ref after);
        Native.GetWindowRect(w.Hwnd, out Native.RECT rectAfter);

        Console.WriteLine($"after    {after}");
        Console.WriteLine($"after    rect={rectAfter} monitor={Native.GetMonitorName(w.Hwnd)} "
            + $"visible={Native.IsWindowVisible(w.Hwnd)} zoomed={Native.IsZoomed(w.Hwnd)}");

        bool sameShowCmd = before.showCmd == after.showCmd;
        bool sameNormal = before.rcNormalPosition.Left == after.rcNormalPosition.Left
            && before.rcNormalPosition.Top == after.rcNormalPosition.Top
            && before.rcNormalPosition.Right == after.rcNormalPosition.Right
            && before.rcNormalPosition.Bottom == after.rcNormalPosition.Bottom;

        Console.WriteLine($"VERDICT  showCmd {(sameShowCmd ? "MATCH" : "DIFFERS")} "
            + $"({Native.ShowCmdName(before.showCmd)} -> {Native.ShowCmdName(after.showCmd)}), "
            + $"rcNormalPosition {(sameNormal ? "MATCH" : "DIFFERS")}");
    }

    private static int CmdCycle(string[] args)
    {
        WindowInfo? w = Resolve(args);
        if (w is null)
        {
            return 1;
        }

        int seconds = IntArg(args, "--seconds", 10);
        ArmWatchdog(seconds + 60);

        Native.WINDOWPLACEMENT? before = Hide(w);
        if (before is null)
        {
            return 1;
        }

        Console.WriteLine($"waiting  {seconds}s while hidden…");
        Thread.Sleep(TimeSpan.FromSeconds(seconds));

        RestoreAll("cycle");
        ReportRestore(w, before.Value);
        return 0;
    }

    private static int CmdHold(string[] args)
    {
        WindowInfo? w = Resolve(args);
        if (w is null)
        {
            return 1;
        }

        int maxSeconds = IntArg(args, "--max-seconds", 120);
        ArmWatchdog(maxSeconds);

        Native.WINDOWPLACEMENT? before = Hide(w);
        if (before is null)
        {
            return 1;
        }

        Console.WriteLine($"HELD     window is hidden. Press Enter to restore "
            + $"(watchdog restores after {maxSeconds}s).");
        Console.ReadLine();

        RestoreAll("hold");
        ReportRestore(w, before.Value);
        return 0;
    }

    private static int CmdRescue()
    {
        List<HiddenEntry> entries = Journal.ReadAll();
        Console.WriteLine($"journal  {Journal.Path}");
        if (entries.Count == 0)
        {
            Console.WriteLine("rescue   journal is empty — nothing to restore.");
            return 0;
        }

        foreach (HiddenEntry e in entries)
        {
            Console.WriteLine($"  pending 0x{e.Hwnd:X} {Native.Quote(e.Title)} "
                + $"[{e.Process} pid={e.Pid}] hidden at {e.HiddenAtUtc} "
                + $"by hideshow pid={e.OwnerPid}");
        }

        RestoreAll("rescue", onlyOwn: false);
        return Journal.ReadAll().Count == 0 ? 0 : 4;
    }
}
