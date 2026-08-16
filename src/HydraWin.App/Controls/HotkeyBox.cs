using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HydraWin.App.Controls;

/// <summary>
/// A text box you set by pressing the combination rather than by typing its name.
/// </summary>
/// <remarks>
/// <para>
/// Typing <c>Control+Alt+1</c> by hand is both tedious and easy to get subtly wrong — <c>Ctrl</c>
/// versus <c>Control</c>, <c>D1</c> versus <c>1</c>. Pressing the keys removes the whole class of
/// mistake, and the written form it produces is exactly what
/// <see cref="Core.Workspaces.HotkeyBinding.Split"/> reads back.
/// </para>
/// <para>
/// Modifier-only presses are shown but not committed: they are what the user's fingers are doing
/// on the way to the real key, and committing them would store a combination that can never fire.
/// </para>
/// </remarks>
public sealed class HotkeyBox : TextBox
{
    /// <summary>The combination in written form, e.g. <c>Control+Alt+1</c>.</summary>
    public static readonly DependencyProperty CombinationProperty =
        DependencyProperty.Register(
            nameof(Combination),
            typeof(string),
            typeof(HotkeyBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnCombinationChanged));

    /// <summary>Creates the box.</summary>
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        ToolTip = "Click here and press the combination. Backspace or Delete clears it.";
    }

    /// <summary>The combination in written form.</summary>
    public string Combination
    {
        get => (string)GetValue(CombinationProperty);
        set => SetValue(CombinationProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // Every key press belongs to the capture, including Tab and Escape — the box is a keyboard
        // trap on purpose while it has focus, or the interesting combinations could not be typed.
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Back or Key.Delete)
        {
            Combination = string.Empty;
            return;
        }

        string modifiers = DescribeModifiers(Keyboard.Modifiers);

        if (IsModifier(key))
        {
            // Show progress without committing: Control alone is not a hotkey.
            Text = modifiers.Length == 0 ? string.Empty : modifiers + "+";
            CaretIndex = Text.Length;
            return;
        }

        if (NameOf(key) is not string name)
        {
            return;
        }

        Combination = modifiers.Length == 0 ? name : $"{modifiers}+{name}";
    }

    /// <inheritdoc />
    protected override void OnLostFocus(RoutedEventArgs e)
    {
        // A half-typed modifier run left in the box would read as the stored value.
        Text = Combination;
        base.OnLostFocus(e);
    }

    /// <inheritdoc />
    protected override void OnGotFocus(RoutedEventArgs e)
    {
        SelectAll();
        base.OnGotFocus(e);
    }

    private static void OnCombinationChanged(
        DependencyObject d,
        DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyBox box)
        {
            box.Text = (string)e.NewValue;
            box.CaretIndex = box.Text.Length;
        }
    }

    /// <summary>The modifier names in the order <c>HotkeyBinding</c> writes them.</summary>
    private static string DescribeModifiers(ModifierKeys modifiers)
    {
        List<string> parts = [];

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Control");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        return string.Join("+", parts);
    }

    private static bool IsModifier(Key key) => key
        is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin
        or Key.System;

    /// <summary>
    /// The key's written name, or <see langword="null"/> for keys a hotkey cannot use.
    /// </summary>
    /// <remarks>
    /// Deliberately the same three families <c>HotkeyBinding.TryResolve</c> understands — digits,
    /// letters and F1–F24 — so the box cannot produce something the resolver would then reject.
    /// </remarks>
    private static string? NameOf(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((char)('0' + (key - Key.D0))).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((char)('0' + (key - Key.NumPad0))).ToString();
        }

        if (key is >= Key.A and <= Key.Z)
        {
            return ((char)('A' + (key - Key.A))).ToString();
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return "F" + (key - Key.F1 + 1);
        }

        return null;
    }
}
