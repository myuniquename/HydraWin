# Task 06 — Switch engine

Status: **done** (2026-08-16)
Depends on: task 03 (WindowTracker — inventory + `LastForegroundWindow`), task 05
(RecoveryJournal — journal-before-hide). Consult task 01's recorded results for per-app
hide/show quirks before implementing.

## Motivation

This is the feature: clicking a task hides every other task's windows and brings this task's
windows back exactly as they were. Everything before this task exists to make this step safe and
accurate.

## Background

Architecture recap: `WorkspaceService` (task 04) holds tasks and their window assignments, each
assignment bound to a live HWND or unbound. `RecoveryJournal` (task 05) provides
`RecordBeforeHide` / `ConfirmShown` and `RestoreService` the placement-validating show logic.
The project invariant: **journal entries are flushed before any `SW_HIDE`.** Unassigned windows
are never touched. Per `_plan.md`, HydraWin never closes/moves/resizes foreign windows beyond
placement restore of windows it hid itself.

Focus rule: `SetForegroundWindow` succeeds only when the caller is the foreground process. A
switch is user-initiated (click in HydraWin / hotkey while HydraWin visible), so HydraWin *is*
foreground — no focus hacks allowed for other paths (repo gotcha).

## Work

### A. `SwitchEngine` (Core, `Workspaces/`)
`SwitchTo(taskId)` executes strictly in this order:

1. Compute sets: `toHide` = all bound windows of every *other* task not already hidden;
   `toShow` = bound windows of the target task that are currently HydraWin-hidden.
2. For each `toHide` window capture `GetWindowPlacement` and build a `JournalEntry`;
   `RecoveryJournal.RecordBeforeHide(entries)` — synchronous flush.
3. `ShowWindow(SW_HIDE)` each `toHide` window. Track per-window failure (window died between
   steps — fine, it will be dropped as stale later).
4. For each `toShow` window: restore via the same identity-validating path as
   `RestoreService` (placement + `SW_SHOW`), then `RecoveryJournal.ConfirmShown`.
   Show in bottom-to-top z-order if feasible; last-active window last.
5. Focus: `SetForegroundWindow` on the target task's `LastActiveHwnd` if alive, else the first
   shown window, else no-op. (`LastActiveHwnd` per task: updated whenever WindowTracker's
   `ForegroundChanged` reports a window bound to that task.)
6. Update `WorkspaceState.ActiveTaskId`, persist (debounced store from task 04), raise
   `SwitchCompleted` with a summary (hidden n, shown n, stale n) for the UI/log.

Also: `ShowAllTasks()` — un-hide everything from the journal (delegates to `RestoreService`),
clear `ActiveTaskId`; used by "show all" UI and task deletion.

### B. Hidden-set wiring
`SwitchEngine` (or a small shared `HiddenWindowSet`) now implements the `IHiddenWindowSet` that
`WindowTracker` (task 03) consumes — replace the stub so hidden windows remain tracked. Single
source of truth: the journal's snapshot.

### C. Edge behaviour (implement, don't improvise later)
- Switching to the already-active task → only re-asserts visibility/focus (idempotent).
- A `toShow` assignment whose window died while hidden → drop the binding (rule remains for
  re-attach), count as stale in the summary.
- A `toHide` window that refuses `SW_HIDE` → remove its journal entry, leave it visible, mark the
  assignment `Unmanageable` so the UI (task 10) can annotate it. Never leave a journal entry for a
  window that is not actually hidden.
  Task 01 measured the real signature, and it is *not* packaged apps — Teams hides cleanly. It is
  **elevated windows** (UIPI): `ShowWindow` returns `FALSE` with `GetLastError() == 5`
  (`ERROR_ACCESS_DENIED`) and the window stays visible; `SetWindowPlacement` fails the same way.
  Detect the refusal with `IsWindowVisible(hwnd)` *after* the call — never with `ShowWindow`'s
  return value, which is the window's **previous visibility**, not success (a successful hide of a
  visible window returns `TRUE`). Elevation can also be predicted before trying:
  `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` succeeds against elevated processes and is
  useless as a test, but `OpenProcessToken` + `GetTokenInformation(TokenElevation)` works.
