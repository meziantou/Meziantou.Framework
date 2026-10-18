namespace Meziantou.Framework.Fonts;

/// <summary>
/// The trigonometric functions the paints of a color glyph need, computed with additions, multiplications, divisions
/// and square roots only: the functions of <see cref="Math"/> are those of the platform, and their last bits differ
/// from one platform to another.
/// </summary>
/// <remarks>
/// Angles are in half turns (1 is 180 degrees), the unit of the angles of the COLR table. The results are accurate to
/// a few units of the last place: the argument is folded into an eighth of a turn, where a short series converges to
/// the precision of a double.
/// </remarks>
internal static class DeterministicAngle
{
    private const double Pi = 3.14159265358979323846;

    /// <summary>Gets the sine and the cosine of an angle in half turns.</summary>
    public static void SinCos(double halfTurns, out double sin, out double cos)
    {
        if (!double.IsFinite(halfTurns))
        {
            sin = 0;
            cos = 1;
            return;
        }

        // The angle in [0, 2), then the quarter turn it is in and what is left of it, in [-1/4, 1/4].
        var turn = halfTurns - (2 * Math.Floor(halfTurns / 2));
        var quarter = Math.Floor((turn * 2) + 0.5);
        var x = (turn - (quarter / 2)) * Pi;
        var x2 = x * x;

        // The Taylor series of the sine and of the cosine around zero, to the terms in x^17 and x^16.
        var s = x * (1 + (x2 * ((-1.0 / 6) + (x2 * ((1.0 / 120) + (x2 * ((-1.0 / 5040) + (x2 * ((1.0 / 362880) + (x2 * ((-1.0 / 39916800) + (x2 * ((1.0 / 6227020800) + (x2 * ((-1.0 / 1307674368000) + (x2 * (1.0 / 355687428096000)))))))))))))))));
        var c = 1 + (x2 * ((-1.0 / 2) + (x2 * ((1.0 / 24) + (x2 * ((-1.0 / 720) + (x2 * ((1.0 / 40320) + (x2 * ((-1.0 / 3628800) + (x2 * ((1.0 / 479001600) + (x2 * ((-1.0 / 87178291200) + (x2 * (1.0 / 20922789888000))))))))))))))));
        switch ((int)quarter & 3)
        {
            case 0:
                sin = s;
                cos = c;
                break;

            case 1:
                sin = c;
                cos = -s;
                break;

            case 2:
                sin = -s;
                cos = -c;
                break;

            default:
                sin = -c;
                cos = s;
                break;
        }
    }

    /// <summary>Gets the tangent of an angle in half turns; a very large number where it is not defined.</summary>
    public static double Tan(double halfTurns)
    {
        SinCos(halfTurns, out var sin, out var cos);
        return cos == 0 ? (sin < 0 ? -1e300 : 1e300) : sin / cos;
    }

    /// <summary>
    /// Gets the angle of a vector from the positive x axis, counter-clockwise when the y axis points up, in half turns:
    /// from 0 (included) to 2 (excluded). The vector (0, 0) has the angle 0.
    /// </summary>
    public static double Atan2(double y, double x)
    {
        var absoluteX = Math.Abs(x);
        var absoluteY = Math.Abs(y);
        if (!(absoluteX > 0) && !(absoluteY > 0))
            return 0;

        // The angle of the vector folded into the first eighth of a turn: the arc tangent of a ratio in [0, 1].
        var ratio = absoluteX >= absoluteY ? absoluteY / absoluteX : absoluteX / absoluteY;

        // Halving the angle twice brings the ratio under 0.2, where the series converges in a few terms:
        // atan(r) = 2 atan(r / (1 + sqrt(1 + r^2))).
        var r = ratio / (1 + Math.Sqrt(1 + (ratio * ratio)));
        r /= 1 + Math.Sqrt(1 + (r * r));
        var r2 = r * r;
        var series = r * (1 + (r2 * ((-1.0 / 3) + (r2 * ((1.0 / 5) + (r2 * ((-1.0 / 7) + (r2 * ((1.0 / 9) + (r2 * ((-1.0 / 11) + (r2 * ((1.0 / 13) + (r2 * ((-1.0 / 15) + (r2 * ((1.0 / 17) + (r2 * ((-1.0 / 19) + (r2 * (1.0 / 21)))))))))))))))))))));
        var angle = 4 * series / Pi;

        // Unfold: the other eighth of the quadrant, then the quadrant.
        if (absoluteY > absoluteX)
        {
            angle = 0.5 - angle;
        }

        if (x < 0)
        {
            angle = 1 - angle;
        }

        if (y < 0)
        {
            angle = 2 - angle;
        }

        return angle >= 2 ? 0 : angle;
    }
}
