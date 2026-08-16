# Notifications — architecture

## Two channels, and why the flash is primary

| Channel | Mechanism | Reaches hidden windows | Needs a rule |
| --- | --- | --- | --- |
| **Flash** | `RegisterShellHookWindow` + `HSHELL_FLASH` (`0x8006`) | Yes | No |
| **Title** | `SetWinEventHook(EVENT_OBJECT_NAMECHANGE)` | Yes | Yes, per application |

The flash carries every application without HydraWin knowing anything about it, so it is the
default and the title channel ships switched off. The title channel survives as a
user-extensible mechanism for programs that announce something in their title and never flash.

`HSHELL_RUDEAPPACTIVATED` (`0x8004`) is not used: **zero occurrences** across 1,311 captured
shell-hook messages, spanning window creation, activation, hiding, showing and flashing.

### The measurement the design rests on

The original plan assumed a hidden window has no taskbar button and therefore cannot flash. That
assumption was wrong, and the whole notification design depends on it being wrong.

A controlled A/B on one window, identical `FlashWindowEx(FLASHW_ALL | FLASHW_TIMERNOFG)` in both
states, seven messages each — the only difference is `vis=`:

```
19:12:53.058 | 0x8006 | HSHELL_FLASH | 0x002813D0 | vis=Y | WindowsTerminal | "HYDRAWIN-BELL"
…                                                          (7 total, visible)
19:13:14.376 | 0x8006 | HSHELL_FLASH | 0x002813D0 | vis=n | WindowsTerminal | "HYDRAWIN-BELL"
…                                                          (7 total, SW_HIDE-hidden)
```

Independent confirmation arrived unsolicited in the same session: a hidden `msrdc` window that
nobody had touched flashed 447 times, every one with `vis=n`. Of 472 `HSHELL_FLASH` messages
captured overall, **454 were for windows that were not visible**.

`EVENT_OBJECT_NAMECHANGE` likewise fires regardless of visibility — 11 of 135 captured name-change
events carried `vis=n`.

So hiding a window costs no awareness on either channel. What is *not* guaranteed is that the
application calls `FlashWindowEx` in the first place; that is a per-application question, answered
below.

### A shell-hook trap

**The shell reports `SW_HIDE` as `HSHELL_WINDOWDESTROYED` and `SW_SHOW` as
`HSHELL_WINDOWCREATED`, reusing the same handle.**

```
19:15:37.999 | 0x0002 | HSHELL_WINDOWDESTROYED | 0x0002147A | vis=n | chrome | …   ← our SW_HIDE
19:16:02.970 | 0x0001 | HSHELL_WINDOWCREATED   | 0x0002147A | vis=Y | chrome | …   ← our SW_SHOW
19:24:28.817 | 0x0002 | HSHELL_WINDOWDESTROYED | 0x0002147A | vis=n | chrome | …   ← a real close
```

A hide and a genuine close are indistinguishable in the shell hook; `IsWindow(hwnd)` is the
discriminator. HydraWin's inventory is unaffected because it tracks with WinEvents plus an explicit
hidden set, but anything that later consumes the shell hook must not read `WINDOWDESTROYED` as
"gone".

## What the hub does

`NotificationHub` lives in Core and is driven by plain method calls rather than event
subscriptions, which is what makes every rule below testable with no Win32 and no WPF:

| Input | Meaning |
| --- | --- |
| `OnFlash(window)` | A window's taskbar button flashed |
| `OnTitleChanged(window, old, new)` | A title changed; rules are evaluated here |
| `OnForegroundChanged(hwnd)` | Something took focus. `0` means "nothing foreign is in front" |
| `OnWindowDisappeared(hwnd)` | A window closed |

State is **one pending entry per window** — kind, label, timestamp — so repeat signals coalesce
rather than accumulate. `FLASHW_TIMERNOFG` makes a window flash until it is foregrounded, so a
count of signals received would climb without bound and mean nothing; a count of *windows waiting*
is what the user actually wants to know.

A notification is dropped, not recorded, when the window is **unassigned** (it belongs to no task,
so there is no badge to raise and it is visible in every task anyway) or is **already in the
foreground** (the user is looking at it).

The label is built from the window — process file name plus live title — so an application nobody
has configured still produces something readable. A rule's `Label` overrides it only when a rule
actually fires.

The flashing handle is resolved to its root with `GetAncestor(GA_ROOT)` before lookup, so an
application that flashes an owned dialog rather than its main window still badges the right task.

### The clearing matrix

| Event | Badge |
| --- | --- |
| The window gains focus | **cleared** |
| The window closes | cleared |
| Switching to the task | **survives** |
| Anything else | survives |

The third row is the one that took measurement to get right, and it applies to every kind, not just
flash-kind. **Teams flashes once per unread run**: the first message into a read conversation
flashes, and then it is silent until the user actually opens and reads the chat. Clear the badge on
a task switch and no further flash will ever raise it again — the user switches to the task, the
badge disappears, and the unread message is never mentioned again.

So a badge means exactly *"you have not looked at this window yet"*, and only looking at it clears
it. Applying that uniformly also keeps behaviour predictable for applications nobody has tested.

### A window that is hidden still badges

A flash from a hidden window raises a badge exactly as a visible one does — that is the headline
capability, and it is what the measurement above buys. A background window of the *active* task
badges too: the user cannot see it either way.

### The foreground signal has a hole the tracker cannot fill

`WindowTracker` registers its WinEvent hook with `WINEVENT_SKIPOWNPROCESS`, so it never reports
**HydraWin itself** taking focus. Without a second source the hub goes on believing the last
foreign window the user visited is still in front, and suppresses that window's notifications for
the rest of the session — focus a task's window once, come back to HydraWin, and that window can
never badge again.

