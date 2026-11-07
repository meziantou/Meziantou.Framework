namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// The point of the box of a text, along y, that <see cref="TextLayout.GetOrigin(PointD, TextHorizontalAnchor, TextVerticalAnchor, TextBounds)"/>
/// puts at an anchor.
/// </summary>
public enum TextVerticalAnchor
{
    /// <summary>The top of the box.</summary>
    Top = 0,

    /// <summary>The middle of the box.</summary>
    Center = 1,

    /// <summary>The baseline of the first line (<see cref="TextLayout.Baseline"/>), whatever box is measured. A vertical layout has none.</summary>
    Baseline = 2,

    /// <summary>The bottom of the box.</summary>
    Bottom = 3,
}
