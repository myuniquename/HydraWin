# HydraWin

HydraWin is a task workspace manager for Windows 11, written in C# / .NET 10 + WPF. The user works on
several tasks at once, each task being a group of windows (browser, Claude Code terminals,
VS Code, optionally MS Teams). HydraWin shows a table of tasks, lets the user drag live windows onto
tasks, and switches tasks by **fully hiding** every other task's windows (documented Win32
`ShowWindow`, *not* OS virtual desktops) and restoring the selected task's windows with their
placements. Background windows that want attention raise a badge on their task row.

## Gotchas

- **Every hide is journaled to disk *before* it happens.** The recovery journal
  (`%APPDATA%\HydraWin\journal.json`) is the contract that a crash can never permanently lose a
  window. Any code path that calls `ShowWindow(SW_HIDE)` without a flushed journal entry first is
  a bug, no matter what else it does right. `hydrawin.exe --restore-all` must always work, even when
  the UI cannot start.
- **No undocumented Windows APIs.** The OS virtual-desktop COM interfaces
  (`IVirtualDesktopManagerInternal` etc.) were evaluated and rejected — their GUIDs change with
  Windows feature updates. Do not reintroduce them. The rejection rationale lives in
  `tasks/initial_build/_plan.md` § *Investigation results* while that folder exists, and in
  `docs/` after promotion.
- **WinEvent/hook delegates must be kept alive.** A `SetWinEventHook` callback passed as a lambda
  gets garbage-collected and the hook dies silently. Store the delegate in a field with the same
  lifetime as the hook.
- **`SetForegroundWindow` only works when HydraWin is the foreground process** — which it is during
  a user-initiated switch. Do not add focus-stealing workarounds (`AttachThreadInput` tricks) for
  paths where the user didn't just click HydraWin.
- **Elevated processes' windows cannot be hidden from a non-elevated HydraWin** (UIPI). Since
  task 07 they are **kept out of the inventory entirely** rather than listed and marked: a window
  the app can never hide has no business being offered as something to put in a task. Detection is
  `NativeMethods.IsProcessElevated`, and being unable to query the token counts as elevated —
  guessing the other way would put an unmanageable window in front of the user. When HydraWin is
  itself elevated the clause does not apply and such windows are ordinary.
  `WindowAssignment.Unmanageable` stays as the runtime safety net for a hide that fails anyway.
- **Both notification channels reach hidden windows** — task 01 measured this, contradicting the
  original assumption that a missing taskbar button would suppress flashes. `HSHELL_FLASH` is
  delivered for `SW_HIDE`-hidden windows (confirmed for real Teams messages and for a hidden
  Windows Terminal bell), and `EVENT_OBJECT_NAMECHANGE` fires regardless of visibility. Teams
  never changes its window title, and it flashes only once per unread run, so its badge must be
  cleared by focus alone. Trust task 01's recorded results, not assumptions.
- **Claude Code notifies by flash, 61 seconds late; its title is for display only.** A Windows
  Terminal bell does raise `HSHELL_FLASH` given a valid `bellStyle` — `"all"`/`"audible"`/
  `"window"`/`"taskbar"`; **`"taskbarFlash"` is not valid and is silently ignored**, which is what
  made task 01's first two answers wrong. Claude Code rings that bell **~61 s after a session goes
  idle** (61.1 s across five sessions, consistent to 0.1 s). The user accepted that latency:
  **task 09 ships no Claude Code title rule** and badges it from the flash like any other app.
  The `<marker> <name>` title — spinner `◐ ◑ ◒ ◓` (`U+25D0`–`U+25D3`) while working, `✳`
  (`U+2733`) when idle — is still parsed, but only to show live progress in the overview
  (task 07 § F). Requires `bellStyle` to include `"taskbar"` and Claude Code's
  `preferredNotifChannel` to be `terminal_bell`.

## Style

- `net10.0-windows`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</...>`,
  `.editorconfig` at root, `dotnet format` clean before any completion report.
- **`SonarAnalyzer.CSharp` runs on every build of every project in `HydraWin.sln`, and warnings
  are errors — so its findings are build failures. Fix them.** Never suppress a rule, change a
  severity, or add `dotnet_diagnostic.*` entries to `.editorconfig` without the user's explicit
  permission; if a finding looks wrong, stop and ask rather than silence it. Every solution
  project references the analyzer — add it to any new one. `spikes/` deliberately does not have
  it (throwaway task-01 code). The repository's only suppressions are the temporary `#pragma
  warning disable S2094` / `S2326` pairs around task 02's empty placeholder types; each is
  deleted by the task that fills its type in.
- Respect `.editorconfig` for every file it covers, including `*.md` — it sets 2-space indents,
  a 100-column guideline, and disables trailing-whitespace trimming (Markdown uses a trailing
  two-space hard line break). Don't reformat against those rules.
- All P/Invoke lives in `src/HydraWin.Core/Interop/` behind small interfaces. ViewModels and views
  never call Win32 directly.
- MVVM via `CommunityToolkit.Mvvm`. Views bind; ViewModels orchestrate; Core does the work.
- Tests are xUnit in `tests/HydraWin.Core.Tests`, covering the pure logic (matching rules, journal,
  model, badge aggregation). Win32-dependent behaviour is verified by the manual scripts in each
  task file — record the observed results.

## Where to read

| Topic | Where |
| --- | --- |
| The build plan, decisions, and task ordering | `tasks/initial_build/_plan.md` |
| Individual work items (standalone-implementable) | `tasks/initial_build/NN_*.md` |
| Durable architecture docs | `docs/` — empty until findings are promoted (task 11) |

`docs/` holds timeless findings; `tasks/` holds progress. `docs/` never tracks task status.
Completed task folders are deleted after promotion, not archived.

## Shared knowledge

Conventions come from the user's SilverBullet space: `Agent/Godot/DocsAndTasks` defines the
`docs/` / `tasks/` structure used here (the Godot-specific parts — `.gdignore`, gdstyle, GUT,
godot-mcp — do not apply to this repository).

## Working style

- **Version control is the user's job.** Do not `git init`, do not run Perforce write commands.
  Every completion report ends with the list of new / modified / deleted files for the user to
  reconcile in one pass.
- Work tasks in `_plan.md` order unless the dependency notes say otherwise. Each task file
  restates the background it needs — do not assume sibling tasks were read.
- Fill in **Record on completion** honestly: what was actually done, how it differed from the
  plan and why, measured results, and the file list.
- Do not claim success without the actual command output (build, tests, or the manual script's
  observed results).
