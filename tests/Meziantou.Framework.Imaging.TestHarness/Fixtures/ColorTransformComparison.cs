using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The comparison policy of a <see cref="ColorTransformEntry"/>: two color management systems never agree bit for bit.</summary>
public sealed class ColorTransformComparison
{
    /// <summary>The largest tolerance a manifest may declare, in 16-bit units (a quarter of a level of 255).</summary>
    public const int MaximumTolerance = 64;

    /// <summary>Gets the largest accepted difference of a sample, in 16-bit units (257 is one level of 255).</summary>
    [JsonPropertyName("maxAbsoluteError")]
    public required int MaxAbsoluteError { get; init; }

    /// <summary>Gets the largest accepted mean difference over all samples, in 16-bit units.</summary>
    [JsonPropertyName("maxMeanAbsoluteError")]
    public required double MaxMeanAbsoluteError { get; init; }

    /// <summary>Gets the measured reason for the tolerance.</summary>
    [JsonPropertyName("justification")]
    public required string Justification { get; init; }
}
