using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The expected mastering display color volume of an AVIF input (<c>ImageMetadata.MasteringDisplay</c>): the fields of
/// its <c>mdcv</c> box (ISO/IEC 14496-12, section 12.1.7) as they are stored, chromaticities in units of 0.00002 and
/// luminances in units of 0.0001 candela per square meter.
/// </summary>
public sealed class MasteringDisplayExpectation
{
    /// <summary>Gets the x and y chromaticity coordinates of the red primary.</summary>
    [JsonPropertyName("red")]
    public required IReadOnlyList<int> Red { get; init; }

    /// <summary>Gets the x and y chromaticity coordinates of the green primary.</summary>
    [JsonPropertyName("green")]
    public required IReadOnlyList<int> Green { get; init; }

    /// <summary>Gets the x and y chromaticity coordinates of the blue primary.</summary>
    [JsonPropertyName("blue")]
    public required IReadOnlyList<int> Blue { get; init; }

    /// <summary>Gets the x and y chromaticity coordinates of the white point.</summary>
    [JsonPropertyName("whitePoint")]
    public required IReadOnlyList<int> WhitePoint { get; init; }

    /// <summary>Gets the nominal maximum luminance of the display.</summary>
    [JsonPropertyName("maxLuminance")]
    public required long MaxLuminance { get; init; }

    /// <summary>Gets the nominal minimum luminance of the display.</summary>
    [JsonPropertyName("minLuminance")]
    public required long MinLuminance { get; init; }

    /// <summary>Returns <c>R x,y G x,y B x,y W x,y L min..max</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => Format(Red[0], Red[1], Green[0], Green[1], Blue[0], Blue[1], WhitePoint[0], WhitePoint[1], MaxLuminance, MinLuminance);

    /// <summary>Formats a mastering display color volume as <see cref="ToString"/> does.</summary>
    /// <param name="redX">The x of the red primary.</param>
    /// <param name="redY">The y of the red primary.</param>
    /// <param name="greenX">The x of the green primary.</param>
    /// <param name="greenY">The y of the green primary.</param>
    /// <param name="blueX">The x of the blue primary.</param>
    /// <param name="blueY">The y of the blue primary.</param>
    /// <param name="whitePointX">The x of the white point.</param>
    /// <param name="whitePointY">The y of the white point.</param>
    /// <param name="maxLuminance">The maximum luminance.</param>
    /// <param name="minLuminance">The minimum luminance.</param>
    /// <returns>The description.</returns>
    public static string Format(int redX, int redY, int greenX, int greenY, int blueX, int blueY, int whitePointX, int whitePointY, long maxLuminance, long minLuminance)
        => string.Create(CultureInfo.InvariantCulture, $"R {redX},{redY} G {greenX},{greenY} B {blueX},{blueY} W {whitePointX},{whitePointY} L {minLuminance}..{maxLuminance}");
}
