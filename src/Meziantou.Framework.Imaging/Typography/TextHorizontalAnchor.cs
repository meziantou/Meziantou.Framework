namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// The point of the box of a text, along x, that <see cref="TextLayout.GetOrigin(PointD, TextHorizontalAnchor, TextVerticalAnchor, TextBounds)"/>
/// puts at an anchor. The sides are those of the image, whatever the direction of the text.
/// </summary>
public enum TextHorizontalAnchor
{
    /// <summary>The left side of the box.</summary>
    Left = 0,

    /// <summary>The middle of the box.</summary>
    Center = 1,

    /// <summary>The right side of the box.</summary>
    Right = 2,
}
