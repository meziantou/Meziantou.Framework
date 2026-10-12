namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A tuple variation of a glyph: a region, and a delta for some or all of the points of the glyph.</summary>
internal sealed class FontTupleVariation
{
    /// <summary>Gets the peak of the region: a normalized value per axis.</summary>
    public required double[] Peak { get; init; }

    /// <summary>Gets where the region starts, when it is not at the default.</summary>
    public double[]? Start { get; init; }

    public double[]? End { get; init; }

    /// <summary>
    /// Gets the points the deltas are for; <see langword="null"/> for every point of the glyph, with its four phantom
    /// points.
    /// </summary>
    public int[]? Points { get; init; }

    /// <summary>Gets a value indicating whether the points are stored once for the glyph, for the tuple variations that say so.</summary>
    public bool UsesSharedPoints { get; init; }

    /// <summary>Gets a value indicating whether the numbers of the points are written on 16 bits even when 8 would do.</summary>
    public bool WritePointsAsWords { get; init; }

    public required (int X, int Y)[] Deltas { get; init; }
}
