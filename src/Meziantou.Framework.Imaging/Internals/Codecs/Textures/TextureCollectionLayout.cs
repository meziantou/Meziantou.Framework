namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The structure of the texture a collection of texture surfaces describes, checked from the <see cref="TextureSurface"/>
/// and the size of its entries, before anything is decoded or written: the elements of the array, the faces of the cube
/// maps and the mipmap levels.
/// </summary>
internal sealed class TextureCollectionLayout
{
    private TextureCollectionLayout(int width, int height, int layerCount, int faceCount, int levelCount, ImageCollectionEntry[] entries)
    {
        Width = width;
        Height = height;
        LayerCount = layerCount;
        FaceCount = faceCount;
        LevelCount = levelCount;
        Entries = entries;
    }

    public int Width { get; }

    public int Height { get; }

    public int LayerCount { get; }

    public int FaceCount { get; }

    /// <summary>Gets the number of mipmap levels the collection holds.</summary>
    public int LevelCount { get; }

    /// <summary>Gets the entries by array element, face and level (<see cref="GetIndex"/>).</summary>
    public ImageCollectionEntry[] Entries { get; }

    public int GetIndex(int layer, int face, int level) => (((layer * FaceCount) + face) * LevelCount) + level;

    private static ArgumentException CreateMissingSurfaceException(int layer, int faceCount, int face, int level, string paramName)
    {
        var missing = new TextureSurface(layer, faceCount == 6 ? (CubeMapFace)face : null, level, 0);
        return new ArgumentException($"The surface ({missing}) is missing: every element of the array has the same faces and mipmap levels.", paramName);
    }

    /// <summary>Checks the structure of a collection.</summary>
    /// <param name="collection">The collection of texture surfaces.</param>
    /// <param name="format">The output format, for messages.</param>
    /// <param name="generateMipmaps">Whether the encoder generates the mipmap levels.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentException">The surfaces do not form a texture: a surface is missing or repeated, or has the wrong size.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The texture is a volume texture.</exception>
    public static TextureCollectionLayout Create(ImageCollection collection, ImageFormat format, bool generateMipmaps)
    {
        var name = ImageFormatNames.Get(format);
        var layerCount = 0;
        var levelCount = 0;
        bool? isCube = null;
        for (var i = 0; i < collection.Count; i++)
        {
            var surface = collection[i].TextureSurface ?? throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The entry {i} has no TextureSurface: {name} output writes the surfaces of a texture."), nameof(collection));
            if (surface.DepthSlice != 0)
                throw new UnsupportedImageFeatureException($"The entry {i} is a depth slice of a volume texture, which {name} output does not write.", format, "Volume texture");

            if (isCube is { } cube && cube != surface.Face.HasValue)
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The entry {i} {(surface.Face.HasValue ? "is" : "is not")} a face of a cube map, unlike the first entry: the surfaces of a texture are all faces of cube maps, or none."), nameof(collection));

            isCube = surface.Face.HasValue;
            layerCount = Math.Max(layerCount, surface.ArrayIndex + 1);
            levelCount = Math.Max(levelCount, surface.MipLevel + 1);
        }

        var faceCount = isCube == true ? 6 : 1;
        if (generateMipmaps && levelCount > 1)
            throw new ArgumentException($"The collection already holds mipmap levels: {name} output cannot generate them. Set GenerateMipmaps to false, or remove the levels.", nameof(collection));

        // Every surface of the texture appears once: a texture of more surfaces than the collection has entries misses
        // some, and is rejected before its table is allocated (an array index of 10^8 is one entry, not 10^8). The first
        // missing surface is found among at most Count + 1 candidates
        var surfaceCount = (long)layerCount * faceCount * levelCount;
        if (surfaceCount > collection.Count)
        {
            var present = new HashSet<(int Layer, int Face, int Level)>();
            for (var i = 0; i < collection.Count; i++)
            {
                var surface = collection[i].TextureSurface!;
                present.Add((surface.ArrayIndex, (int)(surface.Face ?? CubeMapFace.PositiveX), surface.MipLevel));
            }

            for (var layer = 0; layer < layerCount; layer++)
            {
                for (var face = 0; face < faceCount; face++)
                {
                    for (var level = 0; level < levelCount; level++)
                    {
                        if (!present.Contains((layer, face, level)))
                            throw CreateMissingSurfaceException(layer, faceCount, face, level, nameof(collection));
                    }
                }
            }
        }

        var entries = new ImageCollectionEntry?[surfaceCount];
        var layout = new TextureCollectionLayout(0, 0, layerCount, faceCount, levelCount, null!);
        for (var i = 0; i < collection.Count; i++)
        {
            var entry = collection[i];
            var surface = entry.TextureSurface!;
            ref var slot = ref entries[layout.GetIndex(surface.ArrayIndex, (int)(surface.Face ?? CubeMapFace.PositiveX), surface.MipLevel)];
            if (slot is not null)
                throw new ArgumentException($"The surface ({surface}) appears twice in the collection.", nameof(collection));

            slot = entry;
        }

        var baseSize = Size.Empty;
        for (var layer = 0; layer < layerCount; layer++)
        {
            for (var face = 0; face < faceCount; face++)
            {
                for (var level = 0; level < levelCount; level++)
                {
                    var entry = entries[layout.GetIndex(layer, face, level)];
                    if (entry is null)
                        throw CreateMissingSurfaceException(layer, faceCount, face, level, nameof(collection));

                    if (baseSize.IsEmpty)
                    {
                        baseSize = entry.Size;
                        if (faceCount == 6 && baseSize.Width != baseSize.Height)
                            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The faces of a cube map are square, but the first one is {baseSize.Width}x{baseSize.Height}."), nameof(collection));

                        if (levelCount > TextureMipmaps.GetLevelCount(baseSize.Width, baseSize.Height))
                            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The collection holds {levelCount} mipmap levels, more than a texture of {baseSize.Width}x{baseSize.Height} has."), nameof(collection));
                    }

                    var expected = new Size(TextureMipmaps.GetLevelSize(baseSize.Width, level), TextureMipmaps.GetLevelSize(baseSize.Height, level));
                    if (entry.Size != expected)
                        throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The surface ({entry.TextureSurface}) is {entry.Size.Width}x{entry.Size.Height}, but the level {level} of a texture of {baseSize.Width}x{baseSize.Height} is {expected.Width}x{expected.Height}."), nameof(collection));
                }
            }
        }

        return new TextureCollectionLayout(baseSize.Width, baseSize.Height, layerCount, faceCount, levelCount, entries!);
    }
}
