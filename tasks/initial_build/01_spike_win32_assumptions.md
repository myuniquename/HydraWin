# Task 01 — Spike: verify the risky Win32 assumptions

Status: **done** (2026-08-15) — all three questions answered with observed evidence. The headline
correction to the plan: flashes **do** reach `SW_HIDE`-hidden windows, including real Teams
message notifications, so the notification design works as intended. See *Consequences* below.
Depends on: nothing.

## Motivation

The whole design rests on three assumptions about documented Win32 behaviour that are plausible
but unverified on this machine (Windows 11 Pro). If any fails, tasks 06 and 09 change shape. Half
a day of throwaway code now prevents rework later. The spike's deliverable is **recorded facts**
in this file, not code quality.

## Background

HydraWin will (a) hide other tasks' windows with `ShowWindow(hwnd, SW_HIDE)` and restore them with
`SW_SHOW` + `SetWindowPlacement`, and (b) detect "window wants attention" via the shell hook
message `HSHELL_FLASH` (received by a window registered with `RegisterShellHookWindow`, message id
from `RegisterWindowMessage("SHELLHOOK")`) with a title-change watcher
(`SetWinEventHook(EVENT_OBJECT_NAMECHANGE …)`) as fallback. The three open questions:

1. **Does `HSHELL_FLASH` (wParam = `0x8006`) arrive for a window that is currently hidden with
   `SW_HIDE`?** Suspected no — a hidden window has no taskbar button to flash. If no, the title
   watcher is the *only* notification channel for hidden windows and task 09 must treat it as
   primary, not fallback.
2. **Does hide → show round-trip cleanly for the real target apps** — Windows Terminal, VS Code,
   a Chromium browser with 2+ windows in one process, and new Teams? Specifically: does `SW_HIDE`
   work at all (Teams is packaged/WinUI and may behave differently), does
   `GetWindowPlacement`/`SetWindowPlacement` restore maximized state and position (including on a
   second monitor if available), and does the app keep running normally while hidden (Teams
   receiving messages, terminal processes continuing)?
3. **Does `EVENT_OBJECT_NAMECHANGE` fire for hidden windows**, and what do the actual title
   transitions look like for (a) a Claude Code session finishing / waiting for input in Windows
   Terminal, and (b) Teams receiving a message (does the title carry an unread count)? Capture
   the literal before/after title strings — task 09's default regexes come from these.

## Work

Create `spikes/` at the repository root. Each spike is a small standalone console program
(`dotnet run` from its folder), throwaway quality, but every spike **must re-show everything it
hid before exiting** — including on Ctrl+C (`Console.CancelKeyPress`) and unhandled exceptions —
because the recovery journal does not exist yet.

### A. `spikes/HideShow` — round-trip fidelity
Program: takes a window-title substring as argument, finds the window (`EnumWindows` +
`GetWindowText`), records `GetWindowPlacement`, hides it, waits 10 s, re-shows it, restores
placement, reports before/after placements. Run it against each target app, including: a
maximized window, a normal window moved to a second monitor (if present), and two Chrome/Edge
windows of the same process (verify hiding one leaves the other alone). For Teams, verify while
hidden it still receives messages (send yourself one).

