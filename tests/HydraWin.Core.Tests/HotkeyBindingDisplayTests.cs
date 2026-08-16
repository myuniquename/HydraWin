using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// The written form of a hotkey. The settings dialog prints it, the user types over it, and it
/// goes back into <c>state.json</c> — so printing and parsing have to be exact inverses or a
/// binding degrades every time it is looked at.
/// </summary>
public sealed class HotkeyBindingDisplayTests
{
    [Theory]
    [InlineData("Control+Alt", "1", "Control+Alt+1")]
    [InlineData("Control+Alt+Shift", "R", "Control+Alt+Shift+R")]
    [InlineData("Win", "F12", "Win+F12")]
    [InlineData("", "H", "H")]
    public void TheWrittenFormIsModifiersThenKey(string modifiers, string key, string expected)
    {
        var binding = new HotkeyBinding
        {
            Action = HotkeyAction.ShowAll,
            Modifiers = modifiers,
            Key = key,
        };

        Assert.Equal(expected, binding.ToDisplayString());
    }

    [Theory]
    [InlineData("Control+Alt+1", "Control+Alt", "1")]
    [InlineData("Control+Alt+Shift+R", "Control+Alt+Shift", "R")]
    [InlineData("Win+F12", "Win", "F12")]
    [InlineData("H", "", "H")]
    public void SplittingIsTheInverseOfPrinting(string written, string modifiers, string key)
    {
        (string splitModifiers, string splitKey) = HotkeyBinding.Split(written);

        Assert.Equal(modifiers, splitModifiers);
        Assert.Equal(key, splitKey);
    }

    [Fact]
    public void EveryDefaultBindingRoundTripsThroughItsWrittenForm()
    {
        foreach (HotkeyBinding original in HotkeyBinding.Defaults())
        {
            (string modifiers, string key) = HotkeyBinding.Split(original.ToDisplayString());
            var rebuilt = new HotkeyBinding
            {
                Action = original.Action,
                TaskOrder = original.TaskOrder,
                Modifiers = modifiers,
                Key = key,
            };

            Assert.True(original.TryResolve(out uint wasModifiers, out uint wasKey));
            Assert.True(rebuilt.TryResolve(out uint nowModifiers, out uint nowKey));
            Assert.Equal(wasModifiers, nowModifiers);
            Assert.Equal(wasKey, nowKey);
        }
    }

    [Fact]
    public void EveryActionDescribesItself()
    {
        // The settings list shows this next to each row; "SwitchToTask" is not something to put in
        // front of a user, and an unnamed action would leave a blank row.
        foreach (HotkeyAction action in Enum.GetValues<HotkeyAction>())
        {
            var binding = new HotkeyBinding { Action = action, TaskOrder = 3 };
            Assert.NotEmpty(binding.DescribeAction());
        }
    }

    [Fact]
    public void ASwitchBindingNamesTheTaskItSwitchesTo()
    {
        var binding = new HotkeyBinding { Action = HotkeyAction.SwitchToTask, TaskOrder = 4 };

        Assert.Contains("4", binding.DescribeAction(), StringComparison.Ordinal);
    }
}
