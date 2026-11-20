using Meziantou.Framework.Pdf.Layout;

namespace Meziantou.Framework.Pdf;

/// <summary>Builds the cells of a row of a table of a flow document.</summary>
public sealed class PdfFlowRowBuilder
{
    private readonly PdfFlowOptions _options;

    internal PdfFlowRowBuilder(PdfFlowOptions options)
    {
        _options = options;
    }

    internal List<FlowCell> Cells { get; } = [];

    /// <summary>Adds a cell that holds a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="columnSpan">The number of columns the cell spans.</param>
    /// <param name="rowSpan">The number of rows the cell spans.</param>
    /// <returns>The builder.</returns>
    public PdfFlowRowBuilder Cell(string text, int columnSpan = 1, int rowSpan = 1)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Cell(content => content.Paragraph(text, new PdfParagraphStyle { SpaceAfter = 0 }), columnSpan, rowSpan);
    }

    /// <summary>Adds a cell that holds blocks: paragraphs, lists, images, nested tables...</summary>
    /// <param name="content">The function that adds the blocks of the cell.</param>
    /// <param name="columnSpan">The number of columns the cell spans.</param>
    /// <param name="rowSpan">The number of rows the cell spans.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A span is not positive.</exception>
    public PdfFlowRowBuilder Cell(Action<PdfFlowContainer> content, int columnSpan = 1, int rowSpan = 1)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columnSpan);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowSpan);
        var container = new PdfFlowContainer(_options);
        content(container);
        Cells.Add(new FlowCell(container.Blocks, columnSpan, rowSpan));
        return this;
    }
}
