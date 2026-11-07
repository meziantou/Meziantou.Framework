using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A light at a point, that shines in every direction: <c>fePointLight</c> of SVG.</summary>
/// <remarks>The vector toward the light from a pixel at <c>(x, y)</c> is <c>(lx - x, ly - y, lz - surfaceScale A(x, y))</c>, normalized.</remarks>
public sealed class PointLight : FilterLightSource
{
    /// <summary>Initializes a new instance of the <see cref="PointLight"/> class.</summary>
    /// <param name="x">The x of the light, in the drawing.</param>
    /// <param name="y">The y of the light, in the drawing.</param>
    /// <param name="z">The height of the light above the plane, in units of the drawing.</param>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public PointLight(double x, double y, double z)
    {
        DrawingGuard.Coordinate(x, nameof(x));
        DrawingGuard.Coordinate(y, nameof(y));
        DrawingGuard.Coordinate(z, nameof(z));
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the x of the light.</summary>
    public double X { get; }

    /// <summary>Gets the y of the light.</summary>
    public double Y { get; }

    /// <summary>Gets the height of the light above the plane.</summary>
    public double Z { get; }

    internal override FilterLight Place(FilterProcessor processor)
    {
        var (x, y) = processor.ToCanvas(X, Y);
        return new FilterLight { Kind = FilterLightKind.Point, X = x, Y = y, Z = Z * Math.Sqrt(processor.ScaleX * processor.ScaleY) };
    }
}
