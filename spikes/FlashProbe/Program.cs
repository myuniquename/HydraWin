using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FlashProbe;

/// <summary>
/// Spike B — flash observability (task 01, question 1).
///
/// Creates a real (never shown) top-level window, registers it with RegisterShellHookWindow and
/// logs every SHELLHOOK message. A message-only (HWND_MESSAGE) window would NOT receive shell
/// hook messages, hence the real top-level window.
///
/// WINDOWCREATED/DESTROYED traffic is deliberately logged too: it is the proof that the hook was
/// alive at the moment a FLASH failed to arrive. Without that baseline, "no flash" and
/// "broken hook" look identical in the log.
/// </summary>
public static class Program
{
    private const int WM_DESTROY = 0x0002;
    private const int WM_CLOSE = 0x0010;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;

    private const int HSHELL_HIGHBIT = 0x8000;

    // Delegates handed to Win32 must be rooted for the lifetime of the registration.
    private static Native.WndProc? wndProcDelegate;
    private static IntPtr hostWindow;
    private static uint shellHookMessage;
    private static StreamWriter? log;
    private static string? filter;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string logPath = ArgValue(args, "--log") ?? "flashprobe.log";
        filter = ArgValue(args, "--filter");

        log = new StreamWriter(logPath, append: true, Encoding.UTF8) { AutoFlush = true };

        Write($"=== flashprobe start {DateTime.Now:O} "
            + (filter is null ? "(no filter)" : $"(filter: {filter})") + " ===");

        if (!CreateHostWindow())
        {
            Write($"FAILED to create host window, win32={Marshal.GetLastWin32Error()}");
            return 1;
        }

        shellHookMessage = Native.RegisterWindowMessageW("SHELLHOOK");
        Write($"host hwnd=0x{hostWindow.ToInt64():X} SHELLHOOK message id={shellHookMessage} "
            + $"(0x{shellHookMessage:X})");

        if (!Native.RegisterShellHookWindow(hostWindow))
        {
            Write($"RegisterShellHookWindow FAILED, win32={Marshal.GetLastWin32Error()}");
            return 1;
        }

        Write("RegisterShellHookWindow OK — listening. Ctrl+C to stop.");
        Write("cols: time | wParam(raw) | name | hwnd | visible | process | title");

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Native.PostMessageW(hostWindow, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        };

        while (Native.GetMessageW(out Native.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }

        Native.DeregisterShellHookWindow(hostWindow);
        Write($"=== flashprobe stop {DateTime.Now:O} ===");
        log.Dispose();
        return 0;
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static bool CreateHostWindow()
    {
        wndProcDelegate = HostWndProc;

        var wc = new Native.WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<Native.WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
            hInstance = Native.GetModuleHandleW(null),
            lpszClassName = "HydraWinFlashProbeHost",
        };

        ushort atom = Native.RegisterClassExW(ref wc);
        if (atom == 0)
        {
            return false;
        }

        // A real top-level window (never shown). HWND_MESSAGE windows get no shell hook messages.
        hostWindow = Native.CreateWindowExW(
            0, wc.lpszClassName, "HydraWin FlashProbe", WS_OVERLAPPEDWINDOW,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        return hostWindow != IntPtr.Zero;
    }

    private static IntPtr HostWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == shellHookMessage && shellHookMessage != 0)
        {
            LogShellHook(wParam.ToInt64(), lParam);
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WM_CLOSE:
                Native.DestroyWindow(hWnd);
                return IntPtr.Zero;
            case WM_DESTROY:
                Native.PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return Native.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }

    private static void LogShellHook(long wParam, IntPtr subject)
    {
        string title = Native.GetWindowTitle(subject);
        string process = "?";
        Native.GetWindowThreadProcessId(subject, out uint pid);
        try
        {
            using Process p = Process.GetProcessById((int)pid);
            process = p.ProcessName;
        }
        catch (ArgumentException)
        {
            // process gone
        }

        if (filter is not null
            && !title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            && !process.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool visible = Native.IsWindowVisible(subject);
        Write($"{DateTime.Now:HH:mm:ss.fff} | 0x{wParam:X4} | {CodeName(wParam),-28} | "
            + $"0x{subject.ToInt64():X8} | vis={(visible ? "Y" : "n")} | {process,-18} | "
            + Native.Quote(title));
    }

    private static string CodeName(long wParam)
    {
        bool high = (wParam & HSHELL_HIGHBIT) != 0;
        long code = wParam & ~HSHELL_HIGHBIT;
        string name = code switch
        {
            1 => "HSHELL_WINDOWCREATED",
            2 => "HSHELL_WINDOWDESTROYED",
            3 => "HSHELL_ACTIVATESHELLWINDOW",
            4 => high ? "HSHELL_RUDEAPPACTIVATED" : "HSHELL_WINDOWACTIVATED",
            5 => "HSHELL_GETMINRECT",
            6 => high ? "HSHELL_FLASH" : "HSHELL_REDRAW",
            7 => "HSHELL_TASKMAN",
            8 => "HSHELL_LANGUAGE",
            9 => "HSHELL_SYSMENU",
            10 => "HSHELL_ENDTASK",
            11 => "HSHELL_ACCESSIBILITYSTATE",
            12 => "HSHELL_APPCOMMAND",
            13 => "HSHELL_WINDOWREPLACED",
            14 => "HSHELL_WINDOWREPLACING",
            16 => "HSHELL_MONITORCHANGED",
            _ => $"HSHELL_UNKNOWN({code})",
        };
        return high && code is not (4 or 6) ? name + "|HIGHBIT" : name;
    }

    private static void Write(string line)
    {
        Console.WriteLine(line);
        log?.WriteLine(line);
    }
}
