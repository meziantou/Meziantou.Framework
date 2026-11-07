using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>An external tool of the AV1 conformance vectors: a decoder or a header parser, and how it was run.</summary>
public sealed class Av1VectorTool
{
    /// <summary>Gets the tool and its exact version, e.g. <c>libdav1d 1.5.4</c>.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets the exact command line (symbolic file names).</summary>
    [JsonPropertyName("command")]
    public required string Command { get; init; }
}
