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

**Verify:** toggle it, press Cancel, and confirm `state.json` did not change. Toggle it, press OK,
and confirm it did — and that it survives a restart.

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
