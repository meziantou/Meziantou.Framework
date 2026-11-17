namespace Meziantou.Framework.Pdf;

/// <summary>Why <see cref="PdfTableExtractor"/> could not rebuild a table.</summary>
public enum PdfTableExtractionFailureKind
{
    /// <summary>The region has no grid: fewer than two horizontal or two vertical rules.</summary>
    NoGrid,

    /// <summary>A cell of the grid spans several rows or columns: a rule between two cells is missing.</summary>
    MergedCells,

    /// <summary>Text of the region is not horizontal: rotated or written vertically.</summary>
    NonHorizontalText,

    /// <summary>The region has images but no text: the table is an image, such as a scanned page.</summary>
    ImageOnly,
}
