# Workspaces — architecture

## Why HydraWin hides windows itself

Windows 11 has virtual desktops, and moving windows between them would achieve the same visible
effect with none of the recovery machinery below. They were evaluated and **rejected**.

Programmatic control — creating and switching desktops, and above all moving *another
application's* window to one — requires the undocumented COM interfaces
`IVirtualDesktopManagerInternal` and friends. Microsoft changes their GUIDs across Windows
releases, which is why every wrapper library ships a separate build per Windows version
(MScholtes/VirtualDesktop carries `virtualdesktop11.cs`, `virtualdesktop11-24h2.cs`, …;
Slions.VirtualDesktop tracks builds on NuGet). The only *documented* interface,
`IVirtualDesktopManager`, cannot switch desktops and can only move windows belonging to the calling
process. A tool someone depends on daily must not break on a Windows update.

So HydraWin hides and shows windows itself with documented Win32. Same user-visible effect, plus
exact placement restore — at the cost of a mandatory recovery journal, which is most of what this
document is about.

**Minimize instead of hide was also rejected**: a minimized window keeps its taskbar button and its
Alt-Tab entry, which defeats the isolation the whole app exists for.

## Which windows are managed

`WindowFilter.Evaluate` is pure — it takes a `WindowFacts` snapshot and returns the **first failing
clause**, not a boolean. Returning the reason rather than a verdict pays for itself twice: every
clause gets its own unit test, and the UI can explain *why* a window the user pointed at cannot be
managed instead of silently ignoring them.

Clauses, in evaluation order — the order matters, because the first failure is the one reported:

| # | Verdict | Rejected when |
| --- | --- | --- |
| 1 | `OwnProcess` | The window belongs to HydraWin. It never manages itself. |
| 2 | `Elevated` | The owning process is elevated and HydraWin is not. |
| 3 | `NoTitle` | No title — not something a user thinks of as a window. |
| 4 | `NotVisible` | Invisible, *and* not hidden by HydraWin. |
| 5 | `ToolWindow` | `WS_EX_TOOLWINDOW`: a palette or popup. |
| 6 | `Owned` | Owned by another window — a dialog or tool attached to a real window. |
| 7 | `Cloaked` | DWM-cloaked, *and* not hidden by HydraWin. The usual UWP ghost signature. |

Two clauses carry a hard-won exception, and both exist for the same reason: **a window HydraWin
hid is still part of a task.**

- Clause 4 exempts windows in the hidden set, or hiding a window would immediately remove it from
  the inventory and nothing would be left to restore it.
- Clause 7 exempts them too. Some packaged applications — Teams among them — report themselves
  *cloaked* precisely because HydraWin hid them. An early implementation dropped every cloaked
  window; the effect would have been that hiding Teams silently deleted it from the model.

### Elevated windows are excluded, not annotated

UIPI stops a non-elevated process from hiding an elevated one, so such a window could be put in a
task and then stay stubbornly on screen through every switch. Rather than offer something that can
never work, clause 2 keeps them out of the inventory entirely and the UI explains the refusal when
the user points at one.

Detection is `OpenProcess` → `OpenProcessToken` → `GetTokenInformation(TokenElevation)`. It has to
be the token: `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` **succeeds** against an elevated
process from a normal one, so neither it nor a readable image path tells you anything. **Failing to
query the token counts as elevated** — a same-user process that refuses the query is something
stronger than merely elevated, and guessing the other way would put an unmanageable window in front
of the user. The answer is cached per process id for one minute; elevation never changes for a live
process, and the TTL only bounds a recycled process id inheriting a stale answer. When HydraWin is
*itself* elevated the clause does not apply and such windows are ordinary.

An empty `ProcessPath`, by the way, does **not** mean elevated — it means a genuinely protected
process. Such a window has no durable name to write a rule against, so `RuleMatcher` refuses to
claim one.

### Staying current

`WindowTracker` combines two mechanisms because neither alone is sufficient:

- **WinEvent hooks** (`WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`) for
  `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_DESTROY`, `EVENT_OBJECT_SHOW`, `EVENT_OBJECT_HIDE` and
  `EVENT_OBJECT_NAMECHANGE`. Immediate, but they can be missed.
