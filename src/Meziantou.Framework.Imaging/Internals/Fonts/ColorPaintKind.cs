namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The kinds of paints of a color glyph.</summary>
internal enum ColorPaintKind
{
    /// <summary>One color.</summary>
    Solid,

    /// <summary>A gradient along a line: three numbers, the origin and the vector the position is projected on, divided by its squared length.</summary>
    Linear,

    /// <summary>A gradient between two circles: their centers and radii.</summary>
    Radial,

    /// <summary>A gradient around a point: the center, and the angles of the start and of the end of the color line, in half turns.</summary>
    Sweep,

    /// <summary>The image of the glyph: the left and the top of the image, and the width and the height of one of its pixels.</summary>
    Image,
}
