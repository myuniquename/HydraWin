# Task 10 — Hardening and polish

Status: **not started**
Depends on: tasks 03–09 all landed (this task closes their deliberately deferred edges).

## Motivation

The deferred sharp edges — elevated windows, always-visible "global" windows, rule editing,
settings — are what separate a demo from the daily driver the user asked for. Each item below
was explicitly parked by an earlier task; this closes them.

## Background

Architecture recap: Core services (`WindowTracker`, `WorkspaceService`, `SwitchEngine`,
`RecoveryJournal`, `NotificationHub`) with WPF shell, tray, hotkeys. Known parked edges:
elevated-process windows have empty `ProcessPath` (task 03) and cannot be hidden by a
non-elevated process (UIPI); `Unmanageable` assignments exist but aren't surfaced (task 06);
re-attach and notification rules are hand-edited JSON only (tasks 04/09); hotkeys are fixed
defaults (task 08).

## Work

### A. Elevated / unmanageable windows
- Detect at track time: empty `ProcessPath` ⇒ likely elevated/protected. Mark
  `TrackedWindow.IsRestricted`.
- UI: shield glyph + tooltip "Runs elevated — HydraWin can't hide this window" on its rows;
  assignment still allowed (it groups and badges) but the switch engine skips it for hide/show
  and the switch summary counts it, matching the `Unmanageable` path from task 06. Surface
  `Unmanageable` the same way once it trips.
- Explicitly out (record if requested later): running HydraWin elevated.

### B. Pinned / global windows
- New assignment target: a built-in pseudo-task **Global** (music player, this manager…).
  Model: flag on the assignment, not a real `HydraWinTask`. Global windows are never hidden by any
  switch; UI shows them in a slim section under the task list; drag in/out like any task.
- HydraWin's own window remains special: not trackable at all (task 03 filter), not just global.

### C. Rule editing UI
- Per-assignment: *Edit re-attach rule…* dialog — process file name, pattern, substring/regex
  toggle, live "currently matches: <window title / nothing>" preview against open windows.
- Notification rules: settings page listing rules (process, regex, label, kind, enabled),
  add/edit/delete, same live-preview idea against current titles. Invalid regex → inline error,
  rule saved disabled.

### D. Settings page
Single dialog (or left-nav second view): restore-on-exit toggle (task 08), launch at login
(task 08), toasts on/off (task 09), hotkey editor (capture-style textbox per binding, conflict
warning on registration failure), notification rules (C). All persist through `SettingsModel` /
`WorkspaceStore` (task 04).

### E. Robustness sweep
- Global exception handler (`DispatcherUnhandledException` + `AppDomain.UnhandledException`):
  log to `%APPDATA%\HydraWin\logs\`, attempt `RestoreAll`, then let it crash — never swallow.
  Verify the journal makes this safe (crash drill again after wiring).
- Simple rolling file log for the summaries/events already raised (switches, recoveries, rule
  matches, hook registration failures). No logging framework ceremony — a small append-with-cap
  helper is fine.
- Multi-monitor spot-check: placements restore to a disconnected-then-reconnected monitor
  gracefully (Windows remaps; verify no off-screen strand — if stranded, add a
  `MonitorFromRect` clamp and record it).

## Verification

1. Elevated Notepad (Run as administrator), assign it → shield shown; switching hides everything
   else, elevated window stays, summary notes it; no errors.
2. Pin a media player Global → visible across all switches; unpin → behaves as unassigned.
3. Edit a VS Code re-attach rule to a folder-specific regex with live preview; close/reopen
   VS Code → re-attaches per the new rule. Break the regex → inline error, rule disabled, no
   crash, tracker unaffected.
4. Change the Claude-done hotkey… (n/a) — change `Ctrl+Alt+1` to `Ctrl+Alt+F1` in the editor →
   old binding gone, new works, persists over restart.
5. Throw a test exception (debug-only menu item) with a task hidden → log written, windows
   restored, process exits; relaunch shows recovery notice with empty journal afterwards.
6. `dotnet build` / `dotnet test` clean, totals pasted; `dotnet format --verify-no-changes`
   clean.

## Record on completion

*(what was done, deviations and why, monitor-clamp outcome, log samples, test totals, and the
list of new / modified / deleted files)*
