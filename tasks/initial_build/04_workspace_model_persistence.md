# Task 04 — Workspace model and persistence

Status: **done** (2026-08-15, accepted 2026-08-16)
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

> **Correction (task 05).** Any tracked-window count quoted below is inflated 2× by the harness
> duplicate-listing bug described in task 03's correction note. The assignment, persistence and
> re-attach results are unaffected — they were verified against `state.json` and the task pane,
> not the tracked count.

Built the model, rule generation and matching, `JsonStore<T>` / `WorkspaceStore`, and
`WorkspaceService`, plus a throwaway harness for the manual drills. Two more of task 02's
placeholder suppressions are gone (`WorkspaceState`, `JsonStore<T>`); only `JournalEntry` and
`NotificationRule` remain, for tasks 05 and 09.

### Deviations, and why

- **The volatile-decoration strip list is wider than the task says.** § B specifies a leading
  `●`/`*` and *"strip nothing else"*. Task 01 measured that Claude Code titles its terminal
  `<marker> <session name>` with a spinner advancing about once a second (`◐◑◒◓`) or `✳` when
  idle, so a rule generated from a busy terminal would capture whichever frame was showing and
  never match again — and terminals are a core case for this app. `✳` and the four spinner frames
  were added to the list, agreed with the user beforehand. Still named glyphs only, and consistent
  with task 07 § F, which already treats exactly these as volatile. **This is not theoretical: the
  restart drill below re-attached two Claude Code terminals whose live titles were `◐ prod` and
  `✳ git_submit` against the stored patterns `prod` and `git_submit`.** With the task followed
  literally, neither would have re-attached.
- **`nint?` rather than `IntPtr?`** for `BoundHwnd` — the same type, but `TrackedWindow.Hwnd` is
  already `nint`.
- **`SettingsModel` ships one field**, `RestoreOnExit` (default `true`), because task 08 needs a
  default that fails safe. The rest is documented in XML comments against the task that adds it.
- **`HydraWinPaths` added** (`Persistence/`) so `%APPDATA%\HydraWin` is spelled out once. Task 05
  adds `journal.json` to it; `JsonStore<T>` itself stays path-agnostic so both suites can run
  against temp directories.
- **`WorkspaceStore.SaveFailed`** added — see the bug below.

### Two bugs found before they shipped

- **The debounced save could kill the process.** The write runs on a timer thread, where an
  unhandled exception is fatal — so a transient I/O failure would take HydraWin down, potentially
  with the user's windows hidden. Found when a test's temp directory was deleted while a write was
  pending. `WorkspaceStore.Flush` now catches I/O failures, raises `SaveFailed`, and leaves the
  state pending so a later flush retries. Losing preferences is bad; losing the process that knows
  where the hidden windows went is far worse. Covered by
  `AFailedWriteIsReportedRatherThanThrownAndTheStateStaysPending`.
- **The derived `OrderedTasks` property was being serialized**, duplicating the entire task list in
  `state.json` — 1479 bytes of real content written as ~2.9 KB. The unit tests missed it because
  they only round-tripped; the manual drill caught it on sight. Worse than waste: the property is
  get-only, so anything a user hand-edited in the second copy would be **silently discarded on
  load**, and tasks 08 and 09 both require `state.json` to be hand-editable. Fixed with
  `[JsonIgnore]`, and `OnlyTheRealSchemaIsWrittenNotDerivedProperties` now asserts the exact
  property set at all three levels so the next derived property cannot leak in.

### A stale line in this task's own text

§ E asks the round-trip test to prove "**placements**, GUIDs, settings survive". No type in § A
holds a placement — `WindowPlacementDto` belongs to task 05's `JournalEntry` inside
`journal.json`, which § Background explicitly says is *not* part of `state.json`. The round-trip
test covers GUIDs, tasks, rules and settings; placements are task 05's to prove.

### Verification results

- `dotnet build HydraWin.sln` → **0 warnings, 0 errors**. Two Sonar findings arose and both were
  fixed in code, no suppressions added: **S2743** (a static `JsonSerializerOptions` on a generic
  type is duplicated per closed type — moved to a non-generic holder) and **S108** (an empty catch
  block — merged into one filtered catch with the reasoning written down).
