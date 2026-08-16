using System.Globalization;

namespace HydraWin.Core.Workspaces;

/// <summary>What a hotkey does when it fires.</summary>
public enum HotkeyAction
{
    /// <summary>Unrecognised — a hand-edit typo. Ignored rather than fatal.</summary>
    None = 0,

    /// <summary>Switch to the task whose <see cref="HydraWinTask.Order"/> is <c>TaskOrder</c>.</summary>
    SwitchToTask,

    /// <summary>Bring every hidden window back and leave no task active.</summary>
    ShowAll,

    /// <summary>Restore everything the journal lists, whatever else is going on.</summary>
    PanicRestore,

    /// <summary>Show the manager window, or hide it if it is already in front.</summary>
    ToggleWindow,
}

/// <summary>
/// One global hotkey, as it appears in <c>state.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately flat strings so the file stays hand-editable: <c>"Control+Alt"</c> and
/// <c>"1"</c> rather than numbers nobody can read back. Resolution to the modifier flags and
/// virtual-key code Win32 wants happens in <see cref="TryResolve"/>, which is pure and has its own
/// small key table — WPF's <c>KeyInterop</c> would drag a UI framework into Core and would force
/// the file to spell the digit keys <c>"D1"</c>.
/// </para>
/// <para>
/// An entry that cannot be resolved is skipped with a message, never thrown: a typo in a
/// hand-edited settings file must not stop the app from starting.
/// </para>
/// </remarks>
public sealed class HotkeyBinding
{
    /// <summary>Win32 <c>MOD_ALT</c>.</summary>
    public const uint ModAlt = 0x0001;

    /// <summary>Win32 <c>MOD_CONTROL</c>.</summary>
    public const uint ModControl = 0x0002;

    /// <summary>Win32 <c>MOD_SHIFT</c>.</summary>
    public const uint ModShift = 0x0004;

    /// <summary>Win32 <c>MOD_WIN</c>.</summary>
    public const uint ModWin = 0x0008;

    /// <summary>
    /// Win32 <c>MOD_NOREPEAT</c>: holding the combination fires once, not once per repeat.
    /// Always added — every action here is a command, and none of them wants to auto-repeat.
    /// </summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>What the hotkey does.</summary>
    public HotkeyAction Action { get; set; }

    /// <summary>Which task, for <see cref="HotkeyAction.SwitchToTask"/>. Ignored otherwise.</summary>
    public int TaskOrder { get; set; }

    /// <summary>Modifiers, <c>+</c>-separated: <c>Control</c>, <c>Alt</c>, <c>Shift</c>, <c>Win</c>.</summary>
    public string Modifiers { get; set; } = string.Empty;

    /// <summary>The key: a digit, a letter, or <c>F1</c>–<c>F24</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The bindings HydraWin starts with when <c>state.json</c> has none.
    /// </summary>
    public static List<HotkeyBinding> Defaults()
    {
        List<HotkeyBinding> bindings = [];

        for (int order = 1; order <= 9; order++)
        {
            bindings.Add(new HotkeyBinding
            {
                Action = HotkeyAction.SwitchToTask,
                TaskOrder = order,
                Modifiers = "Control+Alt",
                Key = order.ToString(CultureInfo.InvariantCulture),
            });
        }

        bindings.Add(new HotkeyBinding
        {
            Action = HotkeyAction.ShowAll,
            Modifiers = "Control+Alt",
            Key = "0",
        });

        bindings.Add(new HotkeyBinding
        {
            Action = HotkeyAction.PanicRestore,
            Modifiers = "Control+Alt+Shift",
            Key = "R",
        });

        bindings.Add(new HotkeyBinding
        {
            Action = HotkeyAction.ToggleWindow,
            Modifiers = "Control+Alt",
            Key = "H",
        });

        return bindings;
    }

    /// <summary>
    /// The combination as the user reads and types it — <c>Control+Alt+1</c>, or just the key when
    /// there are no modifiers. Round-trips: what this prints is what the editor stores back.
    /// </summary>
    public string ToDisplayString() =>
        string.IsNullOrEmpty(Modifiers) ? Key : $"{Modifiers}+{Key}";

    /// <summary>What the binding does, spelled out for the settings list and error messages.</summary>
    public string DescribeAction() => Action switch
    {
        HotkeyAction.SwitchToTask => $"Switch to task {TaskOrder}",
        HotkeyAction.ShowAll => "Show all windows",
        HotkeyAction.PanicRestore => "Restore everything (panic)",
        HotkeyAction.ToggleWindow => "Show or hide HydraWin",
        _ => "Unrecognised",
    };

    /// <summary>
    /// Splits a written combination back into <see cref="Modifiers"/> and <see cref="Key"/>.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="ToDisplayString"/>, and the only place the capture box in the
    /// settings dialog needs to know about the storage format. It does not validate — that is
    /// <see cref="TryResolve"/>'s job, which the caller runs next.
    /// </remarks>
    public static (string Modifiers, string Key) Split(string combination)
    {
        ArgumentNullException.ThrowIfNull(combination);

        int last = combination.LastIndexOf('+');
        return last < 0
            ? (string.Empty, combination.Trim())
            : (combination[..last].Trim(), combination[(last + 1)..].Trim());
    }

    /// <summary>
    /// Turns the written form into what <c>RegisterHotKey</c> needs.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the action, the modifiers or the key cannot be understood —
    /// the caller skips that binding and carries on.
    /// </returns>
    public bool TryResolve(out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (Action == HotkeyAction.None || !TryResolveKey(Key, out virtualKey))
        {
            return false;
        }

        foreach (string part in Modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries))
        {
            uint flag = part.ToUpperInvariant() switch
            {
                "CONTROL" or "CTRL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" or "WINDOWS" => ModWin,
                _ => 0,
            };

            if (flag == 0)
            {
                return false;
            }

            modifiers |= flag;
        }

        // A bare key with no modifiers would swallow that key system-wide.
        if (modifiers == 0)
        {
            return false;
        }

        modifiers |= ModNoRepeat;
        return true;
    }

    /// <summary>Maps a written key name to its virtual-key code.</summary>
    private static bool TryResolveKey(string key, out uint virtualKey)
    {
        virtualKey = 0;
        string name = key.Trim().ToUpperInvariant();

        if (name.Length == 1)
        {
            char c = name[0];

            // VK codes for '0'-'9' and 'A'-'Z' are the ASCII values themselves.
            if (c is >= '0' and <= '9' or >= 'A' and <= 'Z')
            {
                virtualKey = c;
                return true;
            }

            return false;
        }

        if (name.Length >= 2
            && name[0] == 'F'
            && int.TryParse(name[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int f)
            && f is >= 1 and <= 24)
        {
            // VK_F1 is 0x70 and the rest follow in order.
            virtualKey = (uint)(0x70 + f - 1);
            return true;
        }

        return false;
    }
}
