using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The expected content light level of an AVIF input (<c>ImageMetadata.ContentLightLevel</c>): the fields of its
/// <c>clli</c> box (ISO/IEC 14496-12, section 12.1.6), in candelas per square meter.
/// </summary>
public sealed class ContentLightLevelExpectation
{
    /// <summary>Gets the light level of the brightest pixel (MaxCLL), or 0 when unknown.</summary>
    [JsonPropertyName("maxContentLightLevel")]
    public required int MaxContentLightLevel { get; init; }

    /// <summary>Gets the largest average light level of a picture (MaxPALL), or 0 when unknown.</summary>
    [JsonPropertyName("maxPictureAverageLightLevel")]
    public required int MaxPictureAverageLightLevel { get; init; }

    /// <summary>Returns <c>MaxCLL content, MaxPALL picture</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"MaxCLL {MaxContentLightLevel}, MaxPALL {MaxPictureAverageLightLevel}");
}
