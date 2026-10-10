using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants and the signature rules of the Windows icon (<c>ICO</c>) and cursor (<c>CUR</c>) containers.
/// </summary>
/// <remarks>
/// An icon file is a directory of <em>alternative representations</em> of the same drawing at different sizes and color
/// depths, not a sequence of frames: nothing in the directory is timing, ordering or compositing information
///.
/// </remarks>
internal static class IcoFormat
{
    /// <summary>The length of the <c>ICONDIR</c> header: reserved, type and entry count.</summary>
    public const int HeaderLength = 6;

    /// <summary>The length of one <c>ICONDIRENTRY</c>.</summary>
    public const int EntryLength = 16;

    /// <summary>The <c>idType</c> value of an icon.</summary>
    public const int IconType = 1;

    /// <summary>The <c>idType</c> value of a cursor.</summary>
    public const int CursorType = 2;

    /// <summary>The largest representation an icon directory can describe: a dimension byte of 0 means 256.</summary>
    public const int MaxDimension = 256;

    /// <summary>Converts a directory dimension byte to pixels (0 means 256).</summary>
    public static int FromDimensionByte(byte value) => value == 0 ? MaxDimension : value;

    /// <summary>Converts a dimension in pixels to a directory dimension byte (256 is stored as 0).</summary>
    public static byte ToDimensionByte(int value) => value == MaxDimension ? (byte)0 : (byte)value;

    /// <summary>Determines whether a prefix is the start of an icon or cursor directory of the requested type.</summary>
    /// <param name="prefix">At least 18 bytes (<see cref="Image.FormatDetectionPrefixLength"/>).</param>
    /// <param name="type">1 for an icon, 2 for a cursor.</param>
    /// <returns><see langword="true"/> when the header and the start of the first directory entry are plausible.</returns>
    /// <remarks>
    /// <para>
    /// The four signature bytes of an icon (<c>00 00 01 00</c>) are weak, and TGA, the only other format without a real
    /// signature, also starts with zeros. The check therefore uses everything the detection prefix holds: the reserved
    /// field, the type, a non-zero entry count, the reserved byte of the first entry and a non-empty first payload. A
    /// valid TGA without a color map has zeros where the entry count is, so the two never both match.
    /// </para>
    /// </remarks>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix, int type)
    {
        if (prefix.Length < HeaderLength + 12)
            return false;

        if (BinaryPrimitives.ReadUInt16LittleEndian(prefix) != 0)
            return false;

        if (BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..]) != type)
            return false;

        if (BinaryPrimitives.ReadUInt16LittleEndian(prefix[4..]) == 0)
            return false;

        // ICONDIRENTRY.bReserved must be zero, and a representation cannot be empty
        return prefix[HeaderLength + 3] == 0 && BinaryPrimitives.ReadUInt32LittleEndian(prefix[(HeaderLength + 8)..]) != 0;
    }

    /// <summary>Gets the format of a directory type.</summary>
    public static ImageFormat GetFormat(int type) => type == CursorType ? ImageFormat.Cur : ImageFormat.Ico;

    /// <summary>Creates the exception reported for malformed icon or cursor data.</summary>
    public static InvalidImageContentException Invalid(ImageFormat format, string message) => new(message, format);

    /// <summary>Creates the exception reported for a recognized but unsupported icon or cursor payload.</summary>
    public static UnsupportedImageFeatureException Unsupported(ImageFormat format, string message, string feature) => new(message, format, feature);
}
