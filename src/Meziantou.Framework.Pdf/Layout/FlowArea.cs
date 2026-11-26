namespace Meziantou.Framework.Pdf.Layout;

/// <summary>
/// Where the blocks of a flow document are laid out: the columns of the pages, which break to the next column or page,
/// or a cell of a table, which grows without bound or breaks between the parts of a row split between columns. <see cref="Y"/> is the top of the next block; the coordinates are
/// those of the page (the y axis goes up).
/// </summary>
internal abstract class FlowArea
{
    /// <summary>Gets the left of the content: the column, after the indentation of the enclosing blocks (the bodies of the list items).</summary>
    public double Left => ColumnLeft + Indent;

    public double Width => Math.Max(1, ColumnWidth - Indent);

    /// <summary>Gets or sets the indentation of the content from the left of the column, kept when the content breaks to the next column.</summary>
    public double Indent { get; set; }

    protected double ColumnLeft { get; set; }

    protected double ColumnWidth { get; set; }

    public double Y { get; set; }

    /// <summary>Gets a value indicating whether nothing was placed since the top of the column: a block that does not fit is placed anyway, rather than breaking to an empty column.</summary>
    public bool AtTop { get; set; } = true;

    public abstract double Bottom { get; }

    public double Available => Y - Bottom;

    public abstract bool CanBreak { get; }

    /// <summary>Gets the index of the page, or -1 for a cell: the place of the headings, for the outline and the table of contents.</summary>
    public abstract int PageIndex { get; }

    /// <summary>Gets the height of the content of a full column: a block taller than it is scaled down or split.</summary>
    public abstract double FullHeight { get; }

    /// <summary>Moves to the top of the next column, or of a new page.</summary>
    public abstract void Break();

    public abstract void Add(DrawCommand command);

    /// <summary>
    /// Gets the height available in the column the area would be in after a number of breaks (0: the rest of the current
    /// column), and whether that column is the first one of a new page, where the header rows of a table are repeated.
    /// </summary>
    public abstract (double Height, bool NewPage) PeekBreaks(int count);

    /// <summary>Breaks when a block of a height does not fit and is not the first one of the column.</summary>
    public void EnsureSpace(double height)
    {
        if (height > Available && CanBreak && !AtTop)
        {
            Break();
        }
    }

    /// <summary>
    /// Gets the lowest <see cref="Y"/> a block reached, without the spaces after the blocks: for a cell that grows without
    /// bound, the bottom of its content without the space after its last block, which a column can leave out.
    /// </summary>
    public double ContentBottom { get; private set; }

    /// <summary>Moves down after a block.</summary>
    public void Advance(double height)
    {
        Y -= height;
        ContentBottom = Math.Min(ContentBottom, Y);
        AtTop = false;
    }
}
