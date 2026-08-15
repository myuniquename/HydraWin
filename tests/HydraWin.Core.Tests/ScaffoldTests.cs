using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Exercises the test pipeline end to end so task 02 can prove <c>dotnet test</c> works. Later
/// tasks replace this with real suites: matching rules, journal, model and badge aggregation.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void CoreTypesAreReachableFromTheTestProject()
    {
        var state = new WorkspaceState();

        Assert.NotNull(state);
    }
}
