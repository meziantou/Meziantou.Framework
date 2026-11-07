using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>A file of the corpus, identified by its path relative to the fixture root and its SHA-256 hash.</summary>
public sealed class FixtureFile
{
    /// <summary>Gets the path relative to <c>tests/Meziantou.Framework.Imaging.Fixtures</c>, using '/' separators.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>Gets the lowercase hexadecimal SHA-256 of the file content.</summary>
    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    /// <summary>Gets the role of the file (e.g. <c>encoded</c>, <c>raw-frame</c>, <c>raw-poster</c>).</summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }
}
