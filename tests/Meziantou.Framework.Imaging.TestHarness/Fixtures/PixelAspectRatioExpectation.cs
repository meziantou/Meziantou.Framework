using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The expected pixel aspect ratio of an AVIF input (<c>ImageMetadata.PixelAspectRatio</c>): the terms of its
/// <c>pasp</c> box (ISO/IEC 14496-12, section 12.1.4), as they are stored.
/// </summary>
public sealed class PixelAspectRatioExpectation
{
    /// <summary>Gets the relative width of a pixel.</summary>
    [JsonPropertyName("horizontalSpacing")]
    public required long HorizontalSpacing { get; init; }

    /// <summary>Gets the relative height of a pixel.</summary>
    [JsonPropertyName("verticalSpacing")]
    public required long VerticalSpacing { get; init; }

    /// <summary>Returns <c>horizontal:vertical</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{HorizontalSpacing}:{VerticalSpacing}");
}
