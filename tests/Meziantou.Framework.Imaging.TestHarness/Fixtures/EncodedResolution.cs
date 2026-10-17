using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Encoded resolution fields (integer densities and their unit).</summary>
public sealed class EncodedResolution
{
    /// <summary>Gets the unit: <see cref="ResolutionExpectation.Meter"/>, <see cref="ResolutionExpectation.Inch"/> or <see cref="ResolutionExpectation.Centimeter"/>.</summary>
    [JsonPropertyName("unit")]
    public required string Unit { get; init; }

    /// <summary>Gets the horizontal density.</summary>
    [JsonPropertyName("x")]
    public required int X { get; init; }

    /// <summary>Gets the vertical density.</summary>
    [JsonPropertyName("y")]
    public required int Y { get; init; }
}
