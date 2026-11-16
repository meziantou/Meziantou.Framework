using System.Collections.ObjectModel;

namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>
/// The immutable header, data format descriptor and key/value data of the KTX 2.0 file an image was decoded from. This is
/// informational only, like <see cref="ImageMetadata.SourceFormat"/>: it describes the file, not the pixels.
/// </summary>
/// <remarks>
/// <para>
/// The decoder applies what changes the pixels the texture means: the component mapping of <c>KTXswizzle</c> (the
/// channels a format lacks are 0, and alpha 1), and the transfer function and the primaries of the data format descriptor
/// as the color labels of the image (<see cref="ImageMetadata.TransferFunction"/>, <see cref="ImageMetadata.Cicp"/>).
/// <c>KTXorientation</c> is reported as <see cref="ImageMetadata.Orientation"/>, which <c>AutoOrient</c> applies.
/// </para>
/// <para>No operation updates it, and every encoder ignores it.</para>
/// </remarks>
public sealed class Ktx2Header : IEquatable<Ktx2Header>
{
    internal Ktx2Header(int vkFormat, string formatName, int supercompressionScheme, int pixelWidth, int pixelHeight, int pixelDepth, int layerCount, int faceCount, int levelCount, int colorModel, int colorPrimaries, int transferFunction, IReadOnlyList<KeyValuePair<string, byte[]>> keyValueData)
    {
        VkFormat = vkFormat;
        FormatName = formatName;
        SupercompressionScheme = supercompressionScheme;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        PixelDepth = pixelDepth;
        LayerCount = layerCount;
        FaceCount = faceCount;
        LevelCount = levelCount;
        ColorModel = colorModel;
        ColorPrimaries = colorPrimaries;
        TransferFunction = transferFunction;
        var values = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var keys = new List<string>(keyValueData.Count);
        foreach (var pair in keyValueData)
        {
            values.Add(pair.Key, pair.Value.AsMemory());
            keys.Add(pair.Key);
        }

        KeyValueData = new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(values);
        Keys = keys.AsReadOnly();
    }

    /// <summary>Gets the <c>vkFormat</c> of the header: a value of the Vulkan <c>VkFormat</c> enumeration, or 0 (<c>VK_FORMAT_UNDEFINED</c>) for Basis Universal data.</summary>
    public int VkFormat { get; }

    /// <summary>Gets the name of the format, such as <c>R8G8B8A8_SRGB</c>, <c>ASTC_6x6_UNORM_BLOCK</c> or <c>UNDEFINED (UASTC)</c>.</summary>
    public string FormatName { get; }

    /// <summary>Gets the supercompression scheme: 0 (none), 1 (BasisLZ), 2 (Zstandard) or 3 (ZLIB).</summary>
    public int SupercompressionScheme { get; }

    /// <summary>Gets the width of the base level, in pixels.</summary>
    public int PixelWidth { get; }

    /// <summary>Gets the height of the base level, in pixels, or 0 for a one-dimensional texture.</summary>
    public int PixelHeight { get; }

    /// <summary>Gets the depth of the base level, in pixels, or 0 for a texture that is not three-dimensional.</summary>
    public int PixelDepth { get; }

    /// <summary>Gets the number of array layers, or 0 for a texture that is not an array.</summary>
    public int LayerCount { get; }

    /// <summary>Gets the number of faces: 6 for a cube map, 1 otherwise.</summary>
    public int FaceCount { get; }

    /// <summary>Gets the number of mip levels the file stores, or 0 when it stores the base level only and asks a loader to generate the others.</summary>
    public int LevelCount { get; }

    /// <summary>Gets the color model of the data format descriptor (Khronos Data Format Specification, section 5.5), such as 1 (RGBSDA), 162 (ASTC) or 166 (UASTC).</summary>
    public int ColorModel { get; }

    /// <summary>Gets the color primaries of the data format descriptor (section 5.7), such as 1 (BT.709) or 10 (Display P3).</summary>
    public int ColorPrimaries { get; }

    /// <summary>Gets the transfer function of the data format descriptor (section 5.8), such as 1 (linear) or 2 (sRGB).</summary>
    public int TransferFunction { get; }

    /// <summary>Gets the keys of the key/value data, in the order of the file.</summary>
    public IReadOnlyList<string> Keys { get; }

    /// <summary>Gets the key/value data: each value as it is stored, a NUL-terminated string included.</summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> KeyValueData { get; }

    /// <summary>Gets the value of <c>KTXorientation</c>, such as <c>rd</c> or <c>ruo</c>, or <see langword="null"/> without it.</summary>
    public string? Orientation => GetString("KTXorientation");

    /// <summary>Gets the value of <c>KTXswizzle</c>, such as <c>rrr1</c>, or <see langword="null"/> without it.</summary>
    public string? Swizzle => GetString("KTXswizzle");

    /// <summary>Gets the value of <c>KTXwriter</c>, the tool that wrote the file, or <see langword="null"/> without it.</summary>
    public string? Writer => GetString("KTXwriter");

    /// <summary>Gets the value of <c>KTXwriterScParams</c>, the options of the tool that wrote the file, or <see langword="null"/> without it.</summary>
    public string? WriterScParams => GetString("KTXwriterScParams");

    /// <summary>Gets the value of <c>KTXastcDecodeMode</c> (<c>rgb9e5</c> or <c>unorm8</c>), or <see langword="null"/> without it. It is reported, never applied.</summary>
    public string? AstcDecodeMode => GetString("KTXastcDecodeMode");

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] Ktx2Header? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (VkFormat != other.VkFormat || FormatName != other.FormatName || SupercompressionScheme != other.SupercompressionScheme || PixelWidth != other.PixelWidth || PixelHeight != other.PixelHeight || PixelDepth != other.PixelDepth
            || LayerCount != other.LayerCount || FaceCount != other.FaceCount || LevelCount != other.LevelCount || ColorModel != other.ColorModel || ColorPrimaries != other.ColorPrimaries || TransferFunction != other.TransferFunction
            || !Keys.SequenceEqual(other.Keys, StringComparer.Ordinal))
        {
            return false;
        }

        foreach (var key in Keys)
        {
            if (!KeyValueData[key].Span.SequenceEqual(other.KeyValueData[key].Span))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Ktx2Header);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(VkFormat, SupercompressionScheme, PixelWidth, PixelHeight, PixelDepth, LayerCount, FaceCount, LevelCount);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{FormatName} {PixelWidth}x{PixelHeight}x{PixelDepth}, {LayerCount} layers, {FaceCount} faces, {LevelCount} levels, scheme {SupercompressionScheme}");

    private string? GetString(string key)
    {
        if (!KeyValueData.TryGetValue(key, out var value))
            return null;

        var span = value.Span;
        if (span.Length > 0 && span[^1] == 0)
        {
            span = span[..^1];
        }

        return System.Text.Encoding.UTF8.GetString(span);
    }
}
