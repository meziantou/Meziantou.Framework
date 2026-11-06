namespace Meziantou.Framework.Charts;

/// <summary>Maps the values of a position channel (x or y) to positions. When a plot has no scale for a channel, it infers one from the values of its marks.</summary>
public abstract class Scale
{
    private protected Scale()
    {
    }

    /// <summary>Gets or sets a value that indicates whether the scale is reversed: the first value is at the end of the axis.</summary>
    public bool Reverse { get; set; }
}
