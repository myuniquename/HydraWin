# Task 02 — Solution scaffold

Status: **done** (2026-08-15, accepted 2026-08-16)
Depends on: nothing (01 informs later tasks, not this one).

## Motivation

Every later task assumes the same solution shape, package set, and build settings. Settling them
once keeps the per-task diffs about behaviour, not plumbing.

## Background

HydraWin is a C# / .NET 10 WPF tray-style utility. Architecture (restated so this file stands alone):

```
┌──────────────────────  HydraWin.App (WPF, MVVM)  ─────────────────────────┐
│ Main window: task table + unassigned pane + drag/drop + badges        │
│ Tray icon, global hotkeys, --restore-all CLI entry                    │
└───────────────────────────────┬───────────────────────────────────────┘
┌──────────────────────────  HydraWin.Core  ───────────────────────────────┐
│ WindowTracker | WorkspaceEngine | RecoveryJournal | NotificationHub   │
│ Persistence (JSON, %APPDATA%\HydraWin) | Interop/ (all P/Invoke)         │
└───────────────────────────────────────────────────────────────────────┘
```

Core is UI-free (no WPF references) so its logic is unit-testable; App references Core.

## Work

### A. Solution and projects
- `HydraWin.sln` at root; `src/HydraWin.Core` (`net10.0-windows`, class library), `src/HydraWin.App`
  (`net10.0-windows`, WPF exe, `<UseWPF>true</UseWPF>`, assembly/exe name `hydrawin`),
  `tests/HydraWin.Core.Tests` (xUnit, references Core).
- `Directory.Build.props` at root: `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<ImplicitUsings>enable</ImplicitUsings>`,
  `<LangVersion>latest</LangVersion>`.
- `.editorconfig` at root: default dotnet conventions, 4-space indent, file-scoped namespaces.
- `.gitignore` for `bin/`/`obj/` (harmless under Perforce; the user manages VCS either way).

