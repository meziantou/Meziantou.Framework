namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>The predefined spot functions of the halftones of type 1 (ISO 32000-2, 10.6.3, Table 128), of the coordinates of a pixel in its cell.</summary>
internal static class SpotFunctions
{
    /// <summary>Gets a predefined spot function by its name; <see langword="null"/> for an unknown name.</summary>
    public static Func<double, double, double>? Get(string name) => name switch
    {
        "SimpleDot" => (x, y) => 1 - ((x * x) + (y * y)),
        "InvertedSimpleDot" => (x, y) => (x * x) + (y * y) - 1,
        "DoubleDot" => (x, y) => (SinDegrees(360 * x) + SinDegrees(360 * y)) / 2,
        "InvertedDoubleDot" => (x, y) => -(SinDegrees(360 * x) + SinDegrees(360 * y)) / 2,
        "CosineDot" => (x, y) => (CosDegrees(180 * x) + CosDegrees(180 * y)) / 2,
        "Double" => (x, y) => (SinDegrees(360 * x / 2) + SinDegrees(360 * y)) / 2,
        "InvertedDouble" => (x, y) => -(SinDegrees(360 * x / 2) + SinDegrees(360 * y)) / 2,
        "Line" => (x, y) => -Math.Abs(y),
        "LineX" => (x, y) => x,
        "LineY" => (x, y) => y,
        "Round" => Round,
        "Ellipse" => Ellipse,
        "EllipseA" => (x, y) => 1 - ((x * x) + (0.9 * y * y)),
        "InvertedEllipseA" => (x, y) => (x * x) + (0.9 * y * y) - 1,
        "EllipseB" => (x, y) => 1 - Math.Sqrt((x * x) + (5.0 / 8 * y * y)),
        "EllipseC" => (x, y) => 1 - ((0.9 * x * x) + (y * y)),
        "InvertedEllipseC" => (x, y) => (0.9 * x * x) + (y * y) - 1,
        "Square" => (x, y) => -Math.Max(Math.Abs(x), Math.Abs(y)),
        "Cross" => (x, y) => -Math.Min(Math.Abs(x), Math.Abs(y)),
        "Rhomboid" => (x, y) => ((0.9 * Math.Abs(x)) + Math.Abs(y)) / 2,
        "Diamond" => Diamond,
        _ => null,
    };

    private static double Round(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        if (x + y <= 1)
            return 1 - ((x * x) + (y * y));

        return ((x - 1) * (x - 1)) + ((y - 1) * (y - 1)) - 1;
    }

    private static double Ellipse(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        var w = (3 * x) + (4 * y) - 3;
        if (w < 0)
            return 1 - (((x * x) + Square(y / 0.75)) / 4);

        if (w > 1)
            return ((Square(1 - x) + Square((1 - y) / 0.75)) / 4) - 1;

        return 0.5 - w;
    }

    private static double Diamond(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        if (x + y <= 0.75)
            return 1 - ((x * x) + (y * y));

        if (x + y <= 1.23)
            return 1 - ((0.85 * x) + y);

        return ((x - 1) * (x - 1)) + ((y - 1) * (y - 1)) - 1;
    }

    private static double Square(double value) => value * value;

    private static double SinDegrees(double degrees) => Math.Sin(degrees * Math.PI / 180);

    private static double CosDegrees(double degrees) => Math.Cos(degrees * Math.PI / 180);
}
