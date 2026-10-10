using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The expected decoded image of a valid fixture: canvas, default working representation, metadata, animation settings,
/// one full-canvas displayed frame per animation frame, and the separate poster when applicable.
/// </summary>
public sealed class FixtureExpectation
{
    /// <summary>Gets the canvas width (stored pixels; EXIF orientation is never applied).</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>Gets the canvas height.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>Gets the expected default working representation (a <c>PixelFormat</c> member name, e.g. <c>Rgba32</c>).</summary>
    [JsonPropertyName("pixelFormat")]
    public required string PixelFormat { get; init; }

    /// <summary>Gets the encoded color model (an <c>ImageColorModel</c> member name).</summary>
    [JsonPropertyName("colorModel")]
    public string? ColorModel { get; init; }

    /// <summary>Gets the encoded bits per component.</summary>
    [JsonPropertyName("bitsPerComponent")]
    public int? BitsPerComponent { get; init; }

    /// <summary>Gets the expected EXIF orientation value (1-8); pixels are never rotated by decoders.</summary>
    [JsonPropertyName("orientation")]
    public required int Orientation { get; init; }

    /// <summary>Gets the expected ICC profile: <c>none</c>, <c>rgb</c> or <c>gray</c>.</summary>
    [JsonPropertyName("iccProfile")]
    public required string IccProfile { get; init; }

    /// <summary>
    /// Gets the expected transfer function label of the decoded samples: <c>srgb</c> (the default when absent) or <c>linear</c>
    /// (QOI colorspace 1). It is a label: the expected pixels are the stored samples, never converted.
    /// </summary>
    [JsonPropertyName("transferFunction")]
    public string? TransferFunction { get; init; }

    /// <summary>Gets the transfer function, <c>srgb</c> when <see cref="TransferFunction"/> is absent.</summary>
    [JsonIgnore]
    public string EffectiveTransferFunction => TransferFunction ?? "srgb";

    /// <summary>Gets the expected physical resolution, or <see langword="null"/> when the input has none (or only an aspect ratio).</summary>
    [JsonPropertyName("resolution")]
    public required ResolutionExpectation? Resolution { get; init; }

    /// <summary>Gets the exact identities of the preserved ICC/EXIF/XMP payloads.</summary>
    [JsonPropertyName("profiles")]
    public required ProfilesExpectation Profiles { get; init; }

    /// <summary>Gets the expected text entries, in file order.</summary>
    [JsonPropertyName("text")]
    public required IReadOnlyList<TextEntryExpectation> Text { get; init; }

    /// <summary>Gets the expected animation settings, or <see langword="null"/> when the image is not animated.</summary>
    [JsonPropertyName("animation")]
    public required AnimationExpectation? Animation { get; init; }

    /// <summary>Gets the expected number of displayed frames (excluding a separate poster). Must equal <see cref="Frames"/>.Count.</summary>
    [JsonPropertyName("frameCount")]
    public required int FrameCount { get; init; }

    /// <summary>Gets the expected full-canvas displayed frames.</summary>
    [JsonPropertyName("frames")]
    public required IReadOnlyList<FrameExpectation> Frames { get; init; }

    /// <summary>Gets the expected separate poster frame (APNG default image not part of the animation), or <see langword="null"/>.</summary>
    [JsonPropertyName("poster")]
    public required FrameExpectation? Poster { get; init; }
}
