using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The color conversion a four-component JPEG fixture (CMYK or YCCK inks) is loaded with, and the converted pixels. No
/// pixel format holds inks: such a file is an <see cref="FixtureKinds.Unsupported"/> fixture, rejected by a load without
/// conversion, and this definition is its only pixel reference.
/// </summary>
public sealed class InkConversionDefinition
{
    /// <summary>The profile embedded in the file describes the inks.</summary>
    public const string Embedded = "embedded";

    /// <summary>The file has no profile: the CMYK profile provided by the library is assumed (<c>icc/cgats001-compat-v2-micro</c> of the corpus).</summary>
    public const string Assumed = "assumed";

    /// <summary>The id of the color profile of the corpus that the library provides as its assumed CMYK profile.</summary>
    public const string AssumedProfileId = "icc/cgats001-compat-v2-micro";

    /// <summary>Gets where the profile of the inks comes from: <see cref="Embedded"/> or <see cref="Assumed"/>.</summary>
    [JsonPropertyName("sourceProfile")]
    public required string SourceProfile { get; init; }

    /// <summary>Gets the id of the destination color profile (an RGB profile of <c>colorProfiles</c>).</summary>
    [JsonPropertyName("destinationProfile")]
    public required string DestinationProfile { get; init; }

    /// <summary>Gets the rendering intent: one of <see cref="ColorTransformEntry.Intents"/>.</summary>
    [JsonPropertyName("intent")]
    public required string Intent { get; init; }

    /// <summary>Gets a value indicating whether black point compensation is applied.</summary>
    [JsonPropertyName("blackPointCompensation")]
    public required bool BlackPointCompensation { get; init; }

    /// <summary>Gets the color model of the encoded samples: <c>Cmyk</c> or <c>Ycck</c> (library <c>ImageColorModel</c> names).</summary>
    [JsonPropertyName("colorModel")]
    public required string ColorModel { get; init; }

    /// <summary>Gets the converted pixels (<c>rgb8</c>).</summary>
    [JsonPropertyName("buffer")]
    public required RawBufferDescriptor Buffer { get; init; }

    /// <summary>Gets how the converted pixels were established.</summary>
    [JsonPropertyName("reference")]
    public required FixtureReference Reference { get; init; }

    /// <summary>Gets the pixel comparison policy.</summary>
    [JsonPropertyName("comparison")]
    public required ComparisonPolicyDefinition Comparison { get; init; }
}
