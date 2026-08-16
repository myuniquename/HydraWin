# Task 05 — RecoveryJournal: the crash-safety contract

Status: **done** (2026-08-15, accepted 2026-08-16)
Depends on: task 04 (reuses `JsonStore<T>` atomic persistence).

## Motivation

HydraWin fully hides windows (`SW_HIDE`): they vanish from the taskbar and Alt-Tab. If HydraWin
crashes with windows hidden and there were no recovery mechanism, the user's windows would be
running but unreachable. This task makes that impossible: a write-ahead journal on disk, startup
recovery, and a `--restore-all` escape hatch that works even when the UI cannot start. This is
the project's one invariant (see `_plan.md` § *Shared ground rules*): **no foreign window is
ever hidden before its journal entry is flushed.**

## Background

Architecture recap: the switch engine (task 06) hides and shows windows. Before hiding, it must
hand the window list to `RecoveryJournal`, which persists them to `%APPDATA%\HydraWin\journal.json`
and only then reports ready. HWNDs are recycled by Windows, so a journal entry must carry enough
identity to avoid un-hiding a *different* window that inherited the handle: PID + process image
path, validated at restore time. Restore also needs the original placement, captured before
hiding. `JsonStore<T>` (task 04, `Persistence/`) already provides atomic JSON save/load — reuse
it, do not write a second persistence mechanism.

## Work

### A. Interop additions (`Interop/NativeMethods.cs`)
`ShowWindow` (`SW_HIDE = 0`, `SW_SHOW = 5`, `SW_SHOWNA = 8`), `IsWindow`,
`GetWindowPlacement`/`SetWindowPlacement` with the `WINDOWPLACEMENT` struct (its `length` field
must be set to `Marshal.SizeOf<WINDOWPLACEMENT>()` before both calls — classic silent-failure
pitfall, note it in code where the struct is created).

### B. Journal model and store (`Recovery/`)
- `JournalEntry { long Hwnd; int Pid; string ProcessPath; string TitleAtHide;
  WindowPlacementDto Placement; DateTimeOffset HiddenAt; }` — `WindowPlacementDto` is a
  serializable mirror of `WINDOWPLACEMENT` (showCmd, normal-position rect, min/max points).
- `RecoveryJournal` over `JsonStore<List<JournalEntry>>` at `%APPDATA%\HydraWin\journal.json`:
  - `RecordBeforeHide(IEnumerable<JournalEntry>)` — appends and **synchronously flushes**
    (no debounce here; this is the write-ahead step).
  - `ConfirmShown(hwnd)` — removes the entry and flushes.
  - `Snapshot()` — current entries.
- Entry lifecycle: added before `SW_HIDE`, removed after a successful `SW_SHOW` (+ placement
  restore). The journal therefore always equals "windows HydraWin currently has hidden".

### C. `RestoreService`
- `RestoreAll(journal)`: for each entry — if `IsWindow(hwnd)` and the hwnd's current PID +
  process path (via `GetWindowThreadProcessId` + `QueryFullProcessImageNameW`, both already in
  Interop from task 03) match the entry: `SetWindowPlacement` (restores position + maximized
  state) then `ShowWindow(SW_SHOW)`, then remove the entry. If identity does not match (stale
  hwnd — window closed while hidden or handle recycled): just remove the entry and log it;
  never show an unverified handle.
- Returns a summary `{ Restored, Stale }` for reporting.

