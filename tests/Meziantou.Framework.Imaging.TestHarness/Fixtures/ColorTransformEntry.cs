using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// Colors converted between two profiles of the corpus by an independent color management system
/// (<c>colorTransforms</c>): the reference of the color conversion conformance tests.
/// </summary>
public sealed class ColorTransformEntry
{
    /// <summary>The intent names accepted in <see cref="Intent"/>, in the order of the ICC intent numbers 0 to 3.</summary>
    public static readonly IReadOnlyList<string> Intents = ["perceptual", "relative-colorimetric", "saturation", "absolute-colorimetric"];

    /// <summary>Gets the identifier.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets the identifier of the source profile.</summary>
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>Gets the identifier of the destination profile.</summary>
    [JsonPropertyName("destination")]
    public required string Destination { get; init; }

    /// <summary>Gets the rendering intent (one of <see cref="Intents"/>).</summary>
    [JsonPropertyName("intent")]
    public required string Intent { get; init; }

    /// <summary>Gets a value indicating whether black point compensation is applied.</summary>
    [JsonPropertyName("blackPointCompensation")]
    public required bool BlackPointCompensation { get; init; }

    /// <summary>Gets the number of source device channels.</summary>
    [JsonPropertyName("sourceChannels")]
    public required int SourceChannels { get; init; }

    /// <summary>Gets the number of destination device channels.</summary>
    [JsonPropertyName("destinationChannels")]
    public required int DestinationChannels { get; init; }

    /// <summary>Gets the number of colors.</summary>
    [JsonPropertyName("sampleCount")]
    public required int SampleCount { get; init; }

    /// <summary>
    /// Gets the vectors: for each color, the source samples then the reference destination samples, as little-endian
    /// 16-bit numbers covering the device range.
    /// </summary>
    [JsonPropertyName("vectors")]
    public required FixtureFile Vectors { get; init; }

    /// <summary>Gets the tool that produced the reference samples.</summary>
    [JsonPropertyName("reference")]
    public required ColorTransformReference Reference { get; init; }

    /// <summary>Gets the comparison policy.</summary>
    [JsonPropertyName("comparison")]
    public required ColorTransformComparison Comparison { get; init; }

    /// <summary>Gets free-form notes.</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
