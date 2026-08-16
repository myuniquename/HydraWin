using System.Windows.Threading;
using HydraWin.Core.Interop;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.Services;

/// <summary>
/// Owns HydraWin's global hotkeys on a thread of their own.
/// </summary>
/// <remarks>
/// <para>
/// The thread exists for one reason: the panic restore has to work when the UI thread does not.
/// <c>WM_HOTKEY</c> goes to the message queue of whichever thread registered the hotkey, so
/// registering from the UI thread would mean a wedged UI takes the panic restore down with it. Here
/// the restore runs inline on this thread — it is pure Win32 plus a mutex-guarded file, safe
/// anywhere — while everything else marshals to the dispatcher, because the switch engine and the
/// view models are not thread-safe.
/// </para>
/// <para>
/// A hotkey another application already owns simply fails to register. That is normal, not an
/// error: the binding is skipped, the rest carry on, and the user is told once.
/// </para>
/// </remarks>
internal sealed class HotkeyService : IDisposable
{
    private readonly IHotkeyApi api;
    private readonly Dispatcher dispatcher;
    private readonly IReadOnlyList<HotkeyBinding> bindings;
    private readonly Dictionary<int, HotkeyBinding> byId = [];
    private readonly List<int> registered = [];

    private Thread? thread;
    private uint threadId;
    private bool disposed;

    internal HotkeyService(
        IHotkeyApi api,
        Dispatcher dispatcher,
        IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(bindings);

        this.api = api;
        this.dispatcher = dispatcher;
        this.bindings = bindings;
    }

    /// <summary>Runs on the dispatcher when a non-panic hotkey fires.</summary>
    internal Action<HotkeyBinding>? Fired { get; set; }

    /// <summary>
    /// Runs <em>on the hotkey thread</em>, deliberately: this is the path that must survive a
    /// wedged UI.
    /// </summary>
    internal Action? PanicRestore { get; set; }

    /// <summary>Reports the bindings that could not be claimed, once, on the dispatcher.</summary>
    internal Action<IReadOnlyList<string>>? RegistrationFailed { get; set; }

    /// <summary>Starts the hotkey thread.</summary>
    internal void Start()
    {
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "HydraWin hotkeys",
        };

        thread.Start();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (threadId != 0)
        {
            api.StopLoop(threadId);
        }

        thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        threadId = api.CurrentThreadId();

        List<string> failures = RegisterAll();
        if (failures.Count > 0)
        {
            dispatcher.BeginInvoke(() => RegistrationFailed?.Invoke(failures));
        }

        while (api.WaitForHotkey(out int id))
        {
            if (!byId.TryGetValue(id, out HotkeyBinding? binding))
            {
                continue;
            }

            if (binding.Action == HotkeyAction.PanicRestore)
            {
                // Inline, on this thread. The whole point of the service.
                PanicRestore?.Invoke();
                continue;
            }

            HotkeyBinding fired = binding;
            dispatcher.BeginInvoke(() => Fired?.Invoke(fired));
        }

        // Hotkeys belong to the thread that claimed them, so they are released here.
        api.UnregisterAll(registered);
    }

    private List<string> RegisterAll()
    {
        List<string> failures = [];
        int id = 1;

        foreach (HotkeyBinding binding in bindings)
        {
            // The id advances whether or not this one takes, so a failure can never leave a later
            // binding sharing an id with an earlier one.
            int thisId = id++;

            if (!binding.TryResolve(out uint modifiers, out uint virtualKey))
            {
                failures.Add($"{Describe(binding)} is not a combination HydraWin understands");
                continue;
            }

            if (api.TryRegister(thisId, modifiers, virtualKey))
            {
                byId[thisId] = binding;
                registered.Add(thisId);
                continue;
            }

            failures.Add($"{Describe(binding)} is already taken by another application");
        }

        return failures;
    }

    private static string Describe(HotkeyBinding binding) =>
        $"{binding.ToDisplayString()} ({binding.DescribeAction()})";
}
