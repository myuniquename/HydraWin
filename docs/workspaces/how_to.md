# Workspaces — how to

## Get windows back when something has gone wrong

The escape hatch, in order of how much has broken.

1. **HydraWin is running and responsive** — press `Ctrl+Alt+0` (*show all*), or use the tray menu's
   *Show all windows*.
2. **The UI is wedged but the process is alive** — press `Ctrl+Alt+Shift+R`. The hotkeys own a
   thread of their own and this one runs the restore inline on it, so it does not need the UI
   thread.
3. **The process is gone, or will not start** — run the headless restore:

   ```
   hydrawin.exe --restore-all
   ```

   It reads `%APPDATA%\HydraWin\journal.json`, restores everything listed, and prints what it did.
   It bypasses the single-instance mutex on purpose, so it works while a wedged first instance
   still holds it.

**Verify:** every window is back on the taskbar, and `%APPDATA%\HydraWin\journal.json` reads `[]`.

## Run the crash drill

This is the acceptance test for the whole invariant. Run it after touching anything in
`SwitchEngine`, `RecoveryJournal` or `RestoreService`.

1. Open a throwaway window — a terminal you can afford to lose. Assign it to a task, then switch to
   a *different* task so it is hidden.
2. Confirm it is genuinely gone: absent from the taskbar and from Alt-Tab.
3. Read `%APPDATA%\HydraWin\journal.json`. It must already contain the window's entry, with its
   placement, **while the window is still hidden**. That file, at that moment, is the invariant made
   visible.
4. Kill HydraWin the hard way — Task Manager, or `Stop-Process -Force`. No clean shutdown, so no
   exit handler runs.
5. Run `hydrawin.exe --restore-all`.

**Verify:** it prints `restored 1 window(s), dropped 0 stale entries`, the window is back at its
previous position, and the journal is `[]`.

Two variants worth running at least once:

- **Startup recovery instead of the CLI.** Repeat steps 1–4, then launch HydraWin normally with no
  arguments. The window comes back during startup and the status bar reports the recovery.
- **A stale entry.** Repeat step 1, then close the hidden window's *process* while it is hidden,
  then kill HydraWin and run `--restore-all`. It must print `dropped 1 stale entry` and exit
  cleanly — the handle is gone, and dropping the entry is correct.

To exercise the worst interleaving — a crash *between* the journal flush and the first hide — use
`SwitchEngine.AfterJournalFlush`, a test hook that fires at exactly that point. Wiring it to
`Environment.FailFast` is a truer crash than a breakpoint, since not even `OnExit` runs.

## Teach HydraWin about a stubborn application

Symptoms and what they mean:

| Symptom | Cause | What to do |
| --- | --- | --- |
| The window is absent from the unassigned list | It failed a filter clause | Point the crosshair picker at it — the app names the reason |
| The row shows a **won't hide** chip | `SW_HIDE` was refused | The process is protected, or became elevated after HydraWin first saw it. Restart it unelevated |
| It does not rejoin its task after a restart | Its rule no longer matches | Edit the rule — below |
| It rejoins the *wrong* task | Two tasks hold rules that both match | Edit the losing rule to be more specific |

To edit a rule: right-click the window's row → **Edit re-attach rule…**. The dialog previews which
*other* open windows the rule currently matches, live as you type, so a pattern that is too broad
shows itself immediately. A title pattern is a substring unless you tick the regex box.

**Verify:** close the application, reopen it, and watch the status bar say
*Re-attached "…" to "…"*. That is the only real proof — the rule is exercised on reappearance, not
on save.

## Hand-edit the model

`%APPDATA%\HydraWin\state.json` is meant to be edited by hand; that is why it uses flat property
names and string enums. Close HydraWin first — it is the only writer, and it saves on a debounce,
so a running instance will overwrite you.

Useful edits: renaming a task, reordering by changing `Order` (renumber from 1 — the
`Ctrl+Alt+1..9` hotkeys address tasks by it), widening a `TitlePattern`, or clearing `Hotkeys` and
`NotificationRules` to `[]` so the shipped defaults are seeded again on next launch.

**Verify:** start HydraWin and check the task list. If the file could not be parsed the app starts
with defaults, preserves your file byte-for-byte as `state.json.corrupt-<timestamp>` beside it, and
says so in the status bar — so a mistake costs a restart, not your layout.

## Add a window to a task without dragging

Two gestures, one code path — they cannot drift apart because both call
`SwitchEngine.AssignWindowToTask`.

- **Drag** a row from the unassigned pane onto a task row.
- **Point** the crosshair on the task row at any window on screen and release. HydraWin drops
  itself to the bottom of the z-order while you aim, so windows it was covering are reachable.

If the target task is not the active one, the window is hidden the moment it is assigned — through
the same journalled path a switch uses.

**Verify:** the row moves out of the unassigned pane, the task's window count goes up, and if the
task was inactive the window disappears and `journal.json` gains exactly one entry.

## Check that unassigned windows are safe

The promise is that HydraWin never touches a window you did not assign. To confirm it after a
change to `SwitchPlan`:

1. Leave a window unassigned — anything you are actually using.
2. Switch between two tasks several times.

**Verify:** it stays visible throughout, and its handle never appears in `journal.json`.
