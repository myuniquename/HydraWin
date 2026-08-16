# Task 08 — Tray icon, global hotkeys, single instance

Status: **done** (2026-08-16) — accepted by the user
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
~~Optional launch-at-login via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.~~
**Dropped from the project** (user's instruction, 2026-08-16) — first deferred to task 10, then cut
outright rather than built there. HydraWin touches no registry at all.

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

### Deviations, and why

- **The hotkeys own a thread, not the main window.** § Background suggests an `HwndSource` hook on
  the main window, but § B also requires the panic restore to work *when the UI thread is wedged* —
  and `WM_HOTKEY` is delivered to the message queue of whichever thread registered the hotkey, so
  those two cannot both be true. Agreed with the user: a dedicated background thread registers the
  hotkeys and runs the loop. The panic restore executes **inline on that thread** (the journal is
  mutex-guarded, the restore is pure Win32, so both are safe there); every other action marshals to
  the dispatcher, because the switch engine and view models are not thread-safe.
- **No window class was needed.** `RegisterHotKey` accepts a **null** window handle, in which case
  `WM_HOTKEY` goes to the *calling thread's* queue rather than to a window. That removed the window
  class, the `WndProc` and the delegate-lifetime hazard that comes with them; the thread is a
  `GetMessage` loop and nothing else.
- **Part D (launch at login) was deferred to task 10 and then dropped entirely**, both on the
  user's instruction (the second on 2026-08-16). The project touches no registry.
- **Key names are resolved in Core, not by WPF's `KeyInterop`.** `HydraWin.Core` has no WPF
  reference, and a small table there keeps the parsing unit-testable while letting `state.json` say
  `"1"` and `"R"` instead of WPF's `"D1"` — which matters because § B requires hand-editing to work.

### Single-instance mechanism

A named mutex `Local\HydraWinSingleton` plus a named `EventWaitHandle` `Local\HydraWinShowWindow`.
The first instance owns the mutex and parks a background thread on the event; a second launch finds
the mutex taken, sets the event, and exits 0. **An event rather than a pipe or `WM_COPYDATA`
because the second instance has nothing to say** — "show yourself" is the entire payload — so there
is no server loop, no message pump and no serialization. `--restore-all` returns before any of it,
as § C requires.

### Focus behaviour observed (§ Verification step 2)

**Focus landed on the task's window.** After `Ctrl+Alt+1` with the manager window hidden,
`GetForegroundWindow` reported `HW-T1 - Google Chrome`; after `Ctrl+Alt+2`, `HW-T2 - Google Chrome`.
No fallback to `SW_SHOWNA` was needed. This is the behaviour the repo gotcha predicted: the
foreground-lock rules grant the input state to the hotkey registrant on `WM_HOTKEY`.

This also exercises the `focusTarget` flag added during task 07's feedback: a click inside HydraWin
keeps the keyboard, a hotkey hands it over. Opposite intents, one switch engine.

### The icon, and how it was made

`Assets/hydrawin.ico`, generated from `Assets/hydrawin.svg` by `scratchpad/make-ico.ps1`: headless
Chrome renders the SVG through an HTML wrapper at 16/20/24/32/48/64/128/256, and the PNGs are packed
into a multi-image ICO (Vista and later accept PNG payloads at every size, so no BMP/AND-mask
encoding is needed).

**The first attempt shipped a broken icon and it took a screenshot to notice.** In PowerShell,
`--window-size=$size,$size` is parsed as an *array* — the comma is an operator — so Chrome never saw
the flag and every render came out at its default 754×480. The `.ico` was structurally valid and its
directory claimed 16×16, 20×20 and so on, but every entry held the same oversized image, and Windows
drew the taskbar icon as a smudge. The argument is now quoted, and the script **verifies each render
is the size it asked for** rather than trusting the flag took.

### Verified (my smoke test, against throwaway windows)

| Step | Observed |
| --- | --- |
| 1 | Tray icon present (in the Windows 11 overflow flyout, where new icons start). Closing the window left the process running; `Ctrl+Alt+H` brought it back |
| 2 | With the window hidden, `Ctrl+Alt+1` → HW-T1 visible, HW-T2 hidden; `Ctrl+Alt+2` → the mirror image. **Focus landed on the task's window both times** |
| 3 | `Ctrl+Alt+Shift+R` with a window hidden → everything visible, journal `[]` |
| 4 | Tray menu showed *Alpha (1 win)* checked as active, *Beta (1 win)*, and every specified item. *Show all windows* un-hid the hidden window and emptied the journal. *Exit* restored the hidden window and left the journal `[]` |
| 5 | Second launch surfaced the first instance and exited — one process id throughout. `hydrawin.exe --restore-all` ran fine while the first instance held the mutex |
| 6 | With a throwaway process holding `Ctrl+Alt+1`, HydraWin started and reported *"1 hotkey(s) unavailable — Control+Alt+1 (switch to task 1) is already taken by another application"*; `Ctrl+Alt+2` still switched, and `Ctrl+Alt+1` correctly did nothing |
| 7 | Build 0 warnings / 0 errors; **198/198** tests; `dotnet format` clean |

### An unrelated bug this task exposed

`state.json` was being written with the default JSON encoder, which escapes anything outside a
conservative ASCII set. Task 08's `"Control+Alt"` came out as `"Control+Alt"`, and window
titles in Cyrillic — which this desktop has — were already being written as walls of `\uXXXX`. The
file is meant to be hand-edited, so `JsonStoreOptions` now uses `UnsafeRelaxedJsonEscaping`
("unsafe" means unsafe to embed in HTML; this is a file on disk). Caught by the round-trip test,
which asserts against what actually lands on disk rather than against an in-memory string.

### Build, tests, format

- `dotnet build HydraWin.sln` — **0 warnings, 0 errors**.
- `dotnet test --solution HydraWin.sln` — **198/198 passed** (175 before; 23 new, all for
  `HotkeyBinding`: key and modifier resolution, unreadable entries refused rather than thrown, the
  seeded defaults, that no two defaults collide, and the on-disk round trip).
- `dotnet format --verify-no-changes` — exit 0.

### Files

**New** — `src/HydraWin.Core/Interop/IHotkeyApi.cs`, `Win32HotkeyApi.cs`;
`src/HydraWin.Core/Workspaces/HotkeyBinding.cs`; `src/HydraWin.App/Services/HotkeyService.cs`,
`TrayIcon.cs`, `SingleInstance.cs`; `src/HydraWin.App/Assets/hydrawin.ico`;
`tests/HydraWin.Core.Tests/HotkeyBindingTests.cs`.

**Modified** — `src/HydraWin.Core/Interop/NativeMethods.cs`,
`src/HydraWin.Core/Persistence/JsonStore.cs`, `src/HydraWin.Core/Workspaces/SettingsModel.cs`,
`src/HydraWin.App/App.xaml.cs`, `src/HydraWin.App/MainWindow.xaml` + `.xaml.cs`,
`src/HydraWin.App/ViewModels/MainViewModel.cs`, `src/HydraWin.App/HydraWin.App.csproj`,
`tasks/initial_build/08_tray_and_hotkeys.md`.

**Deleted** — none.

### User walkthrough

**Accepted by the user on 2026-08-16 without a separate walkthrough being recorded.** The evidence
for steps 1–6 is the implementer's smoke test in the table above, which covered all of them against
throwaway windows. No user-observed results exist for this task, and none should be inferred.
