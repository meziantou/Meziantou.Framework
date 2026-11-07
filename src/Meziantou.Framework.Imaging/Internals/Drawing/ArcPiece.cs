using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An arc of at most a quarter turn of the ellipse <c>center + x u + y v</c>, for the points <c>(x, y)</c> of the unit
/// circle from <c>a</c> to <c>b</c> by the shortest way. It starts at the current point of the path and ends exactly at
/// its end point: both are given, not computed, so that the segments around the arc meet it without a gap.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct ArcPiece
{
    /// <summary>The number of coordinates of a piece in a path.</summary>
    public const int CoordinateCount = 12;

    public ArcPiece(double centerX, double centerY, double ux, double uy, double vx, double vy, double ax, double ay, double bx, double by, double endX, double endY, bool isContinuation)
    {
        CenterX = centerX;
        CenterY = centerY;
        Ux = ux;
        Uy = uy;
        Vx = vx;
        Vy = vy;
        Ax = ax;
        Ay = ay;
        Bx = bx;
        By = by;
        EndX = endX;
        EndY = endY;
        IsContinuation = isContinuation;
    }

    /// <summary>Reads a piece from the coordinates of a path.</summary>
    public ArcPiece(ReadOnlySpan<double> coordinates, bool isContinuation)
        : this(coordinates[0], coordinates[1], coordinates[2], coordinates[3], coordinates[4], coordinates[5], coordinates[6], coordinates[7], coordinates[8], coordinates[9], coordinates[10], coordinates[11], isContinuation)
    {
    }

    public double CenterX { get; }

    public double CenterY { get; }

    public double Ux { get; }

    public double Uy { get; }

    public double Vx { get; }

    public double Vy { get; }

    public double Ax { get; }

    public double Ay { get; }

    public double Bx { get; }

    public double By { get; }

    public double EndX { get; }

    public double EndY { get; }

    /// <summary>Gets a value indicating whether the piece continues the arc of the piece before it: the vertex between them is not a corner of the path.</summary>
    public bool IsContinuation { get; }

    /// <summary>
    /// Gets the point where the tangents at both ends of the piece meet: the piece lies inside the triangle of its start,
    /// this point and its end, as a quadratic Bézier curve lies inside the triangle of its control points.
    /// </summary>
    public void GetCorner(out double x, out double y)
    {
        // The tangents of the unit circle at a and b meet at (a + b) / (1 + a.b); a quarter turn at most keeps the
        // divisor at 1 or more
        var divisor = 1 + ((Ax * Bx) + (Ay * By));
        if (!(divisor >= 0.5))
        {
            divisor = 0.5;
        }

        var px = (Ax + Bx) / divisor;
        var py = (Ay + By) / divisor;
        x = CenterX + ((Ux * px) + (Vx * py));
        y = CenterY + ((Uy * px) + (Vy * py));
    }
}
