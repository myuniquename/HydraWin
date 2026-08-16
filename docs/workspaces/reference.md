# Workspaces — reference

## Files on disk

Everything lives under `%APPDATA%\HydraWin\`, spelled out in exactly one place —
`src/HydraWin.Core/Persistence/HydraWinPaths.cs`.

| File | Holds | Losing it costs |
| --- | --- | --- |
| `state.json` | Tasks, assignments, re-attach rules, settings | The task layout |
| `journal.json` | Every window HydraWin currently has hidden | Potentially the windows themselves |
| `logs\hydrawin.log` | The activity log, rolled once at 1 MB to `hydrawin.1.log` | Nothing — diagnostics only |
| `state.json.corrupt-<yyyyMMdd-HHmmss>` | A document that failed to parse, preserved byte-for-byte | Nothing — it is already a copy |

Both JSON documents are written atomically (temp file, then `File.Replace`), indented, and encoded
with `UnsafeRelaxedJsonEscaping` so they stay readable and hand-editable.

## `state.json`

```json
{
  "Tasks": [
    {
      "Id": "813ecb3a-4749-4475-af80-023d2cc7810d",
      "Name": "Alpha",
      "ColorHex": "#4C8DFF",
      "Order": 1,
      "Assignments": [
        {
          "Id": "a275491a-ab06-44ae-a8bf-900684da97c3",
          "Rule": {
            "ProcessFileName": "WindowsTerminal.exe",
            "TitlePattern": "prod",
            "TitleIsRegex": false
          }
        }
      ]
    }
  ],
  "ActiveTaskId": null,
  "Settings": { }
}
```

| Property | Meaning |
| --- | --- |
| `Tasks[].Id` | Stable identity across restarts |
| `Tasks[].Name` | Display name; unique only by convention |
| `Tasks[].ColorHex` | Row accent, `#RRGGBB` |
| `Tasks[].Order` | 1-based position. **Load-bearing**: `Ctrl+Alt+1..9` address tasks by it, so renumber from 1 when editing by hand |
| `Assignments[].Id` | Stable identity; also the payload of a drag |
| `Rule.ProcessFileName` | Image file name only, compared case-insensitively. The full path changes when an application updates |
| `Rule.TitlePattern` | Substring, or a regex when `TitleIsRegex` is set. Empty matches any title of that process |
| `Rule.TitleIsRegex` | Opt-in regex mode. Substring is the default because it is predictable |
| `ActiveTaskId` | The task currently switched to, or `null` for "everything visible" |

Runtime-only values are deliberately **not** persisted: an assignment's bound window handle, a
task's last-active window, and the `Unmanageable` flag. Handles mean nothing across restarts, and a
persisted `Unmanageable` would carry a stale verdict into a run where the offending process is no
longer elevated. The engine re-tries on each switch, so it heals itself.

Derived properties are never written. A get-only property in the file would duplicate its source
and then silently discard anything edited in the copy.

### `Settings`

