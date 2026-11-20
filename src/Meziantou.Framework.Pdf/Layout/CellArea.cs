namespace Meziantou.Framework.Pdf.Layout;

/// <summary>The content of a cell of a table: it starts at 0 and grows down without bound; the commands are placed with the cell.</summary>
internal sealed class CellArea : FlowArea
{
    public CellArea(double width, double fullHeight)
    {
        ColumnWidth = Math.Max(1, width);
        FullHeight = fullHeight;
        Y = 0;
    }

    public List<DrawCommand> Commands { get; } = [];

    /// <summary>Gets the headings of the cell, at their top in the cell: their page is known when the cell is placed.</summary>
    public List<(int Level, string Text, double Top)> Headings { get; } = [];

    public override double Bottom => double.NegativeInfinity;

    public override bool CanBreak => false;

    public override int PageIndex => -1;

    public override double FullHeight { get; }

    public override void Add(DrawCommand command) => Commands.Add(command);

    public override void Break()
    {
    }
}
