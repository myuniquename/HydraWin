using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HydraWin.App.ViewModels;

namespace HydraWin.App.Views;

/// <summary>
/// The settings dialog. Edits copies; nothing is written until OK.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel settings;

    /// <summary>Opens the dialog over the current settings.</summary>
    public SettingsWindow(MainViewModel main)
    {
        ArgumentNullException.ThrowIfNull(main);

        InitializeComponent();

        settings = new SettingsViewModel(main);
        DataContext = settings;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!settings.Validate())
        {
            // Each bad row already says what is wrong with it; refusing to close is the rest.
            return;
        }

        settings.Save();
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Makes the clicked rule the one <i>Delete</i> acts on.
    /// </summary>
    /// <remarks>
    /// The rules are an <c>ItemsControl</c> rather than a <c>ListBox</c> because each row is a
    /// small form and selection chrome around a form reads as noise — so selection is tracked here
    /// instead of coming for free.
    /// </remarks>
    private void OnRuleClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotificationRuleViewModel rule })
        {
            settings.SelectedRule = rule;
        }
    }
}
