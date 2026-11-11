namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>The modes of the data of QR codes, with the value of their mode indicator in standard QR codes.</summary>
internal enum EncodingMode
{
    Numeric = 1,
    Alphanumeric = 2,
    StructuredAppend = 3,
    Byte = 4,
    Eci = 7,
    Kanji = 8,
}
