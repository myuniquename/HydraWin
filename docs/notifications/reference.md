# Notifications — reference

## `NotificationRule`

Persisted in `state.json` under `Settings.NotificationRules`, and hand-editable.

```json
{
  "ProcessFileName": "chrome.exe",
  "TitleRegex": "^\\(\\d+\\)",
  "Kind": "Title",
  "Label": "Unread",
  "Enabled": false
}
```

| Field | Meaning |
| --- | --- |
| `ProcessFileName` | Image file name, compared case-insensitively. Empty or `*` matches any process, which is how a rule is made application-agnostic |
| `TitleRegex` | The pattern the new title must match. **An empty pattern never fires** |
| `Kind` | `Title` or `Attention`; see below |
| `Label` | What the badge tooltip says. Empty falls back to the window's own description, which is what every rule-less notification uses |
| `Enabled` | Whether the rule is live. Everything shipped is off |

Matching is edge-triggered: `Matches(process, oldTitle, newTitle)` is true when the pattern matches
the new title and not the old one. `MatchesTitle(process, title)` is the non-edge half, used by the
editor's live preview so the two cannot disagree.

Patterns are compiled with `RegexOptions.IgnoreCase` and a **100 ms timeout**. A malformed pattern
or a timeout counts as no match; neither throws.

### The shipped default

Exactly one rule is seeded into a fresh `state.json`, and it is **disabled**:

```json
{ "ProcessFileName": "chrome.exe", "TitleRegex": "^\\(\\d+\\)", "Kind": "Title",
  "Label": "Unread", "Enabled": false }
```

It exists as a worked example to copy. Clearing `NotificationRules` to `[]` re-seeds it on the next
launch.

## `NotificationKind`

| Value | Raised by |
| --- | --- |
| `Attention` | The shell flash channel — the default for every application |
| `Title` | A title rule that fired |

The kind is carried through to the badge tooltip; it does **not** affect clearing, which is the
same for both (focus clears, a task switch does not).

## `PendingNotification`

One entry per waiting window: `Hwnd`, `Kind`, `Label`, `RaisedAt`. Repeat signals for the same
window overwrite the entry rather than adding one, so a task's count is *windows waiting*, not
signals received.

## `NotificationHub`

| Member | Purpose |
| --- | --- |
| `OnFlash(TrackedWindow?)` | A window's taskbar button flashed |
| `OnTitleChanged(window, oldTitle, newTitle)` | A title changed; rules evaluate here |
| `OnForegroundChanged(nint hwnd)` | Something took focus; `0` means nothing foreign is in front |
| `OnWindowDisappeared(nint hwnd)` | A window closed |
| `PendingFor(Guid taskId)` | The task's waiting windows, newest first |
| `CountFor(Guid taskId)`, `TotalPending` | Badge counts |
| `IsPending(nint)`, `LabelFor(nint)` | Per-window state, for the row dot |
| `TaskBadgeChanged` | Raised when a task's badge changes |

The hub takes the foreground handle as an **input** rather than querying Win32 for it. That is what
makes suppression and the whole clearing matrix testable with no Win32 and no WPF involved.

## Win32 surface

| Constant / call | Value | Used for |
| --- | --- | --- |
| `RegisterWindowMessage("SHELLHOOK")` | runtime | The message id shell notifications arrive as |
| `RegisterShellHookWindow` / `DeregisterShellHookWindow` | — | Subscribing a real top-level window. A message-only (`HWND_MESSAGE`) window does **not** receive shell hook messages |
| `HSHELL_FLASH` | `0x8006` | `wParam` value that means "this window wants attention" |
| `HSHELL_WINDOWCREATED` / `HSHELL_WINDOWDESTROYED` | `0x0001` / `0x0002` | Also raised by `SW_SHOW` / `SW_HIDE` — not only by real creation and closing |
| `HSHELL_RUDEAPPACTIVATED` | `0x8004` | Not used; never observed |
| `GetAncestor(hwnd, GA_ROOT)` | — | Resolving a flashing owned dialog to the application window |
| `EVENT_OBJECT_NAMECHANGE` | — | The title channel, via `SetWinEventHook` |

The listener hooks the main window through `HwndSource.AddHook` and never marks the message
handled. The window is created at startup and only ever hidden, never closed, so the subscription
survives close-to-tray.

## Measured figures

Numbers other parts of the system are sized against. See
[architecture.md](architecture.md#per-application-behaviour-as-measured) for the evidence.

| Figure | Value |
| --- | --- |
| Claude Code idle → flash | **61.1 s**, consistent to 0.1 s across five sessions |
| Claude Code idle → title marker | immediate |
| Title events from one busy Claude Code terminal | ~1 per second |
| Teams flashes per incoming message | 3, at ~1.06 s spacing |
| Teams flashes per unread run | 1 run, then silence until read |
| Teams title changes, ever | 0 |
| `HSHELL_RUDEAPPACTIVATED` in 1,311 shell messages | 0 |
| `HSHELL_FLASH` messages for non-visible windows | 454 of 472 |

## Required application settings

Neither is present in a default install.

| Setting | Where | Value |
| --- | --- | --- |
| `bellStyle` | Windows Terminal `settings.json`, under `profiles.defaults` | must include `"taskbar"`. Valid: `"all"`, `"audible"`, `"window"`, `"taskbar"`, `"none"` — **not** `"taskbarFlash"` |
| `preferredNotifChannel` | `~/.claude/settings.json` | `terminal_bell` |
