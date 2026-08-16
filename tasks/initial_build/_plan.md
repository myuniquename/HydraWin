# HydraWin initial build (plan index)

HydraWin is a task workspace manager for Windows 11. The user runs several tasks concurrently; each
task is a group of windows — one or more browser windows, one or more Claude Code terminal
windows, a VS Code window, optionally an MS Teams window. HydraWin presents a table of tasks, lets
the user drag live windows onto task rows, and switches tasks with one click: every other task's
windows are **fully hidden** (gone from taskbar and Alt-Tab) and the selected task's windows are
restored with their exact placements and last focus. When a hidden window wants attention (Teams
message, Claude Code session finished), its task row shows a notification badge; clicking it
switches to that task and focuses the window.

**Scope of this folder:** everything from empty repository to a daily-usable v1 with
notifications and crash-safe recovery. **Explicitly out:** OS virtual-desktop integration,
UI Automation badge reading, direct Claude Code hook/named-pipe integration, a compact
always-on-top strip mode, running HydraWin elevated. Each is noted where it would attach.

The tasks are split because they fail independently: the spike (01) can invalidate assumptions
without touching code structure; the tracker (03), persistence (04), journal (05) and switch
engine (06) are separately testable engines; the UI tasks (07–09) can each be cut back without
undoing the core. If the flash-hook half of 09 is a dead end, the title-watcher half still ships.

## Investigation results / Decision

- **Real Windows 11 virtual desktops: REJECTED.** Programmatic control (create/switch desktops,
  move other apps' windows) requires undocumented COM interfaces (`IVirtualDesktopManagerInternal`
  and friends) whose GUIDs Microsoft changes across Windows releases — wrapper libraries ship
  separate builds per Windows version (MScholtes/VirtualDesktop carries `virtualdesktop11.cs`,
  `virtualdesktop11-24h2.cs`, …; Slions.VirtualDesktop tracks builds on NuGet). The only
  documented API, `IVirtualDesktopManager`, cannot switch desktops and can only move windows of
  the calling process. A tool the user depends on daily must not break on Windows update.
  **Decision: app-managed workspaces** — HydraWin hides/shows windows itself with documented Win32
  APIs. Same user-visible effect, plus exact placement restore, at the cost of a mandatory
  crash-recovery journal (task 05).
- **Minimize-only instead of hide: REJECTED.** Minimized windows stay on the taskbar and in
  Alt-Tab, defeating the isolation purpose. Full hide chosen, made safe by the write-ahead
  journal + `--restore-all` + startup recovery.
- **Stack: C# / .NET 10 + WPF. Chosen** for first-class P/Invoke, WinEvent/shell hooks, mature
  in-app drag-and-drop, and tray support, packaged as one self-contained exe. WinUI 3 rejected
  (packaging/runtime friction for a tray-style utility), Tauri/Rust and Electron rejected (all
  window-management guts would be hand-written interop anyway; Electron adds the heaviest
  footprint).
- **Notifications: shell flash hook + title-change watcher.** `HSHELL_FLASH` via
  `RegisterShellHookWindow` catches "app wants attention" generically. **Task 01 settled the open
  question: flashes from `SW_HIDE`-hidden windows *are* delivered** — the message arrives with
  `IsWindowVisible == false`, confirmed both by a controlled `FlashWindowEx` A/B on one window and
  by an unrelated third-party app flashing while hidden. The earlier suspicion that a missing
  taskbar button would suppress them was wrong. `HSHELL_RUDEAPPACTIVATED` never fired at all and
  is not used. **Claude Code arrives on both channels, at very different speeds — and the decision
  is to use the flash.** A re-test on 2026-08-15 showed a Windows Terminal bell *does* raise
  `HSHELL_FLASH`, including while the window is `SW_HIDE`-hidden; task 01's original negative used
  the invalid `bellStyle` value `"taskbarFlash"` and so tested nothing at all. Claude Code rings
  that bell **61.1 s after the session goes idle** (five sessions, consistent to 0.1 s), whereas
  the `✳` title marker appears immediately. **The user accepted the minute of latency**: task 09
  ships no Claude Code title rule and badges terminals from the flash like any other app, keeping
  one mechanism and no per-app regexes. The title is still parsed, but only so the overview can
  show a session's live progress (task 07 § F).
  UI Automation badge reading rejected for v1 (brittle against Teams UI updates, CPU-heavier).
- **Teams, measured (task 01).** With real messages from a second account: Teams flashes
  identically whether its window is visible, minimized, or `SW_HIDE`-hidden, so the headline
  promise ("a hidden Teams chat gets a message → badge") holds with a plain hide and needs no
  per-app policy. Teams **never changes its window title** in any state, so it is a flash-only app
  and the assumed `^\((\d+)\)` title rule is deleted. It flashes **once per unread run** — the
  first message into a read conversation, then silence until the user reads it — which means a
  Teams badge must only ever be cleared by focusing the window, never by a task switch.

