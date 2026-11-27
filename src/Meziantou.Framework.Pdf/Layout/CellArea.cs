namespace Meziantou.Framework.Pdf.Layout;

/// <summary>
/// The content of a cell of a table, in parts whose coordinates start at 0 at their top and grow down; the commands are
/// placed with the cell. A cell is first laid out in one part that grows without bound, which gives the height of its
/// row; the cells of a row taller than a column are laid out again in the parts of the columns the row is split between,
/// of known heights, and break from one part to the next as the pages do.
/// </summary>
internal sealed class CellArea : FlowArea
{
    private readonly Func<int, double>? _partHeight;

    public CellArea(double width, double fullHeight)
    {
        ColumnWidth = Math.Max(1, width);
        FullHeight = fullHeight;
        Y = 0;
    }

    /// <summary>Initializes a new instance of the <see cref="CellArea"/> class whose content breaks between parts of the given heights.</summary>
    public CellArea(double width, double fullHeight, Func<int, double> partHeight, bool atTop)
        : this(width, fullHeight)
    {
        _partHeight = partHeight;
        AtTop = atTop;
    }

    /// <summary>Gets the commands of each part of the cell, in the coordinates of the part.</summary>
    public List<List<DrawCommand>> Parts { get; } = [[]];

    /// <summary>Gets the headings of the cell, at their top in their part: their page is known when the cell is placed.</summary>
    public List<(int Level, string Text, double Top, int Part)> Headings { get; } = [];

    public int PartIndex => Parts.Count - 1;

    public override double Bottom => _partHeight is null ? double.NegativeInfinity : -_partHeight(PartIndex);

    public override bool CanBreak => _partHeight is not null;

    public override int PageIndex => -1;

    public override double FullHeight { get; }

    public override void Add(DrawCommand command) => Parts[^1].Add(command);

    public override void Break()
    {
        if (_partHeight is null)
            return;

        Parts.Add([]);
        Y = 0;
        AtTop = true;
    }

    public override (double Height, bool NewPage) PeekBreaks(int count)
    {
        if (_partHeight is null)
            return (double.PositiveInfinity, false);

        return count <= 0 ? (Available, false) : (_partHeight(PartIndex + count), false);
    }
}