### B. Packages
- App: `CommunityToolkit.Mvvm`, `Hardcodet.NotifyIcon.Wpf` (tray icon; used from task 08).
- Core: none beyond the BCL (`System.Text.Json` is in-box).
- Tests: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`.
- Pin whatever current stable versions restore cleanly; record them in the completion note.

### C. Skeletons (compile-clean placeholders only)
- `src/HydraWin.Core/Interop/NativeMethods.cs` — empty static partial class with a header comment:
  *all* P/Invoke goes here (later tasks add signatures).
- `src/HydraWin.Core/` folders: `Tracking/`, `Workspaces/`, `Recovery/`, `Notifications/`,
  `Persistence/` — with a placeholder type each so folders exist in the project.
- `src/HydraWin.App/App.xaml(.cs)` — startup that parses args: `--restore-all` prints
  `restore-all: not implemented yet` and exits 0 (task 05 fills it in); otherwise shows an empty
  `MainWindow` titled "HydraWin".
- One trivial unit test in `tests/HydraWin.Core.Tests` so `dotnet test` exercises the pipeline.

### D. Publish profile
- Verify `dotnet publish src/HydraWin.App -c Release -r win-x64 --self-contained
  -p:PublishSingleFile=true` produces a runnable single `hydrawin.exe`. Record its size.

## Verification

- `dotnet build HydraWin.sln` — succeeds with zero warnings.
- `dotnet test HydraWin.sln` — 1/1 passing (paste the total).
- `dotnet run --project src/HydraWin.App` — an empty "HydraWin" window appears and closes cleanly.
- `hydrawin.exe --restore-all` (from publish output) — prints the placeholder line, exit code 0.

## Record on completion

Built the solution, the three projects, the shared build settings and the placeholder skeleton.
Everything in *Verification* above was run and passed; the numbers are below.

### Deviations, and why

- **xunit v3, not v2 — and no `Microsoft.NET.Test.Sdk` / `xunit.runner.visualstudio`.** The user
  chose the v3 line. That forced a second, non-optional change: the .NET 10 SDK **no longer runs
  Microsoft.Testing.Platform projects through VSTest**, so `dotnet test` failed outright with
  *"Testing with VSTest target is no longer supported … on .NET 10 SDK and later"*. The fix is
  `global.json` at the root:

  ```json
  { "test": { "runner": "Microsoft.Testing.Platform" } }
  ```

  With that, the test project needs only `xunit.v3` (it self-hosts the runner as an `Exe`), and
  the two VSTest packages are dropped. **The invocation changes to
  `dotnet test --solution HydraWin.sln`** — a bare `dotnet test HydraWin.sln` is now interpreted
  differently. `_plan.md` § *Working rules* has been updated to match.
- **`global.json` added**, which the task did not call for. It carries only the `test` section
  above; no SDK version is pinned, so the newest installed SDK is still used.
- **`Interop/ConsoleAttach.cs` added** beyond the empty `NativeMethods.cs` the task specifies.
  `hydrawin.exe` is a `WinExe` and has no console, so without `AttachConsole(ATTACH_PARENT_PROCESS)`
  the `--restore-all` line goes nowhere and neither this task's verification nor task 05's real
  implementation could print anything. Per CLAUDE.md the P/Invoke belongs in Core, and keeping it
  in its own file leaves `NativeMethods.cs` literally empty as the task asks.
- **`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` on Core only.** `[LibraryImport]`
  source-generated marshalling requires it (`SYSLIB1062`). Enabling it once in the one project
  allowed to declare P/Invoke lets every later task use the modern generator instead of
  `DllImport`.
- **`Interop/IWindowApi.cs` added** as an empty seam interface. Task 05 needs "fakes of the Win32
  layer" and task 06 must assert call order in a "scripted fake interop layer" to prove the
  journal-before-hide invariant; neither works if callers bind to static P/Invoke. Establishing
  the seam now avoids a refactor at task 05.
- **`HydraWin.sln`, not `.slnx`.** `dotnet new sln` on the .NET 10 SDK defaults to the new XML
  format; `--format sln` was used because `_plan.md` specifies `HydraWin.sln`.
- **`spikes/.gitignore` deleted**, superseded by the root `.gitignore`.

### Also done here: a task 01 correction

`CLAUDE.md` § *Gotchas* still claimed that hidden windows may not produce taskbar-flash signals and
that "notification coverage for hidden windows comes from the title-change watcher". Task 01
measured the opposite; `_plan.md` and `09_notifications.md` were corrected then, but `CLAUDE.md`
was missed — leaving a false statement in the file loaded into every session. Rewritten to the
measured result.

### Package versions pinned

| Package | Version | Where |
| --- | --- | --- |
| `CommunityToolkit.Mvvm` | 8.4.2 | App |
| `Hardcodet.NotifyIcon.Wpf` | 2.0.1 | App (unused until task 08) |
| `xunit.v3` | 4.0.0 | Tests |

Core takes no packages. `Hardcodet.NotifyIcon.Wpf` targets `net8.0-windows7.0`; a
`net10.0-windows` project consumes it without complaint.

### Verification results

- `dotnet build HydraWin.sln` → **Build succeeded, 0 Warning(s), 0 Error(s)** (warnings are errors).
- `dotnet test --solution HydraWin.sln` → **total: 1, failed: 0, succeeded: 1, skipped: 0**.
- App launch → a 900×600 window titled `HydraWin` appears; `CloseMainWindow()` exits with code 0.
  Confirmed via the task 01 spike: `0x000412B0 pid=20124 hydrawin vis=Y SW_SHOWNORMAL(1)
  (245,245)-(1145,845) 900x600 "HydraWin"`.
- `dotnet publish src/HydraWin.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
  → **`hydrawin.exe` is 157.3 MB**. WPF's native components cannot be bundled and sit beside it:
  `D3DCompiler_47_cor3.dll` (4.5 MB), `wpfgfx_cor3.dll` (1.9 MB), `PresentationNative_cor3.dll`
  (1.2 MB), `PenImc_cor3.dll`, `vcruntime140_cor3.dll` — so "one self-contained exe" is really
  six files. Worth knowing before task 11 describes distribution.
