using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A packet diagram: the fields of a network packet or of a register, drawn over their bits, in rows of
/// <see cref="BitsPerRow"/> bits (Mermaid <c>packet</c>).
/// </summary>
public sealed class PacketDiagram : Diagram
{
    private int _bitsPerRow = 32;
    private double _bitWidth = 32;
    private double _rowHeight = 32;

    /// <summary>Gets the fields, in the order of their bits. A field starts after the previous one.</summary>
    public IList<PacketField> Fields { get; } = new List<PacketField>();

    /// <summary>Gets or sets the number of bits of a row. The default is 32.</summary>
    public int BitsPerRow
    {
        get => _bitsPerRow;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1024);
            _bitsPerRow = value;
        }
    }

    /// <summary>Gets or sets the width of a bit, in pixels. The default is 32.</summary>
    public double BitWidth
    {
        get => _bitWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _bitWidth = value;
        }
    }

    /// <summary>Gets or sets the height of a row, in pixels. The default is 32.</summary>
    public double RowHeight
    {
        get => _rowHeight;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _rowHeight = value;
        }
    }

    /// <summary>Gets or sets a value that indicates whether the numbers of the first and the last bits of the fields are drawn. The default is <see langword="true"/>.</summary>
    public bool ShowBits { get; set; } = true;

    /// <summary>
    /// Gets or sets a value that indicates whether each row is drawn from its highest bit on the left to its lowest on the
    /// right, as hardware registers are; otherwise, from its lowest bit, as network packets are (Mermaid <c>bitOrder</c>).
    /// </summary>
    public bool DescendingBitOrder { get; set; }

    /// <summary>Adds a field after the last one.</summary>
    /// <param name="bits">The number of bits.</param>
    /// <param name="label">The name of the field.</param>
    /// <returns>The field.</returns>
    public PacketField AddField(int bits, DiagramText label)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bits, 1);
        var start = Fields.Count is 0 ? 0 : Fields[^1].End + 1;
        var field = new PacketField(start, start + bits - 1, label);
        Fields.Add(field);
        return field;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => PacketDiagramRenderer.Render(this, context, group);
}
