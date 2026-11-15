namespace Meziantou.Framework.Imaging;

/// <summary>
/// Where an entry of a <see cref="ImageCollectionKind.TextureSurfaces"/> collection is in its texture: the element of the
/// array, the face of the cube map, the mipmap level and the depth slice.
/// </summary>
/// <remarks>Instances are immutable; this type is thread-safe.</remarks>
public sealed class TextureSurface
{
    /// <summary>Initializes a new instance of the <see cref="TextureSurface"/> class, for an entry added to a collection of texture surfaces.</summary>
    /// <param name="arrayIndex">The element of the texture array (of the cube map of a cube map array), 0 for a texture that is not an array.</param>
    /// <param name="face">The face of a cube map, or <see langword="null"/> when the texture is not a cube map.</param>
    /// <param name="mipLevel">The mipmap level: 0 is the full-size surface.</param>
    /// <exception cref="ArgumentOutOfRangeException">An index is negative, or <paramref name="face"/> is not a defined <see cref="CubeMapFace"/>.</exception>
    public TextureSurface(int arrayIndex = 0, CubeMapFace? face = null, int mipLevel = 0)
        : this(arrayIndex, face, mipLevel, depthSlice: 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(mipLevel);
        if (face is { } value && !Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(face), face, "The face is not valid.");
    }

    internal TextureSurface(int arrayIndex, CubeMapFace? face, int mipLevel, int depthSlice)
    {
        ArrayIndex = arrayIndex;
        Face = face;
        MipLevel = mipLevel;
        DepthSlice = depthSlice;
    }

    /// <summary>Gets the index of the element of the texture array (of the cube map of a cube map array), 0 for a texture that is not an array.</summary>
    public int ArrayIndex { get; }

    /// <summary>Gets the face of a cube map, or <see langword="null"/> when the texture is not a cube map.</summary>
    public CubeMapFace? Face { get; }

    /// <summary>Gets the mipmap level: 0 is the full-size surface, and each level halves the dimensions (rounded down, at least 1).</summary>
    public int MipLevel { get; }

    /// <summary>Gets the depth slice of a level of a volume texture, 0 for the other textures.</summary>
    public int DepthSlice { get; }

    /// <summary>Returns a description of the surface, such as <c>array 0, face PositiveX, mip 2, slice 0</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"array {ArrayIndex}, {(Face is { } face ? "face " + face + ", " : "")}mip {MipLevel}, slice {DepthSlice}");
}
