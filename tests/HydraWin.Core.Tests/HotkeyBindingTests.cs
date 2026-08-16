using HydraWin.Core.Persistence;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Turning the hand-written form in <c>state.json</c> into what <c>RegisterHotKey</c> wants. Pure —
/// no Win32, which is exactly why the resolution lives in Core rather than using WPF's KeyInterop.
/// </summary>
public class HotkeyBindingTests
{
    private static HotkeyBinding Binding(string modifiers, string key) => new()
    {
        Action = HotkeyAction.ShowAll,
        Modifiers = modifiers,
        Key = key,
    };

    [Theory]

    // Digits and letters are their own ASCII values as virtual keys...
    [InlineData("0", 0x30)]
    [InlineData("1", 0x31)]
    [InlineData("9", 0x39)]
    [InlineData("R", 0x52)]
    [InlineData("h", 0x48)]

    // ...and the function keys count up from VK_F1.
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("F24", 0x87)]
    public void KeyNamesResolveToVirtualKeyCodes(string key, uint expected)
    {
        Assert.True(Binding("Control+Alt", key).TryResolve(out _, out uint vk));
        Assert.Equal(expected, vk);
    }

    [Fact]
    public void ModifiersCombineAndAlwaysCarryNoRepeat()
    {
        // Every action behind a hotkey is a command; holding the keys must fire it once.
        Assert.True(Binding("Control+Alt+Shift", "R").TryResolve(out uint modifiers, out _));

        Assert.Equal(
            HotkeyBinding.ModControl | HotkeyBinding.ModAlt | HotkeyBinding.ModShift
                | HotkeyBinding.ModNoRepeat,
            modifiers);
    }

    [Theory]
    [InlineData("Ctrl+Alt")]
    [InlineData("control + alt")]
    [InlineData("ALT+CONTROL")]
    public void ModifierNamesAreForgivingAboutCaseSpacingAndAbbreviation(string modifiers)
    {
        // The file is hand-edited, so it should accept what a person would plausibly write.
        Assert.True(Binding(modifiers, "1").TryResolve(out uint resolved, out _));
        Assert.Equal(
            HotkeyBinding.ModControl | HotkeyBinding.ModAlt | HotkeyBinding.ModNoRepeat,
            resolved);
    }

    [Theory]
    [InlineData("Control+Alt", "")]
    [InlineData("Control+Alt", "Enter")]
    [InlineData("Control+Alt", "F25")]
    [InlineData("Control+Meta", "1")]
    [InlineData("", "1")]
    public void AnythingUnreadableIsRefusedRatherThanThrown(string modifiers, string key)
    {
        // A typo in a hand-edited settings file must cost that one binding, not the app's start-up.
        Assert.False(Binding(modifiers, key).TryResolve(out _, out _));
    }

    [Fact]
    public void AKeyWithNoModifiersIsRefused()
    {
        // It would swallow that key for every application on the desktop.
        Assert.False(Binding(string.Empty, "R").TryResolve(out _, out _));
    }

    [Fact]
    public void AnUnknownActionIsRefusedEvenWhenTheKeysParse()
    {
        var binding = new HotkeyBinding
        {
            Action = HotkeyAction.None,
            Modifiers = "Control+Alt",
            Key = "1",
        };

        Assert.False(binding.TryResolve(out _, out _));
    }

    [Fact]
    public void TheDefaultsCoverNineTasksShowAllPanicAndToggle()
    {
        List<HotkeyBinding> defaults = HotkeyBinding.Defaults();

        Assert.Equal(9, defaults.Count(b => b.Action == HotkeyAction.SwitchToTask));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9],
            defaults.Where(b => b.Action == HotkeyAction.SwitchToTask).Select(b => b.TaskOrder));
        Assert.Single(defaults, b => b.Action == HotkeyAction.ShowAll);
        Assert.Single(defaults, b => b.Action == HotkeyAction.PanicRestore);
        Assert.Single(defaults, b => b.Action == HotkeyAction.ToggleWindow);
    }

    [Fact]
    public void EveryDefaultResolves()
    {
        Assert.All(
            HotkeyBinding.Defaults(),
            b => Assert.True(b.TryResolve(out _, out _), $"{b.Action} {b.Modifiers}+{b.Key}"));
    }

    [Fact]
    public void TheDefaultsDoNotCollideWithEachOther()
    {
        List<(uint Modifiers, uint Key)> combinations = [];
        foreach (HotkeyBinding binding in HotkeyBinding.Defaults())
        {
            binding.TryResolve(out uint modifiers, out uint vk);
            combinations.Add((modifiers, vk));
        }

        Assert.Equal(combinations.Count, combinations.Distinct().Count());
    }

    [Fact]
    public void BindingsReachTheFileInAFormAPersonCanEdit()
    {
        // Through the real store, because "hand-editable" is a claim about what lands on disk.
        // The default JSON encoder escapes '+' to +, which would make every modifier
        // unreadable — this is what catches that.
        string directory = Path.Combine(
            Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, "state.json");
            var state = new WorkspaceState();
            state.Settings.Hotkeys = HotkeyBinding.Defaults();

            using (var store = new WorkspaceStore(path, TimeSpan.FromMinutes(5)))
            {
                store.SaveDebounced(state);
                store.Flush();
            }

            string json = File.ReadAllText(path);
            Assert.Contains("\"Control+Alt+Shift\"", json, StringComparison.Ordinal);
            Assert.Contains("\"PanicRestore\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u002B", json, StringComparison.OrdinalIgnoreCase);

            using var reopened = new WorkspaceStore(path, TimeSpan.FromMinutes(5));
            WorkspaceState reloaded = reopened.Load();

            Assert.Equal(HotkeyBinding.Defaults().Count, reloaded.Settings.Hotkeys.Count);
            Assert.Contains(
                reloaded.Settings.Hotkeys,
                b => b.Action == HotkeyAction.PanicRestore && b.Key == "R");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
