using System.Text.Json;
using HydraWin.Core.Persistence;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// The theme preference: how it resolves, and how it survives <c>state.json</c>.
/// </summary>
/// <remarks>
/// This is all of theming that can be tested automatically. The brushes, the control templates, the
/// live OS-change listener and the dark title bar are verified by the manual drills in
/// <c>docs/ui/how_to.md</c> — Core holds no WPF reference and never will.
/// </remarks>
public sealed class AppearanceTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(directory, "state.json");

    public AppearanceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FollowingWindowsIsTheDefault() =>
        Assert.Equal(Appearance.System, new SettingsModel().Appearance);

    [Fact]
    public void FollowingWindowsIsAlsoWhatAMissingValueMeans() =>
        // Zero is the default an absent JSON property deserializes to, so the enum's first member
        // has to be the setting's default as well.
        Assert.Equal(Appearance.System, default);

    [Theory]
    [InlineData(Appearance.Light, true, EffectiveTheme.Light)]
    [InlineData(Appearance.Light, false, EffectiveTheme.Light)]
    [InlineData(Appearance.Dark, true, EffectiveTheme.Dark)]
    [InlineData(Appearance.Dark, false, EffectiveTheme.Dark)]
    [InlineData(Appearance.System, true, EffectiveTheme.Dark)]
    [InlineData(Appearance.System, false, EffectiveTheme.Light)]
    public void AnOverrideWinsAndSystemFollowsTheOs(
        Appearance requested,
        bool systemIsDark,
        EffectiveTheme expected) =>
        Assert.Equal(expected, AppearanceResolver.Resolve(requested, systemIsDark, highContrast: false));

    [Theory]
    [InlineData(Appearance.System)]
    [InlineData(Appearance.Light)]
    [InlineData(Appearance.Dark)]
    public void HighContrastBeatsEveryPreference(Appearance requested)
    {
        // The deliberate judgement call: a contrast scheme was asked of the OS for a reason, and no
        // preference inside HydraWin is a good enough argument to paint over it.
        Assert.Equal(
            EffectiveTheme.HighContrast,
            AppearanceResolver.Resolve(requested, systemIsDark: false, highContrast: true));

        Assert.Equal(
            EffectiveTheme.HighContrast,
            AppearanceResolver.Resolve(requested, systemIsDark: true, highContrast: true));
    }

    [Fact]
    public void AnUnmappedValueFollowsTheOsRatherThanThrowing()
    {
        var nonsense = (Appearance)42;

        Assert.Equal(
            EffectiveTheme.Dark,
            AppearanceResolver.Resolve(nonsense, systemIsDark: true, highContrast: false));

        Assert.Equal(
            EffectiveTheme.Light,
            AppearanceResolver.Resolve(nonsense, systemIsDark: false, highContrast: false));
    }

    [Fact]
    public void ThePreferenceSurvivesARoundTrip()
    {
        var store = new JsonStore<WorkspaceState>(StatePath);

        store.Save(new WorkspaceState
        {
            Settings = new SettingsModel { Appearance = Appearance.Dark },
        });

        Assert.Equal(Appearance.Dark, store.Load().Settings.Appearance);
    }

    [Fact]
    public void ThePreferenceIsWrittenAsANameNotANumber()
    {
        // state.json is hand-editable; a 2 on that line would tell a reader nothing.
        var store = new JsonStore<WorkspaceState>(StatePath);
        store.Save(new WorkspaceState
        {
            Settings = new SettingsModel { Appearance = Appearance.Dark },
        });

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(StatePath));

        Assert.Equal(
            "Dark",
            document.RootElement.GetProperty("Settings").GetProperty("Appearance").GetString());
    }

    [Fact]
    public void AHandEditedNameIsRead()
    {
        File.WriteAllText(StatePath, """{"Settings":{"Appearance":"Light"}}""");

        Assert.Equal(
            Appearance.Light,
            new JsonStore<WorkspaceState>(StatePath).Load().Settings.Appearance);
    }

    [Fact]
    public void AnUnrecognisedNameQuarantinesTheFile()
    {
        // Not new behaviour and not specific to this setting — it is what every persisted enum
        // already does, Hotkeys[].Action included. Pinned because state.json is advertised as
        // hand-editable, and the honest answer is that a typo in an enum costs the whole file
        // rather than that one value. Deleting the property is the safe edit.
        File.WriteAllText(StatePath, """{"Settings":{"Appearance":"Darkk"}}""");

        var store = new JsonStore<WorkspaceState>(StatePath);
        string? quarantined = null;
        store.CorruptFileQuarantined += (_, path) => quarantined = path;

        WorkspaceState loaded = store.Load();

        Assert.Equal(Appearance.System, loaded.Settings.Appearance);
        Assert.NotNull(quarantined);
        Assert.True(File.Exists(quarantined));
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public void AMissingPropertyLoadsAsFollowingWindows()
    {
        File.WriteAllText(StatePath, """{"Settings":{"RestoreOnExit":false}}""");

        WorkspaceState loaded = new JsonStore<WorkspaceState>(StatePath).Load();

        Assert.Equal(Appearance.System, loaded.Settings.Appearance);
        Assert.False(loaded.Settings.RestoreOnExit);
    }
}
