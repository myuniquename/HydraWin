# Task 10 — Hardening and polish

Status: **done (2026-08-16)** — awaiting the user's acceptance walkthrough
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
**Superseded — see the record.** This section was written before task 07, which removed elevated
windows from the inventory entirely; confirmed with the user on 2026-08-16 that the exclusion wins.
There is no row to put a shield on, so what remains is finishing the `Unmanageable` runtime safety
net for a window that looked ordinary and refused `SW_HIDE` anyway.
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
Single modal dialog with tabs (the user's choice, 2026-08-16): restore-on-exit toggle (task 08),
close-to-tray, stay-on-top, toasts on/off (task 09), hotkey editor (capture-style textbox per
binding, conflict warning on registration failure), notification rules (C). All persist through
`SettingsModel` / `WorkspaceStore` (task 04). **Launch at login is dropped from the project**, not
deferred — the user's instruction on 2026-08-16.

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

1. Elevated Notepad (Run as administrator) → confirm it is **absent** from the unassigned list, and
   that aiming the picker crosshair at it explains why. (Rewritten: § A's shield was superseded by
   task 07's exclusion.)
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

### Two decisions taken with the user first

- **Elevated windows stay out of the inventory** (§ A). This file predates task 07, which removed
  them entirely on the user's instruction — `CLAUDE.md` records that as settled: *a window the app
  can never hide has no business being offered as something to put in a task*. Rebuilding § A as
  written would have undone that. § A therefore reduced to the work below.
- **Launch at login is dropped from the project**, not deferred again. Struck from this file and
  from `08_tray_and_hotkeys.md` § D. HydraWin touches no registry at all.

### A. Unmanageable windows — what was actually left

Most of § A already existed: the filter drops elevated windows (`TrackableVerdict.Elevated`), the
picker explains a refusal, the window row carries a chip, and `SwitchSummary` counts what refused.
Two gaps closed:

- **The refusal now names the window.** `Unmanageable` was only pushed onto rows during a rebuild,
  so a window that refused mid-switch showed its chip a beat late and the user got a bare count.
  The switch now adds a second status line naming which window stayed on screen.
- **The chip was relabelled** from "elevated" to "won't hide", with a tooltip that says what it
  actually means now that elevation is filtered upstream — a protected process, or one that became
  elevated after HydraWin first saw it.

### B. Always-visible windows

Modelled as `WorkspaceState.GlobalWindows`, a plain list beside `Tasks` — deliberately **not** a
reserved `HydraWinTask`. `SwitchPlan.Compute` builds its hide set by walking `Tasks`, so a pinned
window is *structurally* impossible to hide rather than protected by a rule somebody has to
remember. It also keeps pins out of `HydraWinTask.Order`, which the `Ctrl+Alt+1..9` bindings use.

Consequences worth knowing:

- A pin carries a re-attach rule like any assignment, so it survives a restart. `RuleMatcher`
  checks pins **before** tasks: a window the user pinned must not be claimed by a task rule that
  also matches it, or the next switch would hide the very window pinning exists to keep on screen.
- Pinning a currently-hidden window shows it first, reusing the guard `UnassignWindow` already had.
  Both operations move a window somewhere no switch plan reaches, and doing that to a hidden window
  would strand it.
- Unpinning removes the rule as well as the binding, or the window would silently re-pin itself the
  next time it appeared — the same trap task 04's orphaned-rule fix taught us to test for.

UI: an *ALWAYS VISIBLE* strip under the task list, always present so it can teach the feature and
be dropped onto while empty; plus *Pin as always visible* / *Unpin* on the window row menu.

### C and D. Rule editing and settings

The re-attach editor and the notification-rule list both preview live against the open windows,
through one `RulePreview` helper in Core that calls the **same** `Matches` the tracker uses — a
preview computed by a second implementation could promise something the tracker would never do.
`NotificationRule` grew `MatchesTitle`, which `Matches` now applies twice (new title, old title) so
the edge trigger and the preview cannot drift.

The two rule types treat a broken regex differently, on purpose: a re-attach rule blocks Save (it
has no enabled flag to fall back to), a notification rule is **saved switched off** with the reason
shown — losing a half-written rule would be worse than keeping it quiet.

Settings is a modal dialog with General / Hotkeys / Notifications tabs, editing **copies** so
Cancel is real. The hotkey rows use a capture box that reads the combination from the keypress and
accepts exactly the three key families `HotkeyBinding.TryResolve` understands, so the dialog cannot
produce something the resolver then rejects. On OK, `MainViewModel` raises `HotkeysChanged` and the
App disposes and recreates `HotkeyService` — hotkeys belong to the thread that claimed them, so
rebinding in place is not possible.

### E. Robustness

`AppLog` appends timestamped lines to `%APPDATA%\HydraWin\logs\hydrawin.log`, rolls once at 1 MB,
and swallows its own IO failures — a log that takes the app down defeats the reason for having one.
It is fed from `MainViewModel.Say`, the funnel every user-visible status line already went through.

The crash handlers log the exception, run `RestoreAll`, and then **let the process die**
(`e.Handled` is never set). This was validated twice:

- **For real.** A null-reference bug of mine (`NotificationRuleViewModel` refreshing from a
  generated property setter *during* its own constructor, before the other fields were assigned)
  crashed the app the first time the settings dialog was opened. The log caught it with a full
  stack, and recorded `restore attempted — restored 1 window(s)`. That is how the bug was found at
  all. Fixed with a `ready` flag in both rule view models.
- **Deliberately**, through the `#if DEBUG` tray item, with a task hidden:

  | Check | Observed |
  | --- | --- |
  | Process survives the throw? | no — died, as intended |
  | Hidden window back on screen? | yes |
  | Pinned window still visible? | yes |
  | Journal afterwards | 0 entries |
  | Log | exception + stack + `restore attempted` |

**Multi-monitor: no clamp was needed, and none was added.** Measured rather than assumed. A window
was hidden, its recorded placement rewritten to `(-30000,-30000)` — what a disconnected monitor's
coordinates become — and restored the way `RestoreService` does it (placement, then show):

```
virtual screen  : (0,0)-(6144,3456)
restoring with  : (-30000,-30000)-(-29200,-29520)
after           : (0,0)-(800,480)      <- Windows remapped it itself
```

`SetWindowPlacement` works in workspace coordinates and put the window back inside the virtual
screen on its own, so a `MonitorFromRect` clamp would have been dead code. **This machine has one
monitor**, so the genuine disconnect/reconnect check is still the user's.

### Verified live (my smoke test, throwaway windows)

| Check | Observed |
| --- | --- |
| Pinned window through `Ctrl+Alt+1`, `Ctrl+Alt+2`, show-all | visible at every step |
| Task windows across the same switches | swapped correctly (A hidden ⇄ B hidden) |
| Pin in the journal, ever | never — 0/4 samples |
| Pin re-attaches after a restart by its own rule | yes, into *ALWAYS VISIBLE* |
| Rebind `Ctrl+Alt+1` → `Ctrl+Alt+F1` in the dialog | keypress captured; `state.json` updated |
| Old combination afterwards | dead |
| New combination afterwards | switches, and survives a restart |
| Settings *Cancel* after toggling a checkbox | `state.json` unchanged, dialog closed |
| Rule editor live preview, widened to `HW-SMOKE` | listed the 2 other matching windows |
| Rule editor with `HW-SMOKE-(` as a regex | inline error, preview emptied, Save refused, app fine |
| Notification tab preview | matched real open titles, including the seeded chrome rule |

### Build, tests, format

- `dotnet build HydraWin.sln` — **0 warnings, 0 errors**. Six Sonar findings came up during the
  work (S3267, S3358, S6580, S2699 and friends) and were all fixed in code; nothing was suppressed.
- `dotnet test --solution HydraWin.sln` — **261/261 passed** (224 before; 37 new across
  `GlobalWindowTests`, `RulePreviewTests`, `AppLogTests` and `HotkeyBindingDisplayTests`).
  `JsonStoreTests` needed one edit: it asserts `state.json`'s exact property list, which now
  includes `GlobalWindows`.
- `dotnet format --verify-no-changes` — exit 0.

### Files

**New** — `src/HydraWin.Core/Diagnostics/AppLog.cs`, `src/HydraWin.Core/Workspaces/RulePreview.cs`;
`src/HydraWin.App/Controls/HotkeyBox.cs`, `src/HydraWin.App/Views/SettingsWindow.xaml` + `.cs`,
`src/HydraWin.App/Views/RuleEditorWindow.xaml` + `.cs`,
`src/HydraWin.App/ViewModels/SettingsViewModel.cs`, `HotkeyBindingViewModel.cs`,
`NotificationRuleViewModel.cs`, `RuleEditorViewModel.cs`;
`tests/HydraWin.Core.Tests/GlobalWindowTests.cs`, `RulePreviewTests.cs`, `AppLogTests.cs`,
`HotkeyBindingDisplayTests.cs`.

**Modified** — Core: `Workspaces/WorkspaceState.cs`, `WorkspaceService.cs`, `RuleMatcher.cs`,
`WindowAssignment.cs`, `HotkeyBinding.cs`, `SettingsModel.cs`,
`Notifications/NotificationRule.cs`, `Tracking/TrackedWindow.cs`, `Persistence/HydraWinPaths.cs`.
App: `App.xaml` + `App.xaml.cs`, `MainWindow.xaml` + `.xaml.cs`, `ViewModels/MainViewModel.cs`,
`Services/TrayIcon.cs`, `Services/HotkeyService.cs`. Tests: `JsonStoreTests.cs`.
Docs: `tasks/initial_build/10_hardening_polish.md`, `08_tray_and_hotkeys.md`, `_status.md`.

**Deleted** — none. `RestoreService` is deliberately unchanged: see the monitor result above.

### Left to the user

§ Verification 1 (elevated Notepad is absent and the picker says why), the multi-monitor
disconnect/reconnect check, and the acceptance pass over the two new dialogs.
`%APPDATA%\HydraWin` has been reset and the scratch windows closed.