- **A 2-second reconciliation sweep** that enumerates every top-level window and diffs against the
  inventory. It runs on the timer thread — the desktop carries several hundred top-level windows —
  and only the resulting events are marshalled to the captured `SynchronizationContext`. An
  interlocked flag stops a slow sweep overlapping the next tick.

Two consequences worth knowing. `WINEVENT_SKIPOWNPROCESS` means the tracker never reports
*HydraWin* taking focus, which the notification hub has to be told separately (see
[../notifications/architecture.md](../notifications/architecture.md)). And because both mechanisms
can discover the same window at once, insertion is a `TryAdd` under a lock with only the thread
that actually inserted allowed to raise `WindowAppeared` — otherwise a window appears twice.

## The invariant: journal before hide

**No foreign window is ever hidden before its journal entry is flushed to disk.** Everything else
in this document is negotiable; this is not. The reasoning is asymmetric:

- A journal listing a window that is *not* actually hidden costs a wasted restore attempt, which
  `RestoreService` handles safely.
- A window hidden with *no* journal entry is invisible, has no taskbar button, no Alt-Tab entry,
  and nothing that knows where it went. The user's only recovery is to kill the process.

So the journal is allowed to describe strictly more than reality, never less.

`SwitchEngine.HideAll` implements it in this order, and the order is the whole point:

1. For each window: read its placement with `GetWindowPlacement`, and read its process id and
   image path. **A window whose placement cannot be read is skipped entirely** — hiding a window
   that could never be put back where it was is exactly the outcome this project exists to
   prevent. It counts as neither hidden nor refused.
2. `RecoveryJournal.RecordBeforeHide(entries)`. This write is synchronous and never debounced; it
   returns only once the bytes are on disk.
3. Only now, `ShowWindow(hwnd, SW_HIDE)` for each.
4. Check `IsWindowVisible`. If the window is still visible, the hide was refused: its journal entry
   is **withdrawn** with `ConfirmShown` and the assignment is marked `Unmanageable`. An entry for a
   window that is not hidden would have recovery "restore" something that never moved, and would
   quietly misrepresent the invariant.

Restoring is the mirror: `SetWindowPlacement` first (it carries the maximized state, and setting it
on a hidden window is what makes the window reappear exactly where it was rather than merely
visible), then `SW_SHOW`, then `ConfirmShown` to drop the entry.

The journal is guarded by a named mutex, `Local\HydraWinRecoveryJournal`, because `--restore-all`
can legitimately run while the UI process is live, and two concurrent writers were measured
colliding and losing an entry. `AbandonedMutexException` is treated as ownership: a crashed
holder is precisely the case this journal exists for, and writes are atomic, so whatever is on
disk is a complete document. `state.json` needs none of this — it has one writer.

`HiddenWindowSet` is an in-memory mirror of "what is hidden", seeded from the journal at startup
and updated on every hide and show. It exists because `WindowTracker` asks *is this hidden?* about
every top-level window on every sweep — several hundred questions every two seconds — and answering
from the file would mean hundreds of mutex-guarded reads per second. The journal remains the
durable source of truth.

## Restoring: identity validation

Windows recycles window handles. An entry therefore carries the handle **plus** the process id and
the process image path, and `RestoreService` shows nothing until all three still agree. A handle
whose current owner does not match is dropped as **stale** — the window it named is gone, and
whatever inherited the handle could be anything at all. Showing it unverified could yank an
unrelated window into view, or worse, move it.

Three outcomes, and the third is the one that matters:

| Outcome | Meaning | Entry |
| --- | --- | --- |
| `Restored` | Verified, placement applied, shown | removed |
| `Stale` | Handle gone or now owned by something else | removed |
| `Failed` | Still there, still ours, but refused to show | **kept** |

`Failed` entries stay on the books deliberately. Dropping one would strand a hidden window forever,
which is the exact failure this whole mechanism exists to prevent.

## Switching

`SwitchPlan.Compute` is pure — no Win32, no journal, no persistence — so "who gets hidden" is
decidable and testable on its own. It walks the tasks and produces two sets:

- **to hide**: bound windows of every *other* task that are not already hidden;
- **to show**: bound windows of the target task that HydraWin currently has hidden.

