namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>One sample that does not satisfy the comparison policy.</summary>
/// <param name="X">The column.</param>
/// <param name="Y">The row.</param>
/// <param name="Channel">The channel index.</param>
/// <param name="ChannelName">The channel name (R, G, B, A, Y).</param>
/// <param name="Expected">The expected sample.</param>
/// <param name="Actual">The actual sample.</param>
public sealed record SampleMismatch(int X, int Y, int Channel, char ChannelName, int Expected, int Actual)
{
    /// <summary>Gets the absolute error.</summary>
    public int Error => Math.Abs(Expected - Actual);

    /// <summary>Formats the mismatch with the full numeric detail of the layout.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The description.</returns>
    public string Format(RawPixelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return string.Create(CultureInfo.InvariantCulture, $"({X},{Y}) {ChannelName}: expected {layout.FormatSample(Expected)}, actual {layout.FormatSample(Actual)}, error {Error}");
    }
}
