using System.Windows;
using HydraWin.Core.Interop;

namespace HydraWin.App;

/// <summary>Application entry point and command-line dispatch.</summary>
public partial class App : Application
{
    private const string RestoreAllFlag = "--restore-all";

    protected override void OnStartup(StartupEventArgs e)
    {
        // --restore-all is handled before anything else and never touches the UI. Task 08 adds
        // the single-instance mutex *below* this branch, not above it: the escape hatch has to
        // work while a wedged first instance still holds the mutex.
        if (e.Args.Any(a => string.Equals(a, RestoreAllFlag, StringComparison.OrdinalIgnoreCase)))
        {
            RunRestoreAll();
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        // Task 08: acquire the `Local\HydraWinSingleton` mutex here, and hand off to the running
        // instance if it is already held.
        // Task 05: run startup recovery from the journal before any other window manipulation.
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>
    /// Placeholder for task 05, which replaces this with a real
    /// <c>RestoreService.RestoreAll</c> run over the recovery journal.
    /// </summary>
    private static void RunRestoreAll()
    {
        // A WinExe has no console of its own; without this the line goes nowhere.
        bool attached = ConsoleAttach.TryAttachToParent();
        try
        {
            Console.WriteLine("restore-all: not implemented yet");
            Console.Out.Flush();
        }
        finally
        {
            if (attached)
            {
                ConsoleAttach.Detach();
            }
        }
    }
}
