# UI and shell

The window the user actually drives, and everything around it. **`MainWindow`** shows the task
table and the unassigned pane and owns the mouse handling that keeps "click to switch" and "drag to
rearrange" apart; **`MainViewModel`** turns Core's events into rows; **`WindowPicker`** is the
crosshair that grabs any window on screen; **`TrayIcon`**, **`HotkeyService`** and
**`SingleInstance`** make HydraWin a companion rather than an application you keep in front of you.
This folder is the canonical documentation for HydraWin's presentation layer, its input gestures
and its process lifecycle.

| Doc | Read it for |
| --- | --- |
| [architecture.md](architecture.md) | The layering rule, drag-and-drop, the picker gesture, focus policy, tray and single instance, the hotkey thread, crash handling |
| [how_to.md](how_to.md) | Adding a window to a task, rebinding a hotkey, reading the log, adding a setting |
| [reference.md](reference.md) | Drag payloads and row tags, dialogs, log format and location, keyboard behaviour |

Related: [../workspaces/README.md](../workspaces/README.md) for what a switch actually does ·
[../notifications/README.md](../notifications/README.md) for what puts a badge on a row.

## What it does

One window, two panes: tasks on the left, unassigned windows on the right. A task row expands to
show its windows, carries a colour chip, a live activity marker, a notification badge and a
crosshair picker. Clicking a row switches to that task. Dragging a window row onto a task assigns
it; dragging a task row reorders it.

Around that: a tray icon that survives the window being closed, global hotkeys that work when the
UI does not, a settings dialog, a per-assignment rule editor, and a crash handler whose job is to
get the user's windows back before the process dies.

The layering rule is absolute and is the reason most of the design below looks the way it does:
**no Win32 above Core.** Views bind, view models orchestrate, and every P/Invoke lives in
`src/HydraWin.Core/Interop/` behind a small interface. When the UI needed a window icon, an
`ExtractIconExW` wrapper went into Core behind `IIconSource` rather than a `System.Drawing` call
going into the App.

## Component map

```
   ┌──────────────────────────────────────────────────────────────┐
   │ App  (App.xaml.cs)                                           │
   │   --restore-all ─── before anything else, no UI, no mutex    │
   │   SingleInstance ── mutex + named event                      │
   │   crash handlers ── log, restore, then let it crash          │
   └───────┬───────────────────┬───────────────────┬──────────────┘
           │                   │                   │
   ┌───────▼───────┐   ┌───────▼───────┐   ┌───────▼───────────┐
   │  MainWindow   │   │   TrayIcon    │   │  HotkeyService    │
   │  + dialogs    │   │               │   │  (own thread)     │
   └───────┬───────┘   └───────┬───────┘   └───────┬───────────┘
           │                   │                   │
           │                   ▼                   │ panic restore runs
           │           ┌───────────────┐           │ inline on this thread
           └──────────▶│ MainViewModel │◀──────────┘
                       │  Tasks[]      │
                       │  Unassigned[] │
                       └───────┬───────┘
                               │  no Win32 crosses this line
   ────────────────────────────┼──────────────────────────────────
                               ▼
                    Core: WorkspaceService · SwitchEngine
                          WindowTracker · NotificationHub
```

## Key files

| Purpose | File |
| --- | --- |
| The window, its templates and its mouse handling | `src/HydraWin.App/MainWindow.xaml`, `MainWindow.xaml.cs` |
| Rows, commands, and the bridge to Core | `src/HydraWin.App/ViewModels/MainViewModel.cs` |
| One task row / one window row | `src/HydraWin.App/ViewModels/TaskViewModel.cs`, `WindowViewModel.cs` |
| Drag payloads, hit-testing, ghost and drop adorners | `src/HydraWin.App/DragDropSupport.cs` |
| The crosshair gesture | `src/HydraWin.App/WindowPicker.cs` |
| Settings dialog and its view models | `src/HydraWin.App/Views/SettingsWindow.xaml`, `ViewModels/SettingsViewModel.cs` |
| Re-attach rule editor | `src/HydraWin.App/Views/RuleEditorWindow.xaml`, `ViewModels/RuleEditorViewModel.cs` |
| Press-the-combination hotkey capture | `src/HydraWin.App/Controls/HotkeyBox.cs` |
| Tray presence and menu | `src/HydraWin.App/Services/TrayIcon.cs` |
| Global hotkeys on a thread of their own | `src/HydraWin.App/Services/HotkeyService.cs` |
| One instance, and how a second one surfaces it | `src/HydraWin.App/Services/SingleInstance.cs` |
| Entry point, recovery, crash handlers | `src/HydraWin.App/App.xaml.cs` |
| The activity log | `src/HydraWin.Core/Diagnostics/AppLog.cs` |
