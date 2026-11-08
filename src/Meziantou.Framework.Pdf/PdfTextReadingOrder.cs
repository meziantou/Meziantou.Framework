namespace Meziantou.Framework.Pdf;

/// <summary>The order of the lines of the text extracted from a page (see <see cref="PdfTextExtractionOptions.ReadingOrder"/>).</summary>
public enum PdfTextReadingOrder
{
    /// <summary>The order of the content: the lines are in the order the page paints them.</summary>
    Content,

    /// <summary>
    /// The order of the layout: the text is split into blocks at the blank spaces of the page, such as the gutter between
    /// two columns or the gap between two paragraphs (a recursive XY-cut of the boxes of the text). The blocks are read
    /// from top to bottom, the columns from left to right, and the lines of a block from top to bottom.
    /// </summary>
    Layout,
}
