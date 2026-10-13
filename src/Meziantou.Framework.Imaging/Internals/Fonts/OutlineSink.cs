namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Receives the outline of a glyph: closed contours of lines and Bézier curves, in font units with the y axis up.</summary>
internal abstract class OutlineSink
{
    /// <summary>Starts a contour.</summary>
    public abstract void MoveTo(double x, double y);

    /// <summary>Adds a line from the current point.</summary>
    public abstract void LineTo(double x, double y);

    /// <summary>Adds a quadratic Bézier curve from the current point.</summary>
    public abstract void QuadraticTo(double controlX, double controlY, double x, double y);

    /// <summary>Adds a cubic Bézier curve from the current point.</summary>
    public abstract void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y);

    /// <summary>Ends the contour, whose current point is its first point.</summary>
    public abstract void Close();
}
