using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>An affine transform: a point (x, y) becomes (A x + C y + E, B x + D y + F).</summary>
/// <param name="A">The scale along x.</param>
/// <param name="B">The skew of y by x.</param>
/// <param name="C">The skew of x by y.</param>
/// <param name="D">The scale along y.</param>
/// <param name="E">The translation along x.</param>
/// <param name="F">The translation along y.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneTransform(double A, double B, double C, double D, double E, double F)
{
    /// <summary>Gets the transform that changes nothing.</summary>
    public static SceneTransform Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>Gets a value that indicates whether the transform only translates.</summary>
    public bool IsTranslation => A == 1 && B == 0 && C == 0 && D == 1;

    /// <summary>Creates a translation.</summary>
    /// <param name="x">The translation along x.</param>
    /// <param name="y">The translation along y.</param>
    /// <returns>The transform.</returns>
    public static SceneTransform Translate(double x, double y) => new(1, 0, 0, 1, x, y);

    /// <summary>Creates a rotation around a point.</summary>
    /// <param name="degrees">The angle, in degrees, clockwise.</param>
    /// <param name="centerX">The x coordinate of the center of the rotation.</param>
    /// <param name="centerY">The y coordinate of the center of the rotation.</param>
    /// <returns>The transform.</returns>
    public static SceneTransform Rotate(double degrees, double centerX = 0, double centerY = 0)
    {
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new(cos, sin, -sin, cos, centerX - (cos * centerX) + (sin * centerY), centerY - (sin * centerX) - (cos * centerY));
    }

    /// <summary>Applies the transform to a point.</summary>
    /// <param name="x">The x coordinate of the point.</param>
    /// <param name="y">The y coordinate of the point.</param>
    /// <returns>The transformed point.</returns>
    public (double X, double Y) Apply(double x, double y) => ((A * x) + (C * y) + E, (B * x) + (D * y) + F);
}
