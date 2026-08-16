# Task 07 — UI shell: task table, unassigned pane, drag-and-drop

Status: **done** (2026-08-16) — accepted by the user
Depends on: task 03 (WindowTracker events), task 04 (WorkspaceService), task 06 (SwitchEngine).

## Motivation

The manager window is how the user drives everything: see the tasks, see which windows belong
where, drag windows into tasks, click to switch. This replaces the debug harness and seed
commands from tasks 03–06 with the real interface.

## Background

Architecture recap: `HydraWin.App` is WPF + `CommunityToolkit.Mvvm`. Core services already exist
and raise events on the UI `SynchronizationContext`: `WindowTracker` (live window inventory),
`WorkspaceService` (tasks/assignments, auto re-attach), `SwitchEngine` (`SwitchTo`,
`ShowAllTasks`, `SwitchCompleted` summaries). ViewModels orchestrate; **no Win32 from the App
layer** (repo rule). HydraWin's own window is never assignable or hidden. Unassigned windows are
visible in every task by design — the UI must communicate that, not fight it.

Layout target (single resizable window, ~900×600 default):

```
┌───────────────────────────────┬────────────────────────────┐
│ TASKS                         │ UNASSIGNED WINDOWS         │
│ ┌───────────────────────────┐ │  [icon] Notepad — notes    │
│ │ ● Alpha        4 win  (2) │ │  [icon] Edge — docs        │
│ │   ├ Edge — PR #12         │ │  …                         │
│ │   ├ Terminal — claude     │ │                            │
│ │   ├ Code — hydrawin          │ │  (drag →  onto a task row) │
│ │   └ Teams — Alpha chat    │ │                            │
│ │ ● Beta         3 win      │ │                            │
│ │ ○ + New task              │ │                            │
│ └───────────────────────────┘ │                            │
│ [Show all]                    │                            │
└───────────────────────────────┴────────────────────────────┘
```

## Work

### A. ViewModels (`HydraWin.App/ViewModels/`)
`MainViewModel` (owns children, wires service events), `TaskViewModel` (Name, ColorHex,
WindowCount, IsActive, NotificationCount placeholder — task 09 fills it, ordered
`WindowViewModel` children, IsExpanded), `WindowViewModel` (Title — **live, see § F**,
ProcessName, Icon, IsHydraWinHidden, source `TrackedWindow`/assignment ids),
`UnassignedListViewModel` (tracked windows minus bound ones, live). All updates flow from Core
events; no polling in the UI.

### B. Views
- Task list: `ListBox` of expandable task rows (Expander or toggled `ItemsControl`), color chip,
  name (double-click → inline rename `TextBox`), window count badge, reserved badge slot for
  task 09. **Single click on the row header = `SwitchTo`** — the core interaction; make the
  active task visually unmistakable (accent border + fill).
- Per-window row: icon + title; context menu: *Focus* (switches to its task then focuses),
  *Unassign*, *Move to ▸ [task list]*.
- Unassigned pane: same window-row template, plus context menu *Assign to ▸*.
- Toolbar/footer: *+ New task* (inline name entry), *Show all* (→ `ShowAllTasks`), task context
  menu *Rename* / *Delete* (confirmation states windows will be un-hidden and unassigned, never
  closed — wording matters, it teaches the safety model).
- Window icons: extracted in Core-adjacent App service via `System.Drawing.Icon.ExtractAssociatedIcon`
  on the process path (fallback: generic icon; elevated processes have empty paths → generic).
  Cache per process path.

### C. Drag-and-drop
In-app only (`DragDrop.DoDragDrop` with a custom data format carrying the window's stable id):
- Unassigned row → task row (header or expanded body): assign.
- Window row → other task row: move.
- Window row → unassigned pane: unassign.
- Task rows themselves reorder by drag (updates `Order`).
Visual feedback: insertion/highlight adorner on the current drop target; forbidden cursor over
non-targets (e.g. dropping a task onto a window). Keep it plain WPF; if adorner plumbing gets
long, `GongSolutions.WPF.DragDrop` is an acceptable dependency — note it in the completion
record if used.

### D. Assignment/switch behaviours in the UI
- ~~Assigning a window to a *non-active* task while another task is active: the window stays
  visible until the next switch (predictable; no surprise disappearance on drop). State this in
  a tooltip on the drop.~~ **Superseded on the user's instruction** (see *Window picker* in the
  record): assigning to a task that is not the one on screen hides the window immediately, so it
  behaves as though it had always belonged there. This applies to the drop and to the picker
  alike — both go through `SwitchEngine.AssignWindowToTask`.
