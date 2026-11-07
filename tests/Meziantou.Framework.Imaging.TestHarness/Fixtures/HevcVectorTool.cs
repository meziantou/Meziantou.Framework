using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>An external tool of the HEVC conformance vectors: a decoder or a header parser, and how it was run.</summary>
public sealed class HevcVectorTool
{
    /// <summary>
    /// Gets the short identifier the vectors refer to a decoder by (<c>ffmpeg</c>, <c>libde265</c>, <c>hm</c>), or
    /// <see langword="null"/> for the header parser.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Gets the tool and its exact version, e.g. <c>libde265 1.1.3 (dec265)</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets the exact command line (symbolic file names).</summary>
    [JsonPropertyName("command")]
    public required string Command { get; init; }
}
