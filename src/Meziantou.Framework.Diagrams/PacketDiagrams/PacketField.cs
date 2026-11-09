namespace Meziantou.Framework.Diagrams;

/// <summary>A field of a packet diagram: a name over a range of bits.</summary>
public sealed class PacketField
{
    /// <summary>Initializes a new instance of the <see cref="PacketField"/> class.</summary>
    /// <param name="start">The first bit.</param>
    /// <param name="end">The last bit, included.</param>
    /// <param name="label">The name of the field.</param>
    public PacketField(int start, int end, DiagramText label)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        ArgumentNullException.ThrowIfNull(label);
        Start = start;
        End = end;
        Label = label;
    }

    /// <summary>Gets the first bit.</summary>
    public int Start { get; }

    /// <summary>Gets the last bit, included.</summary>
    public int End { get; }

    /// <summary>Gets the number of bits.</summary>
    public int Bits => End - Start + 1;

    /// <summary>Gets or sets the name of the field.</summary>
    public DiagramText Label { get; set; }
}
