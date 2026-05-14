namespace Meziantou.Framework;

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
}
