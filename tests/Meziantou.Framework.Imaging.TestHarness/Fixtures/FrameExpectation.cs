using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>One expected full-canvas displayed frame (or the poster) and its reference buffers.</summary>
public sealed class FrameExpectation
{
    /// <summary>Gets the exact rational duration in seconds (<c>numerator/denominator</c>); absent for posters.</summary>
    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    /// <summary>Gets the raw delay field read from the encoded input, or <see langword="null"/> when the input has none for this frame.</summary>
    [JsonPropertyName("encodedDelay")]
    public EncodedDelayExpectation? EncodedDelay { get; init; }

    /// <summary>Gets a description of how the frame is encoded (delta rectangle, blend/dispose operations, delay field).</summary>
    [JsonPropertyName("encoding")]
    public string? Encoding { get; init; }

    /// <summary>Gets the reference buffers, one per layout (e.g. canonical <c>rgba8</c> and native <c>gray8</c>).</summary>
    [JsonPropertyName("buffers")]
    public required IReadOnlyList<RawBufferDescriptor> Buffers { get; init; }
}