### B. `spikes/FlashProbe` — flash observability
Program: creates a message-only-style hidden host window (a real top-level window is fine),
calls `RegisterShellHookWindow`, listens for the `SHELLHOOK` message, and logs every
(wParam, hwnd, title) pair. Procedure: set Windows Terminal `"bellStyle": "taskbar"` (**not**
`"taskbarFlash"` — as originally written here; that is not an accepted value, Terminal ignores it,
and it invalidated the first run of this spike), ring
the bell (`printf '\a'`) while the terminal is visible-but-background → expect `0x8006`. Then
hide the terminal with `SW_HIDE` (use spike A's code), ring the bell again → record whether
anything arrives. Also record what Teams produces on an incoming message, visible and hidden.
Also log whether `HSHELL_RUDEAPPACTIVATED` (`0x8004`) shows up anywhere in these scenarios.

### C. `spikes/TitleWatch` — title transitions
Program: `SetWinEventHook(EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE, …,
WINEVENT_OUTOFCONTEXT)` on all processes, filter to `idObject == OBJID_WINDOW (0)` and
`idChild == CHILDID_SELF (0)`, log timestamp + hwnd + new title. Needs a message pump — a bare
console app must run a message loop (`GetMessage`/`DispatchMessage`) or the hook never fires.
Procedure: run a short Claude Code prompt in Windows Terminal and capture the title sequence
through busy → done/waiting; receive a Teams message and capture its title change; repeat both
with the source window hidden and record whether events still arrive.

### D. Record the facts
Fill **Record on completion** below with: a yes/no per question, the literal title strings
captured in C, any per-app quirks from A (especially Teams), and the consequences — explicitly
state whether task 09 should treat the title watcher as primary. If a result contradicts
`_plan.md`, update `_plan.md` § *Investigation results* and the affected task file in the same
change.

## Verification

This task *is* verification; its pass criterion is that all three questions have recorded
answers backed by observed output (paste representative log lines), and that after every spike
run no window remained hidden (check the taskbar and, if in doubt, re-run spike A's re-show
path).

## Record on completion

Run on 2026-08-15, Windows 11 Pro 10.0.26200, two monitors (`\\.\DISPLAY74` primary at
`0,0 3072x1728`; `\\.\DISPLAY73` at `-2048,0 2048x2304` — negative X, which is what makes it a
real placement test). Windows Terminal 1.24.11911, packaged `MSTeams` 26198, Chrome, VS Code.
Raw logs: `tasks/initial_build/reference/` (`flashprobe.log` 1311 events, `titlewatch.log` 135
events, `hideshow-baseline-hidden.txt`, `hideshow-final-hidden.txt`).

**Deviation from the task as written:** the target framework is `net10.0-windows`, not
`net8.0-windows`. Only .NET 10 SDKs are installed on this machine and the user chose to move the
whole project to .NET 10; `CLAUDE.md`, `_plan.md` and `02_solution_scaffold.md` were updated in
the same change.

### Q1 — Does `HSHELL_FLASH` (0x8006) arrive for a window hidden with `SW_HIDE`?

**YES.** The plan's suspicion was wrong, and `_plan.md` has been corrected.

Controlled A/B on one window (Windows Terminal `0x002813D0`), identical
`FlashWindowEx(FLASHW_ALL | FLASHW_TIMERNOFG, uCount = 6)` in both states — 7 flash messages
each, the only difference being `vis=`:

```
19:12:53.058 | 0x8006 | HSHELL_FLASH | 0x002813D0 | vis=Y | WindowsTerminal | "HYDRAWIN-BELL"
19:12:54.114 | 0x8006 | HSHELL_FLASH | 0x002813D0 | vis=Y | WindowsTerminal | "HYDRAWIN-BELL"
…                                                          (7 total, visible)
19:13:14.376 | 0x8006 | HSHELL_FLASH | 0x002813D0 | vis=n | WindowsTerminal | "HYDRAWIN-BELL"
19:13:15.427 | 0x8006 | HSHELL_FLASH | 0x002813D0 | vis=n | WindowsTerminal | "HYDRAWIN-BELL"
…                                                          (7 total, SW_HIDE-hidden)
```

Independent, unsolicited confirmation from a third-party app nobody touched — a hidden `msrdc`
"RemoteApp" window flashed 447 times during the session, every one of them with `vis=n`:

```
19:24:42.041 | 0x8006 | HSHELL_FLASH | 0x000114F2 | vis=n | msrdc | "RemoteApp"
```

Of 472 `HSHELL_FLASH` messages captured, 454 were for windows that were not visible.

`HSHELL_RUDEAPPACTIVATED` (0x8004): **zero occurrences** in 1311 shell-hook messages, across
window creation, activation, hiding, showing and flashing. It is not a useful signal here.

**Caveat that matters more than the answer.** Delivery works; what is not guaranteed is that the
*app* calls `FlashWindowEx` in the first place.

> ### RETRACTED, and re-measured on 2026-08-15 (see *Bell re-test* below)
>
> This section originally read *"a Windows Terminal bell never produced a flash"*, based on runs
> using `"bellStyle": "taskbarFlash"`. **`taskbarFlash` is not a valid value** — Windows Terminal
> accepts only `"all"`, `"audible"`, `"window"`, `"taskbar"`, `"none"`. Terminal therefore ignored
> the setting and no bell could ever have flashed anything. Every "negative" in the original three
> attempts was testing a setting that was never in effect. **A Windows Terminal bell does flash,
> including while the window is `SW_HIDE`-hidden, and Claude Code does ring it — about 61 s after
> a session goes idle.** Details below.

### Bell re-test (2026-08-15, `reference/flashprobe-bell.log`)

Prompted by the user, who had configured `bellStyle` correctly:
`profiles.defaults.bellStyle = ["audible", "window", "taskbar"]`, plus
`"preferredNotifChannel": "terminal_bell"` in `~/.claude/settings.json`.

**1. A BEL flashes the taskbar, from both emission paths.** Six bells three seconds apart, from
two windows made visible-but-background:

```
21:52:54.021 | 0x8006 | HSHELL_FLASH | 0x01FA14C4 | vis=Y | WindowsTerminal | "BELL-PWSH"   ← [Console]::Out.Write([char]7)
21:52:57.151 | 0x8006 | HSHELL_FLASH | 0x003C12E4 | vis=Y | WindowsTerminal | "BELL-WSL"    ← WSL printf '\a'
```

Six bells produced six flashes on each window. The ConPTY path (a Win32 console app writing BEL
through the console API) works just as well as the VT-native one — the original record's
speculation that ConPTY swallows BEL was also wrong.

**2. A hidden terminal still flashes.** The window was hidden with `SW_HIDE` through the spike's
journalled path, then rung eight times:

```
21:55:35.147 | 0x0002 | HSHELL_WINDOWDESTROYED | 0x002E0284 | vis=n | WindowsTerminal | "BELL-HIDDEN"   ← our SW_HIDE
21:55:37.646 | 0x8006 | HSHELL_FLASH           | 0x002E0284 | vis=n | WindowsTerminal | "BELL-HIDDEN"
…                                                                                     (8 flashes, 3 s apart)
21:55:58.672 | 0x8006 | HSHELL_FLASH           | 0x002E0284 | vis=n | WindowsTerminal | "BELL-HIDDEN"
```

So Windows Terminal, unlike the sending-side limitation feared above, keeps flashing with no
taskbar button.

**3. Claude Code rings the bell — 61 seconds after it goes idle.** I first recorded "it never
rings" here; that was wrong too, and the disproof was already inside my own log. I had grepped
20–25 s after each session finished. The user pointed out that the flash arrives late, and
measuring the gap between the idle-marker title and the flash gives a startlingly consistent
answer across five independent sessions:

```
0x00151430  idle 21:55:11.743  ->  flash 21:56:12.813   delay 61.1s   "✳ Respond with pong"
0x000E15E8  idle 21:58:22.170  ->  flash 21:59:23.237   delay 61.1s   "✳ Run bash sleep command and confirm completion"
0x003F1512  idle 22:00:35.602  ->  flash 22:01:36.664   delay 61.1s   "✳ Run background sleep command and monitor completion"
0x00630C56  idle 22:02:06.377  ->  flash 22:03:07.444   delay 61.1s   "✳ Run background sleep command and confirm completion"
0x00A515E6  idle 22:02:11.084  ->  flash 22:03:12.149   delay 61.1s   "✳ Write poem and check git status"
```

**61.1 s in every case, to within 0.1 s** — a deliberate fixed delay, not jitter. One flash per
session, not a repeating train. It fires whether the session was a child of another Claude Code
session or not, and regardless of `CLAUDE_AFK_TIMEOUT_MS`.

**So both channels carry a Claude Code signal, with very different latency:**

| Channel | Latency | Content |
| --- | --- | --- |
| Title (`✳` marker appears) | immediate | distinguishes busy from done/waiting, carries the session name |
| Flash (`HSHELL_FLASH`) | **~61 s** | "this window wants attention", nothing more |

**Consequence, and the decision taken:** the title watcher detects a finished session a full
minute before the flash does. Presented with that number, **the user chose the flash and accepted
the latency** — task 09 therefore ships **no Claude Code title rule**, and a finished session
badges through the same flash channel as Teams, keeping one mechanism and no per-app regexes.

The Claude Code title is still parsed, for a different purpose: **task 07 § F** binds window rows
to the live title so the overview shows a session's marker (`◐ ◑ ◒ ◓` working, `✳` idle) as an
in-progress indication without the user switching to it. Display, not notification — which is also
why the "`✳` appears briefly at the start of an activity" caveat no longer matters.

Two settings this depends on, worth stating because a default install has neither: Windows
Terminal `bellStyle` must include `"taskbar"`, and Claude Code's `preferredNotifChannel` must be
`terminal_bell`.

### Q2 — Does hide → show round-trip cleanly for the real target apps?

**YES for every non-elevated app tested, with exact placement restore.** Restore is
`SW_SHOW` followed by `SetWindowPlacement(saved)`; that pair was pixel-exact every time.

| Target | Result |
| --- | --- |
| Windows Terminal, normal, primary monitor | exact |
| Windows Terminal, **maximized** | `SW_SHOWMAXIMIZED(3)` → `SW_SHOWMAXIMIZED(3)`, `rcNormalPosition` MATCH, on-screen rect `(-7,-7)-(3078,1686)` identical, `zoomed=True` after |
| Windows Terminal on `\\.\DISPLAY73` (negative X) | `normal=(-1600,500)-(-913,1000)` MATCH, monitor MATCH |
| Chrome, 1 of 3 windows in pid 17572 | hidden alone; the other two stayed visible; process `Responding=True` throughout; restore exact |
| VS Code, 1 of 2 windows in pid 3304 | hidden alone; sibling untouched; `Responding=True`; restore exact |
| **Teams** (packaged `MSTeams`, class `TeamsWebView`) | **hides cleanly — it does not refuse.** 170 s hidden, `IsWindowVisible == false`, restore exact. Re-confirmed in the second run on **both** Teams windows simultaneously (240 s and 210 s): `showCmd MATCH, rcNormalPosition MATCH` for each |
| Task Manager (elevated) | **refused**, see below |

Maximized round-trip, verbatim:

```
before   showCmd=SW_SHOWMAXIMIZED(3) flags=0x2 min=(-25600,-25600) max=(-1,-1) normal=(141,147)-(1327,765) 1186x618
before   rect=(-7,-7)-(3078,1686) 3085x1693 monitor=\\.\DISPLAY74 visible=True zoomed=True iconic=False
hide     ShowWindow(SW_HIDE) returned True (previous visibility), win32=0
hide     confirmed: IsWindowVisible == false
after    showCmd=SW_SHOWMAXIMIZED(3) flags=0x2 min=(-1,-1) max=(-1,-1) normal=(141,147)-(1327,765) 1186x618
after    rect=(-7,-7)-(3078,1686) 3085x1693 monitor=\\.\DISPLAY74 visible=True zoomed=True
VERDICT  showCmd MATCH (SW_SHOWMAXIMIZED(3) -> SW_SHOWMAXIMIZED(3)), rcNormalPosition MATCH
```

Teams, verbatim (this is the result task 06 was most worried about):

```
target   0x004911C0 "Anton Suchov (You) | Microsoft Teams" [ms-teams pid=12184]
hide     ShowWindow(SW_HIDE) returned True (previous visibility), win32=0
hide     confirmed: IsWindowVisible == false
waiting  170s while hidden…
VERDICT  showCmd MATCH (SW_SHOWNORMAL(1) -> SW_SHOWNORMAL(1)), rcNormalPosition MATCH
```

**The elevated-window signature** (this is the real "Unmanageable" trigger, not packaged apps):

```
target   0x001205A0 "Task Manager" [Taskmgr pid=3088, ELEVATED]
hide     ShowWindow(SW_HIDE) returned False (previous visibility), win32=5
hide     *** REFUSED: window is still visible ***
  0x1205A0 "Task Manager" -> visible=True setPlacement=False
```

UIPI makes both `ShowWindow` and `SetWindowPlacement` return `FALSE` with
`GetLastError() == 5` (`ERROR_ACCESS_DENIED`), and the window stays visible. Two API notes for
whoever implements task 06:

- **`ShowWindow`'s return value is the window's *previous* visibility, not success.** A
  successful `SW_HIDE` on a visible window returns `TRUE`; the refused call returned `FALSE`
  only because the window was never hidden. `IsWindowVisible(hwnd)` after the call is the
  authority — always check it.
- Elevation is detectable up front: `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` **succeeds**
  against an elevated process from a normal one, so it is not a usable test. `OpenProcessToken` +
  `GetTokenInformation(TokenElevation)` is (`Native.LooksElevated` in the spike).

### Q3 — Does `EVENT_OBJECT_NAMECHANGE` fire for hidden windows, and what are the titles?

**Fires regardless of visibility** — 11 of 135 captured name-change events carried `vis=n`.

**Claude Code in Windows Terminal** (window `0x00060C58`) — literal titles, non-ASCII escaped:

```
19:14:24.867 | vis=Y | WindowsTerminal | "\u2733 Initialize git repository with initial commit"
19:14:45.818 | vis=Y | WindowsTerminal | "\u25D0 verify-win32-assumptions"
19:20:19.129 | vis=Y | WindowsTerminal | "\u2733 verify-win32-assumptions"
        ← no events at all for 8m28s: this is the idle / waiting-for-input state
19:28:47.726 | vis=Y | WindowsTerminal | "\u25D0 verify-win32-assumptions"
19:28:48.687 | vis=Y | WindowsTerminal | "\u25D1 verify-win32-assumptions"
19:28:49.647 | vis=Y | WindowsTerminal | "\u25D0 verify-win32-assumptions"
        ← alternating every ~0.96 s for as long as the session is working
```

The shape is `<marker> <session or activity name>`:

- **Busy:** a rotating spinner frame, `U+25D0 ◐` / `U+25D1 ◑` (the `U+25D0`–`U+25D3` family),
  changing about **once per second**.
- **Idle / waiting for input:** `U+2733 ✳`, after which **the title stops changing**. The 8m28s
  gap above is exactly the period the session sat waiting for the user.

So the done/waiting rule is the arrival of a `^\u2733 ` title (edge-triggered), which is
equivalent to "the spinner stopped". Two things task 09 must budget for:

- ~1 name-change event per second per busy terminal. Harmless for an edge-triggered rule, but
  the hub must not do expensive work per event.
- `✳` also appears mid-session at the start of an activity (`19:14:24.867` above), so a rule
  keyed on `✳` alone will occasionally fire early. If false positives prove annoying, add a
  short debounce — badge only if the `✳` title survives ~2 s unchanged.

`claude -p` (non-interactive) sets **no** Claude-style title; only the interactive session does.

**Teams — verified properly on the third attempt.** Attempt 1 used a self-chat and produced
nothing at all. The user suspected — correctly — that a message from *another* account behaves
differently, created a second Teams account, and sent real messages. Attempts 2 and 3 then
uncovered a confound that initially produced two false negatives; the final, controlled results
are below. Logs: `reference/flashprobe-teams.log`, `reference/titlewatch-teams.log`,
`reference/flashprobe-teams-min.log`, `reference/titlewatch-teams-min.log`.

**The confound, because it will trap the next person too: Teams flashes once per *unread run*.**
It raises `FlashWindowEx` when a conversation goes from read to unread, and then stays silent for
every further message until the user actually opens and reads the chat. Any experiment that sends
a second message without reading the first records a false negative — which is exactly what
happened twice here, once for hidden and once for minimized. **Every result below was taken with
the conversation read (unread cleared) immediately beforehand.**

**Teams never changes its window title.** Not when visible, not when hidden, not on an incoming
message. Across the whole run the title-change watcher captured **zero** events for `ms-teams`.
The titles are steady-state and reflect the selected chat, with no unread count anywhere:

```
"anton suchov | Microsoft Teams"          (main window, TeamsWebView)
"Chat | anton suchov | Microsoft Teams"   (chat window, TeamsWebView)
```

**Teams flashes on an incoming message in all three window states**, always three flashes on the
*Chat* window `0x001111C2`, always the same 1.06 s cadence. The only thing that changes is `vis=`:

```
visible, background (20:02:53–55, unread cleared first)
20:02:53.068 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=Y | ms-teams | "Chat | anton suchov | Microsoft Teams"
20:02:54.133 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=Y | ms-teams | "Chat | anton suchov | Microsoft Teams"
20:02:55.198 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=Y | ms-teams | "Chat | anton suchov | Microsoft Teams"

minimized, SW_SHOWMINIMIZED (20:05:18–20, unread cleared first)
20:05:18.773 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=Y | ms-teams | "Chat | anton suchov | Microsoft Teams"
20:05:19.832 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=Y | ms-teams | "Chat | anton suchov | Microsoft Teams"
20:05:20.902 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=Y | ms-teams | "Chat | anton suchov | Microsoft Teams"

HIDDEN with SW_HIDE (20:07:38–40, unread cleared first) — the decisive one
20:07:19.481 | 0x0002 | HSHELL_WINDOWDESTROYED | 0x0C411144 | vis=n | ms-teams | "anton suchov | Microsoft Teams"
20:07:22.500 | 0x0002 | HSHELL_WINDOWDESTROYED | 0x001111C2 | vis=n | ms-teams | "Chat | anton suchov | Microsoft Teams"
20:07:38.104 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=n | ms-teams | "Chat | anton suchov | Microsoft Teams"
20:07:39.162 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=n | ms-teams | "Chat | anton suchov | Microsoft Teams"
20:07:40.231 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=n | ms-teams | "Chat | anton suchov | Microsoft Teams"
```

**So a `SW_HIDE`-hidden Teams window notifies exactly as well as a visible one.** No special
handling is needed, no pinning, no minimize-instead-of-hide. Teams also keeps running and
receiving normally while hidden — the user confirmed every message sent during a hidden phase was
present in the conversation once the windows were restored.

Two corrections I had to make to my own earlier write-up, recorded because the reasoning matters:
an initial "hidden Teams is silent" conclusion, and then a "minimized Teams is silent" one, were
both artifacts of the unread-run behaviour above. The tell was that a control run — visible,
unread cleared — reproduced the flash, which meant the variable under test was not what I thought.
Re-running each case with unread cleared reversed both results.

The one genuine negative stands: **Teams never changes its window title.** Zero `ms-teams`
name-change events were captured across every run, in every window state, including the messages
that did flash. Titles are steady-state and reflect the selected chat:

```
"anton suchov | Microsoft Teams"          (main window, TeamsWebView)
"Chat | anton suchov | Microsoft Teams"   (chat window, TeamsWebView)
```

Task 09's assumed rule `^\((\d+)\)` for `ms-teams.exe` is therefore **wrong and should be
deleted**, not tuned — the unread count never reaches the window title. Teams is a flash-only app.

### Extra finding (not asked for, but it will bite someone)

**The shell hook reports `SW_HIDE` as `HSHELL_WINDOWDESTROYED` and `SW_SHOW` as
`HSHELL_WINDOWCREATED`, reusing the same HWND.** Chrome window `0x0002147A`:

```
19:15:37.999 | 0x0002 | HSHELL_WINDOWDESTROYED | 0x0002147A | vis=n | chrome | "…CHROME-A…"   ← our SW_HIDE
19:16:02.970 | 0x0001 | HSHELL_WINDOWCREATED   | 0x0002147A | vis=Y | chrome | "…CHROME-A…"   ← our SW_SHOW
19:24:28.817 | 0x0002 | HSHELL_WINDOWDESTROYED | 0x0002147A | vis=n | chrome | "…CHROME-A…"   ← a real close
```

A hide and a genuine close are indistinguishable in the shell hook; `IsWindow(hwnd)` is the
discriminator. Task 03 is unaffected because it uses WinEvents plus an explicit hidden set, but
anything that later consumes the shell hook must not treat `WINDOWDESTROYED` as "gone".

### Design consequence: the plan's core promise holds

The plan's headline scenario — *a hidden Teams chat gets a message, its task badges* — works as
designed, through the flash hook. No per-app backgrounding policy, pinning, or
minimize-instead-of-hide is needed. What task 09 must account for instead is Teams' **one flash
per unread run**: the first message into a read conversation flashes, subsequent ones are silent
until the user reads it. That suits badging well (the badge is already up), but it means the badge
must **not** be cleared by anything other than the user actually focusing the window — clear it on
a switch and no further flash will ever re-raise it. Task 09's existing rule that `SwitchTo` clears
only flash-kind entries is therefore wrong for Teams and should be revisited.

Whatever is chosen, `06_switch_engine.md` and `10_hardening_polish.md` need a per-app
"how to background this window" policy rather than a single global hide.

### Bug found in the spike itself (relevant to task 05)

Two `hideshow` processes hiding two windows at the same instant collided on the journal file: the
second one blocked/failed inside `Journal.Append` (`FileMode.Append` with `FileShare.Read`) and
never hid its window. Harmless in the spike — it fails *before* hiding, so the safe direction —
but **task 05 must handle concurrent journal writers deliberately**, because
`hydrawin.exe --restore-all` can legitimately run while the UI process is live. Either
single-writer-by-design with a named mutex, or a share mode that permits it, plus a defined
reader behaviour for a partially-written line.

### Consequences and plan corrections made

- `_plan.md` § *Investigation results* — the notifications bullet claimed flashes from hidden
  windows "may be unobservable". Corrected to the measured result.
- `09_notifications.md` — the flash hook is documented as working for hidden windows; the Teams
  title rule is deleted as disproved; the terminal-bell verification step is rewritten (the bell
  is now the *easiest* way to exercise the flash path); and, after the bell re-test, the Claude
  Code title rule is deleted too.
  **Answer to the question the task poses — "should task 09 treat the title watcher as primary?"
  — is: no, the flash carries every app.** Teams is flash-only because its title never changes;
  Claude Code *could* go either way — its title is instant and its flash is 61 s late — and the
  user chose the flash, accepting the latency to avoid per-app regexes. Both channels work for
  `SW_HIDE`-hidden windows, so hiding costs no awareness either way. The title watcher survives as
  a user-extensible mechanism (task 10) and as the source of live progress display (task 07 § F).
- `07_ui_shell.md` — new § F: window rows bind to the live title so a Claude Code session's
  progress marker is visible in the overview without switching to it, with the measured
  ~1 event/second/terminal cost noted.
- `06_switch_engine.md` — the "refuses `SW_HIDE`" branch is kept, but its description was wrong on
  two counts: packaged Teams does **not** refuse, and the failure does not "return success but stay
  visible". Corrected to the measured elevated-window signature.

### Verification

- All three spikes build with `TreatWarningsAsErrors`: 0 warnings, 0 errors.
  `dotnet format --verify-no-changes`: exit 0 for all three.
- **Crash-recovery drill passed before any real target was touched:** a window was hidden, the
  spike killed with `TerminateProcess` (no handler could run), the window confirmed still hidden
  with the journal entry intact on disk, and `hideshow rescue` restored it — after which the
  journal was 0 bytes. The watchdog path was also exercised twice (90 s and 25 s caps fired and
  restored).
- **No window left hidden.** `hideshow list --hidden` before vs after differ by exactly three
  entries, all of them the spikes' own windows (`HydraWinFlashProbeHost` and the two hidden
  console windows). `hideshow rescue` reports the journal empty.
