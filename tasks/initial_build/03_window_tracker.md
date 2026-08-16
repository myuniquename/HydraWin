# Task 03 — WindowTracker

Status: **done** (2026-08-15, accepted 2026-08-16)
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

> **Correction (task 05).** Every tracked-window *count* below is inflated 2×. The harness's
> `MainViewModel.Start()` copied `tracker.Windows` after `tracker.Start()` had already raised a
> `WindowAppeared` for each of them, so the list held every window twice. The real figure on this
> desktop is ~14, not the 26–29 quoted here, and the soak series should be halved. Everything
> else stands: the tracker's own dictionary was always correct, so the filter, diff, hooks-only
> and stability results are unaffected. Fixed in task 05.

Built the interop set, the pure filter/diff core, `WindowTracker`, and the debug harness. All
verification below was run against the live desktop on 2026-08-15; numbers are the observed ones.

### Design notes and deviations

- **The filter returns a reason, not a bool.** `WindowFilter.Evaluate` yields a
  `TrackableVerdict` (`Trackable`, `NoTitle`, `NotVisible`, `ToolWindow`, `Owned`, `Cloaked`,
  `OwnProcess`). One design serves two needs: each clause gets its own unit test, and the harness
  can show *why* a window was excluded — which is what makes the task's "confirm absent" step
  evidence rather than an eyeball check. `IsTrackable` remains for callers that want yes/no.
- **`WindowFacts` carries the title string, not its length.** The inventory needs the title
  anyway, so fetching it once halves the Win32 round trips per window per sweep and removes a
  redundant re-fetch in the hook path.
- **Interop exposes coarse operations, not a 1:1 mirror of user32.** Sonar's S4200 rejects both
  non-private externs and trivial forwarding wrappers, which pushed the boundary in a good
  direction: `DescribeWindow` reads everything the filter needs in one pass, and `UnhookAll`
  releases the whole hook list. Four declarations stay on `[DllImport]` — `EnumWindows` and
  `SetWinEventHook` take delegates the source generator cannot marshal, and the two string-buffer
  calls would otherwise force assembly-wide `DisableRuntimeMarshalling` or an `unsafe` block
  (which trips S6640).
- **`IWindowApi` is still empty.** The tracker calls the Core-internal `NativeMethods` wrappers
  directly, which the task's test requirement allows (only the filter and diff must be pure).
  The seam stays reserved for tasks 05/06, whose tests need a scripted fake to prove the
  journal-before-hide ordering.
- **The reconciliation sweep runs on the timer thread**, not the UI thread: it probes every
  top-level window on the desktop (404 of them here), and only the resulting events are marshalled
  through the captured `SynchronizationContext`. An `Interlocked` flag stops a slow sweep
  overlapping the next tick.
- **Harness extras beyond the task**, agreed with the user: a rejection pane with the failing
  clause, a per-clause count summary, and a checkbox toggling the sweep. The toggle replaces the
  task's "kill the reconciliation timer in the debugger" step — there is no debugger in this
  workflow, and a toggle makes the hooks-only test reproducible.

### Two bugs found before they shipped

- **Cloaked + HydraWin-hidden must stay tracked.** Writing the filter tests exposed that my first
  implementation dropped any cloaked window. Task 01 measured that packaged apps report cloaked
  *because* HydraWin hid them, so the hidden set now exempts cloaking exactly as it does
  visibility. Without this, hiding Teams would silently delete it from the inventory and the
  switch engine could never restore it. Covered by
  `AWindowHydraWinHidIsKeptEvenWhenTheSystemReportsItCloaked`.
- **A window-appeared race**, introduced by moving the sweep off the UI thread: the hook thread
  and the sweep thread could both find a window absent and both insert it, raising
  `WindowAppeared` twice and duplicating it in the UI. Fixed by making the insert a `TryAdd` under
  the lock, with only the thread that actually inserted allowed to raise.

### A correction to this task's own text

Section B says `ProcessPath` "may be empty when `OpenProcess` fails — that marks an
elevated/protected process". Task 01 measured that
`OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` **succeeds** against elevated processes from a
normal one, which is why the spike's elevation check had to use `OpenProcessToken` +
`GetTokenInformation(TokenElevation)`. Confirmed again here: elevated Task Manager is tracked
*with* a readable `ProcessPath`. An empty `ProcessPath` therefore means a genuinely protected
process, not elevation. Task 10 owns elevated-window handling and should use the token check.

### Verification results

