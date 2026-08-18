namespace HydraWin.Core.Tests;

/// <summary>
/// A clock the test drives by hand.
/// </summary>
/// <remarks>
/// Hand-rolled rather than <c>FakeTimeProvider</c>, which only ships in
/// <c>Microsoft.Extensions.TimeProvider.Testing</c>: this project deliberately carries no
/// mocking or fake-supplying package, the same rule that keeps <see cref="FakeWindowApi"/>
/// hand-written. Only the timestamp half of <see cref="TimeProvider"/> is overridden, because
/// that is the only half the ledger uses.
/// </remarks>
internal sealed class FakeClock : TimeProvider
{
    private long ticks;

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => ticks;

    /// <summary>Moves the clock forward.</summary>
    internal void Advance(TimeSpan by) => ticks += by.Ticks;

    /// <summary>
    /// Moves the clock backwards. A monotonic clock should never do this; the ledger has to
    /// survive one that does.
    /// </summary>
    internal void Rewind(TimeSpan by) => ticks -= by.Ticks;
}
