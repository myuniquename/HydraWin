using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HydraWin.Core.Interop;

// Both namespaces define these; the picker speaks Win32's physical-pixel versions throughout.
using Point = HydraWin.Core.Interop.Point;
using Rect = HydraWin.Core.Interop.Rect;

namespace HydraWin.App;

/// <summary>
/// The Spy++-style target: press the crosshair on a task row, drag over the desktop with the
/// window under the pointer outlined, release to pick it.
/// </summary>
/// <remarks>
/// <para>
/// While a pick is running the main window is made click-through and translucent. That is not
/// decoration: <em>Stay on top</em> is on by default, so without it every window behind HydraWin
/// would be impossible to point at.
/// </para>
/// <para>
/// All coordinates here are physical screen pixels, straight from Win32. Converting through WPF's
/// device-independent units would need DPI arithmetic that goes wrong as soon as the pointer
/// crosses onto a monitor with a different scale factor.
/// </para>
/// </remarks>
internal sealed class WindowPicker : IDisposable
{
    private readonly IScreenApi screen;
    private readonly Window owner;
    private readonly nint ownerHandle;

    private readonly DispatcherTimer poll;

    private HighlightWindow? highlight;
    private Action<nint>? onPicked;
    private nint currentTarget;
    private bool wasTopmost;
    private bool disposed;

    /// <summary>Creates a picker bound to the main window.</summary>
    internal WindowPicker(Window owner, IScreenApi screen)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(screen);

        this.owner = owner;
        this.screen = screen;
        ownerHandle = new WindowInteropHelper(owner).Handle;

        highlight = new HighlightWindow(screen);
        highlight.HideFrame();

        // 30 ms is well under the eye's threshold for the frame lagging the pointer, and only runs
        // while the button is held.
        poll = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(30),
        };
        poll.Tick += OnPoll;
    }

    /// <summary>Whether a pick is currently in progress.</summary>
    internal bool IsPicking => poll.IsEnabled;

    /// <summary>Told when a pick ends for a reason the user should hear about.</summary>
    internal Action<string>? Report { get; set; }

    /// <summary>
    /// Whether a window is one the app would actually accept. Only these get outlined.
    /// </summary>
    /// <remarks>
    /// The desktop and the taskbar sit under the pointer like anything else, and outlining them
    /// said "you can pick this" about windows that would then be refused. The frame now shows
    /// exactly what a release would take.
    /// </remarks>
    internal Func<nint, bool>? CanPick { get; set; }

    /// <summary>
    /// Begins a pick. <paramref name="picked"/> is called with the chosen window handle on
    /// release, or with 0 when the pointer was over nothing usable.
    /// </summary>
    internal void Start(Action<nint> picked)
    {
        if (IsPicking)
        {
            return;
        }

        onPicked = picked;
        currentTarget = 0;

        wasTopmost = owner.Topmost;
        owner.Topmost = false;
        screen.SendToBottom(ownerHandle);

        Mouse.OverrideCursor = Cursors.Cross;

        Track();
        poll.Start();
    }

    /// <summary>
    /// One beat of the gesture: follow the pointer, and end when the button comes up.
    /// </summary>
    private void OnPoll(object? sender, EventArgs e)
    {
        PickerInput input = screen.ReadPickerInput();

        if (input.CancelRequested)
        {
            Stop();
            Report?.Invoke("Pick cancelled.");
            return;
        }

        if (input.ButtonHeld)
        {
            Track();
            return;
        }

        nint target = currentTarget;
        Action<nint>? callback = onPicked;

        Stop();
        callback?.Invoke(target);
    }

    /// <summary>Abandons the pick without choosing anything.</summary>
    internal void Cancel()
    {
        if (IsPicking)
        {
            Stop();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!disposed)
        {
            Cancel();
            highlight?.Close();
            highlight = null;
            disposed = true;
        }
    }

    private void Track()
    {
        Point cursor = screen.GetCursorPosition();
        nint target = screen.TopLevelWindowAt(cursor);

        // The app's own windows are never targets - neither the main window nor the highlight
        // itself, which is click-through but belongs to this process all the same.
        if (target == ownerHandle || target == highlight?.Handle)
        {
            target = 0;
        }

        // Remembered even when it will not be outlined, so releasing over the taskbar still gets
        // an answer rather than silence.
        currentTarget = target;

        bool pickable = target != 0 && CanPick?.Invoke(target) != false;

        if (pickable && screen.TryGetWindowRect(target, out Rect rect))
        {
            highlight?.ShowAt(in rect);
        }
        else
        {
            highlight?.HideFrame();
        }
    }

    private void Stop()
    {
        poll.Stop();
        onPicked = null;

        Mouse.OverrideCursor = null;

        owner.Topmost = wasTopmost;
        screen.RestoreZOrder(ownerHandle, wasTopmost);

        // Hidden, not closed: it is reused by the next pick, so no HWND is created while a capture
        // is held.
        highlight?.HideFrame();
    }

    /// <summary>
    /// The outline drawn around the window under the pointer: a borderless, click-through,
    /// never-activated frame placed with Win32 in physical pixels.
    /// </summary>
    private sealed class HighlightWindow : Window
    {
        private readonly IScreenApi screen;

        internal HighlightWindow(IScreenApi screen)
        {
            this.screen = screen;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            IsHitTestVisible = false;

            var accent = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF));
            accent.Freeze();
            Content = new System.Windows.Controls.Border
            {
                BorderBrush = accent,
                BorderThickness = new Thickness(4),
                Background = Brushes.Transparent,
            };

            // Off-screen until the first target, so it never flashes at the origin.
            Left = -32000;
            Top = -32000;
            Width = 1;
            Height = 1;
            Show();

            Handle = new WindowInteropHelper(this).Handle;
            screen.MakeOverlay(Handle);
        }

        /// <summary>This window's handle, so the picker can refuse to target it.</summary>
        internal nint Handle { get; }

        /// <summary>Parked far off-screen; the coordinates a hidden overlay lives at.</summary>
        private static readonly Rect OffScreen = new()
        {
            Left = -32000,
            Top = -32000,
            Right = -31999,
            Bottom = -31999,
        };

        internal void ShowAt(in Rect rect) => screen.PositionOverlay(Handle, in rect);

        /// <summary>
        /// Parks the frame off-screen rather than hiding it.
        /// </summary>
        /// <remarks>
        /// Toggling <c>Visibility</c>, like creating the window, is a window operation — and any
        /// window operation performed while the picker holds the mouse capture makes WPF drop that
        /// capture, ending the pick on the first movement. Moving it costs nothing and touches no
        /// window state.
        /// </remarks>
        internal void HideFrame() => screen.PositionOverlay(Handle, in OffScreen);
    }
}