**Unassigned windows can never appear in either set**, because the plan is built from task
assignments only. That is why they stay visible through every switch — a structural property, not a
rule some code path has to remember to apply.

Assignments previously marked `Unmanageable` are still included. The flag records that a window
refused *once*, not that it always will, so the app heals itself if that process is later restarted
without elevation. The cost is one failed call per switch.

`SwitchEngine.SwitchTo` then hides, shows, and finally deals with focus — and what it does with
focus depends on where the switch came from:

- **From inside HydraWin's own window** (`focusTarget: false`): the task's windows are *raised*
  with `SetWindowPos(HWND_TOP, SWP_NOACTIVATE)` but not activated, in reverse order so the
  last-active one ends on top. The user is driving the panel, and taking the keyboard away would
  send their next key press — `Del`, in particular — to whichever application was just raised.
- **From a hotkey or the tray** (`focusTarget: true`): focus lands on the task's last-active
  window, because the user is somewhere else entirely and the point is to land in the task.

`SetForegroundWindow` works here only because HydraWin is the foreground process at the moment of a
user-initiated switch, or holds the input state granted to a hotkey registrant on `WM_HOTKEY`.
There is deliberately no `AttachThreadInput` workaround for paths where neither is true.

Assigning a window to a non-active task hides it immediately, and does so by calling the same
`HideAll` a switch uses — so the invariant applies on that path without a second implementation of
it. Both ways of assigning a window, drag-and-drop and the crosshair picker, go through one method
for the same reason.

Deleting a task shows its hidden windows *first*, then unassigns them. **Deletion never closes a
window.**

## The safety nets, in one place

| Net | When it runs | What it does |
| --- | --- | --- |
| The journal | Before every hide | Records handle, pid, path, title and placement |
| Startup recovery | Every ordinary launch | Restores everything the journal lists, before any other window manipulation |
| `--restore-all` | On demand | Reads the journal and restores, with no UI and no single-instance mutex — it must work while a wedged instance holds it |
| Clean-exit restore | Window close, tray *Exit*, `SessionEnding` | Restores unless the user turned it off; defaults to restoring if the setting cannot be read |
| Crash handler | Unhandled exception on any thread | Logs, restores, then **lets the process die** — see [../ui/architecture.md](../ui/architecture.md) |
| Panic hotkey | `Ctrl+Alt+Shift+R` | Restores from the journal on the hotkey thread, so it works when the UI thread does not |

## Per-application behaviour, as measured

All measured on Windows 11 Pro 10.0.26200, two monitors, one at negative X.

| Application | Behaviour |
| --- | --- |
| Windows Terminal, normal | Hides and restores exactly |
| Windows Terminal, **maximized** | `SW_SHOWMAXIMIZED` → `SW_SHOWMAXIMIZED`, `rcNormalPosition` identical, on-screen rect identical |
| Windows Terminal on a negative-X monitor | `normal=(-1600,500)-(-913,1000)` restored exactly, same monitor |
| Chrome, one of three windows in one process | Hides alone; siblings untouched; process stays responsive; restore exact |
| VS Code, one of two windows in one process | Same |
| **Teams** (packaged, class `TeamsWebView`) | **Hides cleanly.** 170 s hidden, restore exact; both its windows simultaneously in a second run. Keeps running and receiving messages while hidden |
| Task Manager (elevated) | **Refuses** — see below |

Packaged and WinUI applications were the ones expected to be a problem, and were not. **Elevation
is the only thing that actually refuses.** Its signature:

```
target   0x001205A0 "Task Manager" [Taskmgr pid=3088, ELEVATED]
hide     ShowWindow(SW_HIDE) returned False (previous visibility), win32=5
hide     *** REFUSED: window is still visible ***
```

Two API notes that cost real time to learn:

- **`ShowWindow`'s return value is the window's *previous* visibility, not success.** A successful
  `SW_HIDE` on a visible window returns `TRUE`; the refused call above returned `FALSE` only
  because the window was never hidden in the first place. **`IsWindowVisible(hwnd)` after the call
  is the authority** — always check it.
