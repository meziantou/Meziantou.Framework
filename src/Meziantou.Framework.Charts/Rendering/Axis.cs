namespace Meziantou.Framework.Charts;

/// <summary>The options of the axis of a position scale.</summary>
public sealed class Axis
{
    /// <summary>Gets or sets a value that indicates whether the axis is drawn, or <see langword="null"/> for the default: drawn, except for the charts that do not use the scale as a measure (pies, hierarchies, funnels).</summary>
    public bool? Visible { get; set; }

    /// <summary>Gets or sets the label of the axis, or <see langword="null"/> for none.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets a value that indicates whether grid lines are drawn at the ticks, or <see langword="null"/> for the default: on for the axis of the y scale when it is continuous (in polar coordinates, on for both axes), off otherwise.</summary>
    public bool? Grid { get; set; }

    /// <summary>Gets or sets the rotation of the tick labels in degrees, or <see langword="null"/> to rotate them by -45 degrees only when they overlap.</summary>
    public double? TickRotation { get; set; }

    /// <summary>Gets or sets the length of the tick marks. The default is 5.</summary>
    public double TickSize { get; set; } = 5;

    /// <summary>Gets or sets a value that indicates whether the line of the axis is drawn, or <see langword="null"/> for the default: drawn for the horizontal axis of Cartesian coordinates.</summary>
    public bool? Line { get; set; }
}
