# Notifications

Hiding a task's windows buys focus and costs awareness: a hidden Teams chat or a finished Claude
Code session is invisible until you switch to it. Badges buy the awareness back.
**`NotificationHub`** decides which windows are waiting to be looked at, fed by the flash message
through
**`ShellHookListener`** and, optionally, by title changes matched against **`NotificationRule`**.
This folder is the canonical documentation for how a backgrounded window asks for attention.

| Doc | Read it for |
| --- | --- |
| [architecture.md](architecture.md) | The two signal channels and what each can and cannot see, the clearing matrix, per-application measurements, the one leg that is unverified |
| [how_to.md](how_to.md) | Writing a title rule, making a terminal bell reach HydraWin, working out why a window never badges |
| [reference.md](reference.md) | `NotificationRule` fields and the shipped default, notification kinds, the hub's inputs |

Related: [../workspaces/README.md](../workspaces/README.md) for why a window is hidden in the first
place · [../ui/README.md](../ui/README.md) for the badge, its tooltip and the click-to-jump.

## What it does

Every task row can carry a badge: a count of its windows that have asked for attention and have not
been looked at since. Clicking it switches to the task and focuses the window that asked most
recently, which is also what clears it.

The primary signal is **`HSHELL_FLASH`**, raised by the shell for any window whose taskbar button
flashes. That choice is what makes badges application-agnostic — HydraWin needs no per-application
code, no rule and no regex to badge an app nobody has configured, and the label it shows is built
from the window itself. Crucially, the message is delivered for windows hidden with `SW_HIDE`,
which is the whole reason the design works at all.

The secondary signal is a **title-change rule**: a process filter plus a regex, edge-triggered.
Nothing ships enabled. It exists for programs that announce something in their title and never
flash.

## Component map

```
   shell ──HSHELL_FLASH──▶ ┌────────────────────┐
                           │ ShellHookListener  │  HwndSource hook on the main window
                           │  (App layer)       │  resolves the flashing handle to its root
                           └─────────┬──────────┘
                                     │ hwnd
   WindowTracker ──title changed────▶│
                ──foreground────────▶│        ┌──────────────────┐
   MainWindow   ──we're in front────▶├───────▶│ NotificationHub  │
   WindowTracker ──window closed────▶│        │  one pending     │
                                     │        │  entry per hwnd  │
                                     │        └────────┬─────────┘
                        NotificationRule[] ───▶        │ TaskBadgeChanged
                        (title channel only)           ▼
                                              ┌──────────────────┐
                                              │  task row badge  │
                                              │  window row dot  │
                                              │  tray tooltip    │
                                              └──────────────────┘
```

## Key files

| Purpose | File |
| --- | --- |
| Which windows are waiting, and when that clears | `src/HydraWin.Core/Notifications/NotificationHub.cs` |
| A title-watching rule | `src/HydraWin.Core/Notifications/NotificationRule.cs` |
| What one pending notification is | `src/HydraWin.Core/Notifications/PendingNotification.cs`, `NotificationKind.cs` |
| Receiving the shell's flash message | `src/HydraWin.App/Services/ShellHookListener.cs` |
| The shell hook behind an interface | `src/HydraWin.Core/Interop/IShellHookApi.cs`, `Win32ShellHookApi.cs` |
| Live preview for the rule editor | `src/HydraWin.Core/Workspaces/RulePreview.cs` |
| Parsing a Claude Code activity marker | `src/HydraWin.Core/Tracking/ClaudeCodeTitle.cs` |
