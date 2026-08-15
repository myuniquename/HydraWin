# Task 07 — UI shell: task table, unassigned pane, drag-and-drop

Status: **not started**
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
`WindowViewModel` children, IsExpanded), `WindowViewModel` (Title, ProcessName, Icon,
IsHydraWinHidden, source `TrackedWindow`/assignment ids), `UnassignedListViewModel` (tracked
windows minus bound ones, live). All updates flow from Core events; no polling in the UI.

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
- Assigning a window to a *non-active* task while another task is active: the window stays
  visible until the next switch (predictable; no surprise disappearance on drop). State this in
  a tooltip on the drop.
- Auto re-attach (Core, task 04) surfacing: when a rule binds a reappearing window, its row just
  shows up under the task — plus a status-bar line "Re-attached *Code — hydrawin* to *Alpha*".
- Switch summaries (`SwitchCompleted`) and recovery notices (task 05) appear in the same
  status-bar line.
- HydraWin window title: `HydraWin — <active task>` or `HydraWin`.

### E. Remove scaffolding
Delete the task 03 harness list and the task 05/06 debug commands; this UI is now the only
driver.

## Verification

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
8. `dotnet build` warning-free; `dotnet test` totals pasted.

## Record on completion

*(what was done, deviations and why — including whether GongSolutions was pulled in — walkthrough
results, screenshots into `screenshots/`, and the list of new / modified / deleted files)*
