namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Specifies the type of barcode.
/// </summary>
public enum BarcodeType
{
    /// <summary>Code 39 barcode.</summary>
    Code39,

    /// <summary>Code 93 barcode.</summary>
    Code93,

    /// <summary>Code 128 barcode.</summary>
    Code128,

    /// <summary>EAN-8 barcode.</summary>
    Ean8,

    /// <summary>EAN-13 barcode.</summary>
    Ean13,

    /// <summary>UPC-A barcode.</summary>
    UpcA,

    /// <summary>Codabar barcode.</summary>
    Codabar,

    /// <summary>Interleaved 2 of 5 (ITF) barcode.</summary>
    Itf,

    /// <summary>GS1-128 barcode: a Code 128 symbol that starts with FNC1 and holds GS1 element strings.</summary>
    Gs1128,
}
