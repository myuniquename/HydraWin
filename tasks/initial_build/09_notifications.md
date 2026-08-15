# Task 09 — NotificationHub: badges for windows that want attention

Status: **not started**
Depends on: task 03 (title-change events), task 06 (task↔window binding, SwitchTo), task 07
(badge UI slot). **Read task 01's Record on completion first** — it settles whether flashes from
hidden windows are observable and provides the literal Claude Code / Teams title strings the
default rules below must be tuned to.

> **Two measured facts from task 01 that change this task's defaults.** (1) **Teams is
> flash-only**: it flashes identically whether visible, minimized or `SW_HIDE`-hidden, and it
> *never* changes its window title — so the title rule below is deleted, not tuned. (2) Teams
> flashes **once per unread run**: the first message into a read conversation flashes, then
> nothing until the user opens the chat. Consequence for section C's clearing matrix: a Teams
> badge must be cleared **only** by the window gaining focus. Clearing flash-kind entries on
> `SwitchTo`, as currently specified, would drop the badge with no further flash ever coming to
> re-raise it.

## Motivation

The point of hiding background tasks is focus — but the user must still learn when a hidden
Teams chat gets a message or a hidden Claude Code session finishes or waits for input. The
manager surfaces that as a per-task badge with click-to-jump, so backgrounding a task costs no
awareness.

## Background

Architecture recap: `WindowTracker` (task 03) already raises `WindowTitleChanged(old, new)` for
all tracked windows — **including HydraWin-hidden ones** (WinEvents don't require visibility;
task 01 confirmed this — name-change events were captured for windows with
`IsWindowVisible == false`). `WorkspaceService` maps windows to tasks.
`TaskViewModel` (task 07) reserved `NotificationCount`.

Two signal sources:
- **Shell flash hook** — `RegisterShellHookWindow(hwnd)` + `msgId =
  RegisterWindowMessage("SHELLHOOK")`; in the window's `HwndSource` hook, messages with
  `wParam == HSHELL_FLASH (0x8006)` (and log-only: `HSHELL_RUDEAPPACTIVATED (0x8004)`) carry the
  flashing HWND in `lParam`. **Task 01 measured that this works for `SW_HIDE`-hidden windows
  too** — the message arrives with `IsWindowVisible == false`, so treat it as a full second
  channel, not a visible-only fallback. Do *not* bother with `HSHELL_RUDEAPPACTIVATED`: it never
  fired once in 1311 captured shell-hook messages.
  The limitation is on the *sending* side: a Windows Terminal bell produced no flash at all in
  task 01, under either `bellStyle` form and whether the BEL came from a Win32 console app or a
  VT-native one. So the flash hook is expected to carry Teams-style "wants attention", not
  terminal bells.
- **Title watcher** — rule-driven interpretation of `WindowTitleChanged`. **This is the primary
  channel for Claude Code**, since the bell does not flash.

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
- Claude Code done/waiting: process `WindowsTerminal.exe` (and `wt.exe`), regex `^✳\s`
  (`✳`, U+2733). Label "Claude done". Measured in task 01: an interactive Claude Code session
  titles its terminal `<marker> <session name>`, cycling spinner frames `U+25D0`–`U+25D3`
  (`◐ ◑ ◒ ◓`) about **once per second** while busy, and settling on `✳` when it finishes or waits
  for input — after which the title stops changing entirely. Edge-triggering therefore does the
  right thing. Two measured caveats: the hub sees ~1 title event per second per busy terminal, so
  per-event work must stay cheap; and `✳` also appears briefly at the start of an activity, so if
  false positives annoy, debounce — badge only if the `✳` title survives ~2 s unchanged.
- Teams unread: **there is no title rule — task 01 disproved it.** Teams never changes its window
  title, on any event, in any window state (zero name-change events across three test runs with
  real incoming messages from a second account). Do not ship `^\((\d+)\)` for `ms-teams.exe`; the
  unread count never reaches the window title. Teams is handled entirely by the flash channel,
  which works for hidden windows — nothing extra is needed to badge a hidden Teams.
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
2. Flash path (the terminal bell does **not** work for this — task 01 could not make Windows
   Terminal flash on a bell in any configuration). Use an explicit `FlashWindowEx` against a
   window assigned to a task, once while that window is visible-but-background and once while
   HydraWin has it hidden: both must badge, since task 01 showed `HSHELL_FLASH` is delivered in
   both states. No badge if the window is foreground. Record observed.
3. Same terminal in a **hidden** task: run a short Claude Code prompt to completion → its task
   badges via the title rule within ~2 s; tooltip shows the label; clicking the badge switches
   and focuses the terminal; badge clears.
4. Teams hidden in a background task, message sent from a **second account** (a self-chat produces
   nothing — task 01 established that) and with the conversation **read beforehand** (Teams flashes
   only once per unread run, so a stale unread state silently invalidates this test) → badge with
   Teams label via the flash; switch to the task *without* focusing Teams → badge remains; focus
   Teams → clears.
5. Notepad unassigned, flash it (e.g. via a script calling `FlashWindowEx`) → no badge anywhere.
6. Soak: leave HydraWin running 30 min of normal work — no badge storms from title-noise (VS Code
   `●` toggles, browser tab switches); if storms occur, tighten the offending default rule and
   record the change.

## Record on completion

*(what was done, deviations and why, the final tuned regexes — copy them here verbatim, they are
prime `docs/` promotion material — observed latency, test totals, and the list of
new / modified / deleted files)*
