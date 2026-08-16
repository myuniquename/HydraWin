# Workspaces

A **task** is a named group of windows. Switching to one hides every other task's windows outright
and restores this one's where they were. **`WindowTracker`** keeps the live inventory of top-level
windows, **`WorkspaceService`** owns the task model and its re-attach rules, **`SwitchEngine`**
performs a switch, and **`RecoveryJournal`** + **`RestoreService`** make sure a crash can never
leave a window hidden with nothing to put it back. This folder is the canonical documentation for
HydraWin's window inventory, task model, switching and crash recovery.

| Doc | Read it for |
| --- | --- |
| [architecture.md](architecture.md) | Which windows are managed and why, the journal-before-hide invariant, the switch algorithm, per-application behaviour, why OS virtual desktops were rejected |
| [how_to.md](how_to.md) | Running the crash drill, recovering when the UI will not start, hand-editing the model, teaching HydraWin about a stubborn application |
| [reference.md](reference.md) | `state.json` and `journal.json` schemas, the `--restore-all` command line, hotkey defaults, filter verdicts, file locations |

Related: [../notifications/README.md](../notifications/README.md) for how a hidden window asks for
attention · [../ui/README.md](../ui/README.md) for the shell that drives all of this.

## What it does

HydraWin manages *other applications'* windows. Every window on the desktop is offered to a filter;
the ones that survive form an inventory the user can drag onto tasks. Assigning a window records a
durable **re-attach rule** — a process image name plus a title pattern — so the window rejoins its
task when it, or HydraWin, is restarted; window handles mean nothing across runs.

Switching to a task hides the windows of every *other* task with `ShowWindow(SW_HIDE)` and restores
this task's with `SetWindowPlacement` + `SW_SHOW`. Hidden means gone: no taskbar button, no
Alt-Tab. That is the point, and it is also the risk, because a window nobody can see is a window
the user cannot recover by hand. Every hide is therefore written to a journal on disk *first*, so
that whatever happens next — a crash, a power cut, a kill from Task Manager — there is a record of
what to put back and where.

Windows the user has not assigned to any task are never touched. That is structural rather than a
rule anyone has to remember: the switch plan is computed from task assignments, so an unassigned
window is not in it.

## Component map

```
                    ┌──────────────────┐
   desktop ────────▶│  WindowTracker   │  WinEvent hooks + a 2 s reconciliation sweep
   (all top-level)  │   WindowFilter   │  pure: which windows are ours to manage
                    └────────┬─────────┘
                             │ appeared / disappeared / title / foreground
                             ▼
                    ┌──────────────────┐        ┌───────────────────┐
                    │ WorkspaceService │◀──────▶│  WorkspaceStore   │  state.json
                    │  tasks + rules   │        │  (debounced save) │  (preferences)
                    └────────┬─────────┘        └───────────────────┘
                             │ model
                             ▼
                    ┌──────────────────┐
                    │   SwitchPlan     │  pure: who hides, who shows
                    └────────┬─────────┘
                             ▼
                    ┌──────────────────┐        ┌───────────────────┐
                    │   SwitchEngine   │───1───▶│  RecoveryJournal  │  journal.json
                    │                  │        │  (mutex-guarded,  │  (crash safety)
                    │                  │        │   never debounced)│
                    │                  │        └─────────┬─────────┘
                    │                  │───2───▶ ShowWindow(SW_HIDE)  ◀── only after 1 returns
                    │                  │                  │
                    │                  │◀──────────────────┘
                    │                  │───────▶│  RestoreService   │  identity check, then show
                    └──────────────────┘        └───────────────────┘
```

## Key files

| Purpose | File |
| --- | --- |
| Which windows are managed, as a pure decision | `src/HydraWin.Core/Tracking/WindowFilter.cs` |
| The reason a window was rejected | `src/HydraWin.Core/Tracking/TrackableVerdict.cs` |
| Live inventory: hooks, sweep, events | `src/HydraWin.Core/Tracking/WindowTracker.cs` |
| Task and assignment model | `src/HydraWin.Core/Workspaces/HydraWinTask.cs`, `WindowAssignment.cs` |
| Recognising a window again after a restart | `src/HydraWin.Core/Workspaces/ReattachRule.cs`, `RuleMatcher.cs` |
| Owning the model, the only writer of `state.json` | `src/HydraWin.Core/Workspaces/WorkspaceService.cs` |
| Who hides and who shows, as a pure computation | `src/HydraWin.Core/Workspaces/SwitchPlan.cs` |
| Performing a switch, in the order that keeps the invariant | `src/HydraWin.Core/Workspaces/SwitchEngine.cs` |
| The write-ahead record of every hidden window | `src/HydraWin.Core/Recovery/RecoveryJournal.cs` |
| Putting windows back, with identity validation | `src/HydraWin.Core/Recovery/RestoreService.cs` |
| Persistence with atomic writes and corrupt-file quarantine | `src/HydraWin.Core/Persistence/JsonStore.cs`, `WorkspaceStore.cs` |
| Every P/Invoke in the project | `src/HydraWin.Core/Interop/NativeMethods.cs` |
