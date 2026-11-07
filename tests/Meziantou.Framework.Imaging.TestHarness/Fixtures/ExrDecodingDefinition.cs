using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// A decoding of an OpenEXR fixture with options of <c>OpenExrDecodingOptions</c> (a layer, a window mode): the file is an
/// <see cref="FixtureKinds.Unsupported"/> fixture, rejected with the default options (extra channels, layers, pixels
/// outside the display window), and these decodings are its pixel references. A decoding gives either the pixels or the
/// error the options still give (a layer the file does not have).
/// </summary>
public sealed class ExrDecodingDefinition
{
    /// <summary>The window modes (names of <c>OpenExrWindowMode</c>).</summary>
    public static IReadOnlyList<string> WindowModes { get; } = ["DisplayWindow", "CropToDisplayWindow", "DataWindow"];

    /// <summary>Gets the layer (<see cref="string.Empty"/> for the default layer), or <see langword="null"/> to decode every channel.</summary>
    [JsonPropertyName("layer")]
    public string? Layer { get; init; }

    /// <summary>Gets the window mode: one of <see cref="WindowModes"/>.</summary>
    [JsonPropertyName("windowMode")]
    public required string WindowMode { get; init; }

    /// <summary>Gets the expected size and format of the image (with <see cref="Buffer"/>).</summary>
    [JsonPropertyName("width")]
    public int? Width { get; init; }

    /// <summary>Gets the expected height of the image (with <see cref="Buffer"/>).</summary>
    [JsonPropertyName("height")]
    public int? Height { get; init; }

    /// <summary>Gets the pixel format of an untyped load: <c>RgbaHalf</c> or <c>RgbaFloat</c> (with <see cref="Buffer"/>).</summary>
    [JsonPropertyName("pixelFormat")]
    public string? PixelFormat { get; init; }

    /// <summary>Gets the color model of the identified file (library <c>ImageColorModel</c> names, with <see cref="Buffer"/>).</summary>
    [JsonPropertyName("colorModel")]
    public string? ColorModel { get; init; }

    /// <summary>Gets the bits per component of the identified file: 16 or 32 (with <see cref="Buffer"/>).</summary>
    [JsonPropertyName("bitsPerComponent")]
    public int? BitsPerComponent { get; init; }

    /// <summary>Gets the decoded pixels (<c>rgbaf32le</c>), or <see langword="null"/> when the decoding fails with <see cref="ExpectedError"/>.</summary>
    [JsonPropertyName("buffer")]
    public RawBufferDescriptor? Buffer { get; init; }

    /// <summary>Gets how the decoded pixels were established (with <see cref="Buffer"/>).</summary>
    [JsonPropertyName("reference")]
    public FixtureReference? Reference { get; init; }

    /// <summary>Gets the pixel comparison policy (with <see cref="Buffer"/>): exact.</summary>
    [JsonPropertyName("comparison")]
    public ComparisonPolicyDefinition? Comparison { get; init; }

    /// <summary>Gets the error the decoding gives, or <see langword="null"/> when it gives <see cref="Buffer"/>.</summary>
    [JsonPropertyName("expectedError")]
    public ExpectedErrorDefinition? ExpectedError { get; init; }
}
