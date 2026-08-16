# UI and shell — architecture

## The layering rule

**No Win32 above Core.** All P/Invoke lives in `src/HydraWin.Core/Interop/NativeMethods.cs` behind
small interfaces — `IWindowApi`, `IScreenApi`, `IIconSource`, `IHotkeyApi`, `IShellHookApi` — and
the App layer calls those. Views bind, view models orchestrate, Core does the work.

This is not decoration. Two examples of it changing the design for the better:

- Window icons could have been `Icon.ExtractAssociatedIcon` in an App service. That is Win32 by
  proxy plus a `System.Drawing.Common` dependency, so `ExtractIconExW` joined `NativeMethods`
  behind `IIconSource` and the App turns the `HICON` into an `ImageSource` with plain WPF.
  (`ExtractIconExW` over `SHGetFileInfo` because its signature is blittable and so works with
  `[LibraryImport]`.)
- The picker needs screen-space queries and two window tricks. Those became `IScreenApi` rather
  than a handful of `DllImport`s in the view.

The analyser pushes in the same direction: Sonar's S4200 rejects trivial forwarding wrappers, which
forces the interop surface to expose *coarse operations* — `DescribeWindow` reads everything the
filter needs in one pass, `TryGetIdentity` answers "is this still a window, and whose" together —
rather than a 1:1 mirror of user32.

## Rebuilding rows, and the one thing that must not

Structural changes — a task created, a window assigned, a switch completed — rebuild the task list.
That is cheap because tasks and their windows are few.

**Title changes deliberately do not.** A busy Claude Code terminal produces about one title event
per second, so `OnWindowTitleChanged` looks the row up in a handle-keyed dictionary and sets one
property. Nothing else is touched.

A rebuild also destroys the inline rename box — it clears and re-adds every row, regenerating the
item containers, taking focus, caret and half-typed name with it — and windows appear and disappear
constantly. So a rebuild **defers** while a row is being renamed and flushes when the rename
commits or is abandoned. The pane is a second or two stale meanwhile, bounded by the fact that any
click outside commits.

Similarly, a newly created task remembers its **id**, not its row object: creating a task raises
`TasksChanged`, so the row has already been replaced by the time the command returns. `Rebuild`
applies the renaming flag by construction, which holds however many rebuilds happen in between.

## Drag and drop

Hand-rolled, in `DragDropSupport.cs`. `GongSolutions.WPF.DragDrop` was considered and declined: a
task row carries **two mouse semantics on one element** — a click switches to the task, a drag
reorders it — and Gong takes over `PreviewMouseDown`/`Move` on the controls it attaches to. Its one
big saving, the default `ObservableCollection` reorder handler, applies to one of the four drop
kinds here.