## Task index

| # | File | What it settles |
| --- | --- | --- |
| 01 | `01_spike_win32_assumptions.md` | Whether the three risky Win32 assumptions hold; recorded facts the later tasks build on |
| 02 | `02_solution_scaffold.md` | Solution layout, projects, packages, CI-able build |
| 03 | `03_window_tracker.md` | Live inventory of top-level windows (enumeration, hooks, filtering) |
| 04 | `04_workspace_model_persistence.md` | Task/assignment model, re-attach rules, atomic JSON persistence |
| 05 | `05_recovery_journal.md` | The crash-safety contract: write-ahead journal, `--restore-all`, startup recovery |
| 06 | `06_switch_engine.md` | Hide/show switching with placement + focus restore |
| 07 | `07_ui_shell.md` | Main window: task table, unassigned pane, drag-and-drop assignment |
| 08 | `08_tray_and_hotkeys.md` | Tray icon, global hotkeys, single-instance behaviour |
| 09 | `09_notifications.md` | Flash hook + title watcher → per-task badges, click-to-jump |
| 10 | `10_hardening_polish.md` | Elevated-window handling, pinned/global windows, rule editing, settings |
| 11 | `11_promote_docs.md` | Writing `docs/`, deleting this folder |

## Ordering

- **01 first** — its recorded results are inputs to 06 (hide/show fidelity per app) and 09
  (whether flashes from hidden windows are observable). If a spike result contradicts this plan,
  update `_plan.md` and the affected task file *before* implementing them.
- **02 → 03/04 → 05 → 06 → 07 → 08/09 → 10 → 11.** Real dependencies: 05 needs 04's persistence
  helpers; 06 needs 03 (window inventory) and 05 (journal-before-hide invariant); 07 needs 03, 04
  and 06; 08 and 09 need 07 (they surface through its UI) but not each other; 10 needs everything
  before it. 03 and 04 are independent of each other and can be done in either order.
- **What still ships if a task dies:** without 09, HydraWin is a usable switcher with no badges;
  without 08, everything works from the main window; 01–07 are the non-negotiable core.

## Shared ground rules

- **Journal before hide.** No code path may call `ShowWindow(SW_HIDE)` on a foreign window
  before the corresponding journal entry is flushed to disk (task 05 defines the API). This is
  the project's one invariant; treat violations as release blockers.
- **Documented Win32 only.** No undocumented COM interfaces, no reading other processes' memory,
  no DLL injection. All hooks are `WINEVENT_OUTOFCONTEXT`.
- **All P/Invoke in `src/HydraWin.Core/Interop/`** behind interfaces; nothing above Core touches
  Win32 directly.
- **HydraWin never closes, moves, or resizes a window it did not hide**, and never touches windows
  the user has not assigned to a task (unassigned windows stay visible in every task).
- **Version control is the user's job.** No `git init`, no Perforce write commands. Every
  completion note lists new / modified / deleted files.
- `net10.0-windows`, nullable enabled, warnings as errors, `dotnet format` clean.

## Working rules

- Build: `dotnet build HydraWin.sln` from the repository root — must be warning-free.
- Unit tests: `dotnet test --solution HydraWin.sln` — report the actual pass/fail totals, not the
  exit code. (The `--solution` flag is required: `global.json` puts `dotnet test` into
  Microsoft.Testing.Platform mode, because the .NET 10 SDK no longer runs xunit v3 under VSTest.)
- Run: `dotnet run --project src/HydraWin.App` (or the built `hydrawin.exe`).
- Panic restore during development: `hydrawin.exe --restore-all` (works from task 05 onward);
  until then, spike/test programs must re-show every window they hide before exiting, even on
  Ctrl+C (`Console.CancelKeyPress`) and on unhandled exceptions.
- **From task 07 onward the acceptance walkthrough is the user's to run**, on their own desktop and
  their own windows — the UI tasks are about how the thing feels to use, which cannot be delegated.
  The implementer still smoke-tests the same steps first against throwaway windows and resets
  `%APPDATA%\HydraWin` before handing over, so the user meets a clean app rather than the rough
  edges. The user's observed results are what go in **Record on completion**.
- Manual verification scripts in each task name the apps to test with: Windows Terminal
  (with `"bellStyle"` including `"taskbar"` — note `"taskbarFlash"` is **not** a valid value and
  is silently ignored), VS Code, a Chromium browser (Edge or Chrome,
  multiple windows of one process), and new Teams if installed. Record observed results in the
  task file's **Record on completion**.
- Screenshots and throwaway spike programs live inside this folder (`screenshots/`,
  `reference/`); spike code additionally under the repository's `spikes/` as task 01 specifies.
