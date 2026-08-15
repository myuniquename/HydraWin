# Task 09 — NotificationHub: badges for windows that want attention

Status: **not started**
Depends on: task 03 (title-change events), task 06 (task↔window binding, SwitchTo), task 07
(badge UI slot). **Read task 01's Record on completion first** — it settles whether flashes from
hidden windows are observable and provides the literal Claude Code / Teams title strings the
default rules below must be tuned to.

## Motivation

The point of hiding background tasks is focus — but the user must still learn when a hidden
Teams chat gets a message or a hidden Claude Code session finishes or waits for input. The
manager surfaces that as a per-task badge with click-to-jump, so backgrounding a task costs no
awareness.

## Background

Architecture recap: `WindowTracker` (task 03) already raises `WindowTitleChanged(old, new)` for
all tracked windows — **including HydraWin-hidden ones** (WinEvents don't require visibility; task
01 spike C confirmed/denied — follow its record). `WorkspaceService` maps windows to tasks.
`TaskViewModel` (task 07) reserved `NotificationCount`.

Two signal sources:
- **Shell flash hook** — `RegisterShellHookWindow(hwnd)` + `msgId =
  RegisterWindowMessage("SHELLHOOK")`; in the window's `HwndSource` hook, messages with
  `wParam == HSHELL_FLASH (0x8006)` (and log-only: `HSHELL_RUDEAPPACTIVATED (0x8004)`) carry the
  flashing HWND in `lParam`. Catches Teams flashes and terminal bells for windows that have a
  taskbar button. Per the spike, hidden windows likely do **not** produce this — the title
  watcher is the guaranteed channel for them.
- **Title watcher** — rule-driven interpretation of `WindowTitleChanged`.

## Work

### A. Interop additions
`RegisterShellHookWindow`, `DeregisterShellHookWindow`, `RegisterWindowMessageW`.

### B. Rules model (`Notifications/` + `SettingsModel`)
`NotificationRule { string ProcessFileName; string TitleRegex; NotificationKind Kind; string
Label; }` — a rule fires when a window of that process gets a title change whose *new* title
matches `TitleRegex` (and, for done-style rules, the old title did not — edge-triggered, not
level-triggered, so a persistent "(2) Teams" title badges once, not on every repaint).
Compiled with `RegexOptions.IgnoreCase`, 100 ms timeout. Ship defaults in `SettingsModel`
(seeded on first run, user-editable in `state.json`; UI editor is task 10):
- Claude Code done/waiting: process `WindowsTerminal.exe` (and `wt.exe`), regex placeholder
  `TUNE-FROM-SPIKE` — replace with patterns derived from task 01's captured titles (expected
  shapes: an idle/bell marker, disappearance of a busy marker such as `✳`/spinner, or an
  "esc to interrupt" fragment vanishing). Label "Claude done".
- Teams unread: process `ms-teams.exe`, regex `^\((\d+)\)` on the title. Label "Teams".
- Browser unread (off by default): `^\(\d+\)` for `msedge.exe`/`chrome.exe`.

### C. `NotificationHub` (Core)
- Inputs: flash events (hwnd) from the App-layer hook (raised through an interface so Core
  stays UI-free), `WindowTitleChanged`, task binding lookups, `ActiveTaskId`, and
  foreground-window changes.
- State: per-window pending notifications `{ Kind, Label, Timestamp }` (latest per rule wins;
  a flash is its own kind "Attention").
- Suppression: signals from windows of the *active* task are dropped if that window is
  foreground; if not foreground (e.g. behind the editor), still badge — the user can't see it
  either way. Signals from unassigned windows are ignored (nothing to badge).
- Clearing: a window's pending set clears when it becomes foreground; a task's badge is the
  count of windows with pending notifications, cleared naturally as windows are focused, and
  wholesale on `SwitchTo` of that task **only for flash-kind** entries (title-kind entries such
  as Teams unread stay until their window is actually focused — switching to a task with three
  chats shouldn't silently clear the two you didn't open).
- Events for the UI: `TaskBadgeChanged(taskId, count, topLabel)`.

### D. UI (App)
- Task row badge: count chip + tooltip listing pending items ("Claude done — Terminal: claude,
  2 min ago"). Per-window rows show a dot + label.
- Click a badge (not the row header): `SwitchTo(task)` then `SetForegroundWindow` on the
  most-recent pending window (user-initiated → allowed).
- Tray: tooltip shows total pending count; optional balloon/toast per new notification
  (setting, default off — the badge is the product; toasts are noise until proven wanted).

### E. Unit tests
Rule edge-triggering (repeat title events badge once; regression to non-matching then back
re-badges); clearing matrix (focus clears window; switch clears flash-kind only); active-task
suppression; mapping flash hwnd → task; regex timeout safety.

## Verification

1. `dotnet test` — totals pasted.
2. Terminal bell (`bellStyle: taskbarFlash`, `printf '\a'`) in a *visible but background* window
   assigned to the active task → no badge if foreground, badge if backgrounded behind another
   window — record observed.
3. Same terminal in a **hidden** task: run a short Claude Code prompt to completion → its task
   badges via the title rule within ~2 s; tooltip shows the label; clicking the badge switches
   and focuses the terminal; badge clears.
4. Teams hidden, send yourself a message → badge with Teams label; switch to the task *without*
   focusing Teams → unread badge remains; focus Teams → clears.
5. Notepad unassigned, flash it (e.g. via a script calling `FlashWindowEx`) → no badge anywhere.
6. Soak: leave HydraWin running 30 min of normal work — no badge storms from title-noise (VS Code
   `●` toggles, browser tab switches); if storms occur, tighten the offending default rule and
   record the change.

## Record on completion

*(what was done, deviations and why, the final tuned regexes — copy them here verbatim, they are
prime `docs/` promotion material — observed latency, test totals, and the list of
new / modified / deleted files)*