- Published `hydrawin.exe --restore-all` → stdout `restore-all: not implemented yet`, **exit code 0**,
  nothing on stderr.
- `dotnet format --verify-no-changes` → exit 0.
- **The task 01 spikes still build**: all three at 0 warnings / 0 errors under the new root
  `Directory.Build.props`, so the `hideshow rescue` panic tool is intact.

### Files

New:

- `HydraWin.sln`, `Directory.Build.props`, `global.json`, `.gitignore`
- `src/HydraWin.Core/HydraWin.Core.csproj` and placeholders: `Interop/NativeMethods.cs`,
  `Interop/IWindowApi.cs`, `Interop/ConsoleAttach.cs`, `Tracking/TrackedWindow.cs`,
  `Workspaces/WorkspaceState.cs`, `Persistence/JsonStore.cs`, `Recovery/JournalEntry.cs`,
  `Notifications/NotificationRule.cs`
- `src/HydraWin.App/HydraWin.App.csproj`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`,
  `MainWindow.xaml.cs`, `AssemblyInfo.cs` (the WPF template's `ThemeInfo` attribute, kept as-is),
  `ViewModels/MainViewModel.cs`
- `tests/HydraWin.Core.Tests/HydraWin.Core.Tests.csproj`, `ScaffoldTests.cs`

Modified: `CLAUDE.md` (stale gotcha), `tasks/initial_build/_plan.md` (test invocation),
`tasks/initial_build/02_solution_scaffold.md` (this record).

Deleted: `spikes/.gitignore`.

Each placeholder's XML comment names the task that fills it and carries the constraints that task
must honour, so nobody has to re-read task 01 to avoid the traps it found.

### Addendum — SonarAnalyzer.CSharp added (same day, at the user's request)

`SonarAnalyzer.CSharp` **10.32.0.713** is referenced by all three solution projects
(`PrivateAssets=all`), so its findings are build failures under warnings-as-errors. It is
deliberately **not** applied to `spikes/`; the reference sits in each `.csproj` rather than in the
root `Directory.Build.props`, which is shared with the spikes.

The first analyzed build reported **six findings, all in Core, all caused by the deliberately
empty placeholders**:

| Rule | Count | Where |
| --- | --- | --- |
| S2094 *Classes should not be empty* | 5 | `TrackedWindow`, `WorkspaceState`, `JsonStore<T>`, `JournalEntry`, `NotificationRule` |
| S2326 *Unused type parameters should be removed* | 1 | `JsonStore<T>` — a consequence of the same emptiness |

Nothing was reported in `HydraWin.App` or `HydraWin.Core.Tests`. Two types predicted to be flagged
were **not**: `NativeMethods` (S2094 does not fire on an empty *static* class) and `IWindowApi`
(S4023 is not in the default rule set), so neither needed a suppression.

The user granted permission for narrow, temporary suppressions for exactly this placeholder
conflict. Each affected type is wrapped in a `#pragma warning disable` / `restore` pair — never a
file-wide or project-wide setting — with a comment naming the task that removes it and the members
that will replace the emptiness. `#pragma` was chosen over `[SuppressMessage]` because that
attribute only applies when its `Category` string matches the diagnostic exactly, and a wrong
category silently does nothing. **These are the repository's only suppressions.**

`.editorconfig` was **not** changed. The only reason to touch it would be
`dotnet_diagnostic.SXXXX.severity` entries, which is the rule-changing the user withheld
permission for.

Verified: the analyzer genuinely runs rather than merely being referenced. An identical S3923
violation compiled into `HydraWin.Core` fails the build, and the same code in
`spikes/HideShow` builds clean — confirming both that the analyzer is active in the solution and
that the spikes are excluded. S1481 was likewise confirmed to fire in all three solution projects
individually. All probe files were deleted afterwards.
