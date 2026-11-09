namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Specifies the type of two-dimensional barcode.
/// </summary>
public enum Barcode2DType
{
    /// <summary>Data Matrix (ECC 200), ISO/IEC 16022.</summary>
    DataMatrix,

    /// <summary>PDF417, ISO/IEC 15438.</summary>
    Pdf417,

    /// <summary>Aztec Code, ISO/IEC 24778.</summary>
    Aztec,
}
