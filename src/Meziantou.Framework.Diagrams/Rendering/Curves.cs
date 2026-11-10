using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The curves of d3 that Mermaid draws its edges with, through the points of the layout.</summary>
internal static class Curves
{
    public static PathData Build(IReadOnlyList<LayoutPoint> points, EdgeCurve curve)
    {
        var builder = new PathBuilder();
        if (points.Count is 0)
            return builder.Build();

        builder.MoveTo(points[0].X, points[0].Y);
        if (points.Count == 1)
            return builder.Build();

        if (points.Count == 2 && curve is not (EdgeCurve.Step or EdgeCurve.StepBefore or EdgeCurve.StepAfter or EdgeCurve.BumpX or EdgeCurve.BumpY))
        {
            builder.LineTo(points[1].X, points[1].Y);
            return builder.Build();
        }

        switch (curve)
        {
            case EdgeCurve.Basis:
                Basis(builder, points);
                break;
            case EdgeCurve.Step:
                Step(builder, points, 0.5);
                break;
            case EdgeCurve.StepBefore:
                Step(builder, points, 0);
                break;
            case EdgeCurve.StepAfter:
                Step(builder, points, 1);
                break;
            case EdgeCurve.Cardinal:
                Cardinal(builder, points);
                break;
            case EdgeCurve.CatmullRom:
                CatmullRom(builder, points);
                break;
            case EdgeCurve.MonotoneX:
                Monotone(builder, points, swap: false);
                break;
            case EdgeCurve.MonotoneY:
                Monotone(builder, points, swap: true);
                break;
            case EdgeCurve.Natural:
                Natural(builder, points);
                break;
            case EdgeCurve.BumpX:
                for (var i = 1; i < points.Count; i++)
                {
                    var middle = (points[i - 1].X + points[i].X) / 2;
                    builder.CubicTo(middle, points[i - 1].Y, middle, points[i].Y, points[i].X, points[i].Y);
                }

                break;
            case EdgeCurve.BumpY:
                for (var i = 1; i < points.Count; i++)
                {
                    var middle = (points[i - 1].Y + points[i].Y) / 2;
                    builder.CubicTo(points[i - 1].X, middle, points[i].X, middle, points[i].X, points[i].Y);
                }

                break;
            default:
                for (var i = 1; i < points.Count; i++)
                {
                    builder.LineTo(points[i].X, points[i].Y);
                }

                break;
        }

        return builder.Build();
    }

    private static void Basis(PathBuilder builder, IReadOnlyList<LayoutPoint> points)
    {
        // d3.curveBasis: a straight start to the first sixth, B-spline segments, and a straight end
        var x0 = points[0].X;
        var y0 = points[0].Y;
        var x1 = points[1].X;
        var y1 = points[1].Y;
        builder.LineTo(((5 * x0) + x1) / 6, ((5 * y0) + y1) / 6);
        for (var i = 2; i < points.Count; i++)
        {
            var x = points[i].X;
            var y = points[i].Y;
            Point(x, y);
            x0 = x1;
            y0 = y1;
            x1 = x;
            y1 = y;
        }

        Point(x1, y1);
        builder.LineTo(x1, y1);

        void Point(double x, double y)
        {
            builder.CubicTo(((2 * x0) + x1) / 3, ((2 * y0) + y1) / 3, (x0 + (2 * x1)) / 3, (y0 + (2 * y1)) / 3, (x0 + (4 * x1) + x) / 6, (y0 + (4 * y1) + y) / 6);
        }
    }

    private static void Step(PathBuilder builder, IReadOnlyList<LayoutPoint> points, double t)
    {
        var previous = points[0];
        for (var i = 1; i < points.Count; i++)
        {
            var point = points[i];
            if (t <= 0)
            {
                builder.LineTo(previous.X, point.Y);
                builder.LineTo(point.X, point.Y);
            }
            else
            {
                var x = (previous.X * (1 - t)) + (point.X * t);
                builder.LineTo(x, previous.Y);
                builder.LineTo(x, point.Y);
            }

            previous = point;
        }

        if (t is > 0 and < 1)
        {
            builder.LineTo(previous.X, previous.Y);
        }
    }

    private static void Cardinal(PathBuilder builder, IReadOnlyList<LayoutPoint> points)
    {
        const double K = 1.0 / 6;
        for (var i = 1; i < points.Count; i++)
        {
            var p0 = points[Math.Max(0, i - 2)];
            var p1 = points[i - 1];
            var p2 = points[i];
            var p3 = points[Math.Min(points.Count - 1, i + 1)];
            if (i == 1)
            {
                p0 = p2;
            }

            if (i == points.Count - 1)
            {
                p3 = p1;
            }

            builder.CubicTo(p1.X + (K * (p2.X - p0.X)), p1.Y + (K * (p2.Y - p0.Y)), p2.X + (K * (p1.X - p3.X)), p2.Y + (K * (p1.Y - p3.Y)), p2.X, p2.Y);
        }
    }

