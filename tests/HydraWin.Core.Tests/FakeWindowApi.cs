using HydraWin.Core.Interop;

namespace HydraWin.Core.Tests;

/// <summary>
/// A scripted <see cref="IWindowApi"/>. This is what the seam exists for: a recycled window
/// handle cannot be conjured on demand against real Win32, and task 06 will need to assert the
/// order of calls to prove the journal is written before any window is hidden.
/// </summary>
internal sealed class FakeWindowApi : IWindowApi
{
    private readonly Dictionary<nint, FakeWindow> windows = [];

    /// <summary>Every call made, in order, as <c>Verb(0xHANDLE)</c>.</summary>
    internal List<string> Calls { get; } = [];

    /// <summary>Handles whose <see cref="Show"/> should report failure.</summary>
    internal HashSet<nint> RefuseToShow { get; } = [];

    internal FakeWindow Add(nint hwnd, int pid, string path, bool visible = false)
    {
        var window = new FakeWindow
        {
            Pid = pid,
            ProcessPath = path,
            Visible = visible,
        };
        windows[hwnd] = window;
        return window;
    }

    internal FakeWindow? Get(nint hwnd) => windows.GetValueOrDefault(hwnd);

    public bool IsWindow(nint hwnd)
    {
        Calls.Add($"IsWindow(0x{hwnd:X})");
        return windows.ContainsKey(hwnd);
    }

    public bool IsWindowVisible(nint hwnd) => windows.TryGetValue(hwnd, out FakeWindow? w) && w.Visible;

    public int GetProcessId(nint hwnd) => windows.TryGetValue(hwnd, out FakeWindow? w) ? w.Pid : 0;

    public string GetProcessPath(int pid) =>
        windows.Values.FirstOrDefault(w => w.Pid == pid)?.ProcessPath ?? string.Empty;

    public string GetWindowTitle(nint hwnd) =>
        windows.TryGetValue(hwnd, out FakeWindow? w) ? w.Title : string.Empty;

    public bool TryGetPlacement(nint hwnd, out WindowPlacement placement)
    {
        Calls.Add($"GetPlacement(0x{hwnd:X})");
        if (windows.TryGetValue(hwnd, out FakeWindow? w))
        {
            placement = w.Placement;
            return true;
        }

        placement = default;
        return false;
    }

    public bool TrySetPlacement(nint hwnd, in WindowPlacement placement)
    {
        Calls.Add($"SetPlacement(0x{hwnd:X})");
        if (!windows.TryGetValue(hwnd, out FakeWindow? w))
        {
            return false;
        }

        w.Placement = placement;
        return true;
    }

    public ShowWindowResult Hide(nint hwnd)
    {
        Calls.Add($"Hide(0x{hwnd:X})");
        if (!windows.TryGetValue(hwnd, out FakeWindow? w))
        {
            return new ShowWindowResult(false, 0);
        }

        w.Visible = false;
        return new ShowWindowResult(true, 0);
    }

    public ShowWindowResult Show(nint hwnd)
    {
        Calls.Add($"Show(0x{hwnd:X})");
        if (!windows.TryGetValue(hwnd, out FakeWindow? w))
        {
            return new ShowWindowResult(false, 0);
        }

        if (RefuseToShow.Contains(hwnd))
        {
            // Mirrors an elevated window under UIPI: the call is refused with ERROR_ACCESS_DENIED
            // and the window stays as it was (task 01 measured this).
            return new ShowWindowResult(false, 5);
        }

        w.Visible = true;
        return new ShowWindowResult(true, 0);
    }
}

/// <summary>One window in the fake desktop.</summary>
internal sealed class FakeWindow
{
    internal int Pid { get; set; }

    internal string ProcessPath { get; set; } = string.Empty;

    internal string Title { get; set; } = string.Empty;

    internal bool Visible { get; set; }

    internal WindowPlacement Placement { get; set; }
}
