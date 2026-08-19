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

## Drill the away-pause by hand

The whole Win32 half of the timer — lock, unlock, suspend, resume — has no automated coverage,
for the same reason the shell hook has none. Run this after touching `SessionListener`,
`Win32SessionApi` or the `WTS` wrappers in `NativeMethods`. Report what you observed, never an
exit code.

1. Switch to a task. Its clock starts moving a second at a time while every other row stands
   still — that alone is the check that it is running. Note the figure. Open
   `%APPDATA%\HydraWin\logs\hydrawin.log` in something that follows the file.
2. Press `Win+L`. Wait three minutes by a clock that is not this machine's. Unlock.

   **Verify:** the row grew by **at most one minute**, not three, and the log carries
   `Timing paused — the screen is locked.` followed by `Timing resumed.`

3. Switch to a task, note the figure, then Start → Sleep. Wait five minutes. Wake the machine and
   unlock.

   **Verify:** the row grew by at most one minute. Then read the log: whether a pause line appeared
   *before* the sleep tells you whether `PBT_APMSUSPEND` actually arrived on this machine. Either
   answer is fine — the two-minute credit clamp bounds the damage regardless — but it is worth
   recording which one this hardware does.

4. Sleep the machine while it is **already locked**, wake it, and leave it on the lock screen for a
   minute before unlocking.

   **Verify:** the figure does not move until the actual unlock. This is the case a nesting count
   would get wrong, and the only way to see it is to do it.

5. Set a one-minute screensaver with *On resume, display logon screen* **unticked**, switch to a
   task and leave the machine alone.

   **Verify:** the figure keeps climbing. This is the documented limitation, not a bug — see
   *Known limitation: a screensaver that does not lock does not pause the clock* in
   [architecture.md](architecture.md). Tick the box and repeat: now it pauses, because the session
   locks.

6. Switch to a task, wait two minutes, then kill `hydrawin.exe` from Task Manager and relaunch.

   **Verify:** at most one minute was lost, and no other task's figure moved.

## Reset a task's timer

Right-click the task row and choose **Reset time**. There is no confirmation — it is a counter,
not data — but the figure that was discarded is written to the log first, so it can be read back
out of `%APPDATA%\HydraWin\logs\hydrawin.log` if it was cleared by accident.

To zero one by hand instead, close HydraWin and set the task's `ActiveSeconds` to `0` in
`state.json`. Deleting the property entirely reads as `0` as well.

**Verify:** the row reads `00:00:00` and stops moving, and the tooltip reads `Never switched to`.

## Finish a task and close its windows

Right-click the task row and choose **Delete and Close**. It asks each of the task's windows to
close — the same request the title-bar × makes, never a force-quit — and deletes the task only if
they all actually went.

The dialog always appears when the task holds windows, because this is the one command that can
lose unsaved work. A task with no windows deletes straight away.

If an application puts up a "Save changes?" prompt and you dismiss it with *Cancel*, **nothing is
deleted**: the task stays, holding the windows that are left, and the status bar says how many
refused. Answer the prompt and run the command again. Windows that survived are left visible rather
than re-hidden; the next switch puts them back.

**Drill it** with two throwaway windows — one that closes normally, and one that refuses. A
PowerShell WinForms window whose `FormClosing` sets `$e.Cancel = $true` is the cheapest stand-in for
an unanswered save prompt, and it refuses forever, so the drill is not a race:

1. Put both windows in a scratch task, then switch to a different task so they are hidden. Confirm
   `journal.json` lists both.
2. **Delete and Close** the scratch task. Both windows come back *before* anything is asked to
   close — that is what makes a save prompt reachable. The cooperative one goes; the stubborn one
   stays, visible.
3. Expect the task still in the list, the status bar reading *"…" kept — 1 of 2 window(s) did not
   close*, and **`journal.json` back to `[]`** — nothing hidden, no orphan entry for the window that
   closed.
4. Let the stubborn window close, and repeat. The task disappears, the status bar reports the count,
   `journal.json` is `[]`, and `state.json` no longer lists the task.
5. Answer the confirmation with **No** instead, and nothing at all happens: no window is un-hidden,
   the journal entry is untouched, the task stays.

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
on save. A browser is worth testing too: it appears under a placeholder title and is claimed a
moment later, when it renames itself to the page it is showing.

## Hand-edit the model

`%APPDATA%\HydraWin\state.json` is meant to be edited by hand; that is why it uses flat property
names and string enums. Close HydraWin first — it is the only writer, and it saves on a debounce,
so a running instance will overwrite you.

Useful edits: renaming a task, reordering by changing `Order` (renumber from 1 — the
`Ctrl+Alt+1..9` hotkeys address tasks by it), widening a `TitlePattern`, switching `Appearance`
between `"System"`, `"Light"` and `"Dark"`, or clearing `Hotkeys` and `NotificationRules` to `[]` so
the shipped defaults are seeded again on next launch.

**Enum values are the sharp edge.** `Appearance`, `Hotkeys[].Action` and a rule's `Kind` are written
by name, and a name the reader does not recognise costs the **whole file**, not that one value — the
document fails to parse and is quarantined. If you are unsure of a spelling, delete the property
instead; every one of them has a default.

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
