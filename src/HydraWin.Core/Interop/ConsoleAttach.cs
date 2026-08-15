using System.Runtime.InteropServices;

namespace HydraWin.Core.Interop;

/// <summary>
/// Lets the WPF executable write to the console it was launched from.
/// </summary>
/// <remarks>
/// <c>hydrawin.exe</c> is a <c>WinExe</c>, so it has no console of its own and anything written
/// to <see cref="Console"/> goes nowhere. The CLI paths — task 02's <c>--restore-all</c>
/// placeholder and the real implementation in task 05, which must print
/// <c>restored N window(s), dropped M stale entr(ies)</c> — need this first.
/// <para>Safe to call when there is no parent console: it simply reports false.</para>
/// </remarks>
public static partial class ConsoleAttach
{
    private const int AttachParentProcess = -1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeConsole();

    /// <summary>Attaches to the launching process's console, if there is one.</summary>
    /// <returns><see langword="true"/> when console output will now be visible.</returns>
    public static bool TryAttachToParent() => AttachConsole(AttachParentProcess);

    /// <summary>Detaches again. Call before exiting so the shell prompt is not left mid-line.</summary>
    public static void Detach() => FreeConsole();
}
