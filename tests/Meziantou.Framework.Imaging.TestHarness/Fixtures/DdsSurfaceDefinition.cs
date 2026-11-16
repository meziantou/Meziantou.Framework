using System.Text.Json.Serialization;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// A surface of a valid DDS fixture of several surfaces (mipmap levels, cube map faces, array elements, depth slices): the
/// entry of an <c>ImageCollection</c> of the file, where it is in the texture and its pixels, compared like the pixels of
/// the fixture. The first surface is the image the fixture expects.
/// </summary>
public sealed class DdsSurfaceDefinition
{
    /// <summary>The names of the faces of a cube map (names of <c>CubeMapFace</c>), in file order.</summary>
    public static IReadOnlyList<string> FaceNames { get; } = ["PositiveX", "NegativeX", "PositiveY", "NegativeY", "PositiveZ", "NegativeZ"];

    /// <summary>Gets the element of the array.</summary>
    [JsonPropertyName("arrayIndex")]
    public required int ArrayIndex { get; init; }

    /// <summary>Gets the face of a cube map (one of <see cref="FaceNames"/>), or <see langword="null"/>.</summary>
    [JsonPropertyName("face")]
    public string? Face { get; init; }

    /// <summary>Gets the mipmap level.</summary>
    [JsonPropertyName("mipLevel")]
    public required int MipLevel { get; init; }

    /// <summary>Gets the depth slice of a volume texture (0 otherwise).</summary>
    [JsonPropertyName("depthSlice")]
    public required int DepthSlice { get; init; }

    /// <summary>Gets the width of the surface.</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>Gets the height of the surface.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>Gets the pixels of the surface, in the layout of the frames of the fixture.</summary>
    [JsonPropertyName("buffer")]
    public required RawBufferDescriptor Buffer { get; init; }
}
