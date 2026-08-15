# Task 03 — WindowTracker

Status: **not started**
Depends on: task 02 (solution scaffold — projects and `Interop/` location exist).

## Motivation

Everything HydraWin does starts from an accurate, live inventory of the user's top-level
application windows: the unassigned pane lists them, assignments bind to them, the switch engine
hides/shows them, the notification hub attributes signals to them. This task builds that
inventory as a self-contained, observable service.

## Background

Architecture recap: `HydraWin.Core` hosts a `WindowTracker` service that maintains the set of
*trackable* windows and raises change events; `HydraWin.App` (WPF) binds to it. All P/Invoke lives
in `src/HydraWin.Core/Interop/NativeMethods.cs`. Windows that HydraWin itself has hidden must **stay**
in the inventory (they are still part of a task); that is why visibility alone cannot gate
membership.

A window is *trackable* iff all of:
- `GetWindowTextLength(hwnd) > 0`
- `IsWindowVisible(hwnd)` **or** hwnd is in the HydraWin-hidden set (injected; see interface below)
- extended style has no `WS_EX_TOOLWINDOW` (`GetWindowLongPtr(hwnd, GWL_EXSTYLE = -20)`,
  `WS_EX_TOOLWINDOW = 0x80`)
- unowned: `GetWindow(hwnd, GW_OWNER = 4) == 0`
- not DWM-cloaked: `DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED = 14, out int cloaked, 4)` returns
  0 and `cloaked == 0` (filters UWP ghost windows; note some packaged apps report cloaked while
  genuinely hidden — acceptable, the HydraWin-hidden set keeps *our* hidden windows tracked)
- not a HydraWin process window (`GetWindowThreadProcessId` pid ≠ current pid)

## Work

### A. Interop additions (`Interop/NativeMethods.cs`)
`EnumWindows`, `GetWindowTextW` + `GetWindowTextLengthW`, `IsWindowVisible`,
`GetWindowLongPtrW`, `GetWindow`, `GetWindowThreadProcessId`, `DwmGetWindowAttribute` (dwmapi),
`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION = 0x1000)` + `QueryFullProcessImageNameW` +
`CloseHandle`, `SetWinEventHook`/`UnhookWinEvent` and the `WinEventDelegate`. Constants used
below: `EVENT_SYSTEM_FOREGROUND = 0x0003`, `EVENT_OBJECT_DESTROY = 0x8001`,
`EVENT_OBJECT_SHOW = 0x8002`, `EVENT_OBJECT_HIDE = 0x8003`, `EVENT_OBJECT_NAMECHANGE = 0x800C`,
`WINEVENT_OUTOFCONTEXT = 0`, `WINEVENT_SKIPOWNPROCESS = 2`, `OBJID_WINDOW = 0`,
`CHILDID_SELF = 0`.

### B. Model (`Tracking/`)
`TrackedWindow { IntPtr Hwnd; int Pid; string ProcessPath; string Title; bool IsHydraWinHidden; }`
(mutable record-style class; `ProcessPath` may be empty when `OpenProcess` fails — that marks an
elevated/protected process, surfaced later by task 10, not an error here).

### C. `WindowTracker` service
- Constructor takes an `IHiddenWindowSet` (a read-only view; the switch engine implements it in
  task 06 — until then a stub returning empty).
- `Start()`: initial `EnumWindows` sweep applying the trackable filter; then registers WinEvent
  hooks for the five events above, one hook per event or one ranged hook, flags
  `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`. **Must be called on a thread with a message
  pump** (the WPF dispatcher thread); document this on the method. **Keep the delegate in a
  field** — a collected delegate silently kills the hook (repo gotcha).
- Hook handling (filter to `idObject == OBJID_WINDOW && idChild == CHILDID_SELF`):
  SHOW/DESTROY/HIDE → add/remove/re-evaluate against the filter (a HIDE for a window *not* in
  the HydraWin-hidden set means the app hid/closed it → drop it); NAMECHANGE → update `Title`,
  re-evaluate trackability (windows often gain their real title after creation), and re-raise;
  FOREGROUND → remember as `LastForegroundWindow` (consumed by task 06 focus restore).
- Reconciliation sweep every ~2 s (`DispatcherTimer` owned by the caller or a plain timer
  marshalling through a `SynchronizationContext`): full re-enumeration diffed against current
  state, fixing anything the hooks missed. Hooks are the fast path, the sweep is the truth.
- Events: `WindowAppeared`, `WindowDisappeared`, `WindowTitleChanged(old, new)`,
  `ForegroundChanged` — all raised on the caller's `SynchronizationContext` so WPF can bind
  without marshalling. Snapshot property: `IReadOnlyCollection<TrackedWindow> Windows`.
- `Stop()`/`Dispose()`: unhook everything.

### D. Debug harness
Temporary listing in the empty `MainWindow` from task 02: an `ItemsControl` bound to the tracked
set showing `ProcessPath` filename + title, live-updating. (Task 07 replaces this; keeping it
throwaway is fine, but it must exist for verification.)

### E. Unit tests
The filter predicate and diff logic extracted pure (take window property structs, no Win32):
tests for each filter clause and for the reconciliation diff (added/removed/title-changed).

## Verification

- `dotnet build` / `dotnet test` clean; paste test totals.
- Run the app, then, watching the harness list: open Notepad → appears within ~1 s with correct
  title and path; retitle it (type, title gains `*`) → title updates; close it → disappears.
  Open a second Edge/Chrome window → both windows listed separately with the same process path.
  Confirm absent: tooltip/popup tool windows, hidden UWP ghosts (no title-less or cloaked
  entries), HydraWin itself.
- Kill the reconciliation timer in the debugger and repeat the Notepad open/close to confirm
  hooks alone track it; re-enable and confirm no duplicates accumulate over 5 minutes of normal
  desktop use (count stays stable for a stable set of windows).

## Record on completion

*(what was done, deviations and why, observed edge cases — especially any app whose windows are
missed or over-included — test totals, and the list of new / modified / deleted files)*