- `dotnet test --solution HydraWin.sln` → **total: 78, failed: 0, succeeded: 78, skipped: 0**
  (11 `JsonStore`, 6 `WorkspaceStore`, 14 `ReattachRule`, 7 `RuleMatcher`, 13 `WorkspaceService`,
  plus tasks 02–03's 27).
- `dotnet format --verify-no-changes` → exit 0.
- **Manual, driven end to end:**
  - Seeded two tasks, assigned four windows, flushed → `%APPDATA%\HydraWin\state.json` written,
    **1479 bytes**, indented, both tasks present (sample below).
  - **Restart → auto re-attach.** Closed and relaunched; all four assignments re-bound themselves
    from their rules, with the status line reporting `re-attached: HydraWin ↔ Beta`. Handles
    differed from the previous run, which is the entire point of the rule mechanism.
  - **Corrupt-file drill.** Truncated `state.json` to 740 of 1479 bytes and restarted: the app came
    up with **0 tasks** and no crash, the damaged file was preserved byte-for-byte as
    `state.json.corrupt-20260815-224407` (740 bytes), and after the next save a fresh 1479-byte
    `state.json` sat beside it.
- The three `spikes/` projects still build clean and `hideshow rescue` still works.

### Sample `state.json`

```json
{
  "Tasks": [
    {
      "Id": "813ecb3a-4749-4475-af80-023d2cc7810d",
      "Name": "Alpha",
      "ColorHex": "#4C8DFF",
      "Order": 1,
      "Assignments": [
        {
          "Id": "a275491a-ab06-44ae-a8bf-900684da97c3",
          "Rule": {
            "ProcessFileName": "WindowsTerminal.exe",
            "TitlePattern": "prod",
            "TitleIsRegex": false
          }
        }
      ]
    }
  ],
  "ActiveTaskId": null,
  "Settings": {
    "RestoreOnExit": true
  }
}
```

Note the pattern is `prod`, not `◐ prod` — that window's live title carried a spinner frame at the
moment it was assigned.

### Notes for the tasks that build on this

- `WorkspaceService` raises its events on the `SynchronizationContext` captured at construction,
  matching `WindowTracker`, so task 07 can bind directly.
- `IsBound` / `FindTaskOf` are dictionary lookups, for task 07's unassigned pane (tracked minus
  bound) and task 09's hwnd → task mapping.
- **Title changes never persist.** `WorkspaceService` is only called on appear/disappear and user
  actions; at ~1 title event per second per busy terminal (task 07 § F) anything else would thrash
  the disk.
- Task 10's **Global** pseudo-task will need a home for assignments that belong to no
  `HydraWinTask` — either a `WorkspaceState.GlobalAssignments` list or an `IsGlobal` flag plus a
  switch-engine exclusion. Nothing here blocks either; flagging it because the current shape
  assumes every assignment lives under a task.

### Files

New: `src/HydraWin.Core/Workspaces/` — `ReattachRule.cs`, `WindowAssignment.cs`,
`HydraWinTask.cs`, `SettingsModel.cs`, `RuleMatcher.cs`, `WorkspaceService.cs`;
`src/HydraWin.Core/Persistence/` — `HydraWinPaths.cs`, `WorkspaceStore.cs`;
`tests/HydraWin.Core.Tests/` — `JsonStoreTests.cs`, `WorkspaceStoreTests.cs`,
`ReattachRuleTests.cs`, `RuleMatcherTests.cs`, `WorkspaceServiceTests.cs`.

Modified: `Workspaces/WorkspaceState.cs` and `Persistence/JsonStore.cs` (filled in, task 02
suppressions removed), `src/HydraWin.App/MainWindow.xaml`,
`src/HydraWin.App/ViewModels/MainViewModel.cs` (harness: seed / flush / clear buttons, task pane,
and wiring window appear/disappear into the re-attach path),
`tasks/initial_build/04_workspace_model_persistence.md`.

Deleted: none.
