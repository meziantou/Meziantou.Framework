namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Specifies the mode of a <see cref="QRCodeSegment"/>: how its characters are encoded.
/// </summary>
public enum QRCodeSegmentMode
{
    /// <summary>Digits, 10 bits per 3 digits.</summary>
    Numeric,

    /// <summary>Digits, uppercase letters and <c> $%*+-./:</c>, 11 bits per 2 characters.</summary>
    Alphanumeric,

    /// <summary>Bytes, 8 bits each.</summary>
    Byte,

    /// <summary>Kanji and kana of the Shift JIS character set, 13 bits each.</summary>
    Kanji,

    /// <summary>An ECI designator: the character encoding of the byte segments that follow it.</summary>
    Eci,
}
