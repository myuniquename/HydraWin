# UI and shell — how to

## Put a window into a task

Three ways, all ending in the same code path:

- **Drag** its row from the unassigned pane onto a task row.
- **Point** the task row's crosshair at any window on screen and release. HydraWin drops itself to
  the bottom of the z-order while you aim, so windows it was covering are reachable. `Esc` cancels.
- **Right-click** the row → *Assign to* → the task.

To move a window between tasks, drag it onto the other task or use *Move to*. To take it out
entirely, drag it back to the unassigned pane, or right-click → *Unassign*.

**Verify:** the task's window count goes up and the row leaves the unassigned pane. If the target
task is not the active one, the window disappears from the screen as it is assigned.

## Rebind a hotkey

1. **Settings… → Hotkeys.**
2. Click the box next to the action and **press the combination you want**. Backspace or Delete
   clears it; an empty box means no hotkey for that action.
3. **OK.**

The box only accepts what the resolver accepts: at least one modifier plus a digit, a letter or
`F1`–`F24`. A bare key is refused — it would swallow that key system-wide.

Rebinding tears down the hotkey thread and starts a new one, because a hotkey belongs to the thread
that registered it. A combination another application already owns simply fails to register;
HydraWin reports which ones on startup and the rest carry on working.

**Verify:** press the new combination — the task switches. Press the old one — nothing happens.
Restart HydraWin and try again; the binding is in `state.json`.

## Read the log

`%APPDATA%\HydraWin\logs\hydrawin.log`, plain text, one timestamped line per event, rolled once to
`hydrawin.1.log` at 1 MB.

It carries everything the status line said — switches with their summaries, re-attachments,
recoveries, save failures, hook and hotkey registration problems — plus full stacks for unhandled
exceptions. When something has gone wrong the two lines to look for are `CRASH:` and the
`restore attempted` line immediately after it, which says how many windows the handler put back
before the process died.

Deleting the folder is safe; it is diagnostics only.

## Recover from a crash

The handler already tried: it logs, restores every hidden window, and lets the process die. So the
usual answer is *start HydraWin again*.

If windows are still missing, work down the escape hatches in
[../workspaces/how_to.md](../workspaces/how_to.md) — `--restore-all` is the bottom of that list and
needs no working UI.

**Verify:** `%APPDATA%\HydraWin\journal.json` reads `[]`.

To rehearse it deliberately, a Debug build carries a **Throw a test exception** item at the bottom
of the tray menu. Hide a task first, then use it: the process should die, the hidden window should
come back, and the log should hold both the stack and the restore line.

## Keep the window out of the way

- **Stay on top** (toolbar, or Settings… → General) keeps HydraWin above other windows. On by
  default, because a switch ends by raising the task's windows and would otherwise bury the very
  window you click to switch again.
- **Closing the window hides it to the tray** rather than exiting, by default. Left-click the tray
  icon to bring it back, or press `Ctrl+Alt+H` from anywhere.
- The tray menu's two exits differ: **Restore all & exit** is unconditional insurance;
  **Exit** honours the restore-on-exit setting, which is the only way to deliberately leave windows
  hidden.

## Change the theme

**Settings… → General → Appearance.** *Follow Windows* is the default and tracks the system app
theme while HydraWin runs; *Light* and *Dark* pin it whatever Windows is set to.

It applies on **OK**, not on selection — this dialog edits copies and Cancel has to mean nothing
happened. A Windows high-contrast scheme overrides all three; HydraWin defers to it rather than
painting over an accessibility setting.

**Verify:** flip Windows between light and dark in Settings → Personalisation → Colours with
*Follow Windows* selected; HydraWin follows without a restart, title bar included. Then pin the
opposite theme and confirm an OS flip no longer moves it, and that `"Appearance"` in `state.json`
reads back as the name you chose.

The delete-task confirmation is a Win32 message box and stays light in every theme. That is known
and accepted, not a bug to re-report.

## Change a theme colour

