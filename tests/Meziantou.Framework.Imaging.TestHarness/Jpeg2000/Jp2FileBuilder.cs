using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

/// <summary>
/// Assembles the boxes of a JP2 file (Rec. ITU-T T.800 | ISO/IEC 15444-1, Annex I) around a codestream written by a
/// reference tool, field by field: the tests and the corpus generator choose every byte of the container, valid or not.
/// It has no dependency on the library.
/// </summary>
public static class Jp2FileBuilder
{
    /// <summary>EnumCS 12: CMYK.</summary>
    public const uint ColorSpaceCmyk = 12;

    /// <summary>EnumCS 16: sRGB.</summary>
    public const uint ColorSpaceSrgb = 16;

    /// <summary>EnumCS 17: grayscale.</summary>
    public const uint ColorSpaceGray = 17;

    /// <summary>EnumCS 18: sYCC.</summary>
    public const uint ColorSpaceSycc = 18;

    /// <summary>Creates the signature box: the first twelve bytes of a file.</summary>
    public static byte[] Signature() => [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    /// <summary>Creates the identifier of the <c>uuid</c> box that holds an XMP packet.</summary>
    public static byte[] XmpUuid() => [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];

    /// <summary>Creates the identifier of the <c>uuid</c> box that holds EXIF data.</summary>
    public static byte[] ExifUuid() => Encoding.ASCII.GetBytes("JpgTiffExif->JP2");

    /// <summary>Concatenates boxes, or any byte arrays.</summary>
    public static byte[] Concat(params ReadOnlySpan<byte[]> parts)
    {
        var length = 0;
        foreach (var part in parts)
        {
            length += part.Length;
        }

        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    /// <summary>Creates a box: its length in 32 bits, its type, then its content.</summary>
    public static byte[] Box(string type, params ReadOnlySpan<byte[]> content)
    {
        var payload = Concat(content);
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        WriteType(box.AsSpan(4), type);
        payload.CopyTo(box, 8);
        return box;
    }

    /// <summary>Creates a box whose length is in the 64-bit field (LBox is 1).</summary>
    public static byte[] LargeBox(string type, params ReadOnlySpan<byte[]> content)
    {
        var payload = Concat(content);
        var box = new byte[16 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        WriteType(box.AsSpan(4), type);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        payload.CopyTo(box, 16);
        return box;
    }

    /// <summary>Creates a box that extends to the end of the file (LBox is 0).</summary>
    public static byte[] BoxToEnd(string type, params ReadOnlySpan<byte[]> content)
    {
        var box = Box(type, content);
        BinaryPrimitives.WriteUInt32BigEndian(box, 0);
        return box;
    }

    /// <summary>Creates a file type box: the brand, the minor version 0, then the compatible brands.</summary>
    public static byte[] FileType(string brand, params ReadOnlySpan<string> compatibleBrands)
    {
        var payload = new byte[8 + (4 * compatibleBrands.Length)];
        WriteType(payload, brand);
        for (var i = 0; i < compatibleBrands.Length; i++)
        {
            WriteType(payload.AsSpan(8 + (4 * i)), compatibleBrands[i]);
        }

        return Box("ftyp", payload);
    }

    /// <summary>Creates an image header box.</summary>
    /// <param name="width">The width of the image area.</param>
    /// <param name="height">The height of the image area.</param>
    /// <param name="componentCount">The number of components.</param>
    /// <param name="bitsPerComponent">The BPC field: the depth of the components less one, with the sign in the high bit; 255 when a <c>bpcc</c> box gives them.</param>
    /// <param name="compressionType">The C field: 7.</param>
    public static byte[] ImageHeader(int width, int height, int componentCount, int bitsPerComponent, int compressionType = 7)
    {
        var payload = new byte[14];
        BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)height);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)width);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), (ushort)componentCount);
        payload[10] = (byte)bitsPerComponent;
        payload[11] = (byte)compressionType;
        return Box("ihdr", payload);
    }

    /// <summary>Creates a color specification box with an enumerated color space (METH 1).</summary>
    public static byte[] EnumeratedColor(uint colorSpace)
    {
        var payload = new byte[7];
        payload[0] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(3), colorSpace);
        return Box("colr", payload);
    }

    /// <summary>Creates a color specification box with an ICC profile.</summary>
    /// <param name="method">The METH field: 2 for a restricted profile, 3 for any profile.</param>
    /// <param name="profile">The profile.</param>
    public static byte[] ProfileColor(int method, byte[] profile) => Box("colr", [(byte)method, 0, 0], profile);

    /// <summary>Creates a palette box.</summary>
    /// <param name="columnDepths">For each column, the depth of its values less one, with the sign in the high bit.</param>
    /// <param name="entries">The values of each entry, one per column.</param>
    public static byte[] Palette(ReadOnlySpan<byte> columnDepths, params ReadOnlySpan<int[]> entries)
    {
        var payload = new List<byte> { (byte)(entries.Length >> 8), (byte)entries.Length, (byte)columnDepths.Length };
        payload.AddRange(columnDepths);
        foreach (var entry in entries)
        {
            for (var column = 0; column < columnDepths.Length; column++)
            {
                var length = ((columnDepths[column] & 0x7F) + 8) / 8;
                for (var i = length - 1; i >= 0; i--)
                {
                    payload.Add((byte)(entry[column] >> (8 * i)));
                }
            }
        }

        return Box("pclr", [.. payload]);
    }

    /// <summary>Creates a component mapping box: for each channel, a component, the mapping type (0 direct, 1 palette) and the palette column.</summary>
    public static byte[] ComponentMapping(params ReadOnlySpan<(int Component, int MappingType, int PaletteColumn)> channels)
    {
        var payload = new byte[4 * channels.Length];
        for (var i = 0; i < channels.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4 * i), (ushort)channels[i].Component);
            payload[(4 * i) + 2] = (byte)channels[i].MappingType;
            payload[(4 * i) + 3] = (byte)channels[i].PaletteColumn;
        }

        return Box("cmap", payload);
    }

    /// <summary>Creates a channel definition box: for each entry, the channel, its type (0 color, 1 opacity, 2 premultiplied opacity) and its association (0 the image, or the number of a color).</summary>
    public static byte[] ChannelDefinition(params ReadOnlySpan<(int Channel, int Type, int Association)> channels)
    {
        var payload = new byte[2 + (6 * channels.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)channels.Length);
        for (var i = 0; i < channels.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2 + (6 * i)), (ushort)channels[i].Channel);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4 + (6 * i)), (ushort)channels[i].Type);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6 + (6 * i)), (ushort)channels[i].Association);
        }

        return Box("cdef", payload);
    }

    /// <summary>Creates a capture (<c>resc</c>) or default display (<c>resd</c>) resolution box: grid points per meter, as <c>numerator / denominator * 10^exponent</c> on each axis.</summary>
    public static byte[] Resolution(string type, int verticalNumerator, int verticalDenominator, int verticalExponent, int horizontalNumerator, int horizontalDenominator, int horizontalExponent)
    {
        var payload = new byte[10];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)verticalNumerator);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), (ushort)verticalDenominator);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), (ushort)horizontalNumerator);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), (ushort)horizontalDenominator);
        payload[8] = (byte)verticalExponent;
        payload[9] = (byte)horizontalExponent;
        return Box(type, payload);
    }

    /// <summary>Creates a <c>uuid</c> box: the 16-byte identifier, then the data.</summary>
    public static byte[] Uuid(byte[] identifier, byte[] data) => Box("uuid", identifier, data);

    /// <summary>
    /// Creates a JP2 file around a codestream: the signature box, a file type box of the brand <c>jp2 </c>, the JP2 header
    /// box with the specified boxes, then the codestream box.
    /// </summary>
    /// <param name="codestream">The codestream.</param>
    /// <param name="headerBoxes">The boxes of the JP2 header box, in order.</param>
    public static byte[] File(byte[] codestream, params ReadOnlySpan<byte[]> headerBoxes)
        => Concat(Signature(), FileType("jp2 ", "jp2 "), Box("jp2h", headerBoxes), Box("jp2c", codestream));

    private static void WriteType(Span<byte> destination, string type)
    {
        if (type.Length != 4)
            throw new ArgumentException("A box type or a brand has four characters.", nameof(type));

        for (var i = 0; i < 4; i++)
        {
            destination[i] = (byte)type[i];
        }
    }
}
