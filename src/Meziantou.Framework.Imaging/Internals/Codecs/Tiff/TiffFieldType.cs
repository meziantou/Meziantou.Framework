namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The field types of a TIFF directory entry (TIFF 6.0 and the BigTIFF 64-bit additions).</summary>
internal enum TiffFieldType : ushort
{
    /// <summary>A type this version does not know; its values are never interpreted.</summary>
    Unknown = 0,

    Byte = 1,
    Ascii = 2,
    Short = 3,
    Long = 4,
    Rational = 5,
    SByte = 6,
    Undefined = 7,
    SShort = 8,
    SLong = 9,
    SRational = 10,
    Float = 11,
    Double = 12,
    Ifd = 13,
    Long8 = 16,
    SLong8 = 17,
    Ifd8 = 18,
}
