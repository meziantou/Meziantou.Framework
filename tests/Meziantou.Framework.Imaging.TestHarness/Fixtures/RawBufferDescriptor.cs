using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// A raw reference pixel buffer file and its exact layout. Raw files carry no metadata: this descriptor is authoritative.
/// </summary>
public sealed class RawBufferDescriptor
{
    /// <summary>Gets the path relative to <c>tests/Meziantou.Framework.Imaging.Fixtures</c>, using '/' separators.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>Gets the lowercase hexadecimal SHA-256 of the stored file.</summary>
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    /// <summary>Gets the layout name (<c>rgba8</c>, <c>rgba16le</c>, <c>rgb8</c>, <c>gray8</c>, <c>gray16le</c>, <c>graya8</c>, <c>graya16le</c>).</summary>
    [JsonPropertyName("layout")]
    public required string Layout { get; init; }

    /// <summary>Gets the width in pixels.</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>Gets the height in pixels.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>Gets the row byte count (always tightly packed: width x bytes per pixel).</summary>
    [JsonPropertyName("rowBytes")]
    public required int RowBytes { get; init; }

    /// <summary>Gets the exact decoded buffer length (row bytes x height).</summary>
    [JsonPropertyName("byteLength")]
    public required int ByteLength { get; init; }

    /// <summary>Gets <c>straight</c> or <c>premultiplied</c> for layouts with alpha, <c>none</c> otherwise.</summary>
    [JsonPropertyName("alpha")]
    public required string Alpha { get; init; }

    /// <summary>Gets the row order; only <c>top-down</c> is defined.</summary>
    [JsonPropertyName("rowOrder")]
    public required string RowOrder { get; init; }

    /// <summary>Gets the storage: <c>none</c> (raw bytes) or <c>gzip</c> (decompressed with a bounded, non-image decompressor).</summary>
    [JsonPropertyName("compression")]
    public required string Compression { get; init; }
}
