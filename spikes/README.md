# spikes/

Throwaway programs written to answer three questions before the app existed: does a hidden window
still flash, does hide → show round-trip cleanly for real applications, and what do the interesting
title transitions actually look like. They are not part of `HydraWin.sln` and are not meant to be.
Their durable descendants live in `src/HydraWin.Core/Interop/`; the *findings* they produced are in
`docs/workspaces/architecture.md` and `docs/notifications/architecture.md`.

They are kept because they are the tooling for re-measuring any of it — three separate wrong
answers were recorded here before the settings involved were right, so measure rather than assume.

Each is a standalone `net10.0-windows` console app — `dotnet run` from its own folder, or run the
built exe directly.

## Safety contract

`HideShow` is the only spike that hides anything. It predates HydraWin's real recovery journal and
does not use it, so it carries a miniature one of its own at
`%APPDATA%\HydraWin\spike-hidden.jsonl`: one line per hidden window, written and
`Flush(flushToDisk: true)`-ed **before** `ShowWindow(SW_HIDE)`, removed only after a verified
re-show. Restore runs from `finally`, `Console.CancelKeyPress`, `ProcessExit`,
`UnhandledException`, a `SetConsoleCtrlHandler` callback and a watchdog timer.

**If anything is ever left hidden, run `hideshow rescue`.** It re-shows every window still listed
in the journal, including ones hidden by a process that has since died. This was verified by
hiding a window, killing the spike with `TerminateProcess` so no handler could run, and
recovering with `rescue`.

Exit paths restore only what *this process* hid (entries carry `OwnerPid`), so running
`hideshow list` never yanks back a window another run is deliberately holding hidden. `rescue`
ignores the owner and sweeps everything.

## HideShow — round-trip fidelity (question 2)

```
hideshow list [substring] [--all] [--hidden]     inventory; --hidden is the baseline snapshot
hideshow cycle (<substring> | --hwnd 0xABC) [--seconds N]    hide, wait, restore, report
hideshow hold  (<substring> | --hwnd 0xABC) [--max-seconds N]   hide until Enter/watchdog
hideshow rescue                                   re-show everything left in the journal
```

`cycle` prints the before/after `WINDOWPLACEMENT`, `GetWindowRect` and monitor device name, and a
`VERDICT` line comparing `showCmd` and `rcNormalPosition`. Note that `ShowWindow` returns the
window's *previous* visibility, not success — the spike checks `IsWindowVisible` afterwards and
reports `GetLastError`, which is how the elevated-window refusal was characterised.

## FlashProbe — flash observability (question 1)

```
flashprobe [--log <path>] [--filter <substring>]
```

Creates a real, never-shown top-level window (a `HWND_MESSAGE` window would **not** receive shell
hook messages), calls `RegisterShellHookWindow`, and logs every `SHELLHOOK` message with its raw
and high-bit-masked wParam. `HSHELL_WINDOWCREATED`/`DESTROYED` traffic is logged on purpose: it is
the baseline proving the hook was alive when a flash fails to arrive.

## TitleWatch — title transitions (question 3)

```
titlewatch [--log <path>] [--filter <substring>] [--visible-only] [--seconds N]
```

`SetWinEventHook(EVENT_OBJECT_NAMECHANGE, …, WINEVENT_OUTOFCONTEXT)` across all processes,
filtered to `idObject == OBJID_WINDOW && idChild == CHILDID_SELF`, running its own
`GetMessage`/`DispatchMessage` pump (a console app has none, and the hook would never fire).
Titles are logged with non-ASCII escaped as `\uXXXX` so markers such as `✳` survive being
pasted into Markdown.
