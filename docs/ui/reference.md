# UI and shell — reference

## Main window

| Element | Behaviour |
| --- | --- |
| Task row, clicked | Switches to that task; HydraWin keeps the keyboard |
| Task row, double-clicked name | Inline rename. Enter commits, Esc abandons, any click elsewhere commits |
| Task row, dragged | Reorders; `Order` is renumbered from 1 across the whole list |
| Expander | Shows or hides the task's window rows |
| Colour chip | The task's accent, from `ColorHex` |
| Activity marker | The strongest live Claude Code marker among the task's windows, visible even when the task is collapsed |
| Badge | Count of windows waiting for attention; clicking it switches, focuses the newest and clears it |
| `N win` | How many windows the task holds |
| Crosshair | Press and drag onto any window on screen to add it to this task |
| Window row, dragged | Assigns to a task, or unassigns when dropped on the right-hand pane |
| Window row, right-clicked | Focus · Unassign · Move to · Edit re-attach rule… |
| Unassigned pane | Every managed window belonging to no task. These stay visible through every switch |

Row chips: **hidden** while HydraWin is the reason a window is off screen, and **won't hide** when
a window refused `SW_HIDE` — a protected process, or one that became elevated after HydraWin first
saw it.

## Keyboard

| Key | Effect |
| --- | --- |
| `Del` | Deletes the active task. Ignored while a text box has focus or a pick is running. Confirmation only when the task holds windows |
| `Enter` | Commits an inline rename |
| `Esc` | Abandons an inline rename; cancels a pick |
| `Ctrl+Alt+1`…`9` | Switch to that task, and land focus in it |
| `Ctrl+Alt+0` | Show all windows |
| `Ctrl+Alt+H` | Show or hide the HydraWin window |
| `Ctrl+Alt+Shift+R` | Panic restore, straight from the journal |

Global hotkeys are configurable; the stored form is in
[../workspaces/reference.md](../workspaces/reference.md).

## Drag-and-drop contract

| Constant | Value | Carries |
| --- | --- | --- |
| `DragDropSupport.WindowFormat` | `"HydraWin.Window"` | A window handle, boxed as `long` |
| `DragDropSupport.TaskFormat` | `"HydraWin.Task"` | A task `Guid` |

Rows are located by walking the visual tree for a tagged ancestor:

| Tag | Element |
| --- | --- |
| `TaskRow` | A whole task row — the drop target and the reorder subject |
| `WindowRow` | One window row |
| `Picker` | The crosshair, which handles its own press |
| `Badge` | The notification badge, which handles its own press |

