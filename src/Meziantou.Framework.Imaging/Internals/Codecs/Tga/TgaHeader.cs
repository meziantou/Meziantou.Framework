using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The 18-byte fixed header of a TGA file, parsed and validated.</summary>
/// <remarks>
/// TGA has no magic number, so <see cref="IsPlausible"/> is the detection rule: every field of the header must be a legal
/// value and the fields must agree with each other (a color map exactly when the image type is color-mapped, an alpha-bit
/// count the sample depth can hold, no reserved descriptor bit). Recognized values this version cannot decode are accepted
/// by <see cref="IsPlausible"/> and rejected by <see cref="Parse"/>, so they are reported as unsupported features instead of
/// as an unknown format.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct TgaHeader
{
    private TgaHeader(int idLength, bool hasColorMap, TgaImageType imageType, int colorMapFirstEntry, int colorMapLength, int colorMapEntryBits, int width, int height, int pixelDepth, byte descriptor)
    {
        IdLength = idLength;
        HasColorMap = hasColorMap;
        ImageType = imageType;
        ColorMapFirstEntry = colorMapFirstEntry;
        ColorMapLength = colorMapLength;
        ColorMapEntryBits = colorMapEntryBits;
        Width = width;
        Height = height;
        PixelDepth = pixelDepth;
        Descriptor = descriptor;
    }

    /// <summary>Gets the length of the image identification field that follows the header.</summary>
    public int IdLength { get; }

    /// <summary>Gets a value indicating whether a color map is stored.</summary>
    public bool HasColorMap { get; }

    /// <summary>Gets the image type.</summary>
    public TgaImageType ImageType { get; }

    /// <summary>Gets the index the first stored color-map entry corresponds to.</summary>
    public int ColorMapFirstEntry { get; }

    /// <summary>Gets the number of stored color-map entries.</summary>
    public int ColorMapLength { get; }

    /// <summary>Gets the number of bits of one color-map entry (15, 16, 24 or 32), or 0 without a color map.</summary>
    public int ColorMapEntryBits { get; }

    /// <summary>Gets the positive width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the positive height, in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the number of bits of one stored pixel (8, 15, 16, 24 or 32).</summary>
    public int PixelDepth { get; }

    /// <summary>Gets the image descriptor byte.</summary>
    public byte Descriptor { get; }

    /// <summary>Gets the number of alpha bits declared by the image descriptor.</summary>
    public int AlphaBits => Descriptor & TgaFormat.DescriptorAlphaBitsMask;

    /// <summary>Gets a value indicating whether the columns are stored right to left.</summary>
    public bool IsRightToLeft => (Descriptor & TgaFormat.DescriptorRightToLeft) != 0;

    /// <summary>Gets a value indicating whether the first stored row is the top row.</summary>
    public bool IsTopToBottom => (Descriptor & TgaFormat.DescriptorTopToBottom) != 0;

    /// <summary>Gets a value indicating whether the pixel data is run-length encoded.</summary>
    public bool IsRunLengthEncoded => ImageType is TgaImageType.RleColorMapped or TgaImageType.RleTrueColor or TgaImageType.RleGrayscale;

    /// <summary>Gets a value indicating whether the pixels are color-map indexes.</summary>
    public bool IsColorMapped => ImageType is TgaImageType.ColorMapped or TgaImageType.RleColorMapped;

    /// <summary>Gets a value indicating whether the samples are grayscale.</summary>
    public bool IsGrayscale => ImageType is TgaImageType.Grayscale or TgaImageType.RleGrayscale;

    /// <summary>Gets the number of bits of the samples the alpha-bit count applies to (the color-map entry for indexed images).</summary>
    public int SampleBits => IsColorMapped ? ColorMapEntryBits : PixelDepth;

    /// <summary>Gets the number of bytes of one stored pixel or index.</summary>
    public int BytesPerStoredPixel => (PixelDepth + 7) / 8;

    /// <summary>Gets the number of bytes of one stored color-map entry.</summary>
    public int BytesPerColorMapEntry => (ColorMapEntryBits + 7) / 8;

    /// <summary>Gets the number of bytes of the stored color map.</summary>
    public int ColorMapByteLength => ColorMapLength * BytesPerColorMapEntry;

    /// <summary>
    /// Determines whether 18 bytes are a legal, self-consistent TGA header. This is the content detection rule of the format
    ///: TGA stores no signature.
    /// </summary>
    /// <param name="prefix">At least <see cref="TgaFormat.HeaderLength"/> bytes.</param>
    /// <returns><see langword="true"/> when every field is legal and consistent.</returns>
    public static bool IsPlausible(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < TgaFormat.HeaderLength)
            return false;

        var colorMapType = prefix[1];
        var imageType = (TgaImageType)prefix[2];
        var colorMapFirstEntry = BinaryPrimitives.ReadUInt16LittleEndian(prefix[3..]);
        var colorMapLength = BinaryPrimitives.ReadUInt16LittleEndian(prefix[5..]);
        var colorMapEntryBits = prefix[7];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(prefix[12..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(prefix[14..]);
        var pixelDepth = prefix[16];
        var descriptor = prefix[17];

        if (colorMapType > 1 || width == 0 || height == 0 || (descriptor & TgaFormat.DescriptorReservedMask) != 0)
            return false;

        var isColorMapped = imageType is TgaImageType.ColorMapped or TgaImageType.RleColorMapped;
        if (isColorMapped != (colorMapType == 1))
            return false;

        if (isColorMapped)
        {
            if (colorMapLength == 0 || colorMapEntryBits is not (15 or 16 or 24 or 32) || colorMapFirstEntry + colorMapLength > ushort.MaxValue + 1)
                return false;
        }
        else if (colorMapFirstEntry != 0 || colorMapLength != 0 || colorMapEntryBits != 0)
        {
            return false;
        }

        var depthIsLegal = imageType switch
        {
            TgaImageType.ColorMapped or TgaImageType.RleColorMapped => pixelDepth is 8 or 16,
            TgaImageType.TrueColor or TgaImageType.RleTrueColor => pixelDepth is 15 or 16 or 24 or 32,
            TgaImageType.Grayscale or TgaImageType.RleGrayscale => pixelDepth is 8 or 16,
            _ => false,
        };

        if (!depthIsLegal)
            return false;

        var sampleBits = isColorMapped ? colorMapEntryBits : pixelDepth;
        return IsAlphaBitCountLegal(descriptor & TgaFormat.DescriptorAlphaBitsMask, sampleBits);
    }

    /// <summary>Parses and validates a TGA header.</summary>
    /// <param name="header">Exactly <see cref="TgaFormat.HeaderLength"/> bytes.</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="InvalidImageContentException">A field is out of range or inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The variant is recognized but not supported.</exception>
    public static TgaHeader Parse(ReadOnlySpan<byte> header)
    {
        if (!IsPlausible(header))
            throw TgaFormat.Invalid("The TGA header is not a legal, self-consistent 18-byte header (image type, color map, dimensions, pixel depth and image descriptor).");

        var imageType = (TgaImageType)header[2];
        var colorMapEntryBits = header[7];
        var pixelDepth = header[16];
        var isColorMapped = imageType is TgaImageType.ColorMapped or TgaImageType.RleColorMapped;
        if (isColorMapped && pixelDepth != 8)
            throw TgaFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TGA stores {pixelDepth}-bit color-map indexes; this version decodes 8-bit indexes."), "Color map: 16-bit indexes");

        if (imageType is TgaImageType.Grayscale or TgaImageType.RleGrayscale && pixelDepth != 8)
            throw TgaFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TGA stores {pixelDepth}-bit grayscale samples; this version decodes 8-bit grayscale."), "Grayscale: 16-bit samples");

        return new TgaHeader(
            header[0],
            hasColorMap: header[1] == 1,
            imageType,
            BinaryPrimitives.ReadUInt16LittleEndian(header[3..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[5..]),
            colorMapEntryBits,
            BinaryPrimitives.ReadUInt16LittleEndian(header[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[14..]),
            pixelDepth,
            header[17]);
    }

    /// <summary>Determines whether an alpha-bit count can be stored by samples of a given width.</summary>
    private static bool IsAlphaBitCountLegal(int alphaBits, int sampleBits) => sampleBits switch
    {
        32 => alphaBits is 0 or 8,
        16 => alphaBits is 0 or 1,
        _ => alphaBits == 0,
    };
}
