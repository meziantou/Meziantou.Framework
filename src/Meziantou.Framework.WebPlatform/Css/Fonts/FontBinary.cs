using System.Buffers.Binary;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// Bounds-checked big-endian reads of the data types of OpenType (https://learn.microsoft.com/typography/opentype/spec/otff#data-types),
/// for the layout tables the shaper reads. A read outside of the data returns 0 and a slice outside of the data is empty: a
/// malformed table cannot make the shaper throw, it only reads zeros.
/// </summary>
internal static class FontBinary
{
    public static byte U8(ReadOnlySpan<byte> data, int offset) => (uint)offset < (uint)data.Length ? data[offset] : (byte)0;

    public static ushort U16(ReadOnlySpan<byte> data, int offset) => offset >= 0 && offset <= data.Length - 2 ? BinaryPrimitives.ReadUInt16BigEndian(data[offset..]) : (ushort)0;

    public static short I16(ReadOnlySpan<byte> data, int offset) => (short)U16(data, offset);

    public static uint U24(ReadOnlySpan<byte> data, int offset) => offset >= 0 && offset <= data.Length - 3 ? (uint)((data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2]) : 0u;

    public static uint U32(ReadOnlySpan<byte> data, int offset) => offset >= 0 && offset <= data.Length - 4 ? BinaryPrimitives.ReadUInt32BigEndian(data[offset..]) : 0u;

    /// <summary>A 2.14 fixed-point number (<c>F2DOT14</c>).</summary>
    public static double F2Dot14(ReadOnlySpan<byte> data, int offset) => I16(data, offset) / 16384.0;

    /// <summary>Returns whether the range is inside of the data (the offsets of a font are unsigned 32-bit integers).</summary>
    public static bool IsInRange(ReadOnlySpan<byte> data, long offset, long length) => offset >= 0 && length >= 0 && offset + length <= data.Length;

    /// <summary>The data from the offset, or an empty span when the offset is outside of the data.</summary>
    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, long offset) => offset >= 0 && offset <= data.Length ? data[(int)offset..] : default;

    /// <summary>The range of the data, or an empty span when the range is outside of the data.</summary>
    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, long offset, long length) => IsInRange(data, offset, length) ? data.Slice((int)offset, (int)length) : default;
}

/// <summary>The tags of the OpenType tables and of the layout and variation records (https://learn.microsoft.com/typography/opentype/spec/otff#data-types).</summary>
internal static class OpenTypeTag
{
    public const uint Avar = 0x61766172; // avar
    public const uint Cff = 0x43464620; // CFF
    public const uint Cff2 = 0x43464632; // CFF2
    public const uint Cbdt = 0x43424454; // CBDT
    public const uint Cmap = 0x636D6170; // cmap
    public const uint Colr = 0x434F4C52; // COLR
    public const uint Cpal = 0x4350414C; // CPAL
    public const uint Fvar = 0x66766172; // fvar
    public const uint Gdef = 0x47444546; // GDEF
    public const uint Glyf = 0x676C7966; // glyf
    public const uint Gpos = 0x47504F53; // GPOS
    public const uint Gsub = 0x47535542; // GSUB
    public const uint Head = 0x68656164; // head
    public const uint Hhea = 0x68686561; // hhea
    public const uint Hmtx = 0x686D7478; // hmtx
    public const uint Hvar = 0x48564152; // HVAR
    public const uint Kern = 0x6B65726E; // kern (the table and the feature)
    public const uint Loca = 0x6C6F6361; // loca
    public const uint Math = 0x4D415448; // MATH
    public const uint Maxp = 0x6D617870; // maxp
    public const uint Mvar = 0x4D564152; // MVAR
    public const uint Name = 0x6E616D65; // name
    public const uint OS2 = 0x4F532F32; // OS/2
    public const uint Post = 0x706F7374; // post
    public const uint Sbix = 0x73626978; // sbix
    public const uint Svg = 0x53564720; // SVG
    public const uint Vhea = 0x76686561; // vhea
    public const uint Vmtx = 0x766D7478; // vmtx

    // The scripts and the features of the layout tables (https://learn.microsoft.com/typography/opentype/spec/scripttags)
    public const uint DefaultScript = 0x44464C54; // DFLT
    public const uint LatinScript = 0x6C61746E; // latn
    public const uint VerticalKerning = 0x766B726E; // vkrn
    public const uint Halt = 0x68616C74; // halt
    public const uint Vhal = 0x7668616C; // vhal
    public const uint RightToLeftMirroredForms = 0x72746C6D; // rtlm

    // The registered variation axes (https://learn.microsoft.com/typography/opentype/spec/dvaraxisreg)
    public const uint WeightAxis = 0x77676874; // wght
    public const uint WidthAxis = 0x77647468; // wdth
    public const uint SlantAxis = 0x736C6E74; // slnt
    public const uint ItalicAxis = 0x6974616C; // ital
    public const uint OpticalSizeAxis = 0x6F70737A; // opsz

    // The signatures of the font files
    public const uint TrueTypeVersion = 0x00010000;
    public const uint OpenTypeCff = 0x4F54544F; // OTTO
    public const uint AppleTrueType = 0x74727565; // true
    public const uint PostScriptType1 = 0x74797031; // typ1
    public const uint Collection = 0x74746366; // ttcf
    public const uint Woff = 0x774F4646; // wOFF
    public const uint Woff2 = 0x774F4632; // wOF2

    /// <summary>Creates a tag from its characters (padded with spaces when there are less than 4); returns false when the string is not a tag.</summary>
    public static bool TryParse(string value, out uint tag)
    {
        tag = 0;
        if (value.Length is 0 or > 4)
            return false;

        for (var i = 0; i < 4; i++)
        {
            var c = i < value.Length ? value[i] : ' ';
            if (c is < (char)0x20 or > (char)0x7E)
                return false;

            tag = (tag << 8) | c;
        }

        return true;
    }

    /// <summary>The tag of 4 characters (e.g. <c>smcp</c>).</summary>
    public static uint FromString(string value) => ((uint)value[0] << 24) | ((uint)value[1] << 16) | ((uint)value[2] << 8) | value[3];

    /// <summary>The 4 characters of a tag.</summary>
    public static string ToString(uint tag) => string.Create(4, tag, static (span, value) =>
    {
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((value >> (24 - (i * 8))) & 0xFF);
            span[i] = c is >= (char)0x20 and <= (char)0x7E ? c : '?';
        }
    });
}
