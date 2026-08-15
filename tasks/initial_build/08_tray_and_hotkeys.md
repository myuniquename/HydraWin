# Task 08 — Tray icon, global hotkeys, single instance

Status: **not started**
Depends on: task 07 (UI shell — the tray reopens it; hotkeys drive SwitchEngine).

## Motivation

HydraWin is a daily companion, not a foreground app: it must live in the tray, switch tasks via
global hotkeys without being visible, never run twice, and always offer the panic restore.

## Background

Architecture recap: `SwitchEngine.SwitchTo(taskId)` / `ShowAllTasks()` exist in Core; tasks have
an `Order` used for hotkey numbering. The scaffold (task 02) already ships
`Hardcodet.NotifyIcon.Wpf`. `RegisterHotKey` requires an HWND and its `WM_HOTKEY` (0x0312)
arrives in that window's message loop — use an `HwndSource` hook on the (possibly hidden) main
window. Focus caveat from the repo gotchas: `SetForegroundWindow` from a *hotkey-initiated*
switch is legitimate — the foreground-lock rules grant the foreground process's input state to
the hotkey registrant on `WM_HOTKEY`; if focus is still denied in practice, fall back to showing
the switched-to window without stealing focus (`SW_SHOWNA`) rather than adding input hacks.

## Work

### A. Tray icon
- Always-present `TaskbarIcon`: left-click toggles the main window (restore + activate / hide to
  tray); closing the main window minimizes to tray instead of exiting (setting-controlled later,
  default on).
- Context menu: task list (name + window count, click = switch, active task checked),
  separator, *Show all windows* (`ShowAllTasks`), *Open HydraWin*, separator, *Restore all &
  exit* and *Exit* (both run the clean-exit restore from task 05; the distinction is *Exit*
  keeps hidden windows hidden **only if** the user disabled restore-on-exit in settings —
  default behaviour restores everything, safety first).
- Tray icon asset: simple multi-head glyph placeholder `.ico` generated for now (record how).

### B. Global hotkeys (`Interop` + App service)
- Add `RegisterHotKey`/`UnregisterHotKey` (`MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4,
  MOD_NOREPEAT = 0x4000`) to `NativeMethods`.
- Defaults: `Ctrl+Alt+1..9` → switch to task with `Order` 1..9; `Ctrl+Alt+0` → show all;
  `Ctrl+Alt+Shift+R` → **panic restore** (journal `RestoreAll`, works even if the UI thread is
  wedged — run it on the hotkey message directly, minimal code path); `Ctrl+Alt+H` → toggle
  main window.
- Registration failures (collision with another app) must not crash: log, surface once in the
  status bar, continue without that binding.
- Hotkey map lives in `SettingsModel` (task 04) as `vk`+`modifiers` pairs; no UI editor yet
  (task 10) but hand-editing `state.json` must work.

### C. Single instance
Named mutex (`Local\HydraWinSingleton`). Second normal launch: signal the first instance to show
its window (named pipe or `EVENT` + `WM_COPYDATA` — pick one, note it), then exit 0.
**Exception:** `--restore-all` bypasses the mutex entirely — it must work while a wedged first
instance still holds it (task 05 built it standalone; keep it that way).

### D. Startup
Optional launch-at-login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value
(setting, default off; written/removed when toggled — the only registry touch in the project).

## Verification

1. Launch → tray icon present. Close main window → app stays in tray; left-click → window back.
2. With the window hidden: `Ctrl+Alt+1` / `Ctrl+Alt+2` switch tasks (visible windows change);
   focus lands on the task's window or, if focus was denied, the window is shown without focus —
   record which behaviour was observed.
3. `Ctrl+Alt+Shift+R` with a task hidden → everything visible, journal empty.
4. Tray menu switch + *Show all* work; *Exit* restores hidden windows (default) and the journal
   file is empty afterwards.
5. Second `hydrawin.exe` launch → first instance's window appears, second process exits;
   `hydrawin.exe --restore-all` runs fine while the first instance is running.
6. Hotkey collision test: pre-register `Ctrl+Alt+1` in another tool (or a throwaway script) →
   HydraWin starts, logs the failure, other hotkeys still work.
7. `dotnet build` / `dotnet test` clean; totals pasted.

## Record on completion

*(what was done, deviations and why, the IPC mechanism chosen for single-instance, focus
behaviour observed in step 2, and the list of new / modified / deleted files)*