Every colour is a named brush in `src/HydraWin.App/Themes/Palette.*.xaml` — the table of keys is in
[reference.md](reference.md#theme-brush-keys). **Edit all three palettes or none:** they must carry
identical key sets, and an unresolved `DynamicResource` fails silently by leaving the property at
its default, so a key added to one file and forgotten in another shows up only in that theme.

For high contrast, map the key onto a `SystemColors` *`ColorKey`* rather than picking a value.

To change a control's *shape* rather than its colour, the templates are in `Themes/Controls.*.xaml`.
Read § *WPF traps met along the way* in [architecture.md](architecture.md) first — most of it was
written from this exact work.

**Verify:** the drills below, in both themes.

## Add a setting

The shape to follow, since every existing setting follows it:

1. Add the property to `SettingsModel` with a default that fails safe, and an XML comment saying
   why that default.
2. Surface it on `MainViewModel` — read-only if only the dialog writes it.
3. Add it to `SettingsViewModel` (a copy, set in the constructor) and to `ApplySettings`, which
   writes everything back in one pass.
4. Add the control to the General tab of `SettingsWindow.xaml`.

Keep it serialising to a flat property name and a string enum: `state.json` is hand-editable and
that is a documented promise, not an implementation detail.

**`ApplySettings` is at seven parameters, which is Sonar S107's limit.** The next setting has to
change its shape — a single record of the dialog's result is the obvious move — rather than reach
for a suppression, which this repository carries none of.

`Appearance` is the only setting that is not a `bool`, and the only one anything reacts to beyond
storing it: `MainViewModel` raises `AppearanceChanged` and `App` re-applies the palette, mirroring
how `HotkeysChanged` already works. Swapping application resources is presentation plumbing and does
not belong in a view model.

**Verify:** toggle it, press Cancel, and confirm `state.json` did not change. Toggle it, press OK,
and confirm it did — and that it survives a restart.

## Drill the theme by hand

None of the rendering has automated coverage — Core holds no WPF reference — so these are the
evidence. Run them in both themes and report what was observed.

1. **Cold start, no flash.** Set Windows dark, restart HydraWin. The window is dark on its *first*
   frame; a one-frame white flash is visible to the eye at 60 Hz and is the specific failure the
   startup ordering exists to prevent.
2. **Live OS switch.** With *Follow Windows* selected, flip the system theme. Client area, toolbar,
   status bar, task rows and **title bar** all follow with no restart, and change **once** — visible
   strobing means the debounce or the unchanged-guard is broken.
3. **Close to tray, then switch, then reopen.** Proves the hook is on a window that is only hidden.
   Right-click the tray icon while it is hidden: the menu is already in the new theme.
4. **Both dialogs.** Settings… on all three tabs, and a window row's *Edit re-attach rule…*. Check
   hover, focus and disabled states, a `HotkeyBox` mid-capture, and an invalid regex — the red error
   text is the one literal that had to change for dark, so read it.
5. **Menus.** Both row context menus and the tray menu. *Move to* and *Assign to* are
   `SubmenuHeader`s and are the items most likely to be left light; the separators are the next.
   *Close to tray* must still show its checkmark.
6. **Gestures.** Drag a window row onto a task (drop outline and wash), drag a task row to reorder
   (insertion line), and run the crosshair over another window (accent frame).
7. **High contrast.** Settings → Accessibility → Contrast themes → *Desert* → Apply. HydraWin
   repaints in that scheme's own colours without a restart, and returns when it is turned off.

Capture the evidence with `PrintWindow` and `PW_RENDERFULLCONTENT` rather than trusting the eye —
see the next section. Note that an owned dialog hangs off its **owner** in the UI Automation tree,
so drive one by finding its real top-level window handle rather than walking the desktop root.

## Drive the UI from a test

There is no automated UI test suite; the Core logic is unit-tested and the shell is verified by
hand. If you need to drive it anyway, what worked:

- **UI Automation** finds controls by name and control type. Note that an owned dialog hangs off
  its **owner** in the UIA tree, not off the desktop root, so find it by walking real top-level
  windows instead. Tray icons live under `Shell_TrayWnd` with `AutomationId = NotifyItemIcon`, and
  the shell pads their accessible name — match on a *contains*, not equality.
- **UI Automation only exposes realised list items**, so a long list cannot be read in full from
  outside even with virtualisation disabled.
- **`PrintWindow` with `PW_RENDERFULLCONTENT`** captures the window including parts that are
  occluded, which is the only reliable way to check what was actually rendered.
- Be **DPI-aware** in the driver (`SetProcessDpiAwarenessContext`) or every coordinate will be
  wrong on a scaled desktop.

## Capture a screenshot for the docs

The images in [`../images/`](../images/) come from the real application, and a published screenshot
of a window manager shows **every window on the machine** — so the shoot is staged rather than taken
of whatever happens to be open.

1. **Shoot on a new OS virtual desktop** (`Win+Ctrl+D`). Everything left behind is DWM-cloaked, and
   `WindowFilter` drops cloaked windows, so the inventory contains only what you open there. Nothing
   personal can reach the picture, and nothing of the user's is hidden, moved or closed.
   `Win+Ctrl+F4` closes it again afterwards. Note the shell only reacts if the whole combination
   arrives in **one `SendInput` batch** — separate `keybd_event` calls are ignored.
2. **Delete `state.json`** and write the demo tasks by hand: a `ReattachRule` per window, matching
   `ProcessFileName` plus a title substring. Terminals take any title you like
   (`$Host.UI.RawUI.WindowTitle`), which is also how you stage a Claude Code activity marker.
3. **Give a browser a couple of seconds** before shooting. It appears under a placeholder title and
   only says what page it is showing a moment later; the rules are offered again on that rename, so
   it does re-attach either way — just not instantly.
4. Drive the rest with the hotkeys and the mouse: `Ctrl+Alt+<n>` to switch, so the hidden chips and
   the switch summary are real, and `FlashWindowEx` against a hidden window for a badge — the same
   `HSHELL_FLASH` any application would raise.

Capturing:

- `PrintWindow` + `PW_RENDERFULLCONTENT` for the window and the dialogs, cropped to
  `DWMWA_EXTENDED_FRAME_BOUNDS`; `GetWindowRect` includes the invisible resize border and would
  leave a dead margin round the picture.
- **Popups need a screen grab.** A context menu, and the tray menu, are separate top-level windows,
  so `PrintWindow` on the main window never contains them. Ask UI Automation for the popup's
  bounding rectangle and `CopyFromScreen` it.
- The tray icon usually lives in the **overflow flyout**, not the taskbar itself: open
  *Show Hidden Icons* first, then find the button by name under the overflow window.
- Flatten to 24-bit before saving. Nothing else has to be true of the file.

Finish by putting the desktop back: **Show all**, `--restore-all`, and confirm `journal.json` reads
`[]` before closing the demo windows.