- UIPI makes both `ShowWindow` and `SetWindowPlacement` fail with `GetLastError() == 5`
  (`ERROR_ACCESS_DENIED`).

Since elevated windows are filtered out of the inventory, an `Unmanageable` assignment now means
something rarer: a window that looked ordinary and refused anyway — a protected process, or one
that became elevated after HydraWin first saw it. It stays visible through every switch and its row
is marked.

### Two more measured facts

**`GetWindowPlacement` reports in the calling process's coordinate space.** On a 150%-DPI desktop,
system-DPI-aware `hydrawin.exe` recorded `300,250-1100,850` for the same window a DPI-unaware
program reported as `150,125-550,425` — exactly 2×. Harmless because only HydraWin writes and reads
the journal, but anything comparing journal placements against numbers from another process must
account for it.

**A placement pointing at a monitor that no longer exists does not strand the window.** Restoring a
window whose recorded `rcNormalPosition` was `(-30000,-30000)` — what a disconnected monitor's
coordinates become — put it back at `(0,0)`, inside the virtual screen. `SetWindowPlacement` works
in workspace coordinates and remaps on its own, so HydraWin adds no clamp of its own.

## Re-attach rules

Window handles are meaningless across restarts, so an assignment stores a rule instead: a process
**image file name** (not the full path — that changes when the application updates) and a title
pattern, matched as a substring by default or as a regex when asked.

Generating a rule from a live window strips one leading **volatile decoration** — the
unsaved-changes markers `●` and `*`, and Claude Code's activity markers `✳ ◐ ◑ ◒ ◓`. Without that,
a rule captured
from a busy terminal would bake in whichever spinner frame happened to be showing and never match
again. A trailing ` - <app>` suffix is deliberately *kept*: it is usually the stable part, and
keeping it stops a rule for `foo.cs - hydrawin - Visual Studio Code` matching every other editor
window.

Two binding rules, both deliberate: a rule binds **at most one window at a time**, so a second
matching window stays unassigned for the user to place rather than silently displacing the first;
and a window that is already bound is never rebound.

**A window is offered to the rules twice: when it appears, and whenever it renames itself.** The
appear edge alone is not enough, and browsers are the proof — a browser window exists, and is
tracked, a moment before it knows what page it is showing, so the rules see a placeholder title,
match nothing, and the window would sit in the unassigned pane for the rest of the session however
plainly it later said which task it belonged to. Measured on Edge: the window appears, and the
title arrives some hundreds of milliseconds later. Offering it again on rename costs a dictionary
lookup for a bound window, which matters because a working Claude Code terminal renames itself
about once a second.

Re-attaching **does not hide the window**, even when the task it joins is not the active one. A
window the user just opened disappearing as they look at it would be a worse bug than the one this
avoids; it takes its place in the task at the next switch, exactly as an assignment made by hand
does.

A malformed or slow user-authored regex counts as **no match** rather than throwing. This runs on
the window-tracking path, where a bad pattern must cost its own rule and nothing else; patterns are
compiled with a 100 ms timeout.

Moving a window between tasks **removes** the old assignment rather than merely unbinding it.
Unbinding alone leaves the previous task holding a rule that still recognises the window, and after
the next restart both tasks claim it — whichever re-attaches first wins.

## Persistence

`state.json` is preference data: tasks, assignments, rules, settings. `journal.json` is
crash-safety data. They are deliberately separate — losing `state.json` costs the user their task
layout, losing `journal.json` could cost them their windows.

`JsonStore<T>` writes atomically (temp file, then replace) and quarantines a corrupt document as
`state.json.corrupt-<timestamp>` rather than failing to start; the app comes up with defaults and
says so. Saves are debounced, because dragging a window between tasks mutates the model many times
a second, and a failed write is **reported rather than thrown** — the write runs on a timer thread,
where an unhandled exception would kill the process, potentially with the user's windows hidden.
The state stays pending so a later flush retries.

Both files are meant to be hand-editable, so everything serialises to flat property names and
string enums, with `UnsafeRelaxedJsonEscaping` so `+` stays `+` and non-ASCII window titles stay
readable rather than becoming walls of `\uXXXX`. Derived properties are `[JsonIgnore]`d — a get-only
property written to the file would silently discard anything a user edited in it.
