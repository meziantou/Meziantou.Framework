namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoded surfaces of a texture before its container is written: for each array element and each face, the texels of
/// each mipmap level, from the largest.
/// </summary>
internal sealed class EncodedTexture : IDisposable
{
    private readonly PooledBuffer?[] _surfaces;

    public EncodedTexture(TextureTarget target, int width, int height, int levelCount, int layerCount, int faceCount, bool isArray)
    {
        Target = target;
        Width = width;
        Height = height;
        LevelCount = levelCount;
        LayerCount = layerCount;
        FaceCount = faceCount;
        IsArray = isArray;
        _surfaces = new PooledBuffer?[layerCount * faceCount * levelCount];
    }

    public TextureTarget Target { get; }

    public int Width { get; }

    public int Height { get; }

    public int LevelCount { get; }

    /// <summary>Gets the number of elements of the array (of cube maps for an array of cube maps), 1 when the texture is not an array.</summary>
    public int LayerCount { get; }

    /// <summary>Gets the number of faces: 6 for a cube map, otherwise 1.</summary>
    public int FaceCount { get; }

    /// <summary>Gets a value indicating whether the texture is an array, even of one element.</summary>
    public bool IsArray { get; }

    public int GetLevelWidth(int level) => TextureMipmaps.GetLevelSize(Width, level);

    public int GetLevelHeight(int level) => TextureMipmaps.GetLevelSize(Height, level);

    /// <summary>Gets the length of the texels of one surface of a level, in bytes.</summary>
    public long GetSurfaceLength(int level) => Target.GetSurfaceLength(GetLevelWidth(level), GetLevelHeight(level));

    /// <summary>Stores the encoded texels of a surface; the texture owns the buffer from now on.</summary>
    public void Set(int layer, int face, int level, PooledBuffer texels)
    {
        ref var slot = ref _surfaces[GetIndex(layer, face, level)];
        slot?.Dispose();
        slot = texels;
    }

    /// <summary>Gets the encoded texels of a surface.</summary>
    public ReadOnlyMemory<byte> Get(int layer, int face, int level) => (_surfaces[GetIndex(layer, face, level)] ?? throw new InvalidOperationException("The surface is not encoded.")).Memory;

    public void Dispose()
    {
        for (var i = 0; i < _surfaces.Length; i++)
        {
            _surfaces[i]?.Dispose();
            _surfaces[i] = null;
        }
    }

    private int GetIndex(int layer, int face, int level) => (((layer * FaceCount) + face) * LevelCount) + level;
}
