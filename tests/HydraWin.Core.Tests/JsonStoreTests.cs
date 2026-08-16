using System.Text.Json;
using HydraWin.Core.Persistence;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Persistence behaviour. Every case runs against a temp directory — the store must never know
/// about <c>%APPDATA%</c>, because task 05 reuses it for the recovery journal.
/// </summary>
public sealed class JsonStoreTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "hydrawin-tests-" + Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(directory, "state.json");

    public JsonStoreTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AMissingFileLoadsAsDefaults()
    {
        var store = new JsonStore<WorkspaceState>(StatePath);

        WorkspaceState state = store.Load();

        Assert.Empty(state.Tasks);
        Assert.Null(state.ActiveTaskId);
        Assert.NotNull(state.Settings);
    }

    [Fact]
    public void TasksGuidsAndSettingsSurviveARoundTrip()
    {
        var store = new JsonStore<WorkspaceState>(StatePath);
        var task = new HydraWinTask { Name = "Alpha", Order = 3, ColorHex = "#123456" };
        var assignment = new WindowAssignment
        {
            Rule = new ReattachRule
            {
                ProcessFileName = "Code.exe",
                TitlePattern = "hydrawin",
                TitleIsRegex = true,
            },
        };
        task.Assignments.Add(assignment);

        var original = new WorkspaceState
        {
            Tasks = [task],
            ActiveTaskId = task.Id,
            Settings = new SettingsModel { RestoreOnExit = false },
        };

        store.Save(original);
        WorkspaceState reloaded = store.Load();

        HydraWinTask loadedTask = Assert.Single(reloaded.Tasks);
        Assert.Equal(task.Id, loadedTask.Id);
        Assert.Equal("Alpha", loadedTask.Name);
        Assert.Equal(3, loadedTask.Order);
        Assert.Equal("#123456", loadedTask.ColorHex);
        Assert.Equal(task.Id, reloaded.ActiveTaskId);
        Assert.False(reloaded.Settings.RestoreOnExit);

        WindowAssignment loadedAssignment = Assert.Single(loadedTask.Assignments);
        Assert.Equal(assignment.Id, loadedAssignment.Id);
        Assert.Equal("Code.exe", loadedAssignment.Rule.ProcessFileName);
        Assert.Equal("hydrawin", loadedAssignment.Rule.TitlePattern);
        Assert.True(loadedAssignment.Rule.TitleIsRegex);
    }

    [Fact]
    public void BoundHandlesAreNotPersisted()
    {
        // Handles are meaningless across restarts; persisting them would invite binding to a
        // recycled handle belonging to some other window.
        var store = new JsonStore<WorkspaceState>(StatePath);
        var task = new HydraWinTask { Name = "Alpha", Order = 1 };
        task.Assignments.Add(new WindowAssignment { BoundHwnd = 0x1234 });
        store.Save(new WorkspaceState { Tasks = [task] });

        Assert.DoesNotContain("BoundHwnd", File.ReadAllText(StatePath), StringComparison.Ordinal);
        Assert.Null(Assert.Single(Assert.Single(store.Load().Tasks).Assignments).BoundHwnd);
    }

    [Fact]
    public void OnlyTheRealSchemaIsWrittenNotDerivedProperties()
    {
        // state.json is hand-edited (tasks 08 and 09 both require it) and task 11 documents this
        // schema. A derived, get-only property such as OrderedTasks would duplicate the entire
        // task list in the file and then silently ignore anything edited in the copy.
        var store = new JsonStore<WorkspaceState>(StatePath);
        var task = new HydraWinTask { Name = "Alpha", Order = 1 };
        task.Assignments.Add(new WindowAssignment());
        store.Save(new WorkspaceState { Tasks = [task] });

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(StatePath));

        Assert.Equal(
            ["Tasks", "ActiveTaskId", "Settings"],
            document.RootElement.EnumerateObject().Select(p => p.Name));

        JsonElement firstTask = document.RootElement.GetProperty("Tasks")[0];
        Assert.Equal(
            ["Id", "Name", "ColorHex", "Order", "Assignments"],
            firstTask.EnumerateObject().Select(p => p.Name));

        Assert.Equal(
            ["Id", "Rule"],
            firstTask.GetProperty("Assignments")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void TheDocumentIsIndentedSoItCanBeHandEdited()
    {
        var store = new JsonStore<WorkspaceState>(StatePath);
        store.Save(new WorkspaceState { Tasks = [new HydraWinTask { Name = "Alpha", Order = 1 }] });

        Assert.Contains("\n", File.ReadAllText(StatePath), StringComparison.Ordinal);
    }

    [Fact]
    public void SavingLeavesNoTemporaryFileBehind()
    {
        var store = new JsonStore<WorkspaceState>(StatePath);

        store.Save(new WorkspaceState());
        store.Save(new WorkspaceState());

        Assert.True(File.Exists(StatePath));
        Assert.False(File.Exists(StatePath + ".tmp"));
    }

    [Fact]
    public void SavingOverAnExistingDocumentReplacesIt()
    {
        var store = new JsonStore<WorkspaceState>(StatePath);
        store.Save(new WorkspaceState { Tasks = [new HydraWinTask { Name = "First", Order = 1 }] });
        store.Save(new WorkspaceState { Tasks = [new HydraWinTask { Name = "Second", Order = 1 }] });

        Assert.Equal("Second", Assert.Single(store.Load().Tasks).Name);
    }

    [Fact]
    public void TheDirectoryIsCreatedOnFirstSave()
    {
        string nested = Path.Combine(directory, "does", "not", "exist", "state.json");
        var store = new JsonStore<WorkspaceState>(nested);

        store.Save(new WorkspaceState());

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void ACorruptDocumentIsSetAsideAndDefaultsAreUsed()
    {
        File.WriteAllText(StatePath, "{ \"Tasks\": [ { \"Name\": \"half a fi");
        var store = new JsonStore<WorkspaceState>(StatePath);
        string? quarantined = null;
        store.CorruptFileQuarantined += (_, path) => quarantined = path;

        WorkspaceState state = store.Load();

        Assert.Empty(state.Tasks);
        Assert.NotNull(quarantined);
        Assert.False(File.Exists(StatePath));

        // The evidence must survive — the user may want to salvage it by hand.
        string[] evidence = Directory.GetFiles(directory, "state.json.corrupt-*");
        Assert.Single(evidence);
        Assert.StartsWith("{ \"Tasks\"", File.ReadAllText(evidence[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void AStoreCanRoundTripAListWhichIsWhatTheRecoveryJournalNeeds()
    {
        // Task 05 uses JsonStore<List<JournalEntry>>; the constraint must stay loose enough.
        string path = Path.Combine(directory, "journal.json");
        var store = new JsonStore<List<string>>(path);

        store.Save(["first", "second"]);

        Assert.Equal(["first", "second"], store.Load());
    }

    [Fact]
    public void TemporaryAndCorruptNamesFollowTheConfiguredFileName()
    {
        // journal.json.tmp, not state.json.tmp - task 05 shares this class.
        string path = Path.Combine(directory, "journal.json");
        File.WriteAllText(path, "not json at all");
        var store = new JsonStore<List<string>>(path);

        store.Load();

        Assert.Single(Directory.GetFiles(directory, "journal.json.corrupt-*"));
    }

    [Fact]
    public void EnumsSerializeAsStringsForHandEditing()
    {
        // Guards the option that keeps state.json readable once task 09 adds NotificationKind.
        string path = Path.Combine(directory, "enum.json");
        var store = new JsonStore<EnumDocument>(path);

        store.Save(new EnumDocument { Day = DayOfWeek.Friday });

        Assert.Contains("\"Friday\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(DayOfWeek.Friday, store.Load().Day);
    }

    private sealed class EnumDocument
    {
        public DayOfWeek Day { get; set; }
    }
}
