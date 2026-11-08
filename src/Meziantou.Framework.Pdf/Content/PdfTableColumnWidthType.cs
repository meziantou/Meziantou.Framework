namespace Meziantou.Framework.Pdf;

/// <summary>How the width of a column of a <see cref="PdfTable"/> is computed.</summary>
public enum PdfTableColumnWidthType
{
    /// <summary>The width of the content: the widest paragraph of the cells of the column, reduced down to their widest word when the table is too wide.</summary>
    Auto,

    /// <summary>A width in points.</summary>
    Fixed,

    /// <summary>A share of the width left by the fixed and automatic columns, in proportion to the weight of the column.</summary>
    Relative,
}
