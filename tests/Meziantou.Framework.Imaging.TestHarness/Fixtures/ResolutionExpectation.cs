using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The expected physical resolution (dots per inch) and the encoded fields it was converted from.</summary>
public sealed class ResolutionExpectation
{
    /// <summary>The meter unit (PNG <c>pHYs</c> unit 1): <c>dpi = x * 0.0254</c>.</summary>
    public const string Meter = "meter";

    /// <summary>The inch unit (JFIF units 1): <c>dpi = x</c>.</summary>
    public const string Inch = "inch";

    /// <summary>The centimeter unit (JFIF units 2): <c>dpi = x * 2.54</c>.</summary>
    public const string Centimeter = "centimeter";

    /// <summary>Gets the horizontal resolution in dots per inch.</summary>
    [JsonPropertyName("horizontalDpi")]
    public required double HorizontalDpi { get; init; }

    /// <summary>Gets the vertical resolution in dots per inch.</summary>
    [JsonPropertyName("verticalDpi")]
    public required double VerticalDpi { get; init; }

    /// <summary>Gets the encoded fields.</summary>
    [JsonPropertyName("encoded")]
    public required EncodedResolution Encoded { get; init; }

    /// <summary>Computes the resolution in dots per inch from an encoded density (IEEE double arithmetic, as documented in the manifest).</summary>
    /// <param name="unit">The unit.</param>
    /// <param name="value">The encoded density.</param>
    /// <returns>The resolution in dots per inch, or <see langword="null"/> for an unknown unit.</returns>
    public static double? ToDpi(string unit, int value) => unit switch
    {
        Meter => value * 0.0254,
        Inch => value,
        Centimeter => value * 2.54,
        _ => null,
    };
}
