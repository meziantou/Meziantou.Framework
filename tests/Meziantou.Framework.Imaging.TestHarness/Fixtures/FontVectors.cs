using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// The reference values of a font of the corpus (<see cref="FontEntry.Vectors"/>), recorded by the corpus generator
/// from independent font readers. A value is only recorded when the readers agree.
/// </summary>
public sealed class FontVectors
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Gets the tools the values come from, with their versions.</summary>
    [JsonPropertyName("tools")]
    public required IReadOnlyList<string> Tools { get; init; }

    /// <summary>Gets the values of each face of the file, in order.</summary>
    [JsonPropertyName("faces")]
    public required IReadOnlyList<FontFaceVectors> Faces { get; init; }

    /// <summary>Reads the vectors of a font.</summary>
    /// <param name="path">The path of the JSON file.</param>
    /// <returns>The vectors.</returns>
    public static FontVectors Load(FullPath path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<FontVectors>(stream, SerializerOptions) ?? throw new InvalidDataException($"'{path}' is empty");
    }
}
