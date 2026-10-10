namespace Meziantou.Framework.Imaging.TestHarness.AutoCrop;

/// <summary>The parameters of a reference auto-crop (independent of the library types).</summary>
/// <param name="PaddingX">The padding on the left and on the right of the content, in pixels.</param>
/// <param name="PaddingY">The padding above and below the content, in pixels.</param>
/// <param name="ColorThreshold">The color threshold, from 1 to 255 on the 8-bit scale.</param>
/// <param name="BucketThreshold">The minimum share of border pixels in the luma bucket of the background, or <see langword="null"/>.</param>
/// <param name="Contain">Whether the padded rectangle is clamped to the canvas instead of enlarging it.</param>
/// <param name="AnalyzeWeights">Whether the weights are computed and shift the padded rectangle.</param>
public sealed record ReferenceAutoCropOptions(
    int PaddingX = 0,
    int PaddingY = 0,
    int ColorThreshold = 35,
    decimal? BucketThreshold = null,
    bool Contain = false,
    bool AnalyzeWeights = false)
{
    /// <inheritdoc/>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"padding {PaddingX}x{PaddingY}{(Contain ? " contained" : "")}, threshold {ColorThreshold}{(BucketThreshold is { } bucket ? $", bucket {bucket}" : "")}{(AnalyzeWeights ? ", weights" : "")}");
}
