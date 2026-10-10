using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Low-level reads of the ICC profile encoding (ICC.1:2022 section 4 and 7): big-endian numbers, fixed-point numbers,
/// four-character signatures and the tag table. Every read is bounds-checked by the span it is given; nothing here
/// allocates or throws for malformed data.
/// </summary>
internal static class IccReader
{
    /// <summary>The size of the profile header.</summary>
    public const int HeaderSize = 128;

    /// <summary>The size of one tag table entry (signature, offset, size).</summary>
    public const int TagEntrySize = 12;

    public const uint SignatureGray = 0x47524159; // 'GRAY'
    public const uint SignatureRgb = 0x52474220; // 'RGB '
    public const uint SignatureCmyk = 0x434D594B; // 'CMYK'
    public const uint SignatureXyz = 0x58595A20; // 'XYZ '
    public const uint SignatureLab = 0x4C616220; // 'Lab '

    public const uint ClassInput = 0x73636E72; // 'scnr'
    public const uint ClassDisplay = 0x6D6E7472; // 'mntr'
    public const uint ClassOutput = 0x70727472; // 'prtr'
    public const uint ClassDeviceLink = 0x6C696E6B; // 'link'
    public const uint ClassColorSpace = 0x73706163; // 'spac'
    public const uint ClassAbstract = 0x61627374; // 'abst'
    public const uint ClassNamedColor = 0x6E6D636C; // 'nmcl'

    public const uint TagRedColorant = 0x7258595A; // 'rXYZ'
    public const uint TagGreenColorant = 0x6758595A; // 'gXYZ'
    public const uint TagBlueColorant = 0x6258595A; // 'bXYZ'
    public const uint TagRedCurve = 0x72545243; // 'rTRC'
    public const uint TagGreenCurve = 0x67545243; // 'gTRC'
    public const uint TagBlueCurve = 0x62545243; // 'bTRC'
    public const uint TagGrayCurve = 0x6B545243; // 'kTRC'
    public const uint TagMediaWhitePoint = 0x77747074; // 'wtpt'
    public const uint TagAToB0 = 0x41324230; // 'A2B0'
    public const uint TagAToB1 = 0x41324231; // 'A2B1'
    public const uint TagAToB2 = 0x41324232; // 'A2B2'
    public const uint TagBToA0 = 0x42324130; // 'B2A0'
    public const uint TagBToA1 = 0x42324131; // 'B2A1'
    public const uint TagBToA2 = 0x42324132; // 'B2A2'

    public const uint TypeXyz = 0x58595A20; // 'XYZ '
    public const uint TypeCurve = 0x63757276; // 'curv'
    public const uint TypeParametricCurve = 0x70617261; // 'para'
    public const uint TypeLut8 = 0x6D667431; // 'mft1'
    public const uint TypeLut16 = 0x6D667432; // 'mft2'
    public const uint TypeLutAToB = 0x6D414220; // 'mAB '
    public const uint TypeLutBToA = 0x6D424120; // 'mBA '

    /// <summary>Reads a big-endian 16-bit unsigned number.</summary>
    public static ushort ReadUInt16(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt16BigEndian(data);

    /// <summary>Reads a big-endian 32-bit unsigned number (also a four-character signature).</summary>
    public static uint ReadUInt32(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32BigEndian(data);

    /// <summary>Reads an <c>s15Fixed16Number</c> (ICC.1:2022 section 4.6).</summary>
    public static double ReadS15Fixed16(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadInt32BigEndian(data) / 65536.0;

    /// <summary>Reads a <c>u8Fixed8Number</c> (ICC.1:2022 section 4.9).</summary>
    public static double ReadU8Fixed8(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt16BigEndian(data) / 256.0;

    /// <summary>Formats a signature for messages: its four characters when printable, else its hexadecimal value.</summary>
    public static string FormatSignature(uint signature)
    {
        Span<char> text = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var value = (byte)(signature >> (24 - (8 * i)));
            if (value is < 0x20 or > 0x7E)
                return string.Create(CultureInfo.InvariantCulture, $"0x{signature:X8}");

            text[i] = (char)value;
        }

        return $"'{text}'";
    }

    /// <summary>
    /// Finds the data of the first tag with a signature. The tag table and the tag must be inside the profile; several
    /// tags may share or overlap the same data (ICC.1:2022 section 7.3.1).
    /// </summary>
    /// <param name="data">The profile bytes.</param>
    /// <param name="signature">The tag signature.</param>
    /// <param name="tag">The tag data: type signature, four reserved bytes, then the type-specific content.</param>
    /// <returns><see langword="true"/> if the tag exists and is inside the profile.</returns>
    public static bool TryGetTag(ReadOnlySpan<byte> data, uint signature, out ReadOnlySpan<byte> tag)
    {
        if (TryFindTag(data, signature, out var offset, out var length))
        {
            tag = data.Slice(offset, length);
            return true;
        }

        tag = default;
        return false;
    }

    /// <summary>Finds the position of the data of the first tag with a signature (see <see cref="TryGetTag"/>).</summary>
    /// <param name="data">The profile bytes.</param>
    /// <param name="signature">The tag signature.</param>
    /// <param name="offset">The offset of the tag data in the profile.</param>
    /// <param name="length">The length of the tag data.</param>
    /// <returns><see langword="true"/> if the tag exists and is inside the profile.</returns>
    public static bool TryFindTag(ReadOnlySpan<byte> data, uint signature, out int offset, out int length)
    {
        offset = 0;
        length = 0;
        if (data.Length < HeaderSize + 4)
            return false;

        var count = ReadUInt32(data[HeaderSize..]);
        if (count > (data.Length - HeaderSize - 4) / TagEntrySize)
            return false;

        for (var i = 0; i < count; i++)
        {
            var entry = data.Slice(HeaderSize + 4 + (TagEntrySize * i), TagEntrySize);
            if (ReadUInt32(entry) != signature)
                continue;

            var tagOffset = ReadUInt32(entry[4..]);
            var tagSize = ReadUInt32(entry[8..]);
            if (tagOffset > (uint)data.Length || tagSize > (uint)data.Length - tagOffset)
                return false;

            offset = (int)tagOffset;
            length = (int)tagSize;
            return true;
        }

        return false;
    }
}
