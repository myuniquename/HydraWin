# Task 09 — NotificationHub: badges for windows that want attention

Status: **done (2026-08-16) — accepted by the user; the bell→flash leg is unverified, see the
record**
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
  A Windows Terminal bell **does** raise `HSHELL_FLASH` — from a Win32 console app and a VT-native
  one alike, and even while the window is `SW_HIDE`-hidden — provided `bellStyle` is valid
  (`"taskbarFlash"`, used in task 01's original test, is not). Claude Code rings it too, but
  **~61 s after the session goes idle** (61.1 s across five measured sessions). **This is now the
  channel for Claude Code as well**: the user accepted the minute of latency in exchange for
  having no per-app title rules. It requires `bellStyle` to include `"taskbar"` and Claude Code's
  `preferredNotifChannel` to be `terminal_bell`; if either is unset the badge simply does not
  appear, which is a configuration matter, not a bug to work around here.
- **Title watcher** — rule-driven interpretation of `WindowTitleChanged`. Retained as the generic
  mechanism (task 10 lets the user add rules), but **no default rule ships for Claude Code**. The
  Claude Code title is consumed for *live display* instead — task 07 § F.

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
- Claude Code done/waiting: **no title rule — ship none.** The user's decision, taken with the
  61 s latency measured and accepted: a finished Claude Code session badges via the **flash**
  channel like any other app, about a minute after it goes idle. That keeps this task free of
  per-app regexes and keeps one mechanism for both Teams and terminals.
  The Claude Code title is still *parsed and displayed* — see task 07 § F — it just does not
  raise notifications here.
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
2. Flash path. Easiest reliable trigger is a **terminal bell**: with `bellStyle` including
   `"taskbar"`, `printf '\a'` (or `[Console]::Out.Write([char]7)`) in a Windows Terminal window
   assigned to a task raises `HSHELL_FLASH`, verified working both visible-but-background and
   `SW_HIDE`-hidden. An explicit `FlashWindowEx` against the window does the same. Test both
   states: each must badge, and a foreground window must not. Record observed.
3. Same terminal in a **hidden** task: run a short Claude Code prompt to completion → its task
   badges via the **flash** channel about **61 s** after the session goes idle (§ B: no title rule
   ships for Claude Code, and the measured bell latency was accepted); tooltip shows the label;
   clicking the badge switches and focuses the terminal; badge clears.
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

### Built to work with any application, at the user's instruction

The user asked that this work the same for any Windows program, not just the two this file is
written around. That mostly falls out of the signal: **`HSHELL_FLASH` is raised by the shell**, for
any window whose taskbar button flashes, so HydraWin never has to know the application. Concretely:

- No per-app code and no per-app rules on by default. Teams and Claude Code are not special cases;
  they are two applications that happen to flash.
- **The label is built from the window** — process file name plus live title — so an application
  nobody has configured still produces a readable badge. A rule's `Label` only overrides it when a
  rule actually fires.
- **The flashing handle is resolved to its root** (`GetAncestor(GA_ROOT)`), so an app that flashes
  an owned dialog rather than its main window still badges the right task.
- **Repeat flashes coalesce.** `FLASHW_TIMERNOFG` flashes until the window is foregrounded; one
  pending entry per window with its timestamp refreshed, so the count tracks windows needing
  attention rather than signals received.

### Deviations

- **Badges clear only on focus, never on a task switch** — for every kind, not just title-kind. § C
  says a switch clears flash-kind entries wholesale; the warning box at the top of this file says
  that is wrong for Teams, which flashes once per unread run. Agreed with the user: the box wins,
  and applying it uniformly is also what keeps behaviour predictable for applications nobody has
  tested. A badge now means exactly "you have not looked at this window yet".
- **No title rule ships enabled.** One disabled browser-unread rule is seeded as a worked example
  to copy, since the editor is task 10 and an empty list gives a hand-editor nothing to start from.
  The final regex, verbatim: `^\(\d+\)` for `chrome.exe`, `Enabled: false`.
- **The task-02 placeholder is filled in and its `#pragma warning disable S2094` pair deleted**, per
  `CLAUDE.md`. Its doc comment also asserted that Claude Code is title-only and its bell never
  flashed — which task 01's re-test disproved — so that was corrected rather than carried forward.

### A bug live testing found

`WindowTracker` registers its WinEvent hook with `WINEVENT_SKIPOWNPROCESS`, so **no foreground event
fires when HydraWin's own window is activated**. The hub therefore kept believing the last foreign
window the user had visited was still in front, and suppressed its notifications *for the rest of
the session*: focus a task's window once, come back to HydraWin, and that window could never badge
again. Found by flashing a window that had been focused earlier and getting nothing.

Fixed by having `MainWindow.OnActivated` report "nothing foreign is in front" to the hub, which is
the other half of a signal the tracker structurally cannot provide. Covered by a regression test.

### Verified live (my smoke test, throwaway windows, generic path only)

| Check | Observed |
| --- | --- |
| Flash an assigned, visible-but-background window | badge on the task row, dot on the window row |
| Flash the same window while **HydraWin had it hidden** | badges — the headline capability |
| Flash an **unassigned** window | nothing, anywhere (§ 5) |
| Flash the **foreground** window | nothing |
| Flash the same window 5× | count stays 1 — coalesced |
| Focus the window | badge clears |
| **Switch to the task** | badge **survives** — the decisive rule |
| Click the badge | switches, focuses the waiting window, badge clears |
| Regression: focus a window, return to HydraWin, flash it again | badges (was silently suppressed before the fix) |

### Not verified: the terminal bell as a flash source

**I could not get a Windows Terminal bell to produce a badge, and I am not claiming it does.** After
fixing the suppression bug above I retried with a fresh terminal ringing `[Console]::Out.Write(
[char]7)` while backgrounded; the task row showed no dot for that window. `bellStyle` on this
machine is `audible, window, taskbar`, so the configuration was right.

What this does *not* establish: my harness had confounds — `wt` reuses an existing process and
opens tabs rather than windows unless forced, the window title tracks the running command, and I
could not observe from outside whether the bell actually rang. So this may be a Windows Terminal
behaviour, a harness artefact, or something in HydraWin that the `FlashWindowEx` tests do not reach.

It matters because it is the channel Claude Code depends on, so **§ Verification steps 3 and 4 are
genuinely outstanding**, not merely delegated. They need a real Claude Code session going idle and a
real Teams message from a second account — the same conditions task 01 used to measure both.

### Build, tests, format

- `dotnet build HydraWin.sln` — **0 warnings, 0 errors**, with the placeholder pragmas removed
  rather than extended; `src/` now contains no `#pragma warning` at all.
- `dotnet test --solution HydraWin.sln` — **224/224 passed** (198 before; 26 new across
  `NotificationRuleTests` and `NotificationHubTests`, including the suppression regression and a
  catastrophic-backtracking pattern proving the 100 ms timeout degrades to "no match").
- `dotnet format --verify-no-changes` — exit 0.

### Files

**New** — `src/HydraWin.Core/Notifications/NotificationHub.cs`, `NotificationKind.cs`,
`PendingNotification.cs`; `src/HydraWin.Core/Interop/IShellHookApi.cs`, `Win32ShellHookApi.cs`;
`src/HydraWin.App/Services/ShellHookListener.cs`;
`tests/HydraWin.Core.Tests/NotificationHubTests.cs`, `NotificationRuleTests.cs`.

**Modified** — `src/HydraWin.Core/Notifications/NotificationRule.cs`,
`src/HydraWin.Core/Interop/NativeMethods.cs`, `src/HydraWin.Core/Workspaces/SettingsModel.cs`,
`src/HydraWin.App/App.xaml.cs`, `src/HydraWin.App/MainWindow.xaml` + `.xaml.cs`,
`src/HydraWin.App/DragDropSupport.cs`, `src/HydraWin.App/Services/TrayIcon.cs`,
`src/HydraWin.App/ViewModels/MainViewModel.cs`, `TaskViewModel.cs`, `WindowViewModel.cs`,
`tasks/initial_build/09_notifications.md`, `tasks/initial_build/_status.md`.

**Deleted** — none.

### User walkthrough

*(outstanding: § 3 Claude Code, § 4 Teams, § 6 soak — and the bell→flash question above)*

### Post-acceptance fix: the digit sat low in the badge (2026-08-16)

The user reported the count rendering below the circle's centre. Cause: `VerticalAlignment="Center"`
centres the text *box*, and Segoe UI reserves more room above the baseline (ascent ≈ 1.08 em) than
digits occupy (cap height ≈ 0.70 em), so the ink lands ≈ 0.6 px low inside a correctly-centred
box; `UseLayoutRounding="True"` then snaps that out to a whole device pixel. Measured in a
throwaway rig reproducing the badge at 192 dpi: **+2 device pixels low** as shipped, and **0** with
a `TranslateTransform Y="-1"` on the TextBlock — identical for counts 1, 2, 9 and 12. Confirmed in
the running app by flashing a scratch window and measuring the capture: **−0.5 px**, i.e. centred
within noise. A render transform rather than a margin, so the pill width that `Margin="4,0"`
sets for two-digit counts is untouched. `MainWindow.xaml` only; 224 tests still pass.