The two payloads are a window handle and a task id; rows are found by walking the visual tree for a
tagged ancestor. Details in [reference.md](reference.md#drag-and-drop-contract).

**When a drag starts differs by row kind, and that is deliberate:**

- A **window row** begins its drag on mouse-*down*. It has no click action to protect, and
  mouse-down is the moment the user expects to see they have picked something up.
- A **task row** waits for the movement threshold. A click there switches tasks, so starting on the
  press would make every switch a drag.

Feedback is a translucent copy of the row (`DragGhostAdorner`) tracked at *window* level so it
keeps up over the toolbar, the splitter and the gaps between drop targets — the drop cursor alone
says a drag is happening but not what is being dragged, and shows nothing until the pointer is over
a valid target.

One WPF trap worth knowing: the rename `TextBox` is `AllowDrop="False"`, because a `TextBox`
handles `DragOver`/`Drop` itself for text and would swallow a window dropped on that row.

## The picker gesture

A Spy++-style crosshair on every task row: press it, drag over the desktop with the window under
the pointer outlined, release, and that window joins the task. It exists because **a live OS window
cannot be dragged into the list** — OLE drag-and-drop carries data, not window handles, and Windows
exposes no shell protocol for dragging a window.

Three things about it are not obvious.

**It does not use WPF mouse capture.** The obvious implementation — capture the mouse on the
crosshair, track `MouseMove`, finish on `MouseUp` — does not survive this gesture, because *any
window operation performed while the capture is held makes WPF release it*, and the pick then ends
on the first movement with no way to tell why. Two such operations are unavoidable here: getting
the main window out of the way, and showing the highlight. Taking the capture later did not help,
and neither did `CaptureMode.Element`. So the picker follows the hardware instead: a 30 ms
`DispatcherTimer` reads `GetCursorPos` and `GetAsyncKeyState` and ends the gesture when the button
comes up. That is what Spy++ has always done, and it is indifferent to activation, z-order and
focus. The timer runs only while the button is held.

**HydraWin gets out of the way by dropping to the bottom of the z-order**, not by becoming
click-through. *Stay on top* is on by default, so without moving it every window behind the app
would be unpickable. The first attempt used `WS_EX_TRANSPARENT` plus a translucent `WS_EX_LAYERED`
— but WPF owns `WS_EX_LAYERED` on a window whose `AllowsTransparency` is false and strips it
straight back out, leaving the window **opaque but invisible to the mouse**; if a pick then ended
abnormally the whole app stayed that way and ignored every click. Z-order demotion achieves the
same thing with nothing to strand.

**The frame only outlines what a release would actually take.** It first highlighted whatever
`WindowFromPoint` returned, which made the desktop and the taskbar look selectable and then refused
them — the highlight promised something the drop would not honour. The test is now "is this handle
in the inventory", which is the very condition for accepting it, so the highlight and the drop
cannot disagree. The handle is still remembered when it is not outlined, so releasing over the
taskbar gets an explanation rather than silence.

Refusals reuse the filter: being in the inventory *is* the whitelist, since `OwnProcess` and
`Elevated` are already clauses of it. Only on a miss is `WindowTracker.ExplainOne` asked for the
reason.

The highlight is positioned in **physical pixels** through `SetWindowPos`, not through WPF's
`Left`/`Top`, whose device-independent units would need DPI arithmetic that breaks the moment the
pointer crosses to a monitor with a different scale factor. It carries `WS_EX_TRANSPARENT` so
`WindowFromPoint` never returns it, and `WS_EX_TOOLWINDOW` so HydraWin's own filter would reject it
even if it were enumerated.

## Focus policy

Who gets the keyboard after a switch depends on where the switch came from, and getting this wrong
is worse than it sounds.

- **A click inside HydraWin** keeps the keyboard with HydraWin. The task's windows are raised with
  `SetWindowPos(HWND_TOP, SWP_NOACTIVATE)` — in reverse order, so the last-active one ends up on
  top — but not activated.
- **A hotkey or the tray** hands focus to the task's last-active window. The user is somewhere else
  entirely and the point is to land in the task.

The gap that produced this rule: switching used to end by focusing one of the task's windows, so
HydraWin was no longer the foreground application and a `Del` pressed straight after clicking a row
went to *that application* — at best doing nothing, at worst deleting something in it.

`SetForegroundWindow` works on both paths only because HydraWin is the foreground process during a
user-initiated switch, or holds the input state the foreground-lock rules grant to a hotkey
registrant on `WM_HOTKEY` (measured: after `Ctrl+Alt+1` with the manager window hidden, the
foreground window was the task's window, with no `SW_SHOWNA` fallback needed). There is
deliberately no `AttachThreadInput` trick for paths where neither holds.

*Stay on top* is a persisted setting, on by default, and cannot usefully be scoped to "only while
switching": `SwitchTo` is synchronous, so a flag raised and lowered inside it never reaches a
rendered frame. What buries the window is the switch *ending* by raising the task's windows.

## Keyboard

`Del` deletes the **active** task — the list has no selection, since clicking a row switches to it,
so the accent-bordered active task is the only sensible target. It is ignored while a `TextBox` has
focus (or `Del` would eat the task instead of a character mid-rename) and while a pick is running.

Deleting asks for confirmation **only when the task holds windows**. The dialog exists to say what
becomes of them; with none open it has nothing to say and only costs a keystroke.

A new task's name box takes focus with its text selected, so the name can be typed straight away.
Getting that to work needed the right dispatcher priority: WPF runs **Normal (9) before Render (7)
and Loaded (6)**, so a `Focus()` queued at the default priority ran against a container that had
not been arranged, returned `false`, and said nothing. It is dispatched at `Input` (5), and also
wired to the box's `Loaded`, because a box created *already* visible never raises
`IsVisibleChanged`.

## Tray, single instance, and the hotkey thread

**The tray icon is owned by `App`, not by the window**, so it does not depend on the window existing
or being visible — which is the entire point once closing hides to tray. The menu is rebuilt each
time it opens, because the task list is most of what it shows.

**Single instance** is a named mutex (`Local\HydraWinSingleton`) plus a named `EventWaitHandle`
(`Local\HydraWinShowWindow`). The first instance owns the mutex and parks a background thread on the
event; a second launch finds the mutex taken, sets the event, and exits 0. An event rather than a
pipe or `WM_COPYDATA` **because the second instance has nothing to say** — "show yourself" is the
entire payload — so there is no server loop, no message pump and no serialisation. `--restore-all`
returns before any of this, so it works while a wedged first instance holds the mutex.

**The hotkeys own a thread.** `WM_HOTKEY` is delivered to the message queue of whichever thread
registered the hotkey, so registering from the UI thread would mean a wedged UI takes the panic
restore down with it. A dedicated background thread registers them and runs the loop; the panic
restore executes **inline on that thread** — the journal is mutex-guarded and the restore is pure
Win32, so both are safe there — while every other action marshals to the dispatcher, because the
switch engine and the view models are not thread-safe.

No window class was needed: `RegisterHotKey` accepts a **null** window handle, in which case
`WM_HOTKEY` goes to the calling thread's queue rather than to a window. That removed the window
class, the `WndProc` and the delegate-lifetime hazard that comes with them.

Hotkeys belong to the thread that claimed them, so rebinding means stopping the service and
starting a new one; the loop exits cleanly on `StopLoop` and releases what it registered on the way
out.

## Settings and rule dialogs

Both edit **copies** and write back only on OK, so Cancel is real. Binding straight at the live
settings would persist every keystroke and leave a half-edited hotkey registered.

The hotkey capture box reads modifiers and key from `PreviewKeyDown` and accepts exactly the three
key families `HotkeyBinding.TryResolve` understands — digits, letters, `F1`–`F24` — so the dialog
cannot produce something the resolver would then reject. Modifier-only presses are shown but not
committed: they are what the fingers are doing on the way to the real key. It is a keyboard trap
while focused, deliberately, or the interesting combinations could not be typed.

**The two rule types treat a broken regex differently, on purpose:**

- A **re-attach rule** blocks Save with an inline error. It has no enabled flag to fall back to,
  and a rule that silently never matches would look like a window that simply stopped rejoining
  its task.
- A **notification rule** is *saved switched off* with the reason shown. These are the secondary
  channel and the flash covers every application without them, so losing a half-written rule
  because the user tabbed away mid-regex would be worse than keeping it quiet.

Both preview live against the open windows, through one `RulePreview` helper in Core that calls the
same matching the tracker uses.

## Crash handling and the log

`DispatcherUnhandledException` and `AppDomain.UnhandledException` both log the exception with its
stack, run `RestoreAll`, and then **let the process die** — `e.Handled` is never set.

That is deliberate rather than defeatist. An application in an unknown state is exactly what the
recovery journal exists for, and pretending otherwise risks a second, worse failure with the user's
windows still hidden. What the handler buys is the two things a bare crash would not do: a line in
the log saying what happened, and the windows back on screen before the process goes. The restore
is safe from either thread — pure Win32 over a mutex-guarded file — which matters because
`AppDomain.UnhandledException` can arrive on any of them.

This was validated in the most convincing way available: a real null-reference bug crashed the
first attempt to open the settings dialog, and the log caught it with a full stack **and** recorded
`restore attempted — restored 1 window(s)`. That is how the bug was found.

`AppLog` is an append-with-cap file logger, not a framework. It has to survive being called from
the hotkey thread while the UI thread is busy, never grow without bound, and above all never be the
reason HydraWin fails — so every operation swallows its own IO exceptions. One rollover, not a
numbered series: two files bound the disk cost with no cleanup logic to get wrong, and nobody
debugging yesterday's switch needs the week before it. It is fed from the same funnel that writes
the status line, so anything worth telling the user is also in the file.

## WPF traps met along the way

Worth knowing before touching the XAML.

- **A local value outranks a style trigger.** `Background`, `BorderBrush` and `BorderThickness` set
  as attributes on the task-row `Border` silently beat the `IsActive` `DataTrigger`, and the active
  task never looked active. Every visual default belongs in the `Style`.
- **`CommandParameter="1"` is a string.** A generated `RelayCommand<int>` throws on it during
  layout and kills the app at startup — a build-clean solution proves nothing until it is run.
- **Centring text centres its box, not the ink.** Segoe UI reserves more room above the baseline
  than digits occupy, so a vertically centred digit in the notification badge renders about 0.6 px
  low, which `UseLayoutRounding` then snaps out to a whole device pixel. Fixed with a
  `TranslateTransform` on the glyph — a margin would be halved by the centring and would disturb
  the pill width that the horizontal margin sets for two-digit counts.
- **A generated property setter fires its change hook immediately**, including during the
  constructor that is still assigning the other fields. Both rule view models set a `ready` flag
  last and refuse to refresh before it.
