using System.Windows;
using HydraWin.App.Services;
using HydraWin.Core.Interop;
using HydraWin.Core.Recovery;
using HydraWin.Core.Workspaces;

namespace HydraWin.App;

/// <summary>Application entry point, command-line dispatch, and crash recovery.</summary>
public partial class App : Application
{
    private const string RestoreAllFlag = "--restore-all";

    private RecoveryJournal? journal;
    private RestoreService? restoreService;
    private SingleInstance? singleInstance;
    private TrayIcon? tray;
    private HotkeyService? hotkeys;
    private MainWindow? window;
    private bool forceRestoreOnExit;

    protected override void OnStartup(StartupEventArgs e)
    {
        // --restore-all is handled before anything else and never touches the UI *or the mutex*.
        // The escape hatch has to work while a wedged first instance still holds it.
        if (e.Args.Any(a => string.Equals(a, RestoreAllFlag, StringComparison.OrdinalIgnoreCase)))
        {
            RunRestoreAll();
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            // Someone is already running: ask them to surface, and get out of the way.
            singleInstance.AskFirstInstanceToShow();
            singleInstance.Dispose();
            singleInstance = null;
            Shutdown(0);
            return;
        }

        journal = new RecoveryJournal();
        restoreService = new RestoreService(Win32WindowApi.Instance);

        // Startup recovery, before any other window manipulation. A non-empty journal here means
        // the previous run did not exit cleanly, and visible windows are always the safe state to
        // start from. This needs no command-line flag: an ordinary launch repairs itself.
        RestoreSummary recovered = restoreService.RestoreAll(journal);

        SessionEnding += OnSessionEnding;

        window = new MainWindow(journal, restoreService);
        MainWindow = window;

        if (recovered.Restored > 0 || recovered.Stale > 0)
        {
            window.ShowRecoveryNotice(recovered);
        }

        window.Show();

        singleInstance.ListenForShowRequests(
            () => Dispatcher.BeginInvoke(() => window?.ShowFromTray()));

        tray = new TrayIcon(window.ViewModel, ShowWindow, ExitApplication);
        StartHotkeys();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        hotkeys?.Dispose();
        tray?.Dispose();
        singleInstance?.Dispose();

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

    private void StartHotkeys()
    {
        if (window is null)
        {
            return;
        }

        hotkeys = new HotkeyService(
            Win32HotkeyApi.Instance,
            Dispatcher,
            window.ViewModel.Hotkeys)
        {
            Fired = OnHotkey,

            // Runs on the hotkey thread, not here: that is what makes it survive a wedged UI.
            PanicRestore = PanicRestore,

            RegistrationFailed = failures => window.ViewModel.Note(
                $"{failures.Count} hotkey(s) unavailable — {string.Join("; ", failures)}"),
        };

        hotkeys.Start();
    }

    private void OnHotkey(HotkeyBinding binding)
    {
        if (window is null)
        {
            return;
        }

        switch (binding.Action)
        {
            case HotkeyAction.SwitchToTask:
                window.ViewModel.SwitchToOrder(binding.TaskOrder);
                break;

            case HotkeyAction.ShowAll:
                window.ViewModel.ShowAllCommand.Execute(null);
                break;

            case HotkeyAction.ToggleWindow:
                ToggleWindow();
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Brings everything back, straight from the journal.
    /// </summary>
    /// <remarks>
    /// Called on the hotkey thread on purpose, and deliberately does not touch the view models: the
    /// point of this key is that it works when the UI thread does not. The journal is mutex-guarded
    /// and the restore is pure Win32, so both are safe from here. The UI catches up on its next
    /// reconciliation sweep.
    /// </remarks>
    private void PanicRestore()
    {
        if (journal is not null && restoreService is not null)
        {
            restoreService.RestoreAll(journal);
        }
    }

    private void ShowWindow() => window?.ShowFromTray();

    private void ToggleWindow()
    {
        if (window is null)
        {
            return;
        }

        if (window.IsVisible && window.IsActive)
        {
            window.Hide();
            return;
        }

        window.ShowFromTray();
    }

    /// <summary>Ends the app. <paramref name="forceRestore"/> overrides the restore-on-exit setting.</summary>
    private void ExitApplication(bool forceRestore)
    {
        forceRestoreOnExit = forceRestore;

        if (window is not null)
        {
            window.ExitRequested = true;
            window.Close();
        }

        Shutdown(0);
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e) =>
        RestoreOnShutdown();

    /// <summary>
    /// The clean-exit restore. Honours the setting, except when the user chose
    /// <i>Restore all &amp; exit</i>, and defaults to restoring if the setting cannot be read —
    /// leaving windows hidden after HydraWin is gone is the one failure this project cannot afford.
    /// </summary>
    private void RestoreOnShutdown()
    {
        if (journal is null || restoreService is null)
        {
            return;
        }

        bool restore = forceRestoreOnExit || window?.ViewModel.RestoreOnExit != false;
        if (restore)
        {
            restoreService.RestoreAll(journal);
        }
    }
}
