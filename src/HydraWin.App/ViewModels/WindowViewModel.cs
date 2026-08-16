using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HydraWin.Core.Tracking;

namespace HydraWin.App.ViewModels;

/// <summary>
/// One window as a row, in either the unassigned pane or under a task.
/// </summary>
/// <remarks>
/// The same instance is reused as a window moves between tasks and the unassigned pane, so its
/// live title and icon survive the move.
/// </remarks>
public sealed partial class WindowViewModel : ObservableObject
{
    /// <summary>Creates a row over a tracked window.</summary>
    public WindowViewModel(TrackedWindow window, ImageSource? icon)
    {
        ArgumentNullException.ThrowIfNull(window);
        Source = window;
        Icon = icon;
        Title = window.Title;
    }

    /// <summary>The tracked window behind this row.</summary>
    public TrackedWindow Source { get; }

    /// <summary>The window handle, and the payload of a drag.</summary>
    public nint Hwnd => Source.Hwnd;

    /// <summary>The process image file name, e.g. <c>Code.exe</c>.</summary>
    public string ProcessName => Source.ProcessFileName;

    /// <summary>The app's icon, or <see langword="null"/> to fall back to a generic glyph.</summary>
    public ImageSource? Icon { get; }

    /// <summary>
    /// The live window title. Updated straight from <c>WindowTracker.WindowTitleChanged</c>, so a
    /// row shows what its window is doing without the user switching to it.
    /// </summary>
    /// <remarks>
    /// Task 01 measured about one title event per second per busy Claude Code terminal, so setting
    /// this must stay cheap: it recomputes three properties of one row and touches no list.
    /// </remarks>
    [ObservableProperty]
    public partial string Title { get; set; }

    /// <summary>The title with any activity marker stripped — what the row actually shows.</summary>
    [ObservableProperty]
    public partial string DisplayTitle { get; set; } = string.Empty;

    /// <summary>Whether a Claude Code session in this window is working or idle.</summary>
    [ObservableProperty]
    public partial TitleActivity Activity { get; set; }

    /// <summary>
    /// The marker glyph as it currently appears in the title, or empty. Bound directly, so the
    /// spinner in the row animates for free as the title events arrive.
    /// </summary>
    [ObservableProperty]
    public partial string ActivityGlyph { get; set; } = string.Empty;

    /// <summary>True while HydraWin is the reason this window is not on screen.</summary>
    [ObservableProperty]
    public partial bool IsHydraWinHidden { get; set; }

    /// <summary>
    /// True when this window refused to be hidden — in practice an elevated one, which UIPI stops
    /// a non-elevated HydraWin from touching. Task 10 gives it a proper treatment.
    /// </summary>
    [ObservableProperty]
    public partial bool IsUnmanageable { get; set; }

    partial void OnTitleChanged(string value)
    {
        (TitleActivity activity, string text) = ClaudeCodeTitle.Parse(value);

        Activity = activity;
        DisplayTitle = text.Length > 0 ? text : value;
        ActivityGlyph = activity == TitleActivity.None
            ? string.Empty
            : value.TrimStart()[..1];
    }
}
