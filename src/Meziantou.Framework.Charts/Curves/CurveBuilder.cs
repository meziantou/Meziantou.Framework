namespace Meziantou.Framework.Charts;

/// <summary>Appends the points of a line to a path with a <see cref="Curve"/>, as the curves of d3-shape (ISC license).</summary>
internal static class CurveBuilder
{
    private const double Epsilon = 1e-12;

    /// <summary>Appends a curve through the points.</summary>
    /// <param name="builder">The path.</param>
    /// <param name="points">The points, in order.</param>
    /// <param name="curve">The curve.</param>
    /// <param name="connect">Whether the curve continues the current contour with a line to its first point, instead of starting a new contour.</param>
    /// <param name="closed">Whether the curve is closed: the last point joins the first.</param>
    public static void Append(PathBuilder builder, IReadOnlyList<(double X, double Y)> points, Curve curve, bool connect = false, bool closed = false)
    {
        if (points.Count is 0)
            return;

        if (connect)
        {
            builder.LineTo(points[0].X, points[0].Y);
        }
        else
        {
            builder.MoveTo(points[0].X, points[0].Y);
        }

        switch (curve)
        {
            case Curve.MonotoneX when points.Count > 2 && !closed:
                AppendMonotoneX(builder, points);
                break;
            case Curve.CatmullRom when points.Count > 2:
                AppendCatmullRom(builder, points, closed);
                break;
            case Curve.Step or Curve.StepBefore or Curve.StepAfter when !closed:
                AppendStep(builder, points, curve switch { Curve.StepBefore => 0, Curve.StepAfter => 1, _ => 0.5 });
                break;
            default:
                for (var i = 1; i < points.Count; i++)
                {
                    builder.LineTo(points[i].X, points[i].Y);
                }

                break;
        }

        if (closed)
        {
            builder.Close();
        }
    }

    private static void AppendStep(PathBuilder builder, IReadOnlyList<(double X, double Y)> points, double t)
    {
        for (var i = 1; i < points.Count; i++)
        {
            var (x0, y0) = points[i - 1];
            var (x, y) = points[i];
            if (t <= 0)
            {
                builder.LineTo(x0, y);
            }
            else if (t >= 1)
            {
                builder.LineTo(x, y0);
            }
            else
            {
                var middle = (x0 * (1 - t)) + (x * t);
                builder.LineTo(middle, y0);
                builder.LineTo(middle, y);
            }

            builder.LineTo(x, y);
        }
    }

    /// <summary>The tangents of d3's <c>curveMonotoneX</c> (Steffen 1990).</summary>
    internal static double[] GetMonotoneTangents(IReadOnlyList<(double X, double Y)> points)
    {
        var n = points.Count;
        var tangents = new double[n];
        for (var i = 1; i < n - 1; i++)
        {
            tangents[i] = Slope3(points[i - 1], points[i], points[i + 1]);
        }

        tangents[0] = Slope2(points[0], points[1], tangents[1]);
        tangents[n - 1] = Slope2(points[n - 2], points[n - 1], tangents[n - 2]);
        return tangents;

        static double Slope3((double X, double Y) p0, (double X, double Y) p1, (double X, double Y) p2)
        {
            var h0 = p1.X - p0.X;
            var h1 = p2.X - p1.X;
            var s0 = (p1.Y - p0.Y) / h0;
            var s1 = (p2.Y - p1.Y) / h1;
            var p = ((s0 * h1) + (s1 * h0)) / (h0 + h1);
            var result = (Sign(s0) + Sign(s1)) * Math.Min(Math.Min(Math.Abs(s0), Math.Abs(s1)), 0.5 * Math.Abs(p));
            return double.IsFinite(result) ? result : 0;
        }

        static double Slope2((double X, double Y) p0, (double X, double Y) p1, double t)
        {
            var h = p1.X - p0.X;
            return h != 0 ? ((3 * (p1.Y - p0.Y) / h) - t) / 2 : t;
        }

        static int Sign(double value) => value > 0 ? 1 : value < 0 ? -1 : 0;
    }

    private static void AppendMonotoneX(PathBuilder builder, IReadOnlyList<(double X, double Y)> points)
    {
        var tangents = GetMonotoneTangents(points);
        for (var i = 0; i < points.Count - 1; i++)
        {
            var (x0, y0) = points[i];
            var (x1, y1) = points[i + 1];
            var dx = (x1 - x0) / 3;
            builder.CubicTo(x0 + dx, y0 + (dx * tangents[i]), x1 - dx, y1 - (dx * tangents[i + 1]), x1, y1);
        }
    }

    private static void AppendCatmullRom(PathBuilder builder, IReadOnlyList<(double X, double Y)> points, bool closed)
    {
        var n = points.Count;
        for (var i = 0; i < (closed ? n : n - 1); i++)
        {
            var p1 = points[i];
            var p2 = points[(i + 1) % n];
            (double X, double Y)? p0 = closed ? points[(i - 1 + n) % n] : i > 0 ? points[i - 1] : null;
            (double X, double Y)? p3 = closed ? points[(i + 2) % n] : i + 2 < n ? points[i + 2] : null;
            AppendCatmullRomSegment(builder, p0, p1, p2, p3);
        }
    }

    private static void AppendCatmullRomSegment(PathBuilder builder, (double X, double Y)? previous, (double X, double Y) p1, (double X, double Y) p2, (double X, double Y)? next)
    {
        const double Alpha = 0.5;
        var l12_2a = Math.Pow(Distance2(p1, p2), Alpha);
        var l12_a = Math.Sqrt(l12_2a);
        var c1 = p1;
        var c2 = p2;
        if (previous is { } p0)
        {
            var l01_2a = Math.Pow(Distance2(p0, p1), Alpha);
            var l01_a = Math.Sqrt(l01_2a);
            if (l01_a > Epsilon)
            {
                var a = (2 * l01_2a) + (3 * l01_a * l12_a) + l12_2a;
                var n = 3 * l01_a * (l01_a + l12_a);
                c1 = (((p1.X * a) - (p0.X * l12_2a) + (p2.X * l01_2a)) / n, ((p1.Y * a) - (p0.Y * l12_2a) + (p2.Y * l01_2a)) / n);
            }
        }

        if (next is { } p3)
        {
            var l23_2a = Math.Pow(Distance2(p2, p3), Alpha);
            var l23_a = Math.Sqrt(l23_2a);
            if (l23_a > Epsilon)
            {
                var b = (2 * l23_2a) + (3 * l23_a * l12_a) + l12_2a;
                var m = 3 * l23_a * (l23_a + l12_a);
                c2 = (((p2.X * b) + (p1.X * l23_2a) - (p3.X * l12_2a)) / m, ((p2.Y * b) + (p1.Y * l23_2a) - (p3.Y * l12_2a)) / m);
            }
        }

        builder.CubicTo(c1.X, c1.Y, c2.X, c2.Y, p2.X, p2.Y);

        static double Distance2((double X, double Y) a, (double X, double Y) b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            return (dx * dx) + (dy * dy);
        }
    }
}
