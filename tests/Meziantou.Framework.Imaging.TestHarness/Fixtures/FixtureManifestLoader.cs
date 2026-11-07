using System.Text.Json;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>Reads <c>manifest.json</c> files.</summary>
public static class FixtureManifestLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>Loads and deserializes a manifest. Unknown properties and missing required properties are errors.</summary>
    /// <param name="path">The manifest path.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="JsonException">The manifest is not valid JSON or does not match the model.</exception>
    public static FixtureManifest Load(FullPath path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<FixtureManifest>(stream, SerializerOptions)
            ?? throw new JsonException($"The manifest '{path}' is empty.");
    }
}