`MainWindow.OnActivated` therefore reports `OnForegroundChanged(0)`: nothing foreign is in front.
It is the other half of a signal the tracker structurally cannot provide, and it has a regression
test because the bug is silent.

## Title rules

A rule is a process filter (`*` or empty for any) plus a regex against the window title. Matching
is **edge-triggered**: the rule fires when the new title matches and the old one did not, so a
window sitting at a matching title badges once rather than on every repaint.

The same `MatchesTitle` half is used twice by the edge trigger and once by the editor's live
preview, so the preview cannot promise something the tracker would not do.

A malformed or slow pattern counts as **no match** rather than throwing — this runs on the
title-change path, where a bad pattern must cost its own rule and nothing else. Patterns compile
with a 100 ms timeout, and a catastrophically backtracking pattern degrades to "no match".

The cost budget matters: a busy Claude Code terminal produces about **one title event per second**,
so the hub does no expensive work per event.

## Per-application behaviour, as measured

### Teams

- **Flashes on an incoming message in every window state** — visible-and-background, minimized, and
  `SW_HIDE`-hidden — always three flashes, always the same 1.06 s cadence. Only `vis=` changes:

  ```
  20:07:38.104 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=n | ms-teams | "Chat | anton suchov | Microsoft Teams"
  20:07:39.162 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=n | ms-teams | "Chat | anton suchov | Microsoft Teams"
  20:07:40.231 | 0x8006 | HSHELL_FLASH | 0x001111C2 | vis=n | ms-teams | "Chat | anton suchov | Microsoft Teams"
  ```

- **Never changes its window title.** Not when visible, not when hidden, not on an incoming
  message; zero name-change events across every run. Its titles are steady-state and reflect the
  selected chat, with no unread count anywhere: `"anton suchov | Microsoft Teams"`,
  `"Chat | anton suchov | Microsoft Teams"`. Teams is a **flash-only** application, and the
  plausible-looking rule `^\((\d+)\)` for it is simply wrong — the count never reaches the title.
- **One flash per unread run**, as above. This is also a trap for anyone re-measuring: an
  experiment that sends a second message without reading the first records a false negative. Two
  such false negatives were recorded and later reversed here. **Clear the unread state immediately
  before every measurement.**

### Claude Code in Windows Terminal

Both channels carry a signal, at very different speeds:

| Channel | Latency | Content |
| --- | --- | --- |
| Title (`✳` marker appears) | immediate | Distinguishes busy from idle, and carries the session name |
| Flash (`HSHELL_FLASH`) | **~61 s** | "this window wants attention", nothing more |

The flash delay is a deliberate fixed timer, not jitter — **61.1 s in every case, to within 0.1 s**
across five independent sessions, one flash per session rather than a repeating train:

```
0x00151430  idle 21:55:11.743  ->  flash 21:56:12.813   delay 61.1s
0x000E15E8  idle 21:58:22.170  ->  flash 21:59:23.237   delay 61.1s
0x003F1512  idle 22:00:35.602  ->  flash 22:01:36.664   delay 61.1s
0x00630C56  idle 22:02:06.377  ->  flash 22:03:07.444   delay 61.1s
0x00A515E6  idle 22:02:11.084  ->  flash 22:03:12.149   delay 61.1s
```

**The decision, taken knowing that number: badge from the flash and accept the minute.** HydraWin
therefore ships no Claude Code title rule, and a finished session badges through the same mechanism
as everything else — one channel, no per-application regexes.

The title is still parsed, for a different purpose: the window row shows the live activity marker
so a session's progress is visible in the overview without switching to it. Display, not
notification. The shape is `<marker> <session name>`:

- **busy** — a spinner frame from `◐ ◑ ◒ ◓` (`U+25D0`–`U+25D3`), advancing about once a second;
- **idle** — `✳` (`U+2733`), after which the title stops changing entirely.

`claude -p` (non-interactive) sets no such title; only an interactive session does. `✳` also
appears briefly at the *start* of an activity, which is harmless for display and would have needed
a debounce had it been used for notification.

### Windows Terminal

A BEL character flashes the taskbar from both emission paths — a Win32 console application writing
BEL through the console API, and a VT-native `printf '\a'` — six bells producing six flashes on
each. **A hidden terminal still flashes**, eight bells through an `SW_HIDE`-hidden window producing
eight `vis=n` flashes.

This depends on two settings that a default install has neither of:

- Windows Terminal `bellStyle` must include `"taskbar"`. Valid values are `"all"`, `"audible"`,
  `"window"`, `"taskbar"`, `"none"` — **`"taskbarFlash"` is not one of them**, and Terminal
  silently ignores an invalid value. Three separate "the bell never flashes" results were recorded
  here before anyone noticed the setting had never been in effect.
- Claude Code's `preferredNotifChannel` must be `terminal_bell`.

## Known limitation: the bell-to-badge leg is unverified

Everything above about Windows Terminal's bell was measured with a standalone probe listening for
the shell message. **Driving that same bell into a running HydraWin and getting a badge has not
been demonstrated.** A test with a correctly configured `bellStyle` produced no badge for the
terminal window.

What that does and does not mean: the flash channel itself is thoroughly verified — a
`FlashWindowEx` against an assigned window badges reliably, including while HydraWin has it hidden.
What is untested end to end is specifically *bell → shell flash → badge*, which is the path Claude
Code depends on. The failing test had confounds of its own (`wt` reuses an existing process and
opens tabs rather than windows unless forced, and the bell could not be observed independently), so
the cause could be Windows Terminal, the harness, or HydraWin. Anyone touching this path should
settle it against a real Claude Code session going idle, and expect to wait the 61 s.
