# Task 05 — RecoveryJournal: the crash-safety contract

Status: **not started**
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

*(what was done, deviations and why, test totals, the crash-drill transcript, and the list of
new / modified / deleted files)*
