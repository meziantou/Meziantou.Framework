namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Argument validation shared by the drawing API: every coordinate that reaches the rasterizer is finite and bounded.</summary>
internal static class DrawingGuard
{
    /// <summary>
    /// The largest accepted magnitude of a coordinate or of a length (2^24). Within this range, squares and products of
    /// coordinates stay far from overflow and positions keep at least 2^-28 pixel of precision.
    /// </summary>
    public const double MaxCoordinate = 16777216;

    /// <summary>Validates a coordinate.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of the value exceeds <see cref="MaxCoordinate"/>.</exception>
    public static void Coordinate(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The coordinate must be a finite number.", paramName);

        if (Math.Abs(value) > MaxCoordinate)
            throw new ArgumentOutOfRangeException(paramName, value, "The magnitude of the coordinate must not exceed 16777216.");
    }

    /// <summary>Validates both coordinates of a point.</summary>
    public static void Point(PointD value, string paramName)
    {
        Coordinate(value.X, paramName);
        Coordinate(value.Y, paramName);
    }

    /// <summary>Validates the four edges of a rectangle.</summary>
    public static void Rectangle(RectangleD value, string paramName)
    {
        Coordinate(value.X, paramName);
        Coordinate(value.Y, paramName);
        Coordinate(value.Right, paramName);
        Coordinate(value.Bottom, paramName);
    }

    /// <summary>Validates an angle.</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    public static void Angle(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The angle must be a finite number.", paramName);
    }

    /// <summary>Validates a length that may be zero (a radius).</summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds <see cref="MaxCoordinate"/>.</exception>
    public static void Length(double value, string paramName)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The length must be a finite number.", paramName);

        if (value < 0 || value > MaxCoordinate)
            throw new ArgumentOutOfRangeException(paramName, value, "The length must be between 0 and 16777216.");
    }
}
