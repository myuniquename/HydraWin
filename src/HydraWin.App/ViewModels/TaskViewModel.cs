using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HydraWin.Core.Tracking;

namespace HydraWin.App.ViewModels;

/// <summary>
/// One task as a row: its name, colour, windows, and whether it is the active one.
/// </summary>
public sealed partial class TaskViewModel : ObservableObject
{
    /// <summary>Creates a row for a task.</summary>
    public TaskViewModel(Guid id, string name, string colorHex)
    {
        Id = id;
        Name = name;
        ColorHex = colorHex;
        Windows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WindowCount));
            RefreshActivity();
        };
    }

    /// <summary>The task's stable id, and the payload of a task-reorder drag.</summary>
    public Guid Id { get; }

    /// <summary>Display name.</summary>
    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>Row accent colour, <c>#RRGGBB</c>.</summary>
    [ObservableProperty]
    public partial string ColorHex { get; set; }

    /// <summary>Whether this is the task currently switched to.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Whether the window rows are shown.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>Whether the name is being edited in place.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>
    /// How many of this task's windows are waiting to be looked at. A badge means exactly that —
    /// it clears when the window is focused, not when the task is switched to.
    /// </summary>
    [ObservableProperty]
    public partial int NotificationCount { get; set; }

    /// <summary>One line per waiting window, for the badge's tooltip.</summary>
    [ObservableProperty]
    public partial string NotificationTooltip { get; set; } = string.Empty;

    /// <summary>
    /// The strongest activity among this task's windows, so a <em>collapsed</em> task still shows
    /// that something inside it is working (task 07 § F).
    /// </summary>
    [ObservableProperty]
    public partial TitleActivity Activity { get; set; }

    /// <summary>The glyph to show for <see cref="Activity"/>, taken from the busiest window.</summary>
    [ObservableProperty]
    public partial string ActivityGlyph { get; set; } = string.Empty;

    /// <summary>
    /// Lifetime time this task has been the active one, formatted for the row. Empty when the
    /// task has never been switched to, which collapses the cell.
    /// </summary>
    [ObservableProperty]
    public partial string ActiveTimeText { get; set; } = string.Empty;

    /// <summary>The same total spelled out to the second, for the cell's tooltip.</summary>
    [ObservableProperty]
    public partial string ActiveTimeTooltip { get; set; } = string.Empty;

    /// <summary>The windows belonging to this task.</summary>
    public ObservableCollection<WindowViewModel> Windows { get; } = [];

    /// <summary>How many windows are in this task.</summary>
    public int WindowCount => Windows.Count;

    /// <summary>
    /// Adds a window row and starts watching it, so its activity rolls up into this task.
    /// </summary>
    public void Add(WindowViewModel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.PropertyChanged += OnWindowPropertyChanged;
        Windows.Add(window);
    }

    /// <summary>Detaches every window row. Called before the task list is rebuilt.</summary>
    public void Clear()
    {
        foreach (WindowViewModel window in Windows)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
        }

        Windows.Clear();
    }

    private void OnWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WindowViewModel.Activity)
            or nameof(WindowViewModel.ActivityGlyph))
        {
            RefreshActivity();
        }
    }

    /// <summary>
    /// Recomputes the rollup: working beats idle beats nothing.
    /// </summary>
    /// <remarks>
    /// Runs on every title event of every window in the task, so it stays a linear scan of a
    /// handful of rows and allocates nothing.
    /// </remarks>
    private void RefreshActivity()
    {
        TitleActivity strongest = TitleActivity.None;
        string glyph = string.Empty;

        foreach (WindowViewModel window in Windows)
        {
            if (window.Activity == TitleActivity.Working)
            {
                Activity = TitleActivity.Working;
                ActivityGlyph = window.ActivityGlyph;
                return;
            }

            if (window.Activity == TitleActivity.Idle && strongest == TitleActivity.None)
            {
                strongest = TitleActivity.Idle;
                glyph = window.ActivityGlyph;
            }
        }

        Activity = strongest;
        ActivityGlyph = glyph;
    }
}
