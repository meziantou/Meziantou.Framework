using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>One AV1 conformance vector: a raw OBU stream, its provenance, and the plane digests of its output frames.</summary>
public sealed class Av1VectorEntry
{
    /// <summary>Gets the stable identifier, e.g. <c>av1/lossless-gbr-8bit-34x36</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets the raw OBU stream (the "Low overhead bitstream format" of the AV1 specification, section 5).</summary>
    [JsonPropertyName("input")]
    public required FixtureFile Input { get; init; }

    /// <summary>Gets where the stream comes from (encoder command) and under which license.</summary>
    [JsonPropertyName("provenance")]
    public required FixtureProvenance Provenance { get; init; }

    /// <summary>
    /// Gets what the headers of the stream signal, as parsed by <see cref="Av1VectorSet.HeaderParser"/> (e.g.
    /// <c>av1.lr.y=wiener</c>, <c>av1.cdef_bits=1</c>, <c>av1.tiles=2x2</c>): never what the encoder was asked to do.
    /// </summary>
    [JsonPropertyName("features")]
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>Gets the FFmpeg pixel format the reference decoders produced without conversion (e.g. <c>yuv420p10le</c>, <c>gbrp</c>).</summary>
    [JsonPropertyName("pixelFormat")]
    public required string PixelFormat { get; init; }

    /// <summary>Gets the bit depth of the samples: 8, 10 or 12.</summary>
    [JsonPropertyName("bitDepth")]
    public required int BitDepth { get; init; }

    /// <summary>Gets the chroma subsampling: <c>4:0:0</c> (monochrome, one plane), <c>4:2:0</c>, <c>4:2:2</c> or <c>4:4:4</c>.</summary>
    [JsonPropertyName("subsampling")]
    public required string Subsampling { get; init; }

    /// <summary>
    /// Gets the number of output frames: one per shown frame and per <c>show_existing_frame</c> of the stream. A hidden
    /// frame produces no output. When the operating point has several spatial layers (<c>av1.spatial_layers</c>), a
    /// temporal unit outputs one frame: the shown frame of the highest spatial layer it holds.
    /// </summary>
    [JsonPropertyName("frameCount")]
    public required int FrameCount { get; init; }

    /// <summary>Gets the output frames, in output order. Their sizes can differ inside a stream (reference scaling).</summary>
    [JsonPropertyName("frames")]
    public required IReadOnlyList<Av1VectorFrame> Frames { get; init; }

    /// <summary>Gets the number of planes of a frame: 1 for a monochrome stream, otherwise 3.</summary>
    [JsonIgnore]
    public int PlaneCount => Subsampling == "4:0:0" ? 1 : 3;

    /// <summary>
    /// Gets a value indicating whether the entry declares the specified feature. The value of a feature of a multi-frame
    /// vector lists the distinct values of its frames, separated by commas: <c>key=value</c> matches when the values of
    /// <c>key</c> include <c>value</c>.
    /// </summary>
    /// <param name="feature">The feature, e.g. <c>av1.film_grain</c> or <c>av1.cdef_bits=1</c>.</param>
    /// <returns><see langword="true"/> if the feature is declared.</returns>
    public bool HasFeature(string feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var separator = feature.IndexOf('=', StringComparison.Ordinal);
        if (separator < 0)
            return Features.Contains(feature, StringComparer.Ordinal);

        var prefix = feature[..(separator + 1)];
        var value = feature[(separator + 1)..];
        return Features.Any(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal) && candidate[prefix.Length..].Split(',').Contains(value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets a value indicating whether the entry declares a feature with the specified key, whatever its values:
    /// <c>av1.frame_sizes</c> matches <c>av1.frame_sizes=102x77,85x64</c>, and <c>av1.film_grain</c> matches the tag
    /// <c>av1.film_grain</c> but not <c>av1.film_grain.overlap</c>.
    /// </summary>
    /// <param name="key">The part of a feature before <c>=</c>, e.g. <c>av1.frame_types</c>.</param>
    /// <returns><see langword="true"/> if a feature has this key.</returns>
    public bool HasFeatureKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Features.Any(candidate => candidate.StartsWith(key, StringComparison.Ordinal) && (candidate.Length == key.Length || candidate[key.Length] == '='));
    }

    /// <inheritdoc/>
    public override string ToString() => Id;
}
