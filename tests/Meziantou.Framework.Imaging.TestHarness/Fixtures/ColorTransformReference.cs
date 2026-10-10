using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The independent color management system that produced the reference samples of a <see cref="ColorTransformEntry"/>.</summary>
public sealed class ColorTransformReference
{
    /// <summary>Gets the tool and its exact version, such as <c>transicc (LittleCMS 2.19)</c>.</summary>
    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    /// <summary>Gets the exact command line; the colors are its standard input.</summary>
    [JsonPropertyName("command")]
    public required string Command { get; init; }

    /// <summary>Gets the committed script and function that ran the tool.</summary>
    [JsonPropertyName("generator")]
    public required string Generator { get; init; }
}
