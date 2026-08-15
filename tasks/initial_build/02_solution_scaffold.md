# Task 02 — Solution scaffold

Status: **not started**
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

*(what was done, deviations and why, package versions pinned, publish size, and the list of
new / modified / deleted files)*
