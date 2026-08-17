using System.Windows;
using System.Windows.Threading;
using HydraWin.App.Services;
using HydraWin.App.Themes;
using HydraWin.Core.Diagnostics;
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
    private ShellHookListener? shellHook;
    private ThemeManager? theme;
    private SystemThemeListener? themeListener;
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

        InstallCrashHandlers();

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

        theme = new ThemeManager(Win32AppearanceApi.Instance);

        window = new MainWindow(journal, restoreService, theme);
        MainWindow = window;

        // Between the constructor and Show() is the only place this can go. The constructor is what
        // reads state.json, so the stored preference is not known before it; and WPF creates no
        // window handle and paints nothing until Show(), so applying the palette here means the
        // first frame is already in the right theme. Reading state.json twice to learn it earlier
        // would be two readers of one file, which is how they drift.
        theme.Apply(window.ViewModel.Appearance);

        if (recovered.Restored > 0 || recovered.Stale > 0)
        {
            window.ShowRecoveryNotice(recovered);
        }

        window.Show();

        StartThemeListener();

        singleInstance.ListenForShowRequests(
            () => Dispatcher.BeginInvoke(() => window?.ShowFromTray()));

        tray = new TrayIcon(window.ViewModel, ShowWindow, ExitApplication, ShowSettings);
        StartHotkeys();
        StartShellHook();

        window.ViewModel.HotkeysChanged += (_, _) => RestartHotkeys();
        window.ViewModel.AppearanceChanged += (_, _) => theme.Apply(window.ViewModel.Appearance);

        AppLog.Default.Write($"HydraWin started (recovered {recovered.Restored} window(s)).");
    }

    /// <summary>
    /// Logs an unhandled exception, brings every hidden window back, and then lets the process
    /// die.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not a "keep running" handler. An app in an unknown state is exactly what the
    /// crash-recovery journal exists for, and pretending otherwise risks a second, worse failure
    /// with the user's windows still hidden. What this buys is the two things a bare crash would
    /// not do: a line in the log saying what happened, and the windows back on screen before the
    /// process goes.
    /// </para>
    /// <para>
    /// The restore is safe from either thread — it is pure Win32 over a mutex-guarded file — which
    /// matters because <c>AppDomain.UnhandledException</c> can arrive on any of them.
    /// </para>
    /// </remarks>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            RecordCrash("unhandled exception on the dispatcher", args.Exception);

            // args.Handled stays false on purpose: let it crash.
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            RecordCrash(
                "unhandled exception on a background thread",
                args.ExceptionObject as Exception);
    }

    private void RecordCrash(string context, Exception? exception)
    {
        if (exception is null)
        {
            AppLog.Default.Write($"CRASH: {context} (no exception object).");
        }
        else
        {
            AppLog.Default.WriteException($"CRASH: {context}", exception);
        }

        try
        {
            if (journal is not null && restoreService is not null)
            {
                RestoreSummary summary = restoreService.RestoreAll(journal);
                AppLog.Default.Write($"CRASH: restore attempted — {summary}.");
            }
        }
        catch (Exception restoreFailure)
        {
            // Nothing left to do but say so. Swallowing here does not hide the original crash:
            // the process is still going down, and --restore-all remains the way back.
            AppLog.Default.WriteException("CRASH: the restore itself failed", restoreFailure);
        }
    }

    private void ShowSettings() => window?.ShowSettings();

    /// <summary>
    /// Re-registers the hotkeys after the settings dialog changed them.
    /// </summary>
    /// <remarks>
    /// A hotkey belongs to the thread that claimed it, and <see cref="HotkeyService"/> owns that
    /// thread, so rebinding means stopping the service and starting a new one. Its loop exits
    /// cleanly on <c>StopLoop</c> and releases what it registered on the way out.
    /// </remarks>
    private void RestartHotkeys()
    {
        hotkeys?.Dispose();
        hotkeys = null;
        StartHotkeys();
    }

    /// <summary>
    /// Starts following the Windows theme, so a change out there reaches HydraWin without a restart.
    /// </summary>
    /// <remarks>
    /// Hooked to the main window for the same reason the shell hook is: it is created once and only
    /// ever hidden, so the subscription survives closing to tray.
    /// </remarks>
    private void StartThemeListener()
    {
        if (window is null || theme is null)
        {
            return;
        }

        themeListener = new SystemThemeListener(
            window,
            Win32AppearanceApi.Instance,
            theme.Reevaluate);

        if (!themeListener.IsListening)
        {
            window.ViewModel.Note("Could not watch the Windows theme — it will not follow changes.");
        }
    }

    /// <summary>
    /// Subscribes to the shell's flash notifications — the channel that works for any application
    /// without a rule or a per-app regex.
    /// </summary>
    private void StartShellHook()
    {
        if (window is null)
        {
            return;
        }

        shellHook = new ShellHookListener(
            window,
            Win32ShellHookApi.Instance,
            hwnd => window.ViewModel.OnWindowFlashed(hwnd));

        if (!shellHook.IsListening)
        {
            window.ViewModel.Note("The shell refused the notification hook — badges are off.");
            return;
        }

        window.ViewModel.NotificationRaised += OnNotificationRaised;
    }

    private void OnNotificationRaised(object? sender, string label)
    {
        if (window is null || tray is null)
        {
            return;
        }

        tray.UpdatePendingCount(window.ViewModel.TotalPendingNotifications);

        if (window.ViewModel.NotificationToasts && label.Length > 0)
        {
            tray.ShowBalloon("HydraWin", label);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        themeListener?.Dispose();
        shellHook?.Dispose();
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