    private static void CatmullRom(PathBuilder builder, IReadOnlyList<LayoutPoint> points)
    {
        const double Alpha = 0.5;
        for (var i = 1; i < points.Count; i++)
        {
            var p0 = points[Math.Max(0, i - 2)];
            var p1 = points[i - 1];
            var p2 = points[i];
            var p3 = points[Math.Min(points.Count - 1, i + 1)];
            var d1 = i == 1 ? 0 : Math.Pow(Geometry.Distance(p0, p1), Alpha);
            var d2 = Math.Pow(Geometry.Distance(p1, p2), Alpha);
            var d3 = i == points.Count - 1 ? 0 : Math.Pow(Geometry.Distance(p2, p3), Alpha);

            var c1 = p1;
            if (d1 > 1e-6)
            {
                var a = (2 * d1 * d1) + (3 * d1 * d2) + (d2 * d2);
                var n = 3 * d1 * (d1 + d2);
                c1 = new LayoutPoint(((p1.X * a) - (p0.X * d2 * d2) + (p2.X * d1 * d1)) / n, ((p1.Y * a) - (p0.Y * d2 * d2) + (p2.Y * d1 * d1)) / n);
            }

            var c2 = p2;
            if (d3 > 1e-6)
            {
                var b = (2 * d3 * d3) + (3 * d3 * d2) + (d2 * d2);
                var m = 3 * d3 * (d3 + d2);
                c2 = new LayoutPoint(((p2.X * b) + (p1.X * d3 * d3) - (p3.X * d2 * d2)) / m, ((p2.Y * b) + (p1.Y * d3 * d3) - (p3.Y * d2 * d2)) / m);
            }

            builder.CubicTo(c1.X, c1.Y, c2.X, c2.Y, p2.X, p2.Y);
        }
    }

    private static void Natural(PathBuilder builder, IReadOnlyList<LayoutPoint> points)
    {
        var xs = ControlPoints([.. points.Select(point => point.X)]);
        var ys = ControlPoints([.. points.Select(point => point.Y)]);
        for (var i = 1; i < points.Count; i++)
        {
            builder.CubicTo(xs.A[i - 1], ys.A[i - 1], xs.B[i - 1], ys.B[i - 1], points[i].X, points[i].Y);
        }

        static (double[] A, double[] B) ControlPoints(double[] x)
        {
            var n = x.Length - 1;
            var a = new double[n];
            var b = new double[n];
            var r = new double[n];
            a[0] = 0;
            b[0] = 2;
            r[0] = x[0] + (2 * x[1]);
            for (var i = 1; i < n - 1; i++)
            {
                a[i] = 1;
                b[i] = 4;
                r[i] = (4 * x[i]) + (2 * x[i + 1]);
            }

            a[n - 1] = 2;
            b[n - 1] = 7;
            r[n - 1] = (8 * x[n - 1]) + x[n];
            for (var i = 1; i < n; i++)
            {
                var m = a[i] / b[i - 1];
                b[i] -= m;
                r[i] -= m * r[i - 1];
            }

            a[n - 1] = r[n - 1] / b[n - 1];
            for (var i = n - 2; i >= 0; i--)
            {
                a[i] = (r[i] - a[i + 1]) / b[i];
            }

            b[n - 1] = (x[n] + a[n - 1]) / 2;
            for (var i = 0; i < n - 1; i++)
            {
                b[i] = (2 * x[i + 1]) - a[i + 1];
            }

            return (a, b);
        }
    }

    private static void Monotone(PathBuilder builder, IReadOnlyList<LayoutPoint> source, bool swap)
    {
        // d3.curveMonotoneX (Steffen's method); monotoneY is the same curve in transposed coordinates
        var points = new List<LayoutPoint>(source.Count);
        foreach (var point in source)
        {
            var p = swap ? new LayoutPoint(point.Y, point.X) : point;
            if (points.Count > 0 && points[^1] == p)
                continue;

            points.Add(p);
        }

        if (points.Count < 2)
            return;

        if (points.Count == 2)
        {
            LineTo(points[1]);
            return;
        }

        var tangents = new double[points.Count];
        for (var i = 1; i < points.Count - 1; i++)
        {
            tangents[i] = Slope3(points[i - 1], points[i], points[i + 1]);
        }

        tangents[0] = Slope2(points[0], points[1], tangents[1]);
        tangents[^1] = Slope2(points[^2], points[^1], tangents[^2]);
        for (var i = 1; i < points.Count; i++)
        {
            var p0 = points[i - 1];
            var p1 = points[i];
            var dx = (p1.X - p0.X) / 3;
            CubicTo(new(p0.X + dx, p0.Y + (dx * tangents[i - 1])), new(p1.X - dx, p1.Y - (dx * tangents[i])), p1);
        }

        void LineTo(LayoutPoint point)
        {
            if (swap)
            {
                builder.LineTo(point.Y, point.X);
            }
            else
            {
                builder.LineTo(point.X, point.Y);
            }
        }

        void CubicTo(LayoutPoint c1, LayoutPoint c2, LayoutPoint end)
        {
            if (swap)
            {
                builder.CubicTo(c1.Y, c1.X, c2.Y, c2.X, end.Y, end.X);
            }
            else
            {
                builder.CubicTo(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
            }
        }

        static double Slope3(LayoutPoint p0, LayoutPoint p1, LayoutPoint p2)
        {
            var h0 = p1.X - p0.X;
            var h1 = p2.X - p1.X;
            if (h0 == 0 || h1 == 0 || h0 + h1 == 0)
                return 0;

            var s0 = (p1.Y - p0.Y) / h0;
            var s1 = (p2.Y - p1.Y) / h1;
            var p = ((s0 * h1) + (s1 * h0)) / (h0 + h1);
            var value = (Math.Sign(s0 < 0 ? -1 : 1) + Math.Sign(s1 < 0 ? -1 : 1)) * Math.Min(Math.Min(Math.Abs(s0), Math.Abs(s1)), 0.5 * Math.Abs(p));
            return double.IsFinite(value) ? value : 0;
        }

        static double Slope2(LayoutPoint p0, LayoutPoint p1, double t)
        {
            var h = p1.X - p0.X;
            if (h == 0)
                return t;

            var value = ((3 * (p1.Y - p0.Y) / h) - t) / 2;
            return double.IsFinite(value) ? value : 0;
        }
    }
}
