using HydraWin.Core.Interop;
using HydraWin.Core.Recovery;

namespace HydraWin.Core.Tests;

/// <summary>Mapping between the native placement struct and its serializable mirror.</summary>
public class WindowPlacementDtoTests
{
    private static WindowPlacement NativePlacement() => new()
    {
        Length = 44,
        Flags = 2,
        ShowCmd = 3,
        MinPosition = new Point { X = -1, Y = -2 },
        MaxPosition = new Point { X = -7, Y = -7 },
        NormalPosition = new Rect { Left = 100, Top = 200, Right = 900, Bottom = 700 },
    };

    [Fact]
    public void EveryFieldSurvivesTheRoundTrip()
    {
        WindowPlacement original = NativePlacement();

        WindowPlacement result = WindowPlacementDto.FromPlacement(in original).ToPlacement();

        Assert.Equal(original.ShowCmd, result.ShowCmd);
        Assert.Equal(original.Flags, result.Flags);
        Assert.Equal(original.MinPosition, result.MinPosition);
        Assert.Equal(original.MaxPosition, result.MaxPosition);
        Assert.Equal(original.NormalPosition, result.NormalPosition);
    }

    [Fact]
    public void NegativeCoordinatesSurvive()
    {
        // The user's second monitor sits at negative X (task 01 measured -2048), so this is the
        // normal case here, not an edge one.
        WindowPlacement original = NativePlacement();
        original.NormalPosition = new Rect { Left = -1600, Top = 500, Right = -913, Bottom = 1000 };

        WindowPlacement result = WindowPlacementDto.FromPlacement(in original).ToPlacement();

        Assert.Equal(-1600, result.NormalPosition.Left);
        Assert.Equal(-913, result.NormalPosition.Right);
    }

    [Fact]
    public void TheMaximizedStateIsCarriedByShowCmd()
    {
        WindowPlacement maximized = NativePlacement();
        maximized.ShowCmd = 3; // SW_SHOWMAXIMIZED

        Assert.Equal(3, WindowPlacementDto.FromPlacement(in maximized).ShowCmd);
    }

    [Fact]
    public void TheLengthFieldIsNotCarriedAcross()
    {
        // It is a marshalling detail: the interop wrapper recomputes it from the real struct size
        // at call time, and a value read from a file could be wrong for this build.
        WindowPlacement original = NativePlacement();

        WindowPlacement result = WindowPlacementDto.FromPlacement(in original).ToPlacement();

        Assert.Equal(0, result.Length);
    }
}
