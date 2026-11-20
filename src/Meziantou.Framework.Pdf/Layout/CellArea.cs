namespace Meziantou.Framework.Pdf;

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

    public override double Bottom => double.NegativeInfinity;

    public override bool CanBreak => false;

    public override int PageIndex => -1;

    public override double FullHeight { get; }

    public override void Add(DrawCommand command) => Commands.Add(command);

    public override void Break()
    {
    }
}
