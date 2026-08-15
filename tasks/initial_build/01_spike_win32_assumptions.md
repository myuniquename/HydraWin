# Task 01 — Spike: verify the risky Win32 assumptions

Status: **not started**
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
(wParam, hwnd, title) pair. Procedure: set Windows Terminal `"bellStyle": "taskbarFlash"`, ring
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

*(to be filled by the implementer — answers to Q1/Q2/Q3 with pasted evidence, per-app quirks,
plan corrections made, and the list of new / modified / deleted files)*