### D. Wiring the escape hatches (`HydraWin.App`)
- **CLI**: `hydrawin.exe --restore-all` (replaces task 02's placeholder) — no WPF window: run
  `RestoreService.RestoreAll`, print `restored N window(s), dropped M stale entr(ies)`, exit 0.
  Must not require or start the main instance; it only reads the journal and touches HWNDs.
- **Startup recovery**: on normal launch, if the journal is non-empty, run `RestoreAll` *before*
  any other window manipulation and show a non-blocking notice in the UI ("Recovered N windows
  from a previous session"). Rationale: a non-empty journal at startup means the previous run
  did not exit cleanly; visible windows are always the safe state to start from.
- **Clean exit**: on app shutdown (window close, tray Exit, session ending —
  `Application.SessionEnding`), `RestoreAll` then confirm the journal file is empty.

### E. Unit tests
Journal add/confirm/flush sequencing (using a temp-dir store); `RestoreService` identity
validation against fakes of the Win32 layer (matching entry → shown + removed; stale PID → not
shown, removed; dead hwnd → removed); DTO ↔ struct placement mapping.

## Verification

- `dotnet test` — paste totals.
- Manual crash drill (the acceptance test for the whole invariant), using a temporary debug
  command that hides a chosen window through the journal path (task 06 not yet built):
  1. Hide a Notepad window via the debug command → confirm it is gone from taskbar/Alt-Tab and
     `journal.json` contains its entry with placement.
  2. Kill HydraWin with Task Manager (no clean shutdown).
  3. Run `hydrawin.exe --restore-all` → prints `restored 1…`, Notepad is back at its old position,
     `journal.json` is empty.
  4. Repeat 1–2, then start HydraWin normally → Notepad reappears at startup and the recovery
     notice shows.
  5. Repeat 1, close Notepad's process via Task Manager while hidden, kill HydraWin, run
     `--restore-all` → prints `dropped 1 stale`, no error, journal empty.

## Record on completion

Built the interop additions, `RecoveryJournal`, `RestoreService`, and the three escape hatches
(`--restore-all`, startup recovery, clean exit). The `IWindowApi` seam task 02 reserved is now
filled in, which is what made the identity checks testable. `JournalEntry`'s task 02 `S2094`
suppression is gone; only `NotificationRule` remains, for task 09.

### Deviations, and why

- **`RestoreSummary` carries a third count, `Failed`.** The task specifies `{ Restored, Stale }`.
  But a window that still exists, still passes the identity check, and yet refuses to show is
  neither restored nor stale — and dropping its entry would strand it hidden forever, which is the
  exact failure this task exists to prevent. Such entries stay in the journal for a later attempt
  and are counted separately. The CLI line only mentions them when non-zero, so the specified
  output is unchanged in the normal case.
- **`RecoveryJournal` is guarded by a named mutex** (`Local\HydraWinRecoveryJournal`). Task 01
  recorded this as this task's job: its spike had two processes collide on the journal and lose a
  write. `--restore-all` can legitimately run while the UI process is live, so every
  read-modify-write is serialised. `AbandonedMutexException` is treated as ownership — a crashed
  holder is precisely the case this journal exists for, and `JsonStore` writes atomically so
  whatever is on disk is a complete document. `WorkspaceStore` is untouched: `state.json` has one
  writer.
- **`NativeMethods.TryGetIdentity`** replaces separate `IsWindow` / `IsWindowVisible` /
  `GetProcessId` wrappers. Sonar's S4200 rejected the trivial forwarders, and folding them is the
  better shape anyway: every caller asking whether a handle is still a window also needs to know
  whose it is, since handles are recycled.
- **Structs are `WindowPlacement` / `Rect` / `Point`**, not the Win32 SCREAMING_CASE names, per
  S101. They are public because they cross the `IWindowApi` boundary.
- **Clean-exit restore is unconditional.** `SettingsModel.RestoreOnExit` exists (task 04) and
  defaults to `true`; task 08 owns wiring the toggle, so gating it here would half-implement that
  task. Behaviour is identical at the default.

### A bug this task uncovered in tasks 03/04 — and a correction to their records

While chasing a duplicate row in the harness I found that `MainViewModel.Start()` copied
`tracker.Windows` *after* `tracker.Start()`, which had already delivered a `WindowAppeared` event
for every window in its initial sweep — synchronously, because the captured
`SynchronizationContext` is the calling one. **Every window was listed twice.**

The consequence for the earlier records: **the tracked-window counts quoted in tasks 03 and 04 are
inflated 2×.** Task 03's "26 tracked" and the soak's `27, 28, 27…` series, and task 04's counts,
were all double the real figure. The real number on this desktop is ~14. Nothing else in those
tasks is affected — the tracker's own dictionary was always correct, which is why the filter,
diff, re-attach and persistence results all still stand; only the harness's display was wrong.
The counts in those two records should be read as doubled.

Fixed in two places:
- `MainViewModel.AddWindow` now keys on the handle and refuses to list one twice, which covers
  both the double-copy and a window that flickers away and back as a new instance (packaged
  Notepad does exactly this).
- `WindowSetDiff.Compute` now skips a handle seen twice within one sweep, so `Added` can never
  report the same window twice regardless of what enumeration returns. Covered by
  `AHandleAppearingTwiceInOneSweepIsReportedOnce`.

### Verification results

- `dotnet build HydraWin.sln` → **0 warnings, 0 errors**. One Sonar finding (S4200, the trivial
  `IsWindow` wrapper) fixed in code; no suppressions added.
- `dotnet test --solution HydraWin.sln` → **total: 106, failed: 0, succeeded: 106, skipped: 0**
  (28 new: 12 journal, 12 restore, 4 placement mapping).
- `dotnet format --verify-no-changes` → exit 0.
- The three `spikes/` projects still build clean and `hideshow rescue` still works.

### Crash-drill transcript

Run against a Notepad spawned for the purpose, then repeated on the fixed build against a
disposable terminal window (packaged Notepad kept exiting on its own mid-drill).

```
STEP 1  hide through the journal
        window        0x01021410 "Untitled - Notepad"  vis=n
        journal.json  1 entry, placement NormalLeft/Top/Right/Bottom = 300/250/1100/850

STEP 2  kill HydraWin with TerminateProcess (no clean shutdown)
        hydrawin alive: False
        journal still holds: 1 entry
        notepad still hidden: True

STEP 3  hydrawin.exe --restore-all
        exit code: 0
        stdout:    restored 1 window(s), dropped 0 stale entries
        notepad    vis=Y, back at its pre-hide rect
        journal    []

STEP 4  repeat 1-2, then launch with NO arguments
        notepad    vis=Y
        journal    []
        notice     "Recovered 1 window(s) from a previous session — 23:14:39"

STEP 5  hide, kill Notepad while hidden, kill HydraWin, --restore-all
        exit code: 0
        stdout:    restored 0 window(s), dropped 1 stale entry
        journal    []

RE-RUN on the fixed build (0x00571616 "HYDRAWIN-DRILL")
        hidden     vis=n, journal 1 entry with full identity + placement
        killed     unclean; still hidden
        plain launch (no arguments) → vis=Y at exactly (288,294)-(1474,912), journal []
        clean exit while hidden → restored, journal []
```

The invariant itself was checked directly: with the window hidden, `journal.json` is on disk
holding its entry. (A first attempt appeared to show the file missing — that was my measurement
racing, since `InvokePattern.Invoke` returns before the WPF handler runs. With the wait restored,
the file is there.)

### An incident worth recording

During the first drill attempt I selected the wrong window: my matcher used a substring, and
`"Notepad"` matched the user's **Notepad++**, which HydraWin then hid. It was restored within
seconds by the *Restore all* command, returning to its exact rect `(-1650,266)-(0,1261)` on the
negative-X second monitor — so the accident doubled as unplanned proof that the restore path is
pixel-exact on a real window on a secondary display. The drill switched to matching on the exact
window handle afterwards.

### A measurement note for later tasks

`GetWindowPlacement` values are reported in the *calling process's* coordinate space. On this
150%-DPI desktop, `hydrawin.exe` (system-DPI-aware WPF) recorded `NormalPosition` as
`300,250-1100,850` for the same window the DPI-unaware spike reported as `150,125-550,425` —
exactly 2×. This is consistent and harmless because only HydraWin writes and reads the journal,
but anything that compares journal placements against numbers from another process must account
for it.

### Files

New: `src/HydraWin.Core/Interop/Win32WindowApi.cs`;
`src/HydraWin.Core/Recovery/WindowPlacementDto.cs`, `RecoveryJournal.cs`, `RestoreService.cs`;
`tests/HydraWin.Core.Tests/FakeWindowApi.cs`, `RecoveryJournalTests.cs`, `RestoreServiceTests.cs`,
`WindowPlacementDtoTests.cs`.

Modified: `Interop/NativeMethods.cs` (placement struct, show/hide, `TryGetIdentity`),
`Interop/IWindowApi.cs` (filled in), `Recovery/JournalEntry.cs` (filled in, S2094 suppression
removed), `Persistence/HydraWinPaths.cs` (`JournalFile`),
`Tracking/WindowSetDiff.cs` (duplicate-handle guard),
`src/HydraWin.App/App.xaml.cs` (real `--restore-all`, startup recovery, clean-exit and
`SessionEnding` restore), `MainWindow.xaml(.cs)`, `ViewModels/MainViewModel.cs` (drill commands,
persistent recovery notice, duplicate guard),
`tests/HydraWin.Core.Tests/WindowSetDiffTests.cs`, and this file.

Deleted: none.