- Deleting a task (task 04's `DeleteTask`) with hidden windows → `ShowAll` those windows first,
  then unassign. Deletion never closes windows.

### D. Temporary trigger
Until task 07 lands: a debug menu/keyboard command in the harness window listing tasks 1..9 and
switching on keypress, so verification below is runnable. (This also replaces task 05's
temporary hide command.)

### E. Unit tests
Set computation (who hides, who shows, unassigned untouched); ordering guarantee (journal write
observed before hide in a scripted fake interop layer — assert call order); idempotent
re-switch; dead-window and refuses-hide branches.

## Verification

- `dotnet test` — paste totals.
- Manual, with 3 tasks × (1 browser window, 1 Windows Terminal, 1 VS Code) assigned via a debug
  seed:
  1. Switch task 1 → 2 → 3 → 1: after each, *only* the active task's windows are on the taskbar
     and in Alt-Tab; maximized windows come back maximized; positions pixel-identical (compare
     screenshots for one window); focus lands on the task's last-active window.
  2. Two Chrome windows, same process, different tasks: switching hides one and not the other.
  3. While task 2 is hidden, close its VS Code via the taskbar preview of task… (not possible —
     it's hidden; instead kill via Task Manager). Switch to task 2 → summary reports 1 stale, no
     error, remaining windows show.
  4. Mid-switch crash drill: breakpoint between journal flush and `SW_HIDE`, kill HydraWin, run
     `hydrawin.exe --restore-all` → everything visible again (the invariant holding under the worst
     interleaving).
  5. An unassigned Notepad stays visible through every switch.

## Record on completion

Built `SwitchPlan` (the pure set computation), `SwitchEngine`, and `HiddenWindowSet`, and wired
the real hidden set into the tracker so hidden windows finally stay in the inventory. The
`EmptyHiddenWindowSet` stub task 03 shipped is no longer used by the app.

### Design notes and deviations

- **`RestoreService.RestoreWindow` extracted** rather than duplicating the identity check. § A.4
  says to restore "via the same identity-validating path as `RestoreService`", so `RestoreAll` and
  `SwitchTo` now share one implementation of the rule that an unverified handle is never shown.
- **`HiddenWindowSet` is an in-memory cache, not a journal read.** `WindowTracker` asks
  `Contains` about every top-level window on every sweep — ~400 handles every two seconds — so
  answering from `journal.Snapshot()` would mean hundreds of mutex-guarded file reads per second.
  It is seeded from the journal at construction and updated on every hide and show; the journal
  remains the durable source of truth.
- **A window whose placement cannot be read is not hidden at all.** Not in the task text, but
  hiding a window we could never put back where it was is precisely the outcome this project
  exists to prevent. It is skipped and counted as neither hidden nor unmanageable.
- **`Unmanageable` assignments are still retried on every switch.** The flag records that a window
  refused once, not that it always will, so the app heals itself if that process is later restarted
  without elevation. The cost is one failed call plus a journal add/remove per switch.
- **`SwitchSummary` carries `Unmanageable` alongside hidden/shown/stale**, so the UI can say why a
  window is still on screen.
- **The crash-drill hook replaces the breakpoint** § 4 asks for. `SwitchEngine.AfterJournalFlush`
  fires between the flush and the first hide; the harness wires it to `Environment.FailFast`, which
  is a truer crash than a breakpoint since not even `OnExit` runs.

### Two bugs found during the drill

- **Switch summary counted skipped windows as hidden.** `Hidden` was derived as
  `ToHide.Count - unmanageable`, so a window skipped for want of a placement was reported as
  hidden. Caught by `AWindowWhosePlacementCannotBeReadIsNotHidden`; `HideAll` now returns the
  actual count.
- **A window that closed while hidden orphaned its journal entry.** The tracker unbinds a closed
  window before any switch can notice it, so no assignment referred to the entry any more and only
  a full `RestoreAll` would ever clear it — meanwhile the dead handle stayed in the hidden set, and
  a recycled handle would have made the tracker treat an unrelated new window as hidden. Found by
  reading `journal.json` during the drill and seeing `HW-TASK2-TERM` still listed. Fixed with
  `SwitchEngine.OnWindowDisappeared`, wired to the tracker's event.

A third bug never reached the drill: `CommandParameter="1"` in XAML is a *string*, and the
generated `RelayCommand<int>` threw on it during layout, killing the app at startup. Typed with
`sys:Int32`. Worth noting only because it crashed on launch — a build-clean solution proved
nothing until it was actually run.

### Per-app behaviour vs task 01's predictions

Task 01 predicted the refusal case is elevated windows under UIPI, not packaged apps. Nothing in
this drill refused: three Chrome windows, three Windows Terminal windows (one maximized) all hid
and restored cleanly, so the `Unmanageable` branch is covered by unit tests rather than by a live
observation. Task 01's Teams and elevated-Task-Manager measurements still stand as the evidence
for that path.

### Verification results

- `dotnet build HydraWin.sln` → **0 warnings, 0 errors**. One Sonar finding (S4487, an unread
  private field left over when the view model started delegating to the engine) fixed in code; no
  suppressions added.
- `dotnet test --solution HydraWin.sln` → **total: 132, failed: 0, succeeded: 132, skipped: 0**
  (26 new: 9 `SwitchPlan`, 17 `SwitchEngine`).
- `dotnet format --verify-no-changes` → exit 0.
- The three `spikes/` projects still build clean.

**Manual drill**, against six windows I spawned — three Chrome (all in one process, pid 17572) and
three Windows Terminal, seeded into three tasks. Your own windows stayed unassigned throughout,
which is how step 5 was proved.

```
1. switch 1 -> 2 -> 3 -> 1
   after 1:  TASK1-CHROME VISIBLE  TASK1-TERM VISIBLE   others hidden
   after 2:  TASK2-CHROME VISIBLE  TASK2-TERM VISIBLE   others hidden
   after 3:  TASK3-CHROME VISIBLE  TASK3-TERM VISIBLE   others hidden
   after 1:  TASK1-CHROME VISIBLE  TASK1-TERM VISIBLE   others hidden
   rects pixel-identical through every cycle, e.g. TASK1-CHROME (888,268)-(2477,1455) each time
   the maximized terminal returned SW_SHOWMAXIMIZED(3) at (-7,-7)-(3078,1686) after two cycles

2. same-process Chrome: 0x3F09D4 VISIBLE while 0x9050A and 0x90564 hidden - one process, three
   windows, independently controlled

3. closed HW-TASK2-TERM (WM_CLOSE) while it was hidden, then switched to Task 2:
   no error, the surviving window showed, and the task pane read
   WindowsTerminal.exe "HW-TASK2-TERM" - unbound
   binding dropped, rule kept, so reopening it would re-attach

4. crash drill: hook armed, switched to Task 1, process died between the flush and the first hide.
   journal.json then held 6 entries including HW-TASK2-CHROME - which was still VISIBLE on screen,
   which is the invariant made visible: the write happened before the hide.
   hydrawin.exe --restore-all  ->  exit 0
                               ->  "restored 5 window(s), dropped 1 stale entry"
   all five surviving windows visible, journal []

5. unassigned windows (Explorer, Signal, Task Manager, Telegram, Total Commander, SourceGit)
   visible at every step - the plan is built from assignments only, so they can never be touched
```

After cleanup: no drill windows left, `hideshow rescue` reports the journal empty, and
`%APPDATA%\HydraWin` was reset.

### Files

New: `src/HydraWin.Core/Workspaces/SwitchPlan.cs`, `SwitchEngine.cs`, `HiddenWindowSet.cs`;
`tests/HydraWin.Core.Tests/SwitchPlanTests.cs`, `SwitchEngineTests.cs`.

Modified: `Workspaces/HydraWinTask.cs` (`LastActiveHwnd`), `WindowAssignment.cs` (`Unmanageable`),
`WorkspaceService.cs` (`FindTask`, `SetActiveTask`, `NoteForegroundWindow`),
`Recovery/RestoreService.cs` (`RestoreWindow` + `RestoreOutcome`),
`Interop/NativeMethods.cs` + `IWindowApi.cs` + `Win32WindowApi.cs` (`TryFocus`),
`tests/HydraWin.Core.Tests/FakeWindowApi.cs` (focus, refuse-to-hide, window removal),
`src/HydraWin.App/MainWindow.xaml(.cs)` and `ViewModels/MainViewModel.cs` (switch triggers,
number keys, drill seeding, crash hook, real hidden set), and this file.

Deleted: none. `EmptyHiddenWindowSet` stays for tests and for any host that runs the tracker
without a switch engine.
