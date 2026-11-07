namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>One sample of a resize comparison.</summary>
/// <param name="X">The column.</param>
/// <param name="Y">The row.</param>
/// <param name="Channel">The channel index.</param>
/// <param name="ChannelName">The channel name.</param>
/// <param name="Exact">The exact (unrounded) reference value.</param>
/// <param name="AcceptedLow">The smallest accepted sample.</param>
/// <param name="AcceptedHigh">The largest accepted sample (differs from <paramref name="AcceptedLow"/> only near a rounding tie).</param>
/// <param name="Actual">The actual sample.</param>
public sealed record ResampleMismatch(int X, int Y, int Channel, char ChannelName, decimal Exact, int AcceptedLow, int AcceptedHigh, int Actual)
{
    /// <summary>Gets the distance between the actual sample and the accepted range (0 when accepted).</summary>
    public int Violation => Actual < AcceptedLow ? AcceptedLow - Actual : Actual > AcceptedHigh ? Actual - AcceptedHigh : 0;

    /// <summary>Gets the distance between the actual sample and the exact value.</summary>
    public decimal Deviation => Math.Abs(Actual - Exact);

    /// <inheritdoc/>
    public override string ToString()
    {
        var accepted = AcceptedLow == AcceptedHigh ? AcceptedLow.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{AcceptedLow} or {AcceptedHigh} (near tie)");
        return string.Create(CultureInfo.InvariantCulture, $"({X},{Y}) {ChannelName}: exact {Exact:0.########}, expected {accepted}, actual {Actual} (off by {Violation})");
    }
}
