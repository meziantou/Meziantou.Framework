using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Decode options a fixture must be decoded with.</summary>
public sealed class DecodeOptionsDefinition
{
    /// <summary>Gets resource limits to override, keyed by <c>ImageResourceLimits</c> property name (e.g. <c>MaxFrames</c>).</summary>
    [JsonPropertyName("limits")]
    public IReadOnlyDictionary<string, long>? Limits { get; init; }

    /// <summary>Gets the <c>ImageDecodeOptions.FrameLimit</c> to use.</summary>
    [JsonPropertyName("frameLimit")]
    public int? FrameLimit { get; init; }
}