- Windows Terminal `settings.json` restored from its backup; SHA-256 matches the pre-run file and
  `bellStyle` is absent again.

### Files

New:

- `spikes/.gitignore`, `spikes/README.md`
- `spikes/HideShow/HideShow.csproj`, `Program.cs`, `Native.cs`, `Journal.cs`
- `spikes/FlashProbe/FlashProbe.csproj`, `Program.cs`, `Native.cs`
- `spikes/TitleWatch/TitleWatch.csproj`, `Program.cs`, `Native.cs`
- `tasks/initial_build/reference/flashprobe.log`, `titlewatch.log` (main run),
  `flashprobe-teams.log`, `titlewatch-teams.log` (Teams run 2),
  `flashprobe-teams-min.log`, `titlewatch-teams-min.log` (Teams run 3: minimize, control, and the
  controlled hidden re-test), `hideshow-baseline-hidden.txt`, `hideshow-final-hidden.txt`

Modified:

- `tasks/initial_build/01_spike_win32_assumptions.md` (this record)
- `tasks/initial_build/_plan.md` (Q1 correction, .NET 10)
- `tasks/initial_build/06_switch_engine.md` (refuses-hide signature)
- `tasks/initial_build/09_notifications.md` (flash/hidden, Claude Code regex, verification steps)
- `tasks/initial_build/02_solution_scaffold.md` (.NET 10)
- `CLAUDE.md` (.NET 10)

Deleted: none.

Untracked build output (`spikes/*/bin`, `spikes/*/obj`) is covered by `spikes/.gitignore`.
