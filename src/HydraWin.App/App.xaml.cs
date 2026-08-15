using System.Windows;
using HydraWin.Core.Interop;
using HydraWin.Core.Recovery;

namespace HydraWin.App;

/// <summary>Application entry point, command-line dispatch, and crash recovery.</summary>
public partial class App : Application
{
    private const string RestoreAllFlag = "--restore-all";

    private RecoveryJournal? journal;
    private RestoreService? restoreService;

    protected override void OnStartup(StartupEventArgs e)
    {
        // --restore-all is handled before anything else and never touches the UI. Task 08 adds
        // the single-instance mutex *below* this branch, not above it: the escape hatch has to
        // work while a wedged first instance still holds it.
        if (e.Args.Any(a => string.Equals(a, RestoreAllFlag, StringComparison.OrdinalIgnoreCase)))
        {
            RunRestoreAll();
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        journal = new RecoveryJournal();
        restoreService = new RestoreService(Win32WindowApi.Instance);

        // Startup recovery, before any other window manipulation. A non-empty journal here means
        // the previous run did not exit cleanly, and visible windows are always the safe state to
        // start from. This needs no command-line flag: an ordinary launch repairs itself.
        RestoreSummary recovered = restoreService.RestoreAll(journal);

        // Task 08: acquire the `Local\HydraWinSingleton` mutex here.
        SessionEnding += OnSessionEnding;

        var window = new MainWindow(journal, restoreService);
        MainWindow = window;

        if (recovered.Restored > 0 || recovered.Stale > 0)
        {
            window.ShowRecoveryNotice(recovered);
        }

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean exit: nothing may be left hidden once HydraWin is gone. Task 08 gates this on the
        // restore-on-exit setting; until then it always runs, which is the safe default.
        RestoreOnShutdown();
        journal?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// The headless escape hatch, for when the UI cannot start at all. Reads the journal, shows
    /// what it lists, and exits — no WPF window, no single-instance mutex.
    /// </summary>
    private static void RunRestoreAll()
    {
        // A WinExe has no console of its own; without this the line goes nowhere.
        bool attached = ConsoleAttach.TryAttachToParent();
        try
        {
            using var journal = new RecoveryJournal();
            var service = new RestoreService(Win32WindowApi.Instance);

            Console.WriteLine(service.RestoreAll(journal).ToString());
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

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e) =>
        RestoreOnShutdown();

    private void RestoreOnShutdown()
    {
        if (journal is null || restoreService is null)
        {
            return;
        }

        restoreService.RestoreAll(journal);
    }
}