A drag starts on **mouse-down** for a window row and on the system drag threshold for a task row —
see [architecture.md](architecture.md#drag-and-drop). Adorners: `DragGhostAdorner` follows the
pointer at window level, `HighlightAdorner` outlines a drop target, `InsertionAdorner` shows where a
dragged task will land.

## Dialogs

### Settings

Modal, opened from the toolbar and from the tray. Three tabs; edits are copies, written back only
on OK.

| Tab | Contents |
| --- | --- |
| General | Restore hidden windows on exit · Close to tray · Stay on top · Tray balloon on notification |
| Hotkeys | One capture box per action, with the reason shown inline when a combination is unusable |
| Notifications | Add / edit / delete title rules, with a live preview of what each currently matches |

There is no launch-at-login setting. It was dropped from the project; HydraWin touches no registry.

### Re-attach rule editor

Opened from a window row's context menu. Process file name, title pattern, a substring/regex
toggle, and a live list of which *other* open windows the rule matches. Saving edits the rule in
place and does not disturb the live binding — the rule says how to recognise the window *next* time.

An invalid regex blocks Save with an inline error here, and is saved-but-disabled in the
notification editor; the reasoning is in
[architecture.md](architecture.md#settings-and-rule-dialogs).

## Tray menu

Rebuilt each time it opens.

| Item | Effect |
| --- | --- |
| *(task list)* | Switch to that task, landing focus in it. The active one is marked |
| Show all windows | Restore everything hidden, leave no task active |
| Open HydraWin | Show the main window (same as a left-click on the icon) |
| Settings… | Open the settings dialog |
| Close to tray | Toggles the setting in place |
| Restore all & exit | Unconditional: restores, then exits |
| Exit | Honours the restore-on-exit setting |
| Throw a test exception | **Debug builds only** — the crash drill |

## Process lifecycle

| Name | Kind | Purpose |
| --- | --- | --- |
| `Local\HydraWinSingleton` | Mutex | Held by the first instance |
| `Local\HydraWinShowWindow` | Event | A second launch sets it; the first instance surfaces its window |
| `Local\HydraWinRecoveryJournal` | Mutex | Serialises journal writes across processes |

Startup order: `--restore-all` is handled before anything else and touches neither the UI nor the
single-instance mutex; then the mutex; then startup recovery from the journal; then the window,
tray, hotkeys and shell hook.

## Activity log

| | |
| --- | --- |
| Path | `%APPDATA%\HydraWin\logs\hydrawin.log` |
| Rollover | Once, to `hydrawin.1.log`, at 1 MB — so at most 2 MB on disk |
| Format | `yyyy-MM-dd HH:mm:ss.fff␠␠<message>`, one line per event |
| Exceptions | Context line, then the full exception and stack |
| On its own failure | Silent. A log must never be the reason the app fails |

Every status-line message is also logged; the two share one funnel.

## Build and publish

Two projects plus a test project: `HydraWin.Core` (no packages, no WPF, all the logic and all the
P/Invoke) and `HydraWin.App` (WPF, `CommunityToolkit.Mvvm`, `Hardcodet.NotifyIcon.Wpf`). Package
versions live in the `.csproj` files, which is the only place they should be read from.

| Command | Note |
| --- | --- |
| `dotnet build HydraWin.sln` | Warnings are errors, and `SonarAnalyzer.CSharp` findings are warnings |
| `dotnet test --solution HydraWin.sln` | **The `--solution` flag is required** |
| `dotnet format --verify-no-changes` | Exit 0 before any completion report |
| `dotnet run --project src/HydraWin.App` | Or run the built `hydrawin.exe` |

`--solution` is not optional: `global.json` puts the SDK into Microsoft.Testing.Platform mode,
because the .NET 10 SDK no longer runs xunit v3 projects through VSTest — a bare
`dotnet test HydraWin.sln` is interpreted differently. `global.json` pins no SDK version, only the
test runner.

`<AllowUnsafeBlocks>` is enabled on **Core only**: `[LibraryImport]`'s source-generated marshalling
requires it (`SYSLIB1062`), and Core is the one project allowed to declare P/Invoke.

Publishing self-contained produces **six files, not one**. WPF's native components cannot be
bundled into a single file and sit beside the executable:

```
dotnet publish src/HydraWin.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
  → hydrawin.exe  ~157 MB
  + D3DCompiler_47_cor3.dll, wpfgfx_cor3.dll, PresentationNative_cor3.dll,
    PenImc_cor3.dll, vcruntime140_cor3.dll
```

`spikes/` is deliberately outside the solution and carries no analyser — see
[`spikes/README.md`](../../spikes/README.md).

## Assets

The tray and window icon is `src/HydraWin.App/Assets/hydrawin.ico`, generated from
`hydrawin.svg` by rendering it headless at 16/20/24/32/48/64/128/256 and packing the PNGs into a
multi-image ICO (Vista and later accept PNG payloads at every size, so no BMP/AND-mask encoding is
needed).

If you regenerate it, **assert that each render came out at the size you asked for**. A first
attempt shipped an `.ico` whose directory claimed every size but whose entries all held the same
oversized image, and Windows drew the taskbar icon as a smudge — the renderer had silently ignored
a malformed size argument.
