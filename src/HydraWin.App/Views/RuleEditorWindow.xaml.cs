using System.Windows;
using HydraWin.App.ViewModels;
using HydraWin.Core.Workspaces;

namespace HydraWin.App.Views;

/// <summary>
/// Edits one window's re-attach rule, showing what it currently matches as the user types.
/// </summary>
public partial class RuleEditorWindow : Window
{
    private readonly MainViewModel main;
    private readonly WindowViewModel window;
    private readonly RuleEditorViewModel editor;

    /// <summary>Opens the editor for a window that belongs to a task.</summary>
    public RuleEditorWindow(MainViewModel main, WindowViewModel window)
    {
        ArgumentNullException.ThrowIfNull(main);
        ArgumentNullException.ThrowIfNull(window);

        InitializeComponent();

        this.main = main;
        this.window = window;

        WindowAssignment? assignment = main.AssignmentOf(window);
        editor = new RuleEditorViewModel(
            assignment?.Rule ?? new ReattachRule(),
            main.Inventory,
            window.Hwnd);

        DataContext = editor;
        Title = $"Re-attach rule — {window.DisplayTitle}";
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!editor.CanSave)
        {
            // The message is already on the dialog; refusing to close is the whole feedback.
            return;
        }

        var edited = new ReattachRule();
        editor.ApplyTo(edited);

        if (main.UpdateReattachRule(
            window, edited.ProcessFileName, edited.TitlePattern, edited.TitleIsRegex))
        {
            DialogResult = true;
        }

        Close();
    }
}
