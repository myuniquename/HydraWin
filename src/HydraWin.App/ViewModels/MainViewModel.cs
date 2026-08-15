using CommunityToolkit.Mvvm.ComponentModel;

namespace HydraWin.App.ViewModels;

/// <summary>
/// Root view model for the main window. Placeholder — task 07 builds this out with the task
/// collection, the unassigned pane and drag-and-drop assignment.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Window title; task 07 makes it <c>HydraWin — &lt;active task&gt;</c>.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "HydraWin";
}
