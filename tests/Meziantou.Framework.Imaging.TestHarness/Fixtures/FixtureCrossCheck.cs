using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The comparison of the reference with another independent decoder, performed by the generator.</summary>
public sealed class FixtureCrossCheck
{
    /// <summary>Identical samples.</summary>
    public const string Exact = "exact";

    /// <summary>Identical except the color of pixels that are fully transparent in both buffers (documented in notes).</summary>
    public const string Equivalent = "equivalent";

    /// <summary>Lossy differences within the fixture tolerance.</summary>
    public const string WithinTolerance = "within-tolerance";

    /// <summary>A recorded disagreement (documented in notes); never hidden.</summary>
    public const string Differs = "differs";

    /// <summary>Gets the decoder name and exact version.</summary>
    [JsonPropertyName("decoder")]
    public required string Decoder { get; init; }

    /// <summary>Gets the exact command line.</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>Gets <see cref="Exact"/>, <see cref="Equivalent"/>, <see cref="WithinTolerance"/> or <see cref="Differs"/>.</summary>
    [JsonPropertyName("result")]
    public required string Result { get; init; }

    /// <summary>Gets the measured maximum absolute sample error.</summary>
    [JsonPropertyName("maxAbsoluteError")]
    public int? MaxAbsoluteError { get; init; }

    /// <summary>Gets the measured mean absolute error over color samples.</summary>
    [JsonPropertyName("meanAbsoluteError")]
    public double? MeanAbsoluteError { get; init; }

    /// <summary>Gets a value indicating whether the measurement is a known tool artifact that does not justify the tolerance.</summary>
    [JsonPropertyName("excludedFromTolerance")]
    public bool? ExcludedFromTolerance { get; init; }

    /// <summary>Gets reviewer notes (required for <see cref="Equivalent"/> and <see cref="Differs"/>).</summary>
    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
