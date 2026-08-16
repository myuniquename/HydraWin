# Notifications — how to

## Work out why a window never badges

Walk it in this order; each step rules out a whole class of cause.

1. **Is the window assigned to a task?** An unassigned window is visible in every task, so there is
   no badge to raise and the hub drops the signal deliberately. Assign it.
2. **Was it in the foreground when it signalled?** Notifications from the foreground window are
   suppressed — you were looking at it.
3. **Does the application flash at all?** Everything downstream assumes it calls `FlashWindowEx`.
   Test the channel independently of the application: leave the window assigned and backgrounded,
   and flash it from outside.

   ```powershell
   # FLASHW_ALL | FLASHW_TIMERNOFG against a window you have the handle for
   [Flash]::FlashWindowEx(...)   # see docs/notifications/architecture.md for the flag values
   ```

   If that badges and the application does not, the application is the answer, not HydraWin.
4. **Did the shell hook register?** If it did not, HydraWin says so on startup —
   *"The shell refused the notification hook — badges are off."* Nothing badges at all in that
   state.
5. **Is it Teams, on a second message?** Teams flashes once per unread run. Open and read the chat,
   then send a fresh message; anything else measures the wrong thing.

**Verify:** the task row shows a count and the window's row shows a red dot, with the window's
name in the badge tooltip.

## Make a terminal bell reach HydraWin

Needed for Claude Code, and for anything else that rings the bell.

1. In Windows Terminal settings, set `bellStyle` to include `"taskbar"`:

   ```json
   "profiles": { "defaults": { "bellStyle": [ "audible", "window", "taskbar" ] } }
   ```

   Valid values are `"all"`, `"audible"`, `"window"`, `"taskbar"`, `"none"`. **`"taskbarFlash"` is
   not valid** and Terminal ignores it silently, which is exactly how three earlier measurements
   came out wrong.
2. For Claude Code, set `preferredNotifChannel` to `terminal_bell` in `~/.claude/settings.json`.

**Verify:** background the terminal and ring the bell by hand —
`[Console]::Out.Write([char]7)` in PowerShell, or `printf '\a'` under WSL. The taskbar button
should flash. If the task badges too, the whole path works; see the known limitation in
[architecture.md](architecture.md#known-limitation-the-bell-to-badge-leg-is-unverified) before
concluding anything from a negative.

Expect Claude Code's own bell about **61 seconds** after the session goes idle. That is a fixed
delay in Claude Code, not something HydraWin can shorten.

## Write a title rule

For an application that announces something in its title and never flashes. Nothing ships enabled,
so every rule here is one you chose.

1. Open **Settings… → Notifications**, press **Add rule**.
2. Set the process to an image file name, or leave `*` to match any process.
3. Write a regex for the title. The list under the row previews which open windows it matches right
   now, updated as you type — the fastest way to see that a pattern is too broad.
4. Optionally set a badge label; empty falls back to the window's own name.
5. Tick **On**.

Matching is **edge-triggered**: the rule fires when the title starts matching, not for as long as
it matches. Write the pattern against the transition you care about.

A pattern that is not a valid regex is **saved switched off** with the reason shown, rather than
rejected — losing a half-written rule because you tabbed away would be worse than keeping it quiet.

**Verify:** make the application produce the title in question and watch the task row. If the
preview matched but no badge appears, the title probably already matched before the change; the
rule is edge-triggered.

## Do not write these rules

Two that look obviously right and are not:

- **`^\((\d+)\)` for Teams.** Teams never changes its window title in any state — the unread count
  never reaches it. Teams badges through the flash channel and needs no rule.
- **`^✳ ` for Claude Code.** It would work, and it would fire a full minute before the flash does.
  It is deliberately not shipped: one mechanism with no per-application regexes was judged worth
  the minute. If you want the minute back, this is the rule to add — with `WindowsTerminal.exe` as
  the process — knowing that `✳` also appears briefly at the start of an activity and will
  occasionally badge early.

The one rule that *is* seeded, disabled, is a browser unread count — `chrome.exe` with
`^\(\d+\)`. It is there as a worked example to copy. Before enabling it, note that a browser tab
titled "(2) something" is indistinguishable from two unread messages.

## Clear a badge

Focus the window. That is the only thing that clears one — not switching to its task, not
dismissing anything.

The quickest route is to **click the badge**: it switches to the task, focuses the window that
asked most recently, and clears it in one gesture.

If badges seem sticky, that is the design: a badge means "you have not looked at this window yet",
and Teams' one-flash-per-unread-run behaviour makes any looser rule lose the notification for good.

**Verify:** click the badge, then click back to HydraWin. The count is gone and does not return
until something signals again.

## Turn on tray balloons

**Settings… → General → Show a tray balloon when a window wants attention.** Off by default; the
badge is the product and a pop-up is noise until asked for. The badge appears either way.
