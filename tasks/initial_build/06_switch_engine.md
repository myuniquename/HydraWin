# Task 06 — Switch engine

Status: **not started**
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
- A `toHide` window that refuses `SW_HIDE` (returns success but stays visible — some packaged
  apps; see task 01 findings) → remove its journal entry, leave it visible, mark the assignment
  `Unmanageable` so the UI (task 10) can annotate it. Never leave a journal entry for a window
  that is not actually hidden.
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

*(what was done, deviations and why, per-app quirks actually hit vs task 01's predictions, test
totals, and the list of new / modified / deleted files)*
