using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>The glyphs and positions a reference shaper gives for texts set in a font of the corpus.</summary>
public sealed class ShapingVectors
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>The tool that produced the values, with its version and the options common to every case.</summary>
    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    [JsonPropertyName("cases")]
    public required IReadOnlyList<ShapingCase> Cases { get; init; }

    public static ShapingVectors Load(FullPath path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<ShapingVectors>(stream, SerializerOptions) ?? throw new InvalidDataException($"'{path}' is empty");
    }
}
