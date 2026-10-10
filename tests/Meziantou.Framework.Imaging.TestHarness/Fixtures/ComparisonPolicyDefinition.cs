using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The manifest form of a pixel comparison policy (see <see cref="Pixels.ComparisonPolicy"/>).</summary>
public sealed class ComparisonPolicyDefinition
{
    /// <summary>Gets <c>exact</c> or <c>tolerance</c>.</summary>
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    /// <summary>Gets, for tolerance policies, the maximum absolute error of any color sample, on the 8-bit scale.</summary>
    [JsonPropertyName("maxAbsoluteError")]
    public int? MaxAbsoluteError { get; init; }

    /// <summary>Gets, for tolerance policies, the maximum mean absolute error over color samples, on the 8-bit scale.</summary>
    [JsonPropertyName("maxMeanAbsoluteError")]
    public double? MaxMeanAbsoluteError { get; init; }

    /// <summary>Gets, for tolerance policies, the mandatory justification (measured, attributable differences).</summary>
    [JsonPropertyName("justification")]
    public string? Justification { get; init; }
}
