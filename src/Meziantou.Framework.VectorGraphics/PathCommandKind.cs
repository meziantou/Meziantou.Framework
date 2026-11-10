namespace Meziantou.Framework.VectorGraphics;

/// <summary>The kind of a <see cref="PathCommand"/>.</summary>
public enum PathCommandKind
{
    /// <summary>Starts a new contour at a point.</summary>
    MoveTo,

    /// <summary>Draws a straight line to a point.</summary>
    LineTo,

    /// <summary>Draws a cubic Bézier curve to a point, with two control points.</summary>
    CubicTo,

    /// <summary>Draws a quadratic Bézier curve to a point, with one control point.</summary>
    QuadraticTo,

    /// <summary>Draws an elliptical arc to a point, as the SVG <c>A</c> command.</summary>
    ArcTo,

    /// <summary>Closes the contour with a straight line to its start.</summary>
    Close,
}