- Auto re-attach (Core, task 04) surfacing: when a rule binds a reappearing window, its row just
  shows up under the task — plus a status-bar line "Re-attached *Code — hydrawin* to *Alpha*".
- Switch summaries (`SwitchCompleted`) and recovery notices (task 05) appear in the same
  status-bar line.
- HydraWin window title: `HydraWin — <active task>` or `HydraWin`.

### E. Remove scaffolding
Delete the task 03 harness list and the task 05/06 debug commands; this UI is now the only
driver.

### F. Live titles, and Claude Code progress in the overview
Window rows show the **live** window title: `WindowViewModel.Title` is bound to and updated from
`WindowTracker.WindowTitleChanged`, so a row reflects what the window is doing right now without
the user switching to it. This matters most for Claude Code terminals, and is an explicit user
requirement — task 09 deliberately ships **no** Claude Code notification rule (the flash covers
"finished", a minute late), so the overview is the only place in-progress state is visible.

Task 01 measured the format: an interactive Claude Code session titles its terminal
`<marker> <session or activity name>`, where the marker is

- a rotating spinner frame `U+25D0`–`U+25D3` (`◐ ◑ ◒ ◓`) while **working**, advancing about once
  per second, and
- `U+2733` (`✳`) when it is **idle / waiting for input**, after which the title stops changing.

Surface that as a per-window in-progress indication — e.g. the marker glyph or a small spinner in
the row, with the remaining title text as the label — and let it roll up to the task row so a
collapsed task still shows that something inside it is working.

Two measured constraints:
- **~1 title event per second per busy terminal.** Binding straight through is fine for a handful
  of sessions; keep per-event work cheap and do not re-sort or re-filter the whole list on each
  one.
- **Do not treat the marker as a notification.** `✳` also appears momentarily at the start of an
  activity, and the badge for "Claude finished" comes from the flash channel in task 09. This
  section is about *display* only.

## Verification

**The acceptance walkthrough is run by the user, on their own desktop and their own windows.**
This is the first task where that is true — 01–06 were driven against scratch windows. The
implementer's job is to smoke-test the same eight steps first against throwaway windows, reset
`%APPDATA%\HydraWin`, and hand over a clean app; the user's run is the verification of record and
their observed results are what go in **Record on completion**.

