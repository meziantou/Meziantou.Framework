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

    /// <summary>UPC-E barcode: a UPC-A number of number system 0 or 1, without some of its zeros.</summary>
    UpcE,

    /// <summary>GS1 DataBar Omnidirectional barcode: a GTIN in a single row. Drawn 13 modules high, it is GS1 DataBar Truncated.</summary>
    Gs1DataBar,

    /// <summary>GS1 DataBar Stacked barcode: GS1 DataBar in two short rows.</summary>
    Gs1DataBarStacked,

    /// <summary>GS1 DataBar Stacked Omnidirectional barcode: GS1 DataBar in two full-height rows.</summary>
    Gs1DataBarStackedOmnidirectional,

    /// <summary>GS1 DataBar Limited barcode: a GTIN starting with 0 or 1, in a narrower single row.</summary>
    Gs1DataBarLimited,

    /// <summary>GS1 DataBar Expanded barcode: GS1 element strings in a single row.</summary>
    Gs1DataBarExpanded,

    /// <summary>GS1 DataBar Expanded Stacked barcode: GS1 DataBar Expanded in several rows.</summary>
    Gs1DataBarExpandedStacked,
}