```json
"Settings": {
  "RestoreOnExit": true,
  "AlwaysOnTop": true,
  "CloseToTray": true,
  "Hotkeys": [
    { "Action": "SwitchToTask", "TaskOrder": 1, "Modifiers": "Control+Alt", "Key": "1" }
  ],
  "NotificationRules": [ ],
  "NotificationToasts": false
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `RestoreOnExit` | `true` | Whether a clean exit restores every hidden window first. Off is the only way to deliberately leave windows hidden |
| `AlwaysOnTop` | `true` | Whether the manager window stays above others. On, because a switch ends by raising the task's windows |
| `CloseToTray` | `true` | Whether closing the window hides it instead of exiting |
| `Hotkeys` | seeded | See below |
| `NotificationRules` | one, disabled | See [../notifications/reference.md](../notifications/reference.md) |
| `NotificationToasts` | `false` | Whether a badge also raises a tray balloon |

An empty `Hotkeys` or `NotificationRules` list is re-seeded with the shipped defaults on the next
launch, which is the supported way to get them back after editing.

## Hotkeys

| Action | Default | What it does |
| --- | --- | --- |
| `SwitchToTask` | `Control+Alt+1` … `Control+Alt+9` | Switches to the task whose `Order` is `TaskOrder`, and lands focus in it |
| `ShowAll` | `Control+Alt+0` | Brings every hidden window back and leaves no task active |
| `PanicRestore` | `Control+Alt+Shift+R` | Restores straight from the journal, on the hotkey thread |
| `ToggleWindow` | `Control+Alt+H` | Shows the manager window, or hides it if it is already in front |

Written form is `Modifiers` + `Key`, both flat strings so the file stays readable: modifiers are
`Control`, `Alt`, `Shift`, `Win`, joined with `+`; the key is a single digit, a single letter, or
`F1`–`F24`. `MOD_NOREPEAT` is always added — every action here is a command, and none wants to
auto-repeat.

An entry that cannot be understood is **skipped with a message, never thrown**: a typo in a
hand-edited file must not stop the app from starting. A combination another application already
owns simply fails to register, which is normal rather than an error; the other bindings carry on
and the user is told once. A combination with no modifier is refused — it would swallow that key
system-wide.

## `journal.json`

A flat array. Empty (`[]`) means nothing is hidden.

```json
[
  {
    "Hwnd": 16913424,
    "Pid": 12184,
    "ProcessPath": "C:\\Program Files\\WindowsApps\\...\\ms-teams.exe",
    "TitleAtHide": "Chat | anton suchov | Microsoft Teams",
    "Placement": {
      "ShowCmd": 1, "Flags": 0,
      "MinX": -1, "MinY": -1, "MaxX": -1, "MaxY": -1,
      "NormalLeft": 300, "NormalTop": 250, "NormalRight": 1100, "NormalBottom": 850
    },
    "HiddenAt": "2026-08-16T14:04:07.1234567+03:00"
  }
]
```

| Property | Why it is there |
| --- | --- |
| `Hwnd` | The handle at the time of hiding. **Not** identity on its own — Windows recycles handles |
| `Pid`, `ProcessPath` | The other two thirds of the identity check. All three must still agree before anything is shown |
| `TitleAtHide` | For reporting only, never for matching |
| `Placement` | A serialisable mirror of `WINDOWPLACEMENT`. `ShowCmd` carries the maximized state; `NormalLeft/Top/Right/Bottom` is `rcNormalPosition`, in **workspace** coordinates |
| `HiddenAt` | When it was hidden |

Placement values are in the *calling process's* DPI coordinate space — see
[architecture.md](architecture.md#two-more-measured-facts).

## Command line

| Argument | Effect |
| --- | --- |
| *(none)* | Normal launch: startup recovery, then the UI |
| `--restore-all` | Reads the journal, restores everything it lists, prints a summary, exits. No UI, and **no single-instance mutex** — it must work while a wedged instance holds it |

Output is one line: `restored N window(s), dropped N stale entr(y|ies)`, with
`, N could not be restored` appended only when non-zero. Being a `WinExe`, it attaches to the
parent console to print.

A second ordinary launch does not start a second instance: it signals the first to show its window
and exits 0.

## `TrackableVerdict`

The reason a window is or is not in the inventory. `Trackable` is the only accepting value; the
others name the first clause that failed, in evaluation order.

| Value | Meaning |
| --- | --- |
| `Trackable` | In the inventory |
| `OwnProcess` | One of HydraWin's own windows |
| `Elevated` | Owned by an elevated process while HydraWin is not elevated |
| `NoTitle` | No title |
| `NotVisible` | Invisible, and not hidden by HydraWin |
| `ToolWindow` | Has `WS_EX_TOOLWINDOW` |
| `Owned` | Owned by another window |
| `Cloaked` | DWM-cloaked, and not hidden by HydraWin |

## Switch and restore summaries

`SwitchSummary` — what a switch did: `Hidden`, `Shown`, `Stale`, `Unmanageable`. Rendered as
`hidden N, shown N`, with `, N stale` and `, N could not be hidden` appended only when non-zero.

`RestoreSummary` — what a restore pass did: `Restored`, `Stale`, `Failed`. `Failed` entries stay in
the journal for a later attempt; the other two are removed.
