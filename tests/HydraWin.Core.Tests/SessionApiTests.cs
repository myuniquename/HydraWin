using HydraWin.Core.Interop;
using HydraWin.Core.Workspaces;

namespace HydraWin.Core.Tests;

/// <summary>
/// Turning a window message into "the user left" or "the user is back". The registration half of
/// <see cref="ISessionApi"/> is real P/Invoke and has no coverage here, but the classification is
/// pure arithmetic on a <c>wParam</c> — which is exactly why it lives behind the seam rather than
/// in the listener, where nothing could reach it.
/// </summary>
public sealed class SessionApiTests
{
    private readonly ISessionApi session = Win32SessionApi.Instance;

    [Fact]
    public void LockingTheSessionMeansTheUserLeft() =>
        Assert.Equal(
            new SessionTransition(true, AwayReason.Locked),
            session.Classify(session.SessionChangeMessage, 0x7));

    [Fact]
    public void UnlockingTheSessionMeansTheUserIsBack() =>
        Assert.Equal(
            new SessionTransition(false, AwayReason.Locked),
            session.Classify(session.SessionChangeMessage, 0x8));

    [Fact]
    public void SuspendingTheMachineMeansTheUserLeft() =>
        Assert.Equal(
            new SessionTransition(true, AwayReason.Suspended),
            session.Classify(session.PowerBroadcastMessage, 0x4));

    [Theory]
    [InlineData(0x7)]
    [InlineData(0x12)]
    public void EitherKindOfResumeMeansTheUserIsBack(int code) =>
        Assert.Equal(
            new SessionTransition(false, AwayReason.Suspended),
            session.Classify(session.PowerBroadcastMessage, code));

    [Fact]
    public void TheTwoMessagesShareCodesAndAreNotConfusedForEachOther()
    {
        // 0x7 is WTS_SESSION_LOGON on one message and PBT_APMRESUMESUSPEND on the other. Reading
        // the wParam without the message id would have the machine waking up whenever anybody
        // logged on.
        Assert.NotEqual(
            session.Classify(session.SessionChangeMessage, 0x7),
            session.Classify(session.PowerBroadcastMessage, 0x7));
    }

    [Theory]
    [InlineData(0x1)]
    [InlineData(0x5)]
    [InlineData(0xF)]
    public void ASessionEventThatIsNotALockOrAnUnlockMeansNothing(int code) =>
        Assert.Null(session.Classify(session.SessionChangeMessage, code));

    [Fact]
    public void APowerEventThatIsNotASuspendOrAResumeMeansNothing() =>
        Assert.Null(session.Classify(session.PowerBroadcastMessage, 0xA));

    [Fact]
    public void AMessageOfNoInterestMeansNothingWhateverItsWParam() =>
        Assert.Null(session.Classify(0x0010, 0x7));
}
