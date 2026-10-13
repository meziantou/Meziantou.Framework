using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>One HEVC conformance vector: a raw byte stream, its provenance, and the plane digests of its output pictures.</summary>
public sealed class HevcVectorEntry
{
    /// <summary>The pictures are output in the order they are decoded.</summary>
    public const string DecodingOrder = "decoding";

    /// <summary>The pictures are output in another order than the one they are decoded in.</summary>
    public const string ReorderedOrder = "reordered";

    /// <summary>Gets the stable identifier, e.g. <c>hevc/x265-lossless-intra-gbr-8bit-34x36</c>.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets the raw byte stream (H.265 Annex B: NAL units after start code prefixes).</summary>
    [JsonPropertyName("input")]
    public required FixtureFile Input { get; init; }

    /// <summary>Gets where the stream comes from (encoder commands, derivation) and under which license.</summary>
    [JsonPropertyName("provenance")]
    public required FixtureProvenance Provenance { get; init; }

    /// <summary>
    /// Gets what the headers of the stream signal, as parsed by <see cref="HevcVectorSet.HeaderParser"/> (e.g.
    /// <c>hevc.ctb=64</c>, <c>hevc.tiles=2x2</c>, <c>hevc.nal=CRA,RASL_N,TRAIL_R</c>): never what the encoder was asked to do.
    /// </summary>
    [JsonPropertyName("features")]
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>
    /// Gets the name of the planes of the stream: the FFmpeg pixel format that holds them without conversion (e.g.
    /// <c>yuv420p10le</c>, <c>gbrp</c>, <c>gray12le</c>), or a name of the same form for the formats FFmpeg has none for.
    /// </summary>
    [JsonPropertyName("pixelFormat")]
    public required string PixelFormat { get; init; }

    /// <summary>Gets the bit depth of the luma samples: 8, 10 or 12, or 16 for a stream the library rejects.</summary>
    [JsonPropertyName("bitDepth")]
    public required int BitDepth { get; init; }

    /// <summary>Gets the bit depth of the chroma samples when it is not <see cref="BitDepth"/> (a stream the library rejects).</summary>
    [JsonPropertyName("bitDepthChroma")]
    public int? BitDepthChroma { get; init; }

    /// <summary>Gets the chroma format: <c>4:0:0</c> (monochrome, one plane), <c>4:2:0</c>, <c>4:2:2</c> or <c>4:4:4</c>.</summary>
    [JsonPropertyName("chromaFormat")]
    public required string ChromaFormat { get; init; }

    /// <summary>Gets the number of coded pictures of the stream, including the ones that are not output.</summary>
    [JsonPropertyName("pictureCount")]
    public required int PictureCount { get; init; }

    /// <summary>
    /// Gets the number of output pictures. A RASL picture whose IRAP picture starts a coded video sequence is not decoded,
    /// and a picture that was not output yet is discarded when a CRA picture follows an end of sequence.
    /// </summary>
    [JsonPropertyName("frameCount")]
    public required int FrameCount { get; init; }

    /// <summary>Gets whether the pictures are output in decoding order: <see cref="DecodingOrder"/> or <see cref="ReorderedOrder"/>.</summary>
    [JsonPropertyName("outputOrder")]
    public required string OutputOrder { get; init; }

    /// <summary>Gets which decoders produced the digests, and which are left out.</summary>
    [JsonPropertyName("decoders")]
    public required HevcVectorAgreement Decoders { get; init; }

    /// <summary>Gets the type of the picture hashes of the stream, and the decoders that checked them.</summary>
    [JsonPropertyName("pictureHash")]
    public required HevcVectorPictureHash PictureHash { get; init; }

    /// <summary>Gets the output pictures, in output order. Their sizes can differ inside a stream (two coded video sequences).</summary>
    [JsonPropertyName("frames")]
    public required IReadOnlyList<HevcVectorFrame> Frames { get; init; }

    /// <summary>Gets the number of planes of a picture: 1 for a monochrome stream, otherwise 3.</summary>
    [JsonIgnore]
    public int PlaneCount => ChromaFormat == "4:0:0" ? 1 : 3;

    /// <summary>
    /// Gets a value indicating whether the frames have plane digests: two decoders agreed. A stream the library rejects
    /// (<c>hevc.unsupported</c>) can have none.
    /// </summary>
    [JsonIgnore]
    public bool HasDigests => Decoders.Agree.Count >= 2;

    /// <summary>
    /// Gets a value indicating whether the entry declares the specified feature. The value of a feature lists the
    /// distinct values of the stream, separated by commas: <c>key=value</c> matches when the values of <c>key</c> include
    /// <c>value</c>.
    /// </summary>
    /// <param name="feature">The feature, e.g. <c>hevc.wpp</c> or <c>hevc.nal=CRA</c>.</param>
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
    /// <c>hevc.tiles</c> matches <c>hevc.tiles=2x2</c>, and not <c>hevc.tiles.spacing=uniform</c>.
    /// </summary>
    /// <param name="key">The part of a feature before <c>=</c>, e.g. <c>hevc.sliceTypes</c>.</param>
    /// <returns><see langword="true"/> if a feature has this key.</returns>
    public bool HasFeatureKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Features.Any(candidate => candidate.StartsWith(key, StringComparison.Ordinal) && (candidate.Length == key.Length || candidate[key.Length] == '='));
    }

    /// <summary>Gets the values of the feature that has the specified key: empty when there is none, or when it has no value.</summary>
    /// <param name="key">The part of a feature before <c>=</c>, e.g. <c>hevc.nal</c>.</param>
    /// <returns>The values.</returns>
    public IReadOnlyList<string> GetFeatureValues(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var feature = Features.FirstOrDefault(candidate => candidate.StartsWith(key + "=", StringComparison.Ordinal));
        return feature is null ? [] : feature[(key.Length + 1)..].Split(',');
    }

    /// <inheritdoc/>
    public override string ToString() => Id;
}