- `dotnet build HydraWin.sln` → **0 warnings, 0 errors** (Sonar findings are errors). Seven Sonar
  findings arose during development — S4200 ×3, S1450, S3267 ×2, SYSLIB1051 ×2, S6640 — and every
  one was fixed in code. **No suppressions were added.** Of note: S1450 wanted the WinEvent
  delegate demoted to a local, which is precisely the collected-delegate bug CLAUDE.md warns
  about; it was resolved honestly by making it a `readonly` field assigned in the constructor,
  which both satisfies the rule and shortens no lifetimes.
- `dotnet test --solution HydraWin.sln` → **total: 21, failed: 0, succeeded: 21, skipped: 0**
  (11 filter-clause tests, 8 diff tests, 2 scaffold).
- `dotnet format --verify-no-changes` → exit 0.
- **Live desktop**, read out of the running app through UI Automation:
  - Clause summary, proving every clause fires and HydraWin excludes itself:
    `NoTitle=163   NotVisible=226   ToolWindow=1   Cloaked=5   OwnProcess=9`.
  - Notepad: opened → tracked count 26 → **27**; closed → **27 → 26**.
  - Title change on a live window: a terminal retitled itself `HYDRAWIN-TRACK-A` →
    `HYDRAWIN-TRACK-B` and the tracker reported
    `~ WindowsTerminal.exe: HYDRAWIN-TRACK-B` against the same hwnd (`0xED13B4`).
  - Three Chrome windows of one process listed separately with distinct handles
    (`0x70C56`, `0x881088`, `0x1907A6`), same `chrome.exe` path.
  - **Hooks alone**: sweep toggled off → new window still tracked (28 → **29**), closing it still
    removed it (29 → **28**). Sweep re-enabled → count unchanged, no duplicates.
  - **5-minute soak**, sampled every 30 s: `27, 28, 27, 28, 28, 28, 28, 28, 28, 28, 29`. The
    movement matches real churn observed in the event line (Chrome windows opening and closing,
    uTorrent, an Edge webview), and the count held at 28 for seven consecutive samples. It does
    not grow monotonically, which is the property the task asks about.
  - Clean exit: `CloseMainWindow()` → process exited, hooks released via `Stop()`.
- The three `spikes/` projects still build at 0 warnings and `hideshow rescue` still works.

### Observed edge cases

- **Packaged Notepad** briefly creates a transient window: opening it emitted an add *and* a
  remove before settling at net +1. The sweep reconciles this correctly; anything keying off a
  single `WindowAppeared` should tolerate it.
- **The desktop has far more top-level windows than one expects** — 404 enumerated against 30
  tracked. `NoTitle` and `NotVisible` absorb almost all of it.
- **`Owned` never fired in practice** (0 of 404) even though the clause is correct and unit
  tested: owned windows on this desktop always failed an earlier clause first, since the filter
  reports the first failing clause.
- **Elevated Task Manager is tracked normally**, path readable — see the correction above.
- **UI Automation only exposes realised list items**, so the harness lists could not be read in
  full from outside even with virtualization disabled. The clause summary and the event line were
  used as the evidence channel instead. Worth knowing if task 07 wants automated UI assertions.
- Notepad's own title change (the `*` on edit) could not be driven headlessly — it needs real
  keyboard focus, and `SetForegroundWindow` from a non-foreground process is refused, exactly the
  repo gotcha. A terminal window with a programmatic title change was substituted; it exercises
  the same `EVENT_OBJECT_NAMECHANGE` path on a single hwnd, deterministically.

### Files

New: `src/HydraWin.Core/Tracking/` — `WindowFacts.cs`, `TrackableVerdict.cs`, `WindowFilter.cs`,
`WindowSetDiff.cs`, `WindowProbe.cs`, `IHiddenWindowSet.cs`, `EmptyHiddenWindowSet.cs`,
`WindowTracker.cs`; `tests/HydraWin.Core.Tests/WindowFilterTests.cs`, `WindowSetDiffTests.cs`.

Modified: `src/HydraWin.Core/Interop/NativeMethods.cs`,
`src/HydraWin.Core/Tracking/TrackedWindow.cs` (members; its task 02 `S2094` suppression removed —
the first of those debts paid off), `src/HydraWin.App/MainWindow.xaml(.cs)`,
`src/HydraWin.App/ViewModels/MainViewModel.cs`, `tasks/initial_build/03_window_tracker.md`.

Deleted: none. `App.xaml.cs` needed no change — `MainWindow` owns the harness view model.
