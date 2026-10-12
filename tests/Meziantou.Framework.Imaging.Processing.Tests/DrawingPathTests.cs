using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Building paths: contours, the current point, bounds, validation, and the equivalence of the shortcuts of the canvas.</summary>
public sealed class DrawingPathTests
{
    private static readonly Brush White = new SolidBrush(new Rgba32(255, 255, 255));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AnEmptyPathDrawsNothing()
    {
        var path = new VectorPathBuilder().Build();
        Assert.True(path.IsEmpty);
        Assert.Equal(RectangleD.Empty, path.Bounds);
        using var image = new Image<Gray8>(2, 2, new Gray8(0));
        image.Draw(
            canvas =>
            {
                canvas.Fill(White, path);
                canvas.Stroke(new Pen(White, 3) { LineCap = LineCap.Round }, path);
            },
            Ct);
        Assert.Equal(new int[4], DrawingTestSupport.ReadSamples(image.Frames[0]));
    }

    [Fact]
    public void BoundsContainEveryPointAndControlPoint()
    {
        var builder = new VectorPathBuilder();
        Assert.Equal(new RectangleD(3, -2, 0, 0), builder.MoveTo(3, -2).Build().Bounds);
        Assert.False(builder.Build().IsEmpty);
        Assert.Equal(RectangleD.FromLTRB(3, -2, 5, 1), builder.LineTo(5, 1).Build().Bounds);

        // The control points count, whether the curve reaches them or not
        Assert.Equal(RectangleD.FromLTRB(-4, -2, 5, 9), builder.QuadraticBezierTo(-4, 9, 0, 0).Build().Bounds);
        Assert.Equal(RectangleD.FromLTRB(-4, -7, 12, 9), builder.CubicBezierTo(12, 0, 0, -7, 1, 1).Build().Bounds);
        Assert.Equal(RectangleD.FromLTRB(-4, -7, 12, 9), builder.Close().Build().Bounds);

        Assert.Equal(RectangleD.FromLTRB(7, 8.5, 13, 11.5), new VectorPathBuilder().AddEllipse(new PointD(10, 10), 3, 1.5).Build().Bounds);
        Assert.Equal(RectangleD.FromLTRB(-2, -2, 2, 2), new VectorPathBuilder().AddCircle(default, 2).Build().Bounds);
        Assert.Equal(RectangleD.FromLTRB(1, 2, 4, 6), new VectorPathBuilder().AddRectangle(RectangleD.FromLTRB(1, 2, 4, 6)).Build().Bounds);
        Assert.Equal(RectangleD.FromLTRB(-1, 0, 6, 5), new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(6, 2), new PointD(-1, 5)]).Build().Bounds);
    }

    [Fact]
    public void ASegmentNeedsACurrentPoint()
    {
        Assert.Throws<InvalidOperationException>(() => new VectorPathBuilder().LineTo(1, 1));
        Assert.Throws<InvalidOperationException>(() => new VectorPathBuilder().LineTo(new PointD(1, 1)));
        Assert.Throws<InvalidOperationException>(() => new VectorPathBuilder().QuadraticBezierTo(1, 1, 2, 2));
        Assert.Throws<InvalidOperationException>(() => new VectorPathBuilder().QuadraticBezierTo(new PointD(1, 1), new PointD(2, 2)));
        Assert.Throws<InvalidOperationException>(() => new VectorPathBuilder().CubicBezierTo(1, 1, 2, 2, 3, 3));
        Assert.Throws<InvalidOperationException>(() => new VectorPathBuilder().CubicBezierTo(new PointD(1, 1), new PointD(2, 2), new PointD(3, 3)));

        // Closing without a contour does nothing
        Assert.True(new VectorPathBuilder().Close().Build().IsEmpty);

        // After Clear, the builder is as new
        var builder = new VectorPathBuilder().MoveTo(1, 1).LineTo(2, 2);
        builder.Clear();
        Assert.True(builder.Build().IsEmpty);
        Assert.Equal(RectangleD.Empty, builder.Build().Bounds);
        Assert.Throws<InvalidOperationException>(() => builder.LineTo(1, 1));
    }

    [Fact]
    public void ASegmentAfterAClosedContourStartsANewContourAtItsFirstPoint()
    {
        // A closed triangle, then a line from its first point: the same as an explicit MoveTo there
        var implicitStart = new VectorPathBuilder().MoveTo(1, 1).LineTo(5, 1).LineTo(5, 4).Close().LineTo(1, 6).Build();
        var explicitStart = new VectorPathBuilder().MoveTo(1, 1).LineTo(5, 1).LineTo(5, 4).Close().MoveTo(1, 1).LineTo(1, 6).Build();
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 1.5) { LineCap = LineCap.Square }, implicitStart), canvas => canvas.Stroke(new Pen(White, 1.5) { LineCap = LineCap.Square }, explicitStart));

        // The same after a rectangle, a polygon, a circle (which starts at the right end of its horizontal diameter)
        var afterRectangle = new VectorPathBuilder().AddRectangle(new Rectangle(1, 1, 3, 2)).LineTo(6, 6).Build();
        var afterRectangleExplicit = new VectorPathBuilder().AddRectangle(new Rectangle(1, 1, 3, 2)).MoveTo(1, 1).LineTo(6, 6).Build();
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 1), afterRectangle), canvas => canvas.Stroke(new Pen(White, 1), afterRectangleExplicit));

        var afterPolygon = new VectorPathBuilder().AddPolygon([new PointD(2, 1), new PointD(6, 2), new PointD(3, 5)]).LineTo(1, 7).Build();
        var afterPolygonExplicit = new VectorPathBuilder().AddPolygon([new PointD(2, 1), new PointD(6, 2), new PointD(3, 5)]).MoveTo(2, 1).LineTo(1, 7).Build();
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 1), afterPolygon), canvas => canvas.Stroke(new Pen(White, 1), afterPolygonExplicit));

        var afterCircle = new VectorPathBuilder().AddCircle(new PointD(3, 3), 2).LineTo(7, 7).Build();
        var afterCircleExplicit = new VectorPathBuilder().AddCircle(new PointD(3, 3), 2).MoveTo(5, 3).LineTo(7, 7).Build();
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 1), afterCircle), canvas => canvas.Stroke(new Pen(White, 1), afterCircleExplicit));
    }

    [Fact]
    public void ABuiltPathDoesNotChangeWithItsBuilder()
    {
        var builder = new VectorPathBuilder().AddRectangle(new Rectangle(0, 0, 2, 2));
        var first = builder.Build();
        var second = builder.AddRectangle(new Rectangle(4, 4, 2, 2)).Build();
        Assert.Equal(new RectangleD(0, 0, 2, 2), first.Bounds);
        Assert.Equal(new RectangleD(0, 0, 6, 6), second.Bounds);
        builder.Clear();

        using var image = new Image<Gray8>(8, 8, new Gray8(0));
        image.Draw(canvas => canvas.Fill(White, first), Ct);
        Assert.Equal(new Gray8(255), image.Frames[0][1, 1]);
        Assert.Equal(new Gray8(0), image.Frames[0][5, 5]);
        image.Draw(canvas => canvas.Fill(White, second), Ct);
        Assert.Equal(new Gray8(255), image.Frames[0][5, 5]);
    }

    [Fact]
    public void TheShortcutsOfTheCanvasDrawTheSamePixelsAsAPath()
    {
        var rectangle = RectangleD.FromLTRB(1.25, 0.5, 6.75, 5.125);
        var center = new PointD(4.2, 3.9);
        var pen = new Pen(White, 1.75) { LineJoin = LineJoin.Bevel, LineCap = LineCap.Round };
        (PointD A, PointD B, PointD C, PointD D) points = (new(0.5, 7), new(3, -2), new(6, 9), new(7.5, 1));
        AssertSamePixels(canvas => canvas.FillRectangle(White, rectangle), canvas => canvas.Fill(White, new VectorPathBuilder().AddRectangle(rectangle).Build()));
        AssertSamePixels(canvas => canvas.StrokeRectangle(pen, rectangle), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddRectangle(rectangle).Build()));
        AssertSamePixels(canvas => canvas.FillEllipse(White, center, 3.1, 2.2), canvas => canvas.Fill(White, new VectorPathBuilder().AddEllipse(center, 3.1, 2.2).Build()));
        AssertSamePixels(canvas => canvas.StrokeEllipse(pen, center, 3.1, 2.2), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddEllipse(center, 3.1, 2.2).Build()));
        AssertSamePixels(canvas => canvas.FillCircle(White, center, 2.6), canvas => canvas.Fill(White, new VectorPathBuilder().AddCircle(center, 2.6).Build()));
        AssertSamePixels(canvas => canvas.FillCircle(White, center, 2.6), canvas => canvas.FillEllipse(White, center, 2.6, 2.6));
        AssertSamePixels(canvas => canvas.StrokeCircle(pen, center, 2.6), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddCircle(center, 2.6).Build()));
        AssertSamePixels(canvas => canvas.StrokeLine(pen, points.A, points.D), canvas => canvas.Stroke(pen, new VectorPathBuilder().MoveTo(points.A).LineTo(points.D).Build()));
        AssertSamePixels(canvas => canvas.StrokeBezier(pen, points.A, points.B, points.D), canvas => canvas.Stroke(pen, new VectorPathBuilder().MoveTo(points.A).QuadraticBezierTo(points.B, points.D).Build()));
        AssertSamePixels(canvas => canvas.StrokeBezier(pen, points.A, points.B, points.C, points.D), canvas => canvas.Stroke(pen, new VectorPathBuilder().MoveTo(points.A).CubicBezierTo(points.B, points.C, points.D).Build()));

        // The overloads taking points and those taking coordinates
        AssertSamePixels(
            canvas => canvas.Fill(White, new VectorPathBuilder().MoveTo(points.A).LineTo(points.B).QuadraticBezierTo(points.C, points.D).CubicBezierTo(points.B, points.C, points.A).Build()),
            canvas => canvas.Fill(White, new VectorPathBuilder().MoveTo(0.5, 7).LineTo(3, -2).QuadraticBezierTo(6, 9, 7.5, 1).CubicBezierTo(3, -2, 6, 9, 0.5, 7).Build()));

        // A polygon and the same contour segment by segment
        AssertSamePixels(
            canvas => canvas.Fill(White, new VectorPathBuilder().AddPolygon([points.A, points.B, points.C]).Build()),
            canvas => canvas.Fill(White, new VectorPathBuilder().MoveTo(points.A).LineTo(points.B).LineTo(points.C).Close().Build()));
    }

    [Fact]
    public void ShapesWithoutAreaAreNotFilledAndAreStrokedAsLinesOrPoints()
    {
        var pen = new Pen(White, 2) { LineCap = LineCap.Square };
        using var image = new Image<Gray8>(6, 6, new Gray8(0));
        image.Draw(
            canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(1, 1, 0, 4));
                canvas.FillRectangle(White, new RectangleD(1, 1, 4, 0));
                canvas.FillCircle(White, new PointD(3, 3), 0);
                canvas.FillEllipse(White, new PointD(3, 3), 2, 0);
                canvas.Fill(White, new VectorPathBuilder().MoveTo(1, 1).LineTo(5, 5).Build());
                canvas.Fill(White, new VectorPathBuilder().MoveTo(1, 1).Build());
            },
            Ct);
        Assert.Equal(new int[36], DrawingTestSupport.ReadSamples(image.Frames[0]));

        // A circle without radius is a point: the square cap of a pen of width 2 draws the square around it
        image.Draw(canvas => canvas.StrokeCircle(pen, new PointD(3, 3), 0), Ct);
        Assert.Equal(new Gray8(255), image.Frames[0][2, 2]);
        Assert.Equal(new Gray8(255), image.Frames[0][3, 3]);
        Assert.Equal(new Gray8(0), image.Frames[0][1, 1]);
        Assert.Equal(new Gray8(0), image.Frames[0][4, 4]);
    }

    [Fact]
    public void CoordinatesAreValidatedWhenThePathIsBuilt()
    {
        const double TooFar = 16777217;
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(invalid, 0));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(new PointD(0, invalid)));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).LineTo(0, invalid));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).LineTo(new PointD(invalid, 0)));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).QuadraticBezierTo(invalid, 0, 1, 1));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).QuadraticBezierTo(new PointD(0, 0), new PointD(1, invalid)));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).CubicBezierTo(0, 0, 1, invalid, 1, 1));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).CubicBezierTo(new PointD(0, 0), new PointD(1, 1), new PointD(invalid, 1)));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddEllipse(new PointD(invalid, 0), 1, 1));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddEllipse(default, invalid, 1));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddCircle(default, invalid));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddPolygon([default, new PointD(1, invalid)]));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().MoveTo(TooFar, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().MoveTo(0, 0).LineTo(0, -TooFar));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().MoveTo(0, 0).QuadraticBezierTo(0, 0, TooFar, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().MoveTo(0, 0).CubicBezierTo(0, 0, 0, TooFar, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddRectangle(new RectangleD(16777216, 0, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddEllipse(default, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddEllipse(default, 1, TooFar));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddCircle(default, -0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddPolygon([new PointD(0, TooFar)]));
        Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddPolygon([]));

        // A rejected element leaves the builder as it was
        var builder = new VectorPathBuilder().MoveTo(1, 1).LineTo(2, 2);
        Assert.Throws<ArgumentException>(() => builder.LineTo(double.NaN, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddPolygon([new PointD(5, 5), new PointD(TooFar, 0)]));
        Assert.Equal(RectangleD.FromLTRB(1, 1, 2, 2), builder.Build().Bounds);

        // The largest coordinates are accepted and drawn
        var huge = new VectorPathBuilder().AddRectangle(RectangleD.FromLTRB(-16777216, -16777216, 16777216, 16777216)).Build();
        using var image = new Image<Gray8>(2, 2, new Gray8(0));
        image.Draw(canvas => canvas.Fill(White, huge), Ct);
        Assert.Equal([255, 255, 255, 255], DrawingTestSupport.ReadSamples(image.Frames[0]));
    }

    [Fact]
    public void ATransformedPathIsThePathOfTheTransformedPoints()
    {
        var path = new VectorPathBuilder().MoveTo(1, 1).LineTo(3, 1).QuadraticBezierTo(4, 2, 3, 3).CubicBezierTo(2.5, 4, 1.5, 4, 1, 3).Close().Build();
        Assert.Same(path, path.Transform(AffineTransform.Identity));
        Assert.Same(path, path.Transform(default));

        // Every coefficient is exact in binary, so the transformed control points are those of the second path exactly
        var transform = new AffineTransform(1.5, 0.25, -0.5, 1, 2, -0.5);
        var expected = new VectorPathBuilder()
            .MoveTo(transform.Transform(new PointD(1, 1)))
            .LineTo(transform.Transform(new PointD(3, 1)))
            .QuadraticBezierTo(transform.Transform(new PointD(4, 2)), transform.Transform(new PointD(3, 3)))
            .CubicBezierTo(transform.Transform(new PointD(2.5, 4)), transform.Transform(new PointD(1.5, 4)), transform.Transform(new PointD(1, 3)))
            .Close()
            .Build();
        var transformed = path.Transform(transform);
        Assert.Equal(expected.Bounds, transformed.Bounds);
        AssertSamePixels(canvas => canvas.Fill(White, expected), canvas => canvas.Fill(White, transformed));
        AssertSamePixels(canvas => canvas.Stroke(new Pen(White, 1.25), expected), canvas => canvas.Stroke(new Pen(White, 1.25), transformed));

        // The path itself is unchanged
        Assert.Equal(RectangleD.FromLTRB(1, 1, 4, 4), path.Bounds);
        Assert.True(new VectorPathBuilder().Build().Transform(transform).IsEmpty);
    }

    [Fact]
    public void ATransformedEllipseStaysAnEllipse()
    {
        var ellipse = new VectorPathBuilder().AddEllipse(new PointD(2, 3), 1.5, 1).Build();

        // Scaled and moved, with its axes still horizontal and vertical: the ellipse of the scaled radii, bit for bit
        var scaled = ellipse.Transform(AffineTransform.CreateScale(2, 1.5).Then(AffineTransform.CreateTranslation(-0.5, 0.25)));
        var expected = new VectorPathBuilder().AddEllipse(new PointD(3.5, 4.75), 3, 1.5).Build();
        Assert.Equal(expected.Bounds, scaled.Bounds);
        Assert.Equal(Flatten(expected), Flatten(scaled));

        // Turned by a quarter turn: the same vertices as the ellipse whose radii are exchanged, from another start
        var turned = Flatten(ellipse.Transform(AffineTransform.CreateRotation(90, new PointD(2, 3))));
        var exchanged = Flatten(new VectorPathBuilder().AddEllipse(new PointD(2, 3), 1, 1.5).Build());
        Assert.Equal((2d, 4.5d), turned[0]);
        Assert.HasCount(exchanged.Count, turned);
        Assert.Equal([.. exchanged.Distinct().Order()], [.. turned.Distinct().Order()]);

        // Any transformation: the vertices are on the transformed ellipse, and the chords within the tolerance of it
        foreach (var transform in new[]
        {
            AffineTransform.CreateRotation(30, new PointD(1, 1)),
            AffineTransform.CreateSkew(40, -15).Then(AffineTransform.CreateScale(60, 25)),
            AffineTransform.CreateScale(-900, 700),
            new AffineTransform(3, 1, 1, 2, -4, 5),
        })
        {
            Assert.True(transform.TryInvert(out var inverse));
            var points = Flatten(ellipse.Transform(transform));
            Assert.Equal(points[0], points[^1]);
            var start = transform.Transform(new PointD(3.5, 3));
            Assert.Equal(start.X, points[0].X, 1e-9);
            Assert.Equal(start.Y, points[0].Y, 1e-9);
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var vertex = inverse.Transform(new PointD(points[i].X, points[i].Y));
                Assert.Equal(1, (((vertex.X - 2) / 1.5) * ((vertex.X - 2) / 1.5)) + ((vertex.Y - 3) * (vertex.Y - 3)), 1e-9);

                // The middle of a chord, brought back onto the ellipse along the ray from the center
                var middle = inverse.Transform(new PointD((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2));
                var angle = Math.Atan2(middle.Y - 3, (middle.X - 2) / 1.5);
                var onEllipse = transform.Transform(new PointD(2 + (1.5 * Math.Cos(angle)), 3 + Math.Sin(angle)));
                var chordMiddle = transform.Transform(middle);
                Assert.True(onEllipse.DistanceTo(chordMiddle) <= Internals.PathFlattener.Tolerance, $"{transform}, chord {i}: {onEllipse.DistanceTo(chordMiddle)}");
            }
        }

        // Transforming twice is transforming by the combination
        var first = AffineTransform.CreateRotation(30);
        var second = new AffineTransform(2, 0.5, -1, 3, 4, 5);
        var twice = Flatten(ellipse.Transform(first).Transform(second));
        var once = Flatten(ellipse.Transform(first.Then(second)));
        Assert.HasCount(once.Count, twice);
        for (var i = 0; i < once.Count; i++)
        {
            Assert.Equal(once[i].X, twice[i].X, 1e-9);
            Assert.Equal(once[i].Y, twice[i].Y, 1e-9);
        }
    }

    [Fact]
    public void ATransformedPathMustStayWithinTheCoordinateLimit()
    {
        var path = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 10, 10)).AddCircle(new PointD(5, 5), 3).Build();
        Assert.Equal(RectangleD.FromLTRB(0, 0, 16777216, 10), path.Transform(AffineTransform.CreateScale(1677721.6, 1)).Bounds);
        Assert.Throws<ArgumentOutOfRangeException>(() => path.Transform(AffineTransform.CreateScale(1677722, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => path.Transform(AffineTransform.CreateTranslation(0, -16777217)));
        Assert.Throws<ArgumentOutOfRangeException>(() => path.Transform(AffineTransform.CreateScale(1e300, 1e300)));

        // A transformation that flattens the plane gives a path without area
        var flat = path.Transform(AffineTransform.CreateScale(1, 0));
        Assert.Equal(new RectangleD(0, 0, 10, 0), flat.Bounds);
        using var image = new Image<Gray8>(12, 4, new Gray8(0));
        image.Draw(canvas => canvas.Fill(White, flat), Ct);
        Assert.Equal(new int[48], DrawingTestSupport.ReadSamples(image.Frames[0]));
    }

    /// <summary>Gets the vertices of the single contour a path is flattened to.</summary>
    private static List<(double X, double Y)> Flatten(VectorPath path)
    {
        var sink = new VertexSink();
        Internals.PathFlattener.Flatten(path, sink);
        return sink.Points;
    }

    private static void AssertSamePixels(DrawingAction first, DrawingAction second)
    {
        using var left = new Image<Gray8>(8, 8, new Gray8(0));
        using var right = new Image<Gray8>(8, 8, new Gray8(0));
        left.Draw(first, Ct);
        right.Draw(second, Ct);
        var expected = DrawingTestSupport.ReadSamples(left.Frames[0]);
        Assert.Contains(expected, sample => sample != 0);
        Assert.Equal(expected, DrawingTestSupport.ReadSamples(right.Frames[0]));
    }

    private sealed class VertexSink : Internals.PathSink
    {
        public List<(double X, double Y)> Points { get; } = [];

        public override void MoveTo(double x, double y) => Points.Add((x, y));

        public override void LineTo(double x, double y, bool smooth) => Points.Add((x, y));

        public override void Close(bool smooth)
        {
        }

        public override void End()
        {
        }
    }
}