Full walkthrough on a real desktop (record each step's observed result):
1. Start HydraWin → unassigned pane lists the open real windows (per task 03 filter), tasks empty.
2. Create tasks *Alpha*, *Beta*. Drag a browser + a terminal into Alpha; VS Code + browser into
   Beta. Counts update; windows leave the unassigned pane.
3. Click Alpha → Beta's windows vanish from screen/taskbar/Alt-Tab; click Beta → mirror image;
   placements restored; active-task highlight follows.
4. Drag Alpha's terminal onto Beta while Alpha is active → it stays visible now, and after
   switching Alpha → Beta → Alpha it is hidden with Beta. 
5. Unassign a window from the active task → it stays visible in both tasks thereafter.
6. Rename and reorder tasks; restart HydraWin → order, names, assignments persist; close VS Code,
   reopen the same folder → auto re-attaches to Beta with the status-bar notice.
7. Delete Beta while Alpha is active → Beta's hidden windows reappear immediately, all its
   windows land in unassigned, none closed.
8. Live titles (§ F): start a Claude Code prompt in an assigned terminal and watch its row without
   switching to it — the title tracks the session, the in-progress indication is visible while the
   spinner marker cycles, and it clears when the title settles on `✳`. Repeat with that task
   *hidden* — the row must still update, since name-change events do not require visibility.
   Confirm no UI stutter with two busy sessions at once (~2 title events/second).
9. `dotnet build` warning-free; `dotnet test` totals pasted.

## Record on completion

### What was built

The harness is gone; `MainWindow` is now the real interface. `MainViewModel` was rewritten around
`TaskViewModel` / `WindowViewModel` / an unassigned collection, wired to the Core events that
already existed. Structural changes (task created, window assigned, switch completed) rebuild the
task list; title changes deliberately do not — they go through a handle-keyed dictionary and touch
one row, because task 01 measured ~1 title event per second per busy Claude Code terminal.

### Deviations, and why

- **Drag-and-drop is hand-rolled WPF. `GongSolutions.WPF.DragDrop` was not pulled in.** § C allows
  it as a fallback; it was declined because a task row carries two mouse semantics on one element —
  a click switches, a drag reorders — and Gong takes over `PreviewMouseDown`/`Move` on the controls
  it attaches to. Its one big saving (the default `ObservableCollection` reorder handler) applies
  to only one of the four drop kinds here. `DragDropSupport.cs` holds the threshold, the two data
  formats, the hit-testing and the highlight/insertion adorners.
- **Window icons come from `Core/Interop`, not `System.Drawing`.** § B suggested
  `Icon.ExtractAssociatedIcon` in an App service. That is Win32-by-proxy above Core (repo rule) and
  needs the `System.Drawing.Common` package, so `ExtractIconExW` joined `NativeMethods` behind a new
  `IIconSource`, and the App turns the `HICON` into an `ImageSource` with plain WPF
  (`Imaging.CreateBitmapSourceFromHIcon`). `ExtractIconExW` was chosen over `SHGetFileInfo` because
  its signature is blittable and so works with `[LibraryImport]`.
- **Four small Core additions** rather than App-layer workarounds, so they could be unit-tested:
  `ClaudeCodeTitle` (marker parsing, and now the single home for task 01's measured glyphs, which
  `ReattachRule` references instead of repeating), `WorkspaceService.ReorderTask`,
  `SwitchEngine.SwitchToWindow`, and the `AssignWindow` fix below.

### Bugs found and fixed

- **Moving a window between tasks left an orphaned rule.** `AssignWindow` called
  `RemoveBindingLocked`, which unbinds but leaves the old assignment *and its re-attach rule* in the
  previous task. Nothing had exercised it, because until drag-and-drop existed nothing could move a
  window. The consequence was two tasks holding a rule for the same window, the old one silently
  re-claiming it after a restart. Fixed to remove the assignment outright and raise
  `WindowUnassigned` for the old task. The restart step below is the direct proof: after moving
  HW-CH3 from Alpha to Beta, `state.json` shows `"Name": "Alpha"` with `"Assignments": []`.
- **The active-task highlight never appeared.** `Background`, `BorderBrush` and `BorderThickness`
  were set as local attributes on the task-row `Border` as well as in the `IsActive` `DataTrigger`,
  and in WPF a local value outranks a style trigger. Every visual default moved into the `Style`.
  Caught by comparing a screenshot against the spec, not by the build.
- **The rename box appeared and vanished instantly.** `CreateTask` set `IsRenaming` on the new row,
  but the `TasksChanged` rebuild that followed replaced the row object. `Rebuild` now carries
  `IsExpanded` *and* `IsRenaming` across.

### Smoke test (implementer, throwaway windows)

Against three Chrome windows, two Windows Terminal windows and a scratch VS Code window that I
spawned. Steps 1–7 of § Verification plus a synthetic version of step 8; the user's windows were
never assigned and stayed visible throughout.

| Step | Observed |
| --- | --- |
| 1 | 19 windows listed in the unassigned pane, icons resolved for chrome/Code/explorer/Notepad++/Signal, tasks empty |
| 2 | Alpha and Beta created and named; drags moved HW-CH1 + HW-CH3 into Alpha, HW-CH2 + VS Code into Beta; counts followed, rows left the unassigned pane |
| 3 | Alpha → Beta: `hidden 2, shown 2`. Beta → Alpha: HW-CH1 back at **exactly** `(-2206,816)-(-217,2300)` and HW-CH3 at `(-2181,841)-(-187,2328)` — byte-identical to the pre-switch snapshot. Journal held exactly the two hidden windows with full placements while they were hidden |
| 4 | HW-CH3 dragged Alpha → Beta while Alpha active: stayed visible, status said *"it stays visible until you switch tasks"*. After Alpha → Beta → Alpha it was hidden with Beta |
| 5 | HW-CH1 dragged to the unassigned pane: stayed visible, and still visible after switching to Beta |
| 6 | Beta dragged above Alpha → `Order` 1/2 swapped and persisted. Restart: order, names and assignments restored; *"Re-attached “Welcome - scratch - Visual Studio Code” to “Beta”"* in the status bar |
| 7 | Delete Beta with its 3 windows hidden → dialog read *"Its 3 window(s) will be un-hidden and returned to Unassigned. No window is closed."*; all three reappeared at unchanged rects, journal empty, none closed |
| 8 | Synthetic only — a window renamed to `◐ building hydrawin` showed the blue `◐` with the marker stripped from the label, and the marker rolled up to the **collapsed** task row |

Exiting HydraWin restored every hidden window and left `journal.json` as `[]`. `%APPDATA%\HydraWin`
was reset before handover. Screenshots: `screenshots/07-task-table.png`,
`07-active-task-and-reattach.png`, `07-progress-rollup-collapsed.png`.

**Not covered by the smoke test:** step 8 with a real Claude Code session (needs the user's
machine — that is the point of § F), and the `Unmanageable` / elevated-window path, which no window
triggered; task 01's elevated Task Manager measurement remains the evidence for it.

### Feedback round (user, after first hand-over)

- **Drag feedback started too late.** There was no drag ghost at all — the only cue was the drop
  cursor, which says a drag is happening but not what is being dragged, and shows nothing until the
  pointer is over a valid target. Added `DragGhostAdorner`, a translucent copy of the row tracked
  at window level so it keeps up over the toolbar, the splitter and the gaps between drop targets.
  **Window rows now begin their drag on mouse-down**, since a window row has no click action to
  protect. Task rows keep the threshold — a click there switches tasks, so starting on the press
  would make every switch a drag.
- **The manager was buried by its own switch.** Added `SettingsModel.AlwaysOnTop`, persisted, on by
  default, with a *Stay on top* toolbar toggle. Deliberately not scoped to "only during a switch":
  `SwitchTo` is synchronous, so a flag raised and lowered inside it never reaches a rendered frame.
  What actually buries the window is the switch *ending* by focusing one of the task's windows.
- **Dragging a live OS window into the list is not possible.** OLE drag-and-drop carries data, not
  window handles, and Windows exposes no shell protocol for dragging a window. The idiom that would
  work is a crosshair picker (drag onto any window to grab it), which is a new feature, not a fix.

#### Verification of *Stay on top* (measured, not assumed)

Ground truth was `WS_EX_TOPMOST` read off the real HWND, plus `WindowFromPoint` over a deliberate
overlap — not the checkbox state.

| Case | Observed |
| --- | --- |
| Fresh start, default | `exstyle=0x40108`, `WS_EX_TOPMOST=True`, with no harness involvement |
| Rival window raised to `HWND_TOP` **and** activated | overlap point still returns HydraWin |
| **After a switch** | foreground became `HW-TOPTEST - Google Chrome` — the task's window — yet the overlap point still returned HydraWin. This is the case the setting exists for |
| Toggle off | `exstyle=0x40100`, `WS_EX_TOPMOST=False`; the *identical* `HWND_TOP` raise now wins the overlap point — the control that proves the ON result was real |
| Persistence | `"AlwaysOnTop": false` written to `state.json`; still off after a restart; re-checking restores `true` |

One harness trap worth recording: the first run showed `WS_EX_TOPMOST=True` only because the test
driver's own "bring to front" helper used `HWND_TOPMOST`. That was measuring the harness, not the
app. The helper was changed to `HWND_TOP` and the whole sequence re-run from a fresh start.

Two small things this surfaced, both fixed in the second feedback round below.

### Second feedback round

- **The rename box now closes on any other interaction.** `MainViewModel.CommitPendingRename` is
  called from a window-level `PreviewMouseDown` whenever the press lands outside a `TextBox`, so a
  toolbar button, the start of a drag or a click on another row all settle it. Lost-focus alone was
  never enough — it requires the box to have *had* focus, and a drag begun elsewhere would leave it
  open indefinitely.
- **A renaming row accepts drops again.** The rename `TextBox` is `AllowDrop="False"`: a WPF
  `TextBox` handles `DragOver`/`Drop` itself for text and swallowed the payload. With the fix above
  this is now belt-and-braces — any mouse-down closes the box before a drag can start — but the
  control should not be a drop target either way.
- **Elevated windows are excluded from the inventory** when HydraWin is not itself elevated. New
  `TrackableVerdict.Elevated` clause, evaluated immediately after own-process and before the
  cosmetic clauses so the rejection reason is the one that matters. Detection is
  `NativeMethods.IsProcessElevated` (`OpenProcess` → `OpenProcessToken` → `TokenElevation`), cached
  per process id in `WindowProbe` with a one-minute TTL — the sweep asks for several hundred
  windows every two seconds, and elevation never changes for a live process; the TTL only bounds a
  recycled process id inheriting a stale answer. **Being unable to query counts as elevated**: task
  01 established that plain elevation does not block `PROCESS_QUERY_LIMITED_INFORMATION` for a
  same-user process, so a failure means something stronger. This supersedes the "detected, marked
  in the UI, and skipped" line in `CLAUDE.md`, which was updated.
- **HydraWin's own window was already excluded** — `WindowFilter` has rejected `OwnProcess` since
  task 03, and a live check confirmed no row carries the app's own pid. The "HydraWin" row the user
  saw is *SourceGit*, whose window title happens to be the repository name; its process line reads
  `SourceGit.exe`. No change made.

#### Verified

| Check | Observed |
| --- | --- |
| Elevated window excluded | `Administrator: Windows PowerShell` (pid 20720) visible and titled on the desktop, independently confirmed elevated by the test driver's own token query — absent from the unassigned list |
| Own window excluded | no row for `hydrawin.exe`; the `HydraWin` row is `SourceGit.exe` |
| Rename closes on a button | box open → pressed *Show all* → box gone |
| Rename closes on a drag | box open → started dragging a window row → box gone |
| Dropping still works | *"Added “SVG Icons in .NET - Google Chrome” to “Task 3”"* after the `AllowDrop` change |

### Window picker (third feedback round)

A Spy++-style crosshair on every task row: press it, drag over the desktop with the window under
the pointer outlined, release, and that window joins the task.

- **One assignment path for both gestures.** `SwitchEngine.AssignWindowToTask` assigns and then —
  only when some *other* task is active — hides, by calling the switch's own private `HideAll`.
  That is deliberate and load-bearing: hiding on assignment is a new way to hide a foreign window,
  and routing it through `HideAll` means the journal-before-hide invariant applies here without a
  second implementation of it. The drop path was rerouted through the same method, so drag-and-drop
  and the picker cannot drift apart. **This overrides § D** at the user's instruction.
- **Refusals reuse the filter.** A picked window is looked up in the tracker's inventory — being in
  it *is* the whitelist, since `OwnProcess` and `Elevated` are already clauses of `WindowFilter`.
  Only on a miss does `WindowTracker.ExplainOne` name the reason, using the same two calls
  `Explain()` makes. The picker's refusals and the pane's omissions therefore cannot disagree.
- **HydraWin ghosts itself while picking** — `WS_EX_TRANSPARENT` plus ~35% alpha on its own window.
  Not decoration: *Stay on top* is on by default, so without it every window behind the app would
  be unpickable. Restored on release, on Escape, and on lost capture.
- **The highlight is positioned in physical pixels** through `SetWindowPos`, not through WPF's
  `Left`/`Top`, whose device-independent units would need DPI arithmetic that breaks the moment the
  pointer crosses to a monitor with a different scale factor. It is `WS_EX_TRANSPARENT` so
  `WindowFromPoint` never returns it, and `WS_EX_TOOLWINDOW` so HydraWin's own filter would reject
  it even if it were enumerated.
- New `IScreenApi` / `Win32ScreenApi` keeps the App free of P/Invoke, as the repo rule requires.

#### The gesture does not use WPF mouse capture, and that is the interesting part

The obvious implementation — `Mouse.Capture` on the crosshair, track `MouseMove`, finish on
`MouseUp` — does not survive this particular gesture. **Any window operation performed while the
capture is held makes WPF release it**, and the pick ends on the first movement with no way to tell
why. Two such operations are unavoidable here: getting the main window out of the way, and showing
the highlight. Moving them before the capture was taken did not help, and neither did
`CaptureMode.Element` in place of `SubTree`.

So the picker follows the hardware instead: a 30 ms `DispatcherTimer` reads `GetCursorPos` and
`GetAsyncKeyState` and ends the gesture when the button comes up. That is what Spy++ has always
done, and it is indifferent to activation, z-order and focus. The timer only runs while the button
is held.

Getting the main window out of the way went the same way. The first attempt made HydraWin
click-through with `WS_EX_TRANSPARENT` plus a translucent `WS_EX_LAYERED`; WPF owns `WS_EX_LAYERED`
on a window whose `AllowsTransparency` is false and strips it straight back out, leaving the window
**opaque but invisible to the mouse** — and if a pick then ended abnormally, the whole app stayed
that way and ignored every click. Dropping it to the bottom of the z-order for the duration achieves
the same thing with nothing to strand.

#### Verified live

| Check | Observed |
| --- | --- |
| Pick into an **inactive** task | *"Added “HW-PICK2 — Google Chrome” to “Beta” and hid it with the task."*; the window went from `visible=True` to `visible=False`, and the journal held exactly it |
| Journal before hide | the entry was on disk after a hard `Stop-Process` kill, and `hydrawin.exe --restore-all` brought the window back and emptied the journal |
| Pick into the **active** task | *"Added … to “Alpha”."*, window stayed visible, journal empty |
| Pick a window HydraWin completely covers | resolved the covered window, not HydraWin — the z-order drop works |
| Own window / occlusion | pointing at HydraWin resolves whatever is behind it, so the app can never be picked into a task |
| Escape | ends the pick |
| Desktop and taskbar | not outlined; releasing over the taskbar says *"That is not a window HydraWin can manage."* and assigns nothing |

**The frame only outlines what a release would actually take.** It first highlighted whatever
`WindowFromPoint` returned, which meant the desktop and the taskbar looked selectable and were then
refused — the highlight promised something the drop would not honour. `WindowPicker.CanPick` now
asks the view model, whose answer is "is this handle in the inventory", the very condition for
accepting it. The handle is still remembered when it is not outlined, so releasing over the taskbar
gets an explanation instead of silence.

Not verified live: picking an **elevated** window. The one on the desktop
(`Administrator: Windows PowerShell`) turned out to be completely occluded by other windows, so
there was no point at which it was the top-level window and it could not be pointed at. The branch
is covered by the filter's unit tests and by the previous round's live proof that elevated windows
are absent from the inventory — which is the same lookup the picker uses.

### Task-row workflow (fourth feedback round)

- **A new task's name box now takes focus with its text selected**, so the name can be typed
  straight away. The code already called `Focus()` and `SelectAll()`; it queued them with
  `Dispatcher.BeginInvoke` at the default `Normal` priority, and WPF runs **Normal (9) before
  Render (7) and Loaded (6)** — so it tried to focus a container that had not been arranged,
  `Focus()` returned `false`, and nothing said so. Dispatching at `Input` (5) fixes it. The same
  helper is also wired to the box's `Loaded`, because a box created *already* visible never raises
  `IsVisibleChanged`.
- **A rebuild no longer interrupts typing.** `Rebuild()` clears and re-adds every row, which
  regenerates the item containers and destroys the open rename box — focus, caret and half-typed
  name with it — and it runs on every window appearing or disappearing. It now defers while a row
  is renaming and flushes when the rename commits or is abandoned. The pane is a second or two
  stale meanwhile, bounded by the fact that any click outside commits. Diffing `Tasks` in place is
  the better long-term fix but a far larger change to a method drag-and-drop, expansion state and
  the activity rollup all depend on.
- `CreateTask` remembers the new task's **id** rather than its row, and `Rebuild` applies
  `IsRenaming` by construction — the row object is already gone by the time the command returns,
  since creating a task raises `TasksChanged`.
- **Del deletes the active task.** The list has no selection — clicking a row switches to it — so
  the accent-bordered active task is the target. Ignored while a `TextBox` has focus, or Del would
  eat the task instead of a character mid-rename, and ignored while a pick is running.
- **No confirmation dialog when the task holds no windows**, from the key and the context menu
  alike. The dialog exists to say what becomes of the windows; with none open it has nothing to
  say. The message drops the window clause too — *Deleted “Payments”.*

#### Verified live

| Check | Observed |
| --- | --- |
| Press *+ New task*, type without clicking | box read exactly `Payments`, not `Task 1Payments` — focused **and** the placeholder was selected |
| Enter | committed; row shows the new name |
| Del on an **empty** task | deleted at once, no dialog, *"Deleted “Payments”."* |
| Del while renaming | deleted the selected text, task untouched, box still open |
| Del on a task **with** a window | dialog appeared; *No* left the task alone, *Yes* deleted it and the window came back visible and unassigned |
| Window appearing mid-typing | the typed text survived and the box stayed open; the new window appeared in the pane only after the rename committed, confirming the deferral and its flush |

#### Focus now depends on how the switch was started

The first cut of Del exposed a real gap: switching ended by focusing one of the task's windows, so
HydraWin was no longer the foreground app and a Del pressed straight after clicking a row went to
*that app* — at best doing nothing, at worst deleting something in it.

`SwitchTo` therefore takes a `focusTarget` flag:

- **False for a click inside HydraWin's own window.** The user is driving the panel, so the keyboard
  stays with it. The task's windows are still brought to the front, through the new
  `IWindowApi.Raise` (`SetWindowPos(HWND_TOP, SWP_NOACTIVATE)`) rather than `SetForegroundWindow`,
  and they are raised in reverse order so the last-active one ends up on top — the same window
  `FocusTarget` would have picked, just not activated.
- **True everywhere else**, which is the default: the *Focus* command names a window explicitly, and
  task 08's hotkeys and tray switch from outside the app, where landing in the task is the point.

This supersedes task 06's "focus lands on the task's last-active window" for the click path only;
`LastActiveHwnd` is still tracked and still decides which window ends up in front.

Verified live: after clicking a task row the foreground window was still `HydraWin — Focus`, and Del
pressed immediately afterwards — with no mouse input in between — raised the delete confirmation.

### Build, tests, format

- `dotnet build HydraWin.sln` — **0 warnings, 0 errors**.
- `dotnet test --solution HydraWin.sln` — **175/175 passed** (132 before; 43 new: 9 for
  `ClaudeCodeTitle`, 11 for reordering and the move fix, 3 for `SwitchToWindow`, 2 for the settings
  round-trip, 4 for the elevated-window filter clause, 7 for `AssignWindowToTask` — including the
  journal-before-hide invariant on the new hide path — 3 for raise-without-focus, plus the existing
  suites re-run).
- `dotnet format --verify-no-changes` — exit 0. All three `spikes/` projects still build.

### User walkthrough

**Accepted by the user on 2026-08-16 without a separate step-by-step walkthrough being recorded.**
The user drove the UI directly across several rounds of feedback instead — every item they raised
(drag feedback, stay-on-top, elevated and own windows in the list, the rename box, Del to delete,
keeping focus on task selection) was fixed and verified in the rounds recorded above. The evidence
for §§ 1–8 is therefore the implementer's smoke test plus those feedback rounds, not a single
sign-off run; recording it as anything else would overstate it.

### Files

**New** — `src/HydraWin.App/Assets/hydrawin.svg` (app icon; task 08 still needs an `.ico` derived
from it), `src/HydraWin.Core/Tracking/ClaudeCodeTitle.cs`, `src/HydraWin.Core/Interop/IIconSource.cs`,
`src/HydraWin.Core/Interop/Win32IconSource.cs`, `src/HydraWin.App/Converters.cs`,
`src/HydraWin.App/DragDropSupport.cs`, `src/HydraWin.App/Services/WindowIconCache.cs`,
`src/HydraWin.App/ViewModels/TaskViewModel.cs`, `src/HydraWin.App/ViewModels/WindowViewModel.cs`,
`tests/HydraWin.Core.Tests/ClaudeCodeTitleTests.cs`,
`tests/HydraWin.Core.Tests/TaskOrderingTests.cs`, and `tasks/initial_build/screenshots/` (3 files).

**Modified** — `src/HydraWin.App/MainWindow.xaml`, `src/HydraWin.App/MainWindow.xaml.cs`,
`src/HydraWin.App/ViewModels/MainViewModel.cs`, `src/HydraWin.App/DragDropSupport.cs`,
`src/HydraWin.Core/Interop/NativeMethods.cs`, `src/HydraWin.Core/Workspaces/ReattachRule.cs`,
`src/HydraWin.Core/Workspaces/SettingsModel.cs`, `src/HydraWin.Core/Workspaces/SwitchEngine.cs`,
`src/HydraWin.Core/Workspaces/WorkspaceService.cs`,
`tests/HydraWin.Core.Tests/SwitchEngineTests.cs`,
`tests/HydraWin.Core.Tests/WorkspaceServiceTests.cs`, `tasks/initial_build/07_ui_shell.md`,
`tasks/initial_build/_plan.md`.

**Deleted** — none. `UnassignedListViewModel` was not needed: the unassigned pane is a plain
collection on `MainViewModel`, so it never became a type.
