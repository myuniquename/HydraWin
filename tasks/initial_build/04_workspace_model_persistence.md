# Task 04 — Workspace model and persistence

Status: **not started**
Depends on: task 02 (scaffold). Independent of task 03.

## Motivation

Tasks, their window assignments, and settings must survive HydraWin restarts and — via re-attach
rules — survive the *windows themselves* being closed and reopened (a reopened VS Code with the
same folder should land back in its task automatically). This task defines the model, the rules,
and atomic JSON persistence; it is pure logic, fully unit-testable.

## Background

Architecture recap: a HydraWin **task** is a named workspace owning a set of window assignments. At
runtime an assignment binds to an HWND (from task 03's `WindowTracker`); across restarts HWNDs
are meaningless, so each assignment also carries a durable **re-attach rule** used to re-bind
when a matching window (re)appears. A window belongs to at most one task. Persistence lives in
`%APPDATA%\HydraWin\state.json`. (The recovery journal is separate — task 05 — and is *not* part of
this file: state.json is preference data, journal.json is crash-safety data.)

## Work

### A. Model (`Workspaces/`)
- `HydraWinTask { Guid Id; string Name; string ColorHex; int Order; List<WindowAssignment>
  Assignments; }`
- `WindowAssignment { Guid Id; ReattachRule Rule; /* runtime-only, [JsonIgnore]: */
  IntPtr? BoundHwnd; }`
- `ReattachRule { string ProcessFileName; string TitlePattern; bool TitleIsRegex; }` —
  `ProcessFileName` is the image file name only (`Code.exe`), compared case-insensitively; the
  full path is too brittle across updates. `TitlePattern` defaults to a *substring* match
  (`TitleIsRegex = false`); regex is opt-in (edited by the user, task 10).
- `WorkspaceState { List<HydraWinTask> Tasks; Guid? ActiveTaskId; SettingsModel Settings; }` with
  `SettingsModel` holding what later tasks need (hotkey map, notification rules) — start minimal,
  add fields in those tasks.

### B. Rule generation and matching
- `ReattachRule.FromWindow(processPath, title)`: file name from path; title pattern = the title
  with common volatile decorations stripped (a leading `● ` / `*`, and a trailing ` - <app>`
  suffix is *kept* — it is usually the stable part; strip nothing else — keep this dumb and
  predictable, the user can edit rules later). Substring mode.
- `RuleMatcher.FindTask(state, window)`: first task (by `Order`) containing a rule that matches
  `(ProcessFileName, Title)`; regex rules use `RegexOptions.IgnoreCase` with a 100 ms match
  timeout (a bad user regex must not hang the tracker thread).
- Binding rules: a rule binds at most one window at a time (first match wins; a second matching
  window stays unassigned for the user to drag); an already-bound window is never rebound.

### C. Persistence (`Persistence/`)
- `JsonStore<T>` : load-or-default, and `Save(T)` writing atomically — serialize to
  `state.json.tmp` in the same directory, then `File.Replace` (fall back to
  `File.Move(overwrite: true)` when the target doesn't exist yet). `JsonSerializerOptions`:
  `WriteIndented = true`, enum-as-string converter.
- Corrupt-file policy: on deserialization failure, rename the bad file to
  `state.json.corrupt-<yyyyMMdd-HHmmss>` and start with defaults — never crash, never silently
  overwrite the evidence.
- `WorkspaceStore` = `JsonStore<WorkspaceState>` at `%APPDATA%\HydraWin\state.json`
  (`Environment.SpecialFolder.ApplicationData`), directory created on first save. Debounced save
  (~1 s) so drag-storms don't thrash the disk; `Flush()` for shutdown.

### D. `WorkspaceService` (Core orchestration, no UI)
API consumed by tasks 06/07: `CreateTask(name)`, `RenameTask`, `DeleteTask` (returns the
assignments so the caller can un-hide/unassign — deletion never closes windows),
`AssignWindow(taskId, trackedWindow)` (creates rule + binds; reassigning moves between tasks),
`UnassignWindow`, `OnWindowAppeared(trackedWindow)` → auto-bind via `RuleMatcher`,
`OnWindowDisappeared` → unbind (rule stays). Raises change events for UI binding. Persists via
`WorkspaceStore` after each mutation.

### E. Unit tests
Round-trip serialization (placements, GUIDs, settings survive); atomic-save behaviour (tmp file
gone, content replaced); corrupt-file recovery; rule generation from representative titles
(`● file.cs - project - Visual Studio Code`, a Claude Code terminal title, `(2) Chat | Teams`);
matcher precedence, one-window-per-rule, regex timeout; assign/unassign/auto-rebind flows.

## Verification

- `dotnet test` — paste totals; the suites in E all present and green.
- Manual: run the app with a temporary debug action that creates two tasks and assigns fake
  windows; restart; confirm `%APPDATA%\HydraWin\state.json` exists, is indented JSON containing
  both tasks; corrupt it by truncating half the file; restart → app starts with defaults and a
  `state.json.corrupt-*` file sits beside a fresh `state.json`.

## Record on completion

*(what was done, deviations and why, test totals, sample state.json snippet, and the list of
new / modified / deleted files)*
