namespace Meziantou.Framework.Diagrams;

/// <summary>An item of a block diagram, which takes one or more columns of its row: a block or a space.</summary>
public abstract class BlockDiagramItem
{
    private int _span = 1;

    private protected BlockDiagramItem()
    {
    }

    /// <summary>Gets or sets the number of columns the item takes. The default is 1 (Mermaid <c>id:2</c>).</summary>
    public int Span
    {
        get => _span;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _span = value;
        }
    }
}
