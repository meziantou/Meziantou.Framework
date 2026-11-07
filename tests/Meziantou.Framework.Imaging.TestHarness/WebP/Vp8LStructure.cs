namespace Meziantou.Framework.Imaging.TestHarness.WebP;

/// <summary>How a VP8L image stream is coded, as read by <see cref="ReferenceVp8L.Describe"/>.</summary>
public sealed class Vp8LStructure
{
    /// <summary>Gets the transform types in order of appearance: 0 predictor, 1 color, 2 subtract green, 3 color indexing.</summary>
    public IReadOnlyList<int> Transforms => TransformList;

    internal List<int> TransformList { get; } = [];

    /// <summary>Gets the tile size bits of the predictor transform (0 without it).</summary>
    public int PredictorBits { get; internal set; }

    /// <summary>Gets the color cache size bits of the main image (0 without a cache).</summary>
    public int ColorCacheBits { get; internal set; }

    /// <summary>Gets the tile size bits of the entropy image (0 without meta prefix codes).</summary>
    public int PrefixBits { get; internal set; }

    /// <summary>Gets the number of prefix code groups of the main image.</summary>
    public int GroupCount { get; internal set; }
}
