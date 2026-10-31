using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Building paths: contours, the current point, bounds, validation, and the equivalence of the shortcuts of the canvas.</summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
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

    [Fact]
    public void AnArcBetweenTwoPointsFollowsItsFlags()
    {
        // From (0, 5) to (10, 5) with a radius of 5: half the circle around (5, 5). Clockwise on the image is over the top.
        var over = Flatten(new VectorPathBuilder().MoveTo(0, 5).ArcTo(5, 5, 0, largeArc: false, clockwise: true, 10, 5).Build());
        Assert.Equal((0d, 5d), over[0]);
        Assert.Equal((10d, 5d), over[^1]);
        Assert.Contains((5d, 0d), over);
        Assert.All(over, point => Assert.Equal(5, Math.Sqrt(((point.X - 5) * (point.X - 5)) + ((point.Y - 5) * (point.Y - 5))), 1e-12));
        Assert.All(over, point => Assert.True(point.Y <= 5));

        var under = Flatten(new VectorPathBuilder().MoveTo(0, 5).ArcTo(5, 5, 0, largeArc: true, clockwise: false, new PointD(10, 5)).Build());
        Assert.Equal((10d, 5d), under[^1]);
        Assert.Contains((5d, 10d), under);
        Assert.All(under, point => Assert.True(point.Y >= 5));

        // With a radius of sqrt(50), two circles pass through (0, 0) and (10, 0): around (5, 5) and around (5, -5)
        var radius = Math.Sqrt(50);
        foreach (var (largeArc, clockwise, centerY, extremeY) in new[]
        {
            (false, true, 5d, 5 - radius),
            (false, false, -5d, radius - 5),
            (true, true, -5d, -5 - radius),
            (true, false, 5d, 5 + radius),
        })
        {
            var points = Flatten(new VectorPathBuilder().MoveTo(0, 0).ArcTo(radius, radius, 0, largeArc, clockwise, 10, 0).Build());
            Assert.Equal((0d, 0d), points[0]);
            Assert.Equal((10d, 0d), points[^1]);
            Assert.All(points, point => Assert.Equal(radius, Math.Sqrt(((point.X - 5) * (point.X - 5)) + ((point.Y - centerY) * (point.Y - centerY))), 1e-9));
            Assert.Equal(extremeY, clockwise ? points.Min(point => point.Y) : points.Max(point => point.Y), 1e-9);
            Assert.Equal(largeArc, points.Any(point => point.X < -1) && points.Any(point => point.X > 11));
        }
    }

    [Fact]
    public void AnArcBetweenTwoPointsEnlargesRadiiThatAreTooSmallAndTurnsWithItsRotation()
    {
        // Radii of 1 cannot join two points 10 apart: they become 5
        var enlarged = Flatten(new VectorPathBuilder().MoveTo(0, 5).ArcTo(1, 1, 0, largeArc: false, clockwise: true, 10, 5).Build());
        Assert.Equal((10d, 5d), enlarged[^1]);
        Assert.All(enlarged, point => Assert.Equal(5, Math.Sqrt(((point.X - 5) * (point.X - 5)) + ((point.Y - 5) * (point.Y - 5))), 1e-9));

        // In proportion: radii of 1 and 2 become 5 and 10
        var ellipse = Flatten(new VectorPathBuilder().MoveTo(0, 5).ArcTo(1, 2, 0, largeArc: false, clockwise: true, 10, 5).Build());
        Assert.All(ellipse, point => Assert.Equal(1, (((point.X - 5) / 5) * ((point.X - 5) / 5)) + (((point.Y - 5) / 10) * ((point.Y - 5) / 10)), 1e-9));
        Assert.Equal(-5, ellipse.Min(point => point.Y), 1e-9);

        // An ellipse turned by a quarter turn is the ellipse whose radii are exchanged
        var turned = Flatten(new VectorPathBuilder().MoveTo(1, 2).ArcTo(3, 7, 90, largeArc: true, clockwise: false, 4, 6).Build());
        var exchanged = Flatten(new VectorPathBuilder().MoveTo(1, 2).ArcTo(7, 3, 0, largeArc: true, clockwise: false, 4, 6).Build());
        Assert.HasCount(exchanged.Count, turned);
        for (var i = 0; i < turned.Count; i++)
        {
            Assert.Equal(exchanged[i].X, turned[i].X, 1e-9);
            Assert.Equal(exchanged[i].Y, turned[i].Y, 1e-9);
        }

        // Without a radius the arc is a straight segment; to the current point it is nothing
        Assert.Equal([(1d, 2d), (4d, 6d)], Flatten(new VectorPathBuilder().MoveTo(1, 2).ArcTo(0, 5, 0, largeArc: false, clockwise: true, 4, 6).Build()));
        Assert.Equal([(1d, 2d), (4d, 6d)], Flatten(new VectorPathBuilder().MoveTo(1, 2).ArcTo(5, 0, 30, largeArc: true, clockwise: true, 4, 6).Build()));
        Assert.Equal([(1d, 2d)], Flatten(new VectorPathBuilder().MoveTo(1, 2).ArcTo(5, 5, 0, largeArc: true, clockwise: true, 1, 2).Build()));

        // An arc continues the contour: the next segment starts at its end
        Assert.Equal((7d, 7d), Flatten(new VectorPathBuilder().MoveTo(0, 5).ArcTo(5, 5, 0, largeArc: false, clockwise: true, 10, 5).LineTo(7, 7).Build())[^1]);
    }

    [Fact]
    public void AnArcAroundACenterStartsAtItsFirstAngleAndTurnsClockwiseWhenItsSweepIsPositive()
    {
        // A quarter of the ellipse of radii 4 and 2 around (10, 10), from the right of the center to under it
        var quarter = Flatten(new VectorPathBuilder().AddArc(new PointD(10, 10), 4, 2, 0, 90).Build());
        Assert.Equal((14d, 10d), quarter[0]);
        Assert.Equal((10d, 12d), quarter[^1]);
        Assert.All(quarter, point => Assert.Equal(1, (((point.X - 10) / 4) * ((point.X - 10) / 4)) + (((point.Y - 10) / 2) * ((point.Y - 10) / 2)), 1e-12));
        Assert.All(quarter, point => Assert.True(point.X >= 10 && point.Y >= 10));
        Assert.Equal(RectangleD.FromLTRB(10, 10, 14, 12), new VectorPathBuilder().AddArc(new PointD(10, 10), 4, 2, 0, 90).Build().Bounds);

        // The other way
        var back = Flatten(new VectorPathBuilder().AddArc(new PointD(10, 10), 4, 2, 0, -90).Build());
        Assert.Equal((10d, 8d), back[^1]);
        Assert.All(back, point =>
        {
            Assert.True(point.X >= 10);
            Assert.True(point.Y <= 10);
        });

        // A full turn, or more, is the whole ellipse and ends where it starts
        var full = Flatten(new VectorPathBuilder().AddArc(new PointD(10, 10), 4, 2, 30, 360).Build());
        Assert.Equal(full[0], full[^1]);
        Assert.Equal(full, Flatten(new VectorPathBuilder().AddArc(new PointD(10, 10), 4, 2, 30, 900).Build()));
        Assert.Equal(6, full.Min(point => point.X), 0.01);
        Assert.Equal(14, full.Max(point => point.X), 0.01);
        Assert.Equal(8, full.Min(point => point.Y), 0.01);
        Assert.Equal(12, full.Max(point => point.Y), 0.01);

        // An arc without sweep is its first point
        Assert.Equal([(14d, 10d)], Flatten(new VectorPathBuilder().AddArc(new PointD(10, 10), 4, 2, 0, 0).Build()));

        // ArcTo joins the current point to the start of the arc, and starts a contour when there is no current point
        var joined = Flatten(new VectorPathBuilder().MoveTo(1, 1).ArcTo(new PointD(10, 10), 4, 2, 0, 90).Build());
        Assert.Equal([(1d, 1d), (14d, 10d)], joined[..2]);
        Assert.Equal(quarter, joined[1..]);
        Assert.Equal(quarter, Flatten(new VectorPathBuilder().ArcTo(new PointD(10, 10), 4, 2, 0, 90).Build()));

        // A pie is the center, the arc, and the two radii that join them
        var pie = new VectorPathBuilder().AddPie(new PointD(10, 10), 4, 2, 0, 90).LineTo(0, 0).Build();
        var slice = Flatten(pie);
        Assert.Equal((10d, 10d), slice[0]);
        Assert.Equal(quarter, slice[1..^2]);

        // The contour is closed: the segment that follows starts a new contour at the center
        Assert.Equal([(10d, 10d), (0d, 0d)], slice[^2..]);
    }

    [Fact]
    public void TheChordsOfAnArcStayWithinTheToleranceOfTheArc()
    {
        var random = new Random(20261012);
        for (var index = 0; index < 200; index++)
        {
            var scale = Math.Pow(10, random.Next(0, 4));
            var radiusX = (0.05 + random.NextDouble()) * scale;
            var radiusY = (0.05 + random.NextDouble()) * scale;
            var start = (random.NextDouble() * 720) - 360;
            var sweep = (random.NextDouble() * 720) - 360;
            var rotation = random.Next(3) == 0 ? 0 : random.NextDouble() * 360;
            var center = new PointD((random.NextDouble() - 0.5) * 100, (random.NextDouble() - 0.5) * 100);
            var turn = AffineTransform.CreateRotation(rotation, center);
            Assert.True(turn.TryInvert(out var inverse));
            var path = new VectorPathBuilder().AddArc(center, radiusX, radiusY, start, sweep).Build().Transform(turn);
            var points = Flatten(path);
            var description = string.Create(CultureInfo.InvariantCulture, $"case {index}: radii {radiusX:R} {radiusY:R}, start {start:R}, sweep {sweep:R}, rotation {rotation:R}");

            // The ends are where the angles say
            var first = turn.Transform(new PointD(center.X + (radiusX * Math.Cos(start * Math.PI / 180)), center.Y + (radiusY * Math.Sin(start * Math.PI / 180))));
            var last = turn.Transform(new PointD(center.X + (radiusX * Math.Cos((start + sweep) * Math.PI / 180)), center.Y + (radiusY * Math.Sin((start + sweep) * Math.PI / 180))));
            Assert.True(first.DistanceTo(new PointD(points[0].X, points[0].Y)) <= 1e-9 * scale, description);
            Assert.True(last.DistanceTo(new PointD(points[^1].X, points[^1].Y)) <= 1e-9 * scale, description);

            var turned = 0d;
            for (var i = 0; i < points.Count; i++)
            {
                // Every vertex is on the ellipse
                var vertex = inverse.Transform(new PointD(points[i].X, points[i].Y));
                var x = (vertex.X - center.X) / radiusX;
                var y = (vertex.Y - center.Y) / radiusY;
                Assert.True(Math.Abs((x * x) + (y * y) - 1) <= 1e-9, description);
                if (i + 1 == points.Count)
                    break;

                // The angle from a vertex to the next one always has the sign of the sweep, and they add up to it
                var next = inverse.Transform(new PointD(points[i + 1].X, points[i + 1].Y));
                var nextX = (next.X - center.X) / radiusX;
                var nextY = (next.Y - center.Y) / radiusY;
                var step = Math.Atan2((x * nextY) - (y * nextX), (x * nextX) + (y * nextY)) * 180 / Math.PI;
                Assert.True(step * Math.Sign(sweep) > 0, description);
                turned += step;

                // The middle of the chord, brought back onto the ellipse along the ray from the center
                var angle = Math.Atan2(y + nextY, x + nextX);
                var onArc = turn.Transform(new PointD(center.X + (radiusX * Math.Cos(angle)), center.Y + (radiusY * Math.Sin(angle))));
                var middle = new PointD((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
                Assert.True(onArc.DistanceTo(middle) <= Internals.PathFlattener.Tolerance, $"{description}, chord {i}: {onArc.DistanceTo(middle)}");
            }

            Assert.Equal(sweep, turned, 1e-6);
        }
    }

    [Fact]
    public void ARoundedRectangleIsStraightSidesJoinedByQuartersOfEllipses()
    {
        var path = new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0, 0, 20, 10), 3).Build();
        Assert.Equal(new RectangleD(0, 0, 20, 10), path.Bounds);
        var points = Flatten(path);
        Assert.Equal((3d, 0d), points[0]);
        Assert.Equal((3d, 0d), points[^1]);
        foreach (var corner in new[] { (3d, 0d), (17d, 0d), (20d, 3d), (20d, 7d), (17d, 10d), (3d, 10d), (0d, 7d), (0d, 3d) })
        {
            Assert.Contains(corner, points);
        }

        // Every vertex is on a straight side or on the circle of a corner
        Assert.All(points, point =>
        {
            var dx = Math.Max(Math.Abs(point.X - 10) - 7, 0);
            var dy = Math.Max(Math.Abs(point.Y - 5) - 2, 0);
            Assert.Equal(3, dx == 0 || dy == 0 ? Math.Max(dx, dy) : Math.Sqrt((dx * dx) + (dy * dy)), 1e-12);
        });

        // Without radius it is the rectangle, bit for bit
        var rectangle = RectangleD.FromLTRB(1.25, 0.5, 6.75, 5.125);
        Assert.Equal(Flatten(new VectorPathBuilder().AddRectangle(rectangle).Build()), Flatten(new VectorPathBuilder().AddRoundedRectangle(rectangle, 0).Build()));
        AssertSamePixels(canvas => canvas.Fill(White, new VectorPathBuilder().AddRectangle(rectangle).Build()), canvas => canvas.Fill(White, new VectorPathBuilder().AddRoundedRectangle(rectangle, 0, 2).Build()));

        // The radii are limited to half the sides: the largest ones make an ellipse
        var ellipse = Flatten(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(2, 3, 8, 4), 100, 100).Build());
        Assert.Equal((6d, 3d), ellipse[0]);
        Assert.Equal((6d, 3d), ellipse[^1]);
        Assert.All(ellipse, point => Assert.Equal(1, (((point.X - 6) / 4) * ((point.X - 6) / 4)) + (((point.Y - 5) / 2) * ((point.Y - 5) / 2)), 1e-12));
        Assert.Equal(ellipse, Flatten(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(2, 3, 8, 4), 4, 2).Build()));

        // A single radius stays the radius of a circle: it is limited by the shortest side, and the largest one makes
        // two half circles joined by the straight parts of the long sides
        var pill = Flatten(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(2, 3, 8, 4), 100).Build());
        Assert.Equal((4d, 3d), pill[0]);
        Assert.Contains((8d, 3d), pill);
        Assert.Contains((10d, 5d), pill);
        Assert.Contains((2d, 5d), pill);
        Assert.All(pill, point =>
        {
            var dx = Math.Max(Math.Abs(point.X - 6) - 2, 0);
            Assert.Equal(2, Math.Sqrt((dx * dx) + ((point.Y - 5) * (point.Y - 5))), 1e-12);
        });
        Assert.Equal(pill, Flatten(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(2, 3, 8, 4), 2).Build()));

        // The segment that follows starts a new contour at the first point
        Assert.Equal([(3d, 0d), (30d, 30d)], Flatten(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0, 0, 20, 10), 3).LineTo(30, 30).Build())[^2..]);
    }

    [Fact]
    public void ARoundedRectangleCanHaveARadiusPerCorner()
    {
        // Radii of 1, 2 and 3, and a corner that is not rounded
        var rectangle = new RectangleD(0, 0, 20, 10);
        var path = new VectorPathBuilder().AddRoundedRectangle(rectangle, new CornerRadii(1, 2, 3, 0)).Build();
        Assert.Equal(rectangle, path.Bounds);
        var points = Flatten(path);
        Assert.Equal((1d, 0d), points[0]);
        Assert.Equal((1d, 0d), points[^1]);
        foreach (var vertex in new[] { (1d, 0d), (18d, 0d), (20d, 2d), (20d, 7d), (17d, 10d), (0d, 10d), (0d, 1d) })
        {
            Assert.Contains(vertex, points);
        }

        // Every vertex is on a side or on the circle of its corner
        Assert.All(points, point =>
        {
            var (centerX, centerY, radius) = point switch
            {
                { X: < 1, Y: < 1 } => (1d, 1d, 1d),
                { X: > 18, Y: < 2 } => (18d, 2d, 2d),
                { X: > 17, Y: > 7 } => (17d, 7d, 3d),
                _ => (double.NaN, double.NaN, 0d),
            };

            if (radius == 0)
            {
                Assert.True(point.X is 0 or 20 || point.Y is 0 or 10, $"({point.X}, {point.Y}) is not on a side");
            }
            else
            {
                Assert.Equal(radius, Math.Sqrt(((point.X - centerX) * (point.X - centerX)) + ((point.Y - centerY) * (point.Y - centerY))), 1e-12);
            }
        });

        // The same radius for every corner is the rectangle of that radius, bit for bit; no radius is the rectangle
        Assert.Equal(Flatten(new VectorPathBuilder().AddRoundedRectangle(rectangle, 3).Build()), Flatten(new VectorPathBuilder().AddRoundedRectangle(rectangle, new CornerRadii(3)).Build()));
        Assert.Equal(Flatten(new VectorPathBuilder().AddRectangle(rectangle).Build()), Flatten(new VectorPathBuilder().AddRoundedRectangle(rectangle, default(CornerRadii)).Build()));

        // Radii that do not fit are reduced together: 8 + 8 on a side of 10 multiplies every radius by 10 / 16
        Assert.Equal(
            Flatten(new VectorPathBuilder().AddRoundedRectangle(rectangle, new CornerRadii(2.5, 5, 5, 2.5)).Build()),
            Flatten(new VectorPathBuilder().AddRoundedRectangle(rectangle, new CornerRadii(4, 8, 8, 4)).Build()));

        // Two corners that take a whole side meet without a straight part, and the contour stays closed
        var tab = Flatten(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0, 0, 8, 10), new CornerRadii(4, 4, 0, 0)).Build());
        Assert.Equal((4d, 0d), tab[0]);
        Assert.Equal((4d, 0d), tab[^1]);
        Assert.Contains((8d, 4d), tab);
        Assert.Contains((8d, 10d), tab);
        Assert.Contains((0d, 10d), tab);
        Assert.Contains((0d, 4d), tab);
        Assert.DoesNotContain((8d, 0d), tab);

        // The area of the fill: the rectangle without what each corner removes, r^2 (1 - pi / 4). The chords of a
        // corner are inside its circle: they remove a little more.
        using var image = new Image<Gray8>(40, 30, new Gray8(0));
        image.Draw(canvas => canvas.FillRoundedRectangle(White, new RectangleD(0, 0, 40, 30), new CornerRadii(4, 8, 12, 0)), Ct);
        var painted = DrawingTestSupport.ReadSamples(image.Frames[0]).Sum();
        Assert.Equal(1200 - ((16 + 64 + 144) * (1 - (Math.PI / 4))), painted / 255.0, 1.5);

        Assert.Equal(new CornerRadii(2, 2, 2, 2), new CornerRadii(2));
        Assert.NotEqual(new CornerRadii(1, 2, 3, 4), new CornerRadii(1, 2, 3, 5));
        Assert.True(default(CornerRadii).IsEmpty);
        Assert.False(new CornerRadii(0, 0, 0, 1).IsEmpty);
        Assert.Equal("[1 2 3 0.5]", new CornerRadii(1, 2, 3, 0.5).ToString());
        Assert.Throws<ArgumentException>(() => new CornerRadii(double.NaN));
        Assert.Throws<ArgumentException>(() => new CornerRadii(0, double.PositiveInfinity, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CornerRadii(0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CornerRadii(0, 0, 0, 16777217));
        Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddRoundedRectangle(new RectangleD(double.NaN, 0, 1, 1), new CornerRadii(1)));
    }

    [Theory]
    [InlineData(4, LineJoin.Miter)]
    [InlineData(5, LineJoin.Miter)]
    [InlineData(2.5, LineJoin.Bevel)]
    [InlineData(40, LineJoin.Round)]
    public void TheStrokeOfARoundedRectangleStaysAroundItsOutline(double radius, LineJoin join)
    {
        // The corners meet the sides without a corner: no join may stick out, whatever the pen
        var rectangle = new RectangleD(6.5, 7.25, 20, 10);
        var pen = new Pen(White, 3) { LineJoin = join, MiterLimit = 100 };
        using var image = new Image<Gray8>(34, 26, new Gray8(0));
        image.Draw(canvas => canvas.StrokeRoundedRectangle(pen, rectangle, radius), Ct);
        var effective = Math.Min(radius, 5);
        var painted = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                // The distance from the center of the pixel to the outline
                var qx = Math.Abs(x + 0.5 - 16.5) - (10 - effective);
                var qy = Math.Abs(y + 0.5 - 12.25) - (5 - effective);
                var outside = Math.Sqrt((Math.Max(qx, 0) * Math.Max(qx, 0)) + (Math.Max(qy, 0) * Math.Max(qy, 0)));
                var distance = Math.Abs(outside + Math.Min(Math.Max(qx, qy), 0) - effective);
                var value = image.Frames[0][x, y].Value;
                painted += value;
                if (distance > 1.5 + 0.8)
                {
                    Assert.Equal(0, value, $"pixel ({x}, {y}) at {distance} from the outline is painted");
                }
                else if (distance < 1.5 - 0.8)
                {
                    Assert.Equal(255, value, $"pixel ({x}, {y}) at {distance} from the outline is not fully painted");
                }
            }
        }

        // The area of the stroke: the length of the outline times the width of the pen
        var length = (2 * (20 - (2 * effective))) + (2 * (10 - (2 * effective))) + (2 * Math.PI * effective);
        Assert.Equal(length * 3, painted / 255.0, 1.5);
    }

    [Fact]
    public void AnEllipseCanBeTurned()
    {
        var turned = new VectorPathBuilder().AddEllipse(new PointD(10, 12), 6, 2, 90).Build();
        Assert.Equal(RectangleD.FromLTRB(8, 6, 12, 18), turned.Bounds);
        var points = Flatten(turned);

        // The contour starts at the end of the first radius, which the rotation brought under the center
        Assert.Equal((10d, 18d), points[0]);
        Assert.Equal((10d, 18d), points[^1]);
        Assert.All(points, point => Assert.Equal(1, (((point.X - 10) / 2) * ((point.X - 10) / 2)) + (((point.Y - 12) / 6) * ((point.Y - 12) / 6)), 1e-12));

        // Without rotation, it is the ellipse
        Assert.Equal(Flatten(new VectorPathBuilder().AddEllipse(new PointD(10, 12), 6, 2).Build()), Flatten(new VectorPathBuilder().AddEllipse(new PointD(10, 12), 6, 2, 0).Build()));

        // Any angle: the same as transforming the ellipse
        var expected = Flatten(new VectorPathBuilder().AddEllipse(new PointD(10, 12), 6, 2).Build().Transform(AffineTransform.CreateRotation(33, new PointD(10, 12))));
        var actual = Flatten(new VectorPathBuilder().AddEllipse(new PointD(10, 12), 6, 2, 33).LineTo(0, 0).Build());
        Assert.Equal(expected.Count, actual.Count - 2);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, 1e-12);
            Assert.Equal(expected[i].Y, actual[i].Y, 1e-12);
        }

        // The segment that follows starts a new contour at the first point
        Assert.Equal(actual[0], actual[^2]);
    }

    [Fact]
    public void ArcsAreValidatedWhenThePathIsBuilt()
    {
        const double TooFar = 16777217;
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).ArcTo(invalid, 1, 0, false, false, 1, 1));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).ArcTo(1, invalid, 0, false, false, 1, 1));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).ArcTo(1, 1, invalid, false, false, 1, 1));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).ArcTo(1, 1, 0, false, false, invalid, 1));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).ArcTo(1, 1, 0, false, false, new PointD(1, invalid)));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().ArcTo(new PointD(invalid, 0), 1, 1, 0, 90));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().ArcTo(default, 1, 1, invalid, 90));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddArc(default, invalid, 1, 0, 90));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddArc(default, 1, 1, 0, invalid));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddPie(default, 1, invalid, 0, 90));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0, 0, 1, 1), invalid));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0, 0, 1, 1), 1, invalid));
            Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddEllipse(default, 1, 1, invalid));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().MoveTo(0, 0).ArcTo(-1, 1, 0, false, false, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().MoveTo(0, 0).ArcTo(1, 1, 0, false, false, TooFar, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddArc(default, 1, -1, 0, 90));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddArc(new PointD(16777216, 0), 1, 1, 0, 90));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddPie(new PointD(0, -16777216), 1, 1, 0, 90));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().ArcTo(default, TooFar, 1, 0, 90));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0, 0, 1, 1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddRoundedRectangle(new RectangleD(16777216, 0, 1, 1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddEllipse(new PointD(16777216, 0), 1, 1, 45));
        Assert.Throws<InvalidOperationException>(() => new VectorPathBuilder().ArcTo(1, 1, 0, false, false, 1, 1));

        // An arc between two points at the limit may go beyond it: clockwise from the top to the bottom is by the right
        var builder = new VectorPathBuilder().MoveTo(16777216, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.ArcTo(5, 5, 0, largeArc: false, clockwise: true, 16777216, 10));

        // A rejected element leaves the builder as it was
        Assert.Equal(new RectangleD(16777216, 0, 0, 0), builder.Build().Bounds);
        Assert.Equal(RectangleD.FromLTRB(16777211, 0, 16777216, 10), builder.ArcTo(5, 5, 0, largeArc: false, clockwise: false, 16777216, 10).Build().Bounds);
    }

    [Fact]
    public void TheArcAndPolygonShortcutsOfTheCanvasDrawTheSamePixelsAsAPath()
    {
        var rectangle = RectangleD.FromLTRB(1.25, 0.5, 6.75, 5.125);
        var center = new PointD(4.2, 3.9);
        var pen = new Pen(White, 1.25) { LineJoin = LineJoin.Round, LineCap = LineCap.Square };
        PointD[] points = [new(0.5, 7), new(3, -2), new(6, 9), new(7.5, 1), new(1, 3)];
        AssertSamePixels(canvas => canvas.FillRoundedRectangle(White, rectangle, 1.5), canvas => canvas.Fill(White, new VectorPathBuilder().AddRoundedRectangle(rectangle, 1.5).Build()));
        AssertSamePixels(canvas => canvas.StrokeRoundedRectangle(pen, rectangle, 1.5), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddRoundedRectangle(rectangle, 1.5).Build()));
        var radii = new CornerRadii(0.5, 2, 0, 1.25);
        AssertSamePixels(canvas => canvas.FillRoundedRectangle(White, rectangle, radii), canvas => canvas.Fill(White, new VectorPathBuilder().AddRoundedRectangle(rectangle, radii).Build()));
        AssertSamePixels(canvas => canvas.StrokeRoundedRectangle(pen, rectangle, radii), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddRoundedRectangle(rectangle, radii).Build()));
        AssertSamePixels(canvas => canvas.FillPolygon(White, points), canvas => canvas.Fill(White, new VectorPathBuilder().AddPolygon(points).Build()));
        AssertSamePixels(canvas => canvas.FillPolygon(White, points, FillRule.EvenOdd), canvas => canvas.Fill(White, new VectorPathBuilder().AddPolygon(points).Build(), FillRule.EvenOdd));
        AssertSamePixels(canvas => canvas.StrokePolygon(pen, points), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddPolygon(points).Build()));
        AssertSamePixels(canvas => canvas.StrokePolyline(pen, points), canvas => canvas.Stroke(pen, new VectorPathBuilder().MoveTo(points[0]).LineTo(points[1]).LineTo(points[2]).LineTo(points[3]).LineTo(points[4]).Build()));
        AssertSamePixels(canvas => canvas.FillPie(White, center, 3.1, 20, 250), canvas => canvas.Fill(White, new VectorPathBuilder().AddPie(center, 3.1, 3.1, 20, 250).Build()));
        AssertSamePixels(canvas => canvas.FillPie(White, center, 3.1, 20, -110), canvas => canvas.Fill(White, new VectorPathBuilder().AddPie(center, 3.1, 3.1, 20, -110).Build()));
        AssertSamePixels(canvas => canvas.StrokeArc(pen, center, 3.1, 20, 250), canvas => canvas.Stroke(pen, new VectorPathBuilder().AddArc(center, 3.1, 3.1, 20, 250).Build()));

        // A polygon of one point and a line of one point are a point
        AssertSamePixels(canvas => canvas.StrokePolyline(pen, [center]), canvas => canvas.StrokeCircle(pen, center, 0));
        AssertSamePixels(canvas => canvas.StrokePolygon(pen, [center]), canvas => canvas.StrokeCircle(pen, center, 0));
    }

    [Fact]
    public void TheArcAndPolygonShortcutsOfTheCanvasValidateTheirArguments()
    {
        using var image = new Image<Gray8>(4, 4, new Gray8(0));
        var pen = new Pen(White, 1);
        image.Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.FillRoundedRectangle(null!, new RectangleD(0, 0, 1, 1), 1));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.StrokeRoundedRectangle(null!, new RectangleD(0, 0, 1, 1), 1));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.FillRoundedRectangle(null!, new RectangleD(0, 0, 1, 1), new CornerRadii(1)));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.StrokeRoundedRectangle(null!, new RectangleD(0, 0, 1, 1), new CornerRadii(1)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.FillRoundedRectangle(White, new RectangleD(0, 0, 16777217, 1), new CornerRadii(1)));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.FillPolygon(null!, [default]));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.StrokePolygon(null!, [default]));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.StrokePolyline(null!, [default]));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.FillPie(null!, default, 1, 0, 90));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.StrokeArc(null!, default, 1, 0, 90));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.FillPolygon(White, []));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.FillPolygon(White, [default, new PointD(double.NaN, 0)]));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.FillPolygon(White, [new PointD(16777217, 0)]));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.FillPolygon(White, [default], (FillRule)2));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.StrokePolygon(new Pen(White, 1), []));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.StrokePolyline(new Pen(White, 1), []));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.FillRoundedRectangle(White, new RectangleD(0, 0, 1, 1), -1));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.StrokeRoundedRectangle(new Pen(White, 1), new RectangleD(0, 0, 1, 1), double.NaN));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.FillPie(White, default, -1, 0, 90));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.FillPie(White, default, 1, double.NaN, 90));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.StrokeArc(new Pen(White, 1), default, 1, 0, double.PositiveInfinity));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.StrokeArc(new Pen(White, 1), new PointD(double.NaN, 0), 1, 0, 90));
            },
            Ct);

        // A rejected call draws nothing
        Assert.Equal(new int[16], DrawingTestSupport.ReadSamples(image.Frames[0]));
        _ = pen;

        static void AssertThrows<TException>(DrawingCanvas canvas, DrawingAction action)
            where TException : Exception
        {
            try
            {
                action(canvas);
            }
            catch (TException exception) when (exception.GetType() == typeof(TException))
            {
                return;
            }

            Assert.Fail($"{typeof(TException).Name} was not thrown.");
        }
    }

    [Fact]
    public void ContainsTellsWhatAFillPaints()
    {
        // Two squares turning the same way, one inside the other: a hole for the even-odd rule only
        var nested = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 10, 10)).AddRectangle(new RectangleD(3, 3, 4, 4)).Build();
        Assert.True(nested.Contains(new PointD(1, 1)));
        Assert.True(nested.Contains(new PointD(5, 5)));
        Assert.True(nested.Contains(new PointD(1, 1), FillRule.EvenOdd));
        Assert.False(nested.Contains(new PointD(5, 5), FillRule.EvenOdd));
        Assert.False(nested.Contains(new PointD(11, 5)));
        Assert.False(nested.Contains(new PointD(-0.001, 5)));
        Assert.False(nested.Contains(new PointD(5, 10.001), FillRule.EvenOdd));

        // The inner square the other way: a hole for both rules
        var hole = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 10, 10)).AddPolygon([new PointD(3, 3), new PointD(3, 7), new PointD(7, 7), new PointD(7, 3)]).Build();
        Assert.False(hole.Contains(new PointD(5, 5)));
        Assert.True(hole.Contains(new PointD(8, 5)));

        // An open contour is closed by a straight segment, as when it is filled
        var open = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Build();
        Assert.True(open.Contains(new PointD(8, 3)));
        Assert.False(open.Contains(new PointD(3, 8)));

        // Curves and arcs
        var circle = new VectorPathBuilder().AddCircle(new PointD(20, 20), 10).Build();
        var pie = new VectorPathBuilder().AddPie(new PointD(20, 20), 10, 10, 0, 90).Build();
        for (var degrees = 5; degrees < 360; degrees += 10)
        {
            var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
            Assert.True(circle.Contains(new PointD(20 + (9.9 * cos), 20 + (9.9 * sin))));
            Assert.False(circle.Contains(new PointD(20 + (10.1 * cos), 20 + (10.1 * sin))));
            Assert.Equal(degrees < 90, pie.Contains(new PointD(20 + (9.9 * cos), 20 + (9.9 * sin))));
            Assert.False(pie.Contains(new PointD(20 + (10.1 * cos), 20 + (10.1 * sin))));
        }

        // A horizontal line through a vertex counts it once
        var diamond = new VectorPathBuilder().AddPolygon([new PointD(5, 0), new PointD(10, 5), new PointD(5, 10), new PointD(0, 5)]).Build();
        Assert.True(diamond.Contains(new PointD(5, 5)));
        Assert.True(diamond.Contains(new PointD(1, 5)));
        Assert.False(diamond.Contains(new PointD(-1, 5)));
        Assert.False(diamond.Contains(new PointD(11, 5)));

        Assert.False(new VectorPathBuilder().Build().Contains(default));
        Assert.False(circle.Contains(new PointD(double.NaN, 20)));
        Assert.False(circle.Contains(new PointD(20, double.PositiveInfinity)));
        Assert.Throws<ArgumentOutOfRangeException>(() => circle.Contains(default, (FillRule)2));
    }

    [Fact]
    public void ContainsAgreesWithThePixelsOfAFill()
    {
        var path = new VectorPathBuilder()
            .MoveTo(1, 1).CubicBezierTo(15, 0, 2, 15, 15.5, 14.5).QuadraticBezierTo(4, 17, 1.5, 6.25).Close()
            .AddEllipse(new PointD(8, 8), 5.5, 3.25, 25)
            .AddRoundedRectangle(new RectangleD(3, 9, 9, 5), 2)
            .Build();
        foreach (var fillRule in new[] { FillRule.NonZero, FillRule.EvenOdd })
        {
            using var image = new Image<Gray8>(16, 16, new Gray8(0));
            image.Draw(canvas => canvas.Fill(White, path, fillRule), Ct);
            var inside = 0;
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    // A pixel that is entirely painted has its center inside, a pixel that is not painted has it outside
                    var value = image.Frames[0][x, y].Value;
                    if (value is 0 or 255)
                    {
                        Assert.Equal(value == 255, path.Contains(new PointD(x + 0.5, y + 0.5), fillRule), $"pixel ({x}, {y}) with {fillRule}");
                        inside += value == 255 ? 1 : 0;
                    }
                }
            }

            Assert.True(inside > 20);
        }
    }

    [Fact]
    public void TheLengthOfAPathIsTheSumOfItsContours()
    {
        Assert.Equal(0, new VectorPathBuilder().Build().GetLength());
        Assert.Equal(0, new VectorPathBuilder().MoveTo(3, 4).Build().GetLength());
        Assert.Equal(5, new VectorPathBuilder().MoveTo(1, 1).LineTo(4, 5).Build().GetLength());

        // A closed contour includes its closing segment; an open one does not
        Assert.Equal(12, new VectorPathBuilder().MoveTo(0, 0).LineTo(3, 0).LineTo(3, 4).Close().Build().GetLength());
        Assert.Equal(7, new VectorPathBuilder().MoveTo(0, 0).LineTo(3, 0).LineTo(3, 4).Build().GetLength());
        Assert.Equal(26, new VectorPathBuilder().AddRectangle(new RectangleD(1, 1, 6, 7)).Build().GetLength());

        // The gap between two contours is not counted
        Assert.Equal(7, new VectorPathBuilder().MoveTo(0, 0).LineTo(3, 0).MoveTo(100, 100).LineTo(100, 104).Build().GetLength());

        // A circle: its chords are a little shorter than it is
        var circle = new VectorPathBuilder().AddCircle(new PointD(5, 5), 100).Build().GetLength();
        Assert.InRange(circle, (2 * Math.PI * 100) - 0.2, 2 * Math.PI * 100);
        var arc = new VectorPathBuilder().AddArc(new PointD(5, 5), 100, 100, 10, 90).Build().GetLength();
        Assert.InRange(arc, (Math.PI * 50) - 0.05, Math.PI * 50);
    }

    [Fact]
    public void APointAtADistanceIsOnThePathWithItsDirection()
    {
        var path = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).MoveTo(50, 50).LineTo(50, 40).Build();
        Assert.Equal(30, path.GetLength());
        AssertPoint(path, 0, new PointD(0, 0), new PointD(1, 0));
        AssertPoint(path, 2.5, new PointD(2.5, 0), new PointD(1, 0));

        // At a corner, the direction is the one of the segment that starts there
        AssertPoint(path, 10, new PointD(10, 0), new PointD(0, 1));
        AssertPoint(path, 17, new PointD(10, 7), new PointD(0, 1));

        // The next contour continues without the gap
        AssertPoint(path, 20, new PointD(50, 50), new PointD(0, -1));
        AssertPoint(path, 26, new PointD(50, 44), new PointD(0, -1));

        // Before the start and beyond the end, the ends of the path
        AssertPoint(path, 30, new PointD(50, 40), new PointD(0, -1));
        AssertPoint(path, 1000, new PointD(50, 40), new PointD(0, -1));
        AssertPoint(path, double.PositiveInfinity, new PointD(50, 40), new PointD(0, -1));
        AssertPoint(path, -3, new PointD(0, 0), new PointD(1, 0));
        AssertPoint(path, double.NegativeInfinity, new PointD(0, 0), new PointD(1, 0));

        // A closed contour includes its closing segment
        var triangle = new VectorPathBuilder().MoveTo(0, 0).LineTo(3, 0).LineTo(3, 4).Close().Build();
        AssertPoint(triangle, 9.5, new PointD(1.5, 2), new PointD(-0.6, -0.8));

        // On a circle, the point is on the circle and the direction is along it
        var circle = new VectorPathBuilder().AddCircle(new PointD(0, 0), 50).Build();
        Assert.True(circle.TryGetPointAtDistance(25 * Math.PI, out var point, out var tangent));
        Assert.Equal(0, point.X, 0.2);
        Assert.Equal(50, point.Y, 0.1);
        Assert.Equal(-1, tangent.X, 0.01);
        Assert.Equal(1, Math.Sqrt((tangent.X * tangent.X) + (tangent.Y * tangent.Y)), 1e-12);

        // The length of a path is the distance of its last point, exactly, whatever its chords add up to
        foreach (var curve in new[]
        {
            new VectorPathBuilder().MoveTo(20, 100).CubicBezierTo(60, 10, 120, 110, 176, 34).Build(),
            new VectorPathBuilder().MoveTo(0.1, 0.7).QuadraticBezierTo(33.3, -7.7, 12.9, 41.3).LineTo(1.1, 2.3).Build(),
            new VectorPathBuilder().AddArc(new PointD(3.3, 4.7), 51.3, 17.9, 13, 211).Build(),
        })
        {
            var points = Flatten(curve);
            Assert.True(curve.TryGetPointAtDistance(curve.GetLength(), out var end, out _));
            Assert.Equal(new PointD(points[^1].X, points[^1].Y), end);
        }

        // A path without length has no point
        Assert.False(new VectorPathBuilder().Build().TryGetPointAtDistance(0, out point, out tangent));
        Assert.Equal((default(PointD), default(PointD)), (point, tangent));
        Assert.False(new VectorPathBuilder().MoveTo(1, 2).Build().TryGetPointAtDistance(0, out _, out _));
        Assert.False(new VectorPathBuilder().MoveTo(1, 2).LineTo(1, 2).Close().Build().TryGetPointAtDistance(0, out _, out _));
        Assert.Throws<ArgumentException>(() => path.TryGetPointAtDistance(double.NaN, out _, out _));

        static void AssertPoint(VectorPath path, double distance, PointD expectedPoint, PointD expectedTangent)
        {
            Assert.True(path.TryGetPointAtDistance(distance, out var point, out var tangent));
            Assert.Equal(expectedPoint.X, point.X, 1e-12);
            Assert.Equal(expectedPoint.Y, point.Y, 1e-12);
            Assert.Equal(expectedTangent.X, tangent.X, 1e-12);
            Assert.Equal(expectedTangent.Y, tangent.Y, 1e-12);
        }
    }

    [Fact]
    public void TightBoundsAreWhatThePathReaches()
    {
        Assert.Equal(RectangleD.Empty, new VectorPathBuilder().Build().GetTightBounds());
        Assert.Equal(new RectangleD(3, -2, 0, 0), new VectorPathBuilder().MoveTo(3, -2).Build().GetTightBounds());

        // Straight segments: the same as the bounds
        var polygon = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(6, 2), new PointD(-1, 5)]).Build();
        Assert.Equal(polygon.Bounds, polygon.GetTightBounds());

        // A quadratic curve reaches half way to its control point; a symmetric cubic one, three quarters of the way
        var quadratic = new VectorPathBuilder().MoveTo(0, 0).QuadraticBezierTo(5, 10, 10, 0).Build();
        Assert.Equal(new RectangleD(0, 0, 10, 10), quadratic.Bounds);
        Assert.Equal(new RectangleD(0, 0, 10, 5), quadratic.GetTightBounds());
        var cubic = new VectorPathBuilder().MoveTo(0, 0).CubicBezierTo(0, 10, 10, 10, 10, 0).Build();
        Assert.Equal(new RectangleD(0, 0, 10, 7.5), cubic.GetTightBounds());

        // A curve that goes beyond both of its ends, on both axes
        var loop = new VectorPathBuilder().MoveTo(0, 0).CubicBezierTo(60, 10, -50, 10, 10, 0).Build().GetTightBounds();
        Assert.True(loop.Left < -8 && loop.Left > -50 && loop.Right > 18 && loop.Right < 60, loop.ToString());
        Assert.Equal((0d, 7.5d), (loop.Top, loop.Bottom));

        Assert.Equal(RectangleD.FromLTRB(7, 8.5, 13, 11.5), new VectorPathBuilder().AddEllipse(new PointD(10, 10), 3, 1.5).Build().GetTightBounds());

        // An ellipse of radii 2 and 1 turned by an eighth of a turn reaches sqrt(2.5) from its center on both axes
        var turned = new VectorPathBuilder().AddEllipse(new PointD(10, 10), 2, 1, 45).Build();
        var reach = Math.Sqrt(2.5);
        AssertRectangle(RectangleD.FromLTRB(10 - reach, 10 - reach, 10 + reach, 10 + reach), turned.GetTightBounds(), 1e-12);
        Assert.True(turned.Bounds.Contains(turned.GetTightBounds()));
        Assert.True(turned.Bounds.Width > 2 * reach);

        // A quarter of a circle from 45 to 135 degrees: its lowest point is under the center, its ends are its sides
        var arc = new VectorPathBuilder().AddArc(new PointD(0, 0), 5, 5, 45, 90).Build();
        var half = 5 * Math.Sqrt(0.5);
        AssertRectangle(RectangleD.FromLTRB(-half, half, half, 5), arc.GetTightBounds(), 1e-12);
        Assert.Equal(5 * Math.Sqrt(2), arc.Bounds.Bottom, 1e-12);

        // An arc that reaches no extreme of its circle is bounded by its ends
        var small = new VectorPathBuilder().AddArc(new PointD(0, 0), 5, 5, 10, 60).Build();
        AssertRectangle(RectangleD.FromLTRB(5 * Math.Cos(70 * Math.PI / 180), 5 * Math.Sin(10 * Math.PI / 180), 5 * Math.Cos(10 * Math.PI / 180), 5 * Math.Sin(70 * Math.PI / 180)), small.GetTightBounds(), 1e-12);

        // A rounded rectangle is bounded by its rectangle
        Assert.Equal(new RectangleD(1, 2, 20, 10), new VectorPathBuilder().AddRoundedRectangle(new RectangleD(1, 2, 20, 10), 4).Build().GetTightBounds());
    }

    [Fact]
    public void TightBoundsContainTheChordsOfAnyPathAndAreReachedByThem()
    {
        var random = new Random(20261013);
        for (var index = 0; index < 100; index++)
        {
            var builder = new VectorPathBuilder().MoveTo(Next(), Next());
            for (var i = random.Next(1, 5); i > 0; i--)
            {
                _ = random.Next(6) switch
                {
                    0 => builder.LineTo(Next(), Next()),
                    1 => builder.QuadraticBezierTo(Next(), Next(), Next(), Next()),
                    2 => builder.CubicBezierTo(Next(), Next(), Next(), Next(), Next(), Next()),
                    3 => builder.ArcTo(1 + Math.Abs(Next()), 1 + Math.Abs(Next()), Next() * 3, random.Next(2) == 0, random.Next(2) == 0, Next(), Next()),
                    4 => builder.ArcTo(new PointD(Next(), Next()), Math.Abs(Next()), Math.Abs(Next()), Next() * 5, Next() * 8),
                    _ => builder.AddEllipse(new PointD(Next(), Next()), Math.Abs(Next()), Math.Abs(Next()), Next() * 3),
                };
            }

            var path = builder.Build().Transform(new AffineTransform(1 + random.NextDouble(), random.NextDouble() - 0.5, random.NextDouble() - 0.5, 1 + random.NextDouble(), Next(), Next()));
            var tight = path.GetTightBounds();
            var bounds = path.Bounds;
            Assert.True(tight.Left >= bounds.Left - 1e-9 && tight.Top >= bounds.Top - 1e-9 && tight.Right <= bounds.Right + 1e-9 && tight.Bottom <= bounds.Bottom + 1e-9, $"case {index}: {tight} is not inside {bounds}");

            var sink = new VertexSink();
            Internals.PathFlattener.Flatten(path, sink);
            var points = sink.Points;
            const double Tolerance = Internals.PathFlattener.Tolerance + 1e-9;
            Assert.InRange(points.Min(point => point.X), tight.Left - 1e-9, tight.Left + Tolerance, message: $"case {index}: left");
            Assert.InRange(points.Max(point => point.X), tight.Right - Tolerance, tight.Right + 1e-9, message: $"case {index}: right");
            Assert.InRange(points.Min(point => point.Y), tight.Top - 1e-9, tight.Top + Tolerance, message: $"case {index}: top");
            Assert.InRange(points.Max(point => point.Y), tight.Bottom - Tolerance, tight.Bottom + 1e-9, message: $"case {index}: bottom");
        }

        double Next() => (random.NextDouble() - 0.5) * 60;
    }

    [Fact]
    public void FillingAWidenedPathIsStrokingThePath()
    {
        var path = new VectorPathBuilder()
            .MoveTo(1, 1).LineTo(6.5, 1.25).LineTo(2, 3).QuadraticBezierTo(7, 4, 6, 7).Close()
            .MoveTo(0.75, 5).CubicBezierTo(2, 3.5, 3, 8.5, 4.5, 6.25)
            .AddArc(new PointD(4, 4), 3, 2, 200, 120)
            .MoveTo(7, 7)
            .Build();
        foreach (var pen in new[]
        {
            new Pen(White, 1),
            new Pen(White, 1.5) { LineJoin = LineJoin.Round, LineCap = LineCap.Round },
            new Pen(White, 0.75) { LineJoin = LineJoin.Bevel, LineCap = LineCap.Square },
            new Pen(White, 2.25) { MiterLimit = 1.5 },
            new Pen(White, 40),
        })
        {
            var widened = path.Widen(pen);
            Assert.False(widened.IsEmpty);
            AssertSamePixels(canvas => canvas.Stroke(pen, path), canvas => canvas.Fill(White, widened));
        }

        // The stroke of a horizontal line: its bounds are the line, half the width away on each side, and a point of it
        // is found as a point of any path
        var line = new VectorPathBuilder().MoveTo(2, 5).LineTo(12, 5).Build();
        var stroke = line.Widen(new Pen(White, 3));
        Assert.Equal(RectangleD.FromLTRB(2, 3.5, 12, 6.5), stroke.GetTightBounds());
        Assert.True(stroke.Contains(new PointD(7, 6.4)));
        Assert.False(stroke.Contains(new PointD(7, 6.6)));
        Assert.False(stroke.Contains(new PointD(1.9, 5)));
        Assert.Equal(RectangleD.FromLTRB(0.5, 3.5, 13.5, 6.5), line.Widen(new Pen(White, 3) { LineCap = LineCap.Square }).GetTightBounds());

        // Nothing to stroke
        Assert.True(new VectorPathBuilder().Build().Widen(new Pen(White, 3)).IsEmpty);
        Assert.True(new VectorPathBuilder().MoveTo(1, 1).Build().Widen(new Pen(White, 3)).IsEmpty);
        Assert.False(new VectorPathBuilder().MoveTo(1, 1).Build().Widen(new Pen(White, 3) { LineCap = LineCap.Round }).IsEmpty);

        Assert.Throws<ArgumentNullException>(() => path.Widen(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().MoveTo(16777216, 0).LineTo(16777216, 10).Build().Widen(new Pen(White, 3)));
    }

    [Fact]
    public void AStrokeContainsWhatItsCapsJoinsAndDashesCover()
    {
        // A horizontal line 3 wide: half the width on each side, and what the cap adds beyond the ends
        var line = new VectorPathBuilder().MoveTo(2, 5).LineTo(12, 5).Build();
        var butt = new Pen(White, 3);
        Assert.True(line.StrokeContains(new PointD(7, 6.4), butt));
        Assert.False(line.StrokeContains(new PointD(7, 6.6), butt));
        Assert.False(line.StrokeContains(new PointD(1.9, 5), butt));
        Assert.True(line.StrokeContains(new PointD(0.6, 6.4), new Pen(White, 3) { LineCap = LineCap.Square }));
        Assert.True(line.StrokeContains(new PointD(1, 5), new Pen(White, 3) { LineCap = LineCap.Round }));

        // (1, 6.4) is 1.72 from the end: outside the round cap, inside the square one
        Assert.False(line.StrokeContains(new PointD(1, 6.4), new Pen(White, 3) { LineCap = LineCap.Round }));
        Assert.True(line.StrokeContains(new PointD(1, 6.4), new Pen(White, 3) { LineCap = LineCap.Square }));

        // The outer corner of a right angle 2 wide is (11, -1): (10.9, -0.9) is in the miter, 1.27 from the vertex (out
        // of the round join), and beyond the bevel from (11, 0) to (10, -1)
        var corner = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Build();
        var miter = new Pen(White, 2);
        var tip = new PointD(10.9, -0.9);
        Assert.True(corner.StrokeContains(tip, miter));
        Assert.False(corner.StrokeContains(tip, new Pen(White, 2) { LineJoin = LineJoin.Round }));
        Assert.False(corner.StrokeContains(tip, new Pen(White, 2) { LineJoin = LineJoin.Bevel }));
        Assert.True(corner.StrokeContains(new PointD(10.4, -0.4), new Pen(White, 2) { LineJoin = LineJoin.Bevel }));

        // The miter of a right angle is sqrt(2) times the width: a limit of 1.4 bevels it
        Assert.False(corner.StrokeContains(tip, new Pen(White, 2) { MiterLimit = 1.4 }));
        Assert.True(corner.StrokeContains(tip, new Pen(White, 2) { MiterLimit = 1.5 }));

        // Dashes of 4 and gaps of 2, and the pattern moved by its offset
        var dashed = new VectorPathBuilder().MoveTo(0, 0).LineTo(20, 0).Build();
        var dashes = new Pen(White, 2) { DashPattern = [4, 2] };
        Assert.True(dashed.StrokeContains(new PointD(1, 0.5), dashes));
        Assert.False(dashed.StrokeContains(new PointD(5, 0.5), dashes));
        Assert.True(dashed.StrokeContains(new PointD(7, 0.5), dashes));
        Assert.True(dashed.StrokeContains(new PointD(5, 0.5), new Pen(White, 2) { DashPattern = [4, 2], DashOffset = 3 }));
        Assert.True(dashed.StrokeContains(new PointD(4.8, 0.3), new Pen(White, 2) { DashPattern = [4, 2], LineCap = LineCap.Round }));
        Assert.False(dashed.StrokeContains(new PointD(4.8, 0.3), dashes));
        Assert.False(dashed.StrokeContains(new PointD(5, 0.9), new Pen(White, 2) { DashPattern = [4, 2], LineCap = LineCap.Round }));

        // A hairline is one unit wide; a closed contour has no cap
        Assert.True(line.StrokeContains(new PointD(7, 5.45), new Pen(White, 0)));
        Assert.False(line.StrokeContains(new PointD(7, 5.55), new Pen(White, 0)));
        var square = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 10, 10)).Build();
        Assert.True(square.StrokeContains(new PointD(-0.9, -0.9), miter));
        Assert.False(square.StrokeContains(new PointD(5, 5), miter));

        // A stroke on one side of the path
        Assert.True(square.StrokeContains(new PointD(1.5, 5), new Pen(White, 2) { Alignment = PenAlignment.Inside }));
        Assert.False(square.StrokeContains(new PointD(-0.5, 5), new Pen(White, 2) { Alignment = PenAlignment.Inside }));
        Assert.True(square.StrokeContains(new PointD(-1.5, 5), new Pen(White, 2) { Alignment = PenAlignment.Outside }));
        Assert.False(square.StrokeContains(new PointD(0.5, 5), new Pen(White, 2) { Alignment = PenAlignment.Outside }));

        // Nothing to contain
        Assert.False(new VectorPathBuilder().Build().StrokeContains(new PointD(0, 0), butt));
        Assert.False(new VectorPathBuilder().MoveTo(1, 1).Build().StrokeContains(new PointD(1, 1), butt));
        Assert.True(new VectorPathBuilder().MoveTo(1, 1).Build().StrokeContains(new PointD(1, 1), new Pen(White, 3) { LineCap = LineCap.Round }));
        Assert.False(line.StrokeContains(new PointD(double.NaN, 5), butt));
        Assert.False(line.StrokeContains(new PointD(7, double.PositiveInfinity), butt));
        Assert.Throws<ArgumentNullException>(() => line.StrokeContains(new PointD(7, 5), null!));
    }

    [Fact]
    public void AStrokeContainsWhatItsWidenedPathContains()
    {
        // Sharp and blunt corners, a corner sharper than the default miter limit, curves, an arc, a closed contour and a
        // contour of one point
        var path = new VectorPathBuilder()
            .MoveTo(2, 2).LineTo(14, 3).LineTo(5, 9).QuadraticBezierTo(15, 12, 13, 17).Close()
            .MoveTo(1, 14).CubicBezierTo(4, 10, 6, 20, 9, 15)
            .AddArc(new PointD(10, 10), 4, 3, 200, 120)
            .MoveTo(17, 1).LineTo(18, 12).LineTo(16.5, 2)
            .MoveTo(19, 19)
            .Build();
        Pen[] pens =
        [
            .. new[] { LineCap.Butt, LineCap.Round, LineCap.Square }.SelectMany(cap => new[] { LineJoin.Miter, LineJoin.Round, LineJoin.Bevel }.Select(lineJoin => new Pen(White, 1.5) { LineCap = cap, LineJoin = lineJoin })),
            new Pen(White, 1.5) { MiterLimit = 1 },
            new Pen(White, 1.5) { MiterLimit = 20 },
            new Pen(White, 1.25) { DashPattern = [2, 1], DashOffset = 0.5 },
            new Pen(White, 1.25) { DashPattern = [0, 1.5], LineCap = LineCap.Round },
            new Pen(White, 1.25) { DashPattern = [3, 0.75, 0.5, 0.75], LineCap = LineCap.Square, LineJoin = LineJoin.Round },
            new Pen(White, 0),

            // Wider than the curves are round: the chords are swept by the perpendicular to the curve
            new Pen(White, 7) { LineJoin = LineJoin.Round },
            new Pen(White, 1.5) { EndMarker = LineMarker.Arrow, StartMarker = LineMarker.Circle },
            new Pen(White, 1.5) { Alignment = PenAlignment.Inside },
            new Pen(White, 1.5) { Alignment = PenAlignment.Outside, DashPattern = [2, 1] },
        ];

        foreach (var pen in pens)
        {
            var widened = path.Widen(pen);

            // An outline with a side kept is on the grid of the combination of paths, 1/1024 of a pixel
            var near = pen.Alignment == PenAlignment.Center ? 1e-6 : 4.0 / 1024;
            var differences = 0;
            for (var y = -5.0; y <= 25; y += 0.2)
            {
                for (var x = -5.0; x <= 25; x += 0.2)
                {
                    var point = new PointD(x + 0.013, y + 0.007);
                    var expected = widened.Contains(point);
                    if (path.StrokeContains(point, pen) == expected)
                        continue;

                    // A point on the outline may be reported either way
                    differences++;
                    Assert.True(IsNearOutline(widened, point, expected, near), $"pen {pen.Width} {pen.LineCap} {pen.LineJoin} {pen.MiterLimit} {pen.DashPattern.Length} {pen.Alignment}, point ({point.X}, {point.Y})");
                }
            }

            Assert.InRange(differences, 0, 3);
        }

        static bool IsNearOutline(VectorPath path, PointD point, bool inside, double distance)
        {
            for (var i = 0; i < 8; i++)
            {
                var (sin, cos) = Math.SinCos(i * Math.PI / 4);
                if (path.Contains(new PointD(point.X + (distance * cos), point.Y + (distance * sin))) != inside)
                    return true;
            }

            return false;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AStrokeOfARandomPathContainsWhatItsWidenedPathContains(int seed)
    {
        // Long contours whose chords are near the point and far from it in turn: the chords far from the point are not
        // stroked, and those near it keep the joins and the curves of their neighbors
        var random = new Random(20261030 + seed);
        for (var iteration = 0; iteration < 25; iteration++)
        {
            var builder = new VectorPathBuilder();
            var contours = random.Next(1, 4);
            for (var contour = 0; contour < contours; contour++)
            {
                builder.MoveTo(Next());
                var segments = random.Next(1, 40);
                for (var segment = 0; segment < segments; segment++)
                {
                    switch (random.Next(6))
                    {
                        case 0:
                            builder.QuadraticBezierTo(Next(), Next());
                            break;

                        case 1:
                            builder.CubicBezierTo(Next(), Next(), Next());
                            break;

                        case 2:
                            builder.ArcTo(2 + (random.NextDouble() * 10), 2 + (random.NextDouble() * 10), random.Next(90), random.Next(2) == 0, random.Next(2) == 0, Next());
                            break;

                        case 3:
                            // A segment without length
                            var point = Next();
                            builder.LineTo(point).LineTo(point);
                            break;

                        default:
                            builder.LineTo(Next());
                            break;
                    }
                }

                switch (random.Next(3))
                {
                    case 0:
                        builder.Close();
                        break;

                    case 1:
                        // Segments after a closed contour start at its first point
                        builder.Close().LineTo(Next()).LineTo(Next());
                        break;
                }
            }

            var path = builder.Build();
            var pen = new Pen(White, random.NextDouble() * 6)
            {
                LineCap = (LineCap)random.Next(3),
                LineJoin = (LineJoin)random.Next(3),
                MiterLimit = 1 + (random.NextDouble() * 8),
                DashPattern = random.Next(4) == 0 ? [1 + (random.NextDouble() * 6), random.NextDouble() * 4] : [],
            };
            var widened = path.Widen(pen);
            for (var sample = 0; sample < 300; sample++)
            {
                // Points along the path, moved by up to the reach of the stroke, and points anywhere
                PointD position;
                if (sample % 3 != 0 && path.TryGetPointAtDistance(random.NextDouble() * path.GetLength(), out var onPath, out _))
                {
                    position = new PointD(onPath.X + ((random.NextDouble() - 0.5) * pen.Width * pen.MiterLimit), onPath.Y + ((random.NextDouble() - 0.5) * pen.Width * pen.MiterLimit));
                }
                else
                {
                    position = new PointD((random.NextDouble() * 60) - 10, (random.NextDouble() * 60) - 10);
                }

                var expected = widened.Contains(position);
                if (path.StrokeContains(position, pen) != expected)
                {
                    Assert.True(IsNearOutline(widened, position, expected), $"seed {seed}, iteration {iteration}, point ({position.X}, {position.Y})");
                }
            }
        }

        PointD Next() => new(random.NextDouble() * 40, random.NextDouble() * 40);

        static bool IsNearOutline(VectorPath path, PointD point, bool inside)
        {
            for (var i = 0; i < 8; i++)
            {
                var (sin, cos) = Math.SinCos(i * Math.PI / 4);
                if (path.Contains(new PointD(point.X + (1e-6 * cos), point.Y + (1e-6 * sin))) != inside)
                    return true;
            }

            return false;
        }
    }

    [Fact]
    public void AStrokeIsHitTestedWithoutCreatingItsPath()
    {
        // A spiral of many chords: the test of a point allocates the same small amount whatever the length of the path,
        // where widening it allocates its pieces
        static VectorPath CreateSpiral(int turns)
        {
            var builder = new VectorPathBuilder().MoveTo(500, 500);
            for (var i = 1; i <= turns * 64; i++)
            {
                var (sin, cos) = Math.SinCos(i * Math.PI / 32);
                builder.LineTo(500 + (i * 0.1 * cos), 500 + (i * 0.1 * sin));
            }

            return builder.Build();
        }

        var pen = new Pen(White, 2) { LineJoin = LineJoin.Round, LineCap = LineCap.Round };
        var small = CreateSpiral(2);
        var large = CreateSpiral(40);
        var point = new PointD(500.5, 507);
        Assert.Equal(large.Widen(pen).Contains(point), large.StrokeContains(point, pen));
        Assert.Equal(small.Widen(pen).Contains(point), small.StrokeContains(point, pen));

        var smallBytes = MeasureAllocations(() => small.StrokeContains(point, pen));
        var largeBytes = MeasureAllocations(() => large.StrokeContains(point, pen));
        var widenBytes = MeasureAllocations(() => large.Widen(pen).Contains(point));
        Assert.InRange(largeBytes, 0, smallBytes + 256);
        Assert.InRange(largeBytes * 100, 0, widenBytes);

        static long MeasureAllocations(Func<bool> action)
        {
            _ = action();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10; i++)
            {
                _ = action();
            }

            return (GC.GetAllocatedBytesForCurrentThread() - before) / 10;
        }
    }

    [Fact]
    public void TheSegmentsOfAPathAreWhatTheBuilderAdded()
    {
        var path = new VectorPathBuilder()
            .MoveTo(1, 2)
            .LineTo(3, 4)
            .QuadraticBezierTo(5, 6, 7, 8)
            .CubicBezierTo(9, 10, 11, 12, 13, 14)
            .Close()
            .MoveTo(10, 0)
            .ArcTo(10, 10, 0, largeArc: false, clockwise: true, 0, 10)
            .AddEllipse(new PointD(5, 5), 3, 2)
            .Build();
        PathSegment[] segments = [.. path.EnumerateSegments()];
        Assert.Equal(
            [
                "M1 2", "L3 4", "Q5 6 7 8", "C9 10 11 12 13 14", "Z",
                "M10 0", "A10 10 0 0 1 0 10",
                "M8 5", "A3 2 0 0 1 2 5", "A3 2 0 0 1 8 5", "Z",
            ],
            segments.Select(segment => segment.ToString()));
        Assert.Equal(PathSegmentKind.QuadraticBezierTo, segments[2].Kind);
        Assert.Equal(new PointD(5, 6), segments[2].Control1);
        Assert.Equal(new PointD(7, 8), segments[2].Point);
        Assert.Equal(new PointD(11, 12), segments[3].Control2);

        // The end of a closed contour gives the point the contour goes back to
        Assert.Equal(PathSegmentKind.Close, segments[4].Kind);
        Assert.Equal(new PointD(1, 2), segments[4].Point);
        Assert.Equal(new PointD(8, 5), segments[^1].Point);
        Assert.True(segments[6].IsClockwise);
        Assert.False(segments[6].IsLargeArc);
        Assert.Equal(segments[0], segments[0]);
        Assert.NotEqual(segments[8], segments[9]);
        Assert.Equal(segments[8].GetHashCode(), path.EnumerateSegments().ElementAt(8).GetHashCode());

        // An arc of more than a quarter turn comes in pieces, and an arc that turns the other way says so
        var arc = new VectorPathBuilder().AddArc(new PointD(0, 0), 4, 4, 0, -180).Build();
        PathSegment[] pieces = [.. arc.EnumerateSegments()];
        Assert.Equal([PathSegmentKind.MoveTo, PathSegmentKind.ArcTo, PathSegmentKind.ArcTo], pieces.Select(piece => piece.Kind));
        Assert.All(pieces[1..], piece => Assert.False(piece.IsClockwise));
        Assert.Equal(new PointD(4, 0), pieces[0].Point);
        Assert.Equal(new PointD(-4, 0), pieces[2].Point);

        // An ellipse that is turned gives its radii and its rotation
        var turned = new VectorPathBuilder().AddEllipse(new PointD(0, 0), 5, 2, 30).Build().EnumerateSegments().ElementAt(1);
        Assert.Equal(5, turned.RadiusX, 1e-12);
        Assert.Equal(2, turned.RadiusY, 1e-12);
        Assert.Equal(30, turned.Rotation, 1e-9);

        // A mirror makes a clockwise arc turn the other way, and an ellipse flattened to a segment is a segment
        var mirrored = new VectorPathBuilder().MoveTo(10, 0).ArcTo(10, 10, 0, largeArc: false, clockwise: true, 0, 10).Build().Transform(AffineTransform.CreateScale(-1, 1));
        Assert.False(mirrored.EnumerateSegments().ElementAt(1).IsClockwise);
        var flattened = new VectorPathBuilder().MoveTo(10, 0).ArcTo(10, 10, 0, largeArc: false, clockwise: true, 0, 10).Build().Transform(AffineTransform.CreateScale(1, 0));
        Assert.Equal(PathSegmentKind.LineTo, flattened.EnumerateSegments().ElementAt(1).Kind);

        Assert.Empty(new VectorPathBuilder().Build().EnumerateSegments());
        Assert.False(default(PathSegmentEnumerator).MoveNext());
        var enumerator = path.EnumerateSegments();
        Assert.True(enumerator.MoveNext());
        Assert.True(enumerator.MoveNext());
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(segments[0], enumerator.Current);
    }

    [Fact]
    public void APathBuiltFromItsSegmentsDrawsTheSamePixels()
    {
        var pen = new Pen(White, 1.5) { LineJoin = LineJoin.Round };
        VectorPath[] paths =
        [
            new VectorPathBuilder().MoveTo(1, 1).LineTo(6, 2).QuadraticBezierTo(8, 6, 3, 7).CubicBezierTo(0, 6, 2, 3, 1, 5).Close().Build(),
            new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0.5, 1, 7, 5.5), new CornerRadii(1, 2.5, 0.5, 2)).Build(),
            new VectorPathBuilder().AddEllipse(new PointD(4, 4), 3.5, 2).AddEllipse(new PointD(4, 4), 3, 1.5, 35).Build(),
            new VectorPathBuilder().MoveTo(1, 4).ArcTo(4, 2.5, 20, largeArc: true, clockwise: false, 6.5, 5).Build(),
            new VectorPathBuilder().AddPie(new PointD(4, 4), 3.5, 3, 20, 290).Build().Transform(AffineTransform.CreateSkew(15, -10).Then(AffineTransform.CreateScale(-1, 1, new PointD(4, 4)))),
        ];
        foreach (var path in paths)
        {
            var builder = new VectorPathBuilder();
            foreach (var segment in path.EnumerateSegments())
            {
                _ = segment.Kind switch
                {
                    PathSegmentKind.MoveTo => builder.MoveTo(segment.Point),
                    PathSegmentKind.LineTo => builder.LineTo(segment.Point),
                    PathSegmentKind.QuadraticBezierTo => builder.QuadraticBezierTo(segment.Control1, segment.Point),
                    PathSegmentKind.CubicBezierTo => builder.CubicBezierTo(segment.Control1, segment.Control2, segment.Point),
                    PathSegmentKind.ArcTo => builder.ArcTo(segment.RadiusX, segment.RadiusY, segment.Rotation, segment.IsLargeArc, segment.IsClockwise, segment.Point),
                    _ => builder.Close(),
                };
            }

            var rebuilt = builder.Build();
            AssertClosePixels(canvas => canvas.Fill(White, path), canvas => canvas.Fill(White, rebuilt));
            AssertClosePixels(canvas => canvas.Stroke(pen, path), canvas => canvas.Stroke(pen, rebuilt));
        }
    }

    [Fact]
    public void AReversedPathGoesThroughTheSamePointsFromItsEnd()
    {
        // An open contour starts where it ended; a closed one keeps its first point
        var path = new VectorPathBuilder()
            .MoveTo(1, 2).LineTo(3, 4).QuadraticBezierTo(5, 6, 7, 8).CubicBezierTo(9, 10, 11, 12, 13, 14)
            .AddPolygon([new PointD(0, 0), new PointD(4, 0), new PointD(4, 4)])
            .Build();
        var reversed = path.Reverse();
        Assert.Equal(
            ["M0 0", "L4 4", "L4 0", "Z", "M13 14", "C11 12 9 10 7 8", "Q5 6 3 4", "L1 2"],
            reversed.EnumerateSegments().Select(segment => segment.ToString()));
        Assert.Equal(path.Bounds, reversed.Bounds);

        // Reversed twice, a path is itself
        Assert.Equal(path.EnumerateSegments().ToArray(), reversed.Reverse().EnumerateSegments().ToArray());
        var empty = new VectorPathBuilder().Build();
        Assert.Same(empty, empty.Reverse());

        // A closed contour that ends on a curve gets its closing segment first
        var closed = new VectorPathBuilder().MoveTo(0, 0).LineTo(4, 0).QuadraticBezierTo(4, 4, 2, 4).Close().Build();
        Assert.Equal(["M0 0", "L2 4", "Q4 4 4 0", "Z"], closed.Reverse().EnumerateSegments().Select(segment => segment.ToString()));

        // The same chords, the other way, whatever the elements
        var pen = new Pen(White, 1.25) { LineCap = LineCap.Round, DashPattern = [2, 1.5] };
        VectorPath[] shapes =
        [
            new VectorPathBuilder().MoveTo(1, 6).CubicBezierTo(2, -3, 6, 9, 7, 1).Build(),
            new VectorPathBuilder().AddEllipse(new PointD(4, 4), 3.5, 2).Build(),
            new VectorPathBuilder().AddEllipse(new PointD(4, 4), 3, 1.5, 35).Build(),
            new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0.5, 1, 7, 5.5), new CornerRadii(1, 2.5, 0.5, 2)).Build(),
            new VectorPathBuilder().MoveTo(1, 4).ArcTo(4, 2.5, 20, largeArc: true, clockwise: false, 6.5, 5).LineTo(7, 7).Build(),
        ];
        foreach (var shape in shapes)
        {
            var other = shape.Reverse();
            var length = shape.GetLength();
            Assert.Equal(length, other.GetLength(), 1e-9);
            AssertClosePixels(canvas => canvas.Fill(White, shape), canvas => canvas.Fill(White, other));
            for (var i = 0; i <= 8; i++)
            {
                // The middle of a chord has one direction; a vertex has the direction of the chord that starts there
                var distance = length * ((2 * i) + 1) / 18;
                Assert.True(shape.TryGetPointAtDistance(distance, out var point, out var tangent));
                Assert.True(other.TryGetPointAtDistance(length - distance, out var otherPoint, out var otherTangent));
                Assert.Equal(point.X, otherPoint.X, 1e-9);
                Assert.Equal(point.Y, otherPoint.Y, 1e-9);
                Assert.Equal(tangent.X, -otherTangent.X, 1e-6);
                Assert.Equal(tangent.Y, -otherTangent.Y, 1e-6);
            }

            // The dashes start from the other end
            Assert.NotEqual(Render(canvas => canvas.Stroke(pen, shape)), Render(canvas => canvas.Stroke(pen, other)));
        }
    }

    [Fact]
    public void APartOfAPathIsWhatLiesBetweenTwoDistances()
    {
        var line = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Build();
        Assert.Equal(["M5 0", "L10 0", "L10 5"], Describe(line.Trim(5, 15)));
        Assert.Equal(["M0 0", "L10 0", "L10 10"], Describe(line.Trim(-5, 100)));
        Assert.Equal(["M0 0", "L10 0"], Describe(line.Trim(0, 10)));
        Assert.Equal(["M10 0", "L10 10"], Describe(line.Trim(10, double.PositiveInfinity)));
        Assert.Equal(["M2.5 0", "L3 0"], Describe(line.Trim(2.5, 3)));
        Assert.True(line.Trim(3, 3).IsEmpty);
        Assert.True(line.Trim(7, 3).IsEmpty);
        Assert.True(line.Trim(20, 30).IsEmpty);
        Assert.True(line.Trim(-3, 0).IsEmpty);
        Assert.True(new VectorPathBuilder().Build().Trim(0, 10).IsEmpty);
        Assert.True(new VectorPathBuilder().MoveTo(1, 1).Build().Trim(0, 10).IsEmpty);
        Assert.Throws<ArgumentException>(() => line.Trim(double.NaN, 1));
        Assert.Throws<ArgumentException>(() => line.Trim(0, double.NaN));

        // A closed contour that is kept entirely stays closed; one that is cut is open
        var square = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 10, 10)).Build();
        Assert.Equal(["M0 0", "L10 0", "L10 10", "L0 10", "Z"], Describe(square.Trim(0, 40)));
        Assert.Equal(["M5 0", "L10 0", "L10 10", "L0 10", "L0 5"], Describe(square.Trim(5, 35)));
        Assert.Equal(["M0 0", "L10 0", "L10 10", "L0 10", "L0 0.5"], Describe(square.Trim(0, 39.5)));

        // The contours follow each other, without the gap between them
        var two = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).AddRectangle(new RectangleD(20, 20, 10, 10)).MoveTo(50, 50).LineTo(50, 60).Build();
        Assert.Equal(["M8 0", "L10 0", "M20 20", "L30 20", "L30 30", "L20 30", "Z", "M50 50", "L50 54"], Describe(two.Trim(8, 54)));
        Assert.Equal(["M25 20", "L30 20", "L30 25"], Describe(two.Trim(15, 25)));

        // The parts of a curve add up to the curve, and have the length that was asked
        var curve = new VectorPathBuilder().MoveTo(1, 6).CubicBezierTo(2, -3, 6, 9, 7, 1).AddEllipse(new PointD(4, 4), 3, 1.5, 35).Build();
        var length = curve.GetLength();
        Assert.Equal(length / 3, curve.Trim(length / 4, length * 7 / 12).GetLength(), 1e-9);
        Assert.Equal(length, curve.Trim(0, length / 3).GetLength() + curve.Trim(length / 3, length).GetLength(), 1e-9);
        Assert.True(curve.Trim(length / 5, length / 2).TryGetPointAtDistance(0, out var start, out _));
        Assert.True(curve.TryGetPointAtDistance(length / 5, out var expected, out _));
        Assert.Equal(expected.X, start.X, 1e-9);
        Assert.Equal(expected.Y, start.Y, 1e-9);

        // The vertices of the chords are vertices of the part: the whole part is the polygon the path is drawn with. Its
        // stroke is the stroke of that polygon, whose vertices are corners: the stroke of the curve follows the curve.
        var whole = curve.Trim(0, length);
        Assert.Equal(Flatten(curve), Flatten(whole));
        AssertSamePixels(canvas => canvas.Fill(White, curve), canvas => canvas.Fill(White, whole));

        static string[] Describe(VectorPath path) => [.. path.EnumerateSegments().Select(segment => segment.ToString())];
    }

    [Theory]
    [InlineData("M1 2L3 4Q5 6 7 8C9 10 11 12 13 14Z", "M1 2 L3 4 Q5 6 7 8 C9 10 11 12 13 14 Z")]
    [InlineData("m1 2l2 2h3v-1z", "M1 2 L3 4 L6 4 L6 3 Z")]
    [InlineData("M1 2H5V7h-1v-2", "M1 2 L5 2 L5 7 L4 7 L4 5")]
    [InlineData("M0 0 10 0 10 10", "M0 0 L10 0 L10 10")]
    [InlineData("m1 1 2 2 3 3", "M1 1 L3 3 L6 6")]
    [InlineData("M.5.5-1-1", "M0.5 0.5 L-1 -1")]
    [InlineData("M1e1,2E-1L+1,+2.", "M10 0.2 L1 2")]
    [InlineData(" \tM 1,2 \r\n L 3 , 4 \f", "M1 2 L3 4")]
    [InlineData("M0 0C1 2 3 4 5 6S9 10 11 12", "M0 0 C1 2 3 4 5 6 C7 8 9 10 11 12")]
    [InlineData("M0 0c1 2 3 4 5 6s4 4 6 6", "M0 0 C1 2 3 4 5 6 C7 8 9 10 11 12")]
    [InlineData("M1 1S1 2 3 4", "M1 1 C1 1 1 2 3 4")]
    [InlineData("M0 0Q1 2 3 4S5 6 7 8", "M0 0 Q1 2 3 4 C3 4 5 6 7 8")]
    [InlineData("M0 0Q1 2 3 4T7 8T9 9", "M0 0 Q1 2 3 4 Q5 6 7 8 Q9 10 9 9")]
    [InlineData("M0 0q1 2 3 4t4 4", "M0 0 Q1 2 3 4 Q5 6 7 8")]
    [InlineData("M0 0L5 5T7 8", "M0 0 L5 5 Q5 5 7 8")]
    [InlineData("M0 0C1 2 3 4 5 6T7 8", "M0 0 C1 2 3 4 5 6 Q5 6 7 8")]
    [InlineData("M10 0A10 10 0 0 1 0 10", "M10 0 A10 10 0 0 1 0 10")]
    [InlineData("M10 0a10 10 0 01-10 10", "M10 0 A10 10 0 0 1 0 10")]
    [InlineData("M10 0a-10-10 0 0,1,-10,10", "M10 0 A10 10 0 0 1 0 10")]
    [InlineData("M10 0A0 5 0 0 1 0 10", "M10 0 L0 10")]
    [InlineData("M1 1L5 1L5 5ZL9 9", "M1 1 L5 1 L5 5 Z M1 1 L9 9")]
    [InlineData("M1 1L5 1zm2 2l1 0", "M1 1 L5 1 Z M3 3 L4 3")]
    [InlineData("M1 1 M2 2", "M1 1 M2 2")]
    [InlineData("", "")]
    [InlineData(" \n ", "")]
    public void PathDataOfSvgIsReadAsItsCommands(string data, string expected)
    {
        var path = VectorPath.ParseSvgPathData(data);
        Assert.Equal(expected, path.ToSvgPathData());
        Assert.Equal(expected, VectorPath.ParseSvgPathData(data.AsSpan()).ToSvgPathData());
        Assert.True(VectorPath.TryParseSvgPathData(data, out var parsed));
        Assert.Equal(expected, parsed.ToSvgPathData());
        Assert.True(VectorPath.TryParseSvgPathData(data.AsSpan(), out parsed));
        Assert.Equal(expected, parsed.ToSvgPathData());

        // What is written is read back as the same path
        Assert.Equal(path.EnumerateSegments().ToArray(), VectorPath.ParseSvgPathData(expected).EnumerateSegments().ToArray());
    }

    [Theory]
    [InlineData("L1 2", 0)]
    [InlineData("1 2", 0)]
    [InlineData("M1", 2)]
    [InlineData("M1 2 3", 6)]
    [InlineData("M1 2 X", 5)]
    [InlineData("M1 2 L", 6)]
    [InlineData("M1 2,", 5)]
    [InlineData("M1 2 L 3 4,", 11)]
    [InlineData("M,1 2", 1)]
    [InlineData("M1,,2", 3)]
    [InlineData("M1 2 Z 3", 7)]
    [InlineData("M1 2 Z,", 6)]
    [InlineData("M1e999 0", 1)]
    [InlineData("M1 2e", 4)]
    [InlineData("M- 2", 1)]
    [InlineData("M. 2", 1)]
    [InlineData("M0 0A1 1 0 2 0 5 5", 11)]
    [InlineData("M0 0A1 1 0 0 5 5", 13)]
    [InlineData("M0 0L16777217 0", 5)]
    [InlineData("M16777216 0l1 0", 12)]
    [InlineData("M0 0A16777216 16777216 0 1 1 1 0", 5)]
    [InlineData("M0 0A1e9 1 0 0 1 1 0", 5)]
    [InlineData("M0 0 B1 2", 5)]
    public void PathDataThatIsNotValidIsRejectedWithThePlaceOfTheError(string data, int position)
    {
        var exception = Assert.Throws<FormatException>(() => VectorPath.ParseSvgPathData(data));
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"at index {position}:"), exception.Message);
        Assert.Throws<FormatException>(() => VectorPath.ParseSvgPathData(data.AsSpan()));
        Assert.False(VectorPath.TryParseSvgPathData(data, out var path));
        Assert.Null(path);
        Assert.False(VectorPath.TryParseSvgPathData(data.AsSpan(), out path));
        Assert.Null(path);
    }

    [Fact]
    public void APathIsWrittenAsPathDataThatReadsBackAsThePath()
    {
        Assert.Throws<ArgumentNullException>(() => VectorPath.ParseSvgPathData(null!));
        Assert.False(VectorPath.TryParseSvgPathData(null, out _));
        Assert.Equal(string.Empty, new VectorPathBuilder().Build().ToSvgPathData());

        // Numbers are written with the digits that give them back, and without the separator of a culture
        var precise = new VectorPathBuilder().MoveTo(0.1 + 0.2, 1e-7).LineTo(-123456.75, 1.0 / 3).Build();
        Assert.Equal("M0.30000000000000004 1E-07 L-123456.75 0.3333333333333333", precise.ToSvgPathData());
        Assert.Equal(precise.EnumerateSegments().ToArray(), VectorPath.ParseSvgPathData(precise.ToSvgPathData()).EnumerateSegments().ToArray());

        // An ellipse is two arcs, and an arc reads back as the same arc
        Assert.Equal("M8 5 A3 2 0 0 1 2 5 A3 2 0 0 1 8 5 Z", new VectorPathBuilder().AddEllipse(new PointD(5, 5), 3, 2).Build().ToSvgPathData());
        var pen = new Pen(White, 1.5) { LineJoin = LineJoin.Round };
        VectorPath[] paths =
        [
            new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0.5, 1, 7, 5.5), new CornerRadii(1, 2.5, 0.5, 2)).Build(),
            new VectorPathBuilder().AddEllipse(new PointD(4, 4), 3.5, 2).AddEllipse(new PointD(4, 4), 3, 1.5, 35).Build(),
            new VectorPathBuilder().MoveTo(1, 4).ArcTo(4, 2.5, 20, largeArc: true, clockwise: false, 6.5, 5).Build(),
            new VectorPathBuilder().AddPie(new PointD(4, 4), 3.5, 3, 20, 290).Build().Transform(AffineTransform.CreateSkew(15, -10).Then(AffineTransform.CreateScale(-1, 1, new PointD(4, 4)))),
        ];
        foreach (var path in paths)
        {
            var read = VectorPath.ParseSvgPathData(path.ToSvgPathData());
            AssertClosePixels(canvas => canvas.Fill(White, path), canvas => canvas.Fill(White, read));
            AssertClosePixels(canvas => canvas.Stroke(pen, path), canvas => canvas.Stroke(pen, read));
        }

        // The path data of an icon, as it is found in an SVG file
        var heart = VectorPath.ParseSvgPathData("M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z");
        AssertRectangle(RectangleD.FromLTRB(2, 3, 22, 21.35), heart.GetTightBounds(), 1e-9);
        Assert.True(heart.Contains(new PointD(12, 12)));
        Assert.True(heart.Contains(new PointD(5, 6)));
        Assert.False(heart.Contains(new PointD(12, 3.5)));
        Assert.False(heart.Contains(new PointD(4, 19)));
        Assert.Equal(PathSegmentKind.Close, heart.EnumerateSegments().Last().Kind);
    }

    [Fact]
    public void TwoPathsAreCombinedIntoTheOutlineOfWhatIsKeptOfTheirAreas()
    {
        var first = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 10, 10)).Build();
        var second = new VectorPathBuilder().AddRectangle(new RectangleD(5, 5, 10, 10)).Build();
        Assert.Equal("M0 0 L10 0 L10 5 L15 5 L15 15 L5 15 L5 10 L0 10 Z", first.Combine(second, PathCombineMode.Union).ToSvgPathData());
        Assert.Equal("M5 5 L10 5 L10 10 L5 10 Z", first.Combine(second, PathCombineMode.Intersect).ToSvgPathData());
        Assert.Equal("M0 0 L10 0 L10 5 L5 5 L5 10 L0 10 Z", first.Combine(second, PathCombineMode.Exclude).ToSvgPathData());
        Assert.Equal("M10 5 L15 5 L15 15 L5 15 L5 10 L10 10 Z", second.Combine(first, PathCombineMode.Exclude).ToSvgPathData());

        // What only one of them fills: two contours that touch at two vertices, and do not cross
        Assert.Equal("M0 0 L10 0 L10 5 L5 5 L5 10 L0 10 Z M10 5 L15 5 L15 15 L5 15 L5 10 L10 10 Z", first.Combine(second, PathCombineMode.Xor).ToSvgPathData());

        // The way the contours of the paths turn does not matter: each path is an area
        Assert.Equal("M5 5 L10 5 L10 10 L5 10 Z", first.Reverse().Combine(second, PathCombineMode.Intersect).ToSvgPathData());
        Assert.Equal("M0 0 L10 0 L10 5 L15 5 L15 15 L5 15 L5 10 L0 10 Z", first.Combine(second.Reverse(), PathCombineMode.Union).ToSvgPathData());

        // A hole turns the other way: both fill rules leave it out
        var inner = new VectorPathBuilder().AddRectangle(new RectangleD(3, 3, 4, 4)).Build();
        var ring = first.Combine(inner, PathCombineMode.Exclude);
        Assert.Equal("M0 0 L10 0 L10 10 L0 10 Z M3 3 L3 7 L7 7 L7 3 Z", ring.ToSvgPathData());
        Assert.False(ring.Contains(new PointD(5, 5)));
        Assert.False(ring.Contains(new PointD(5, 5), FillRule.EvenOdd));
        Assert.True(ring.Contains(new PointD(1, 5)));
        Assert.Equal(first.ToSvgPathData(), first.Combine(inner, PathCombineMode.Union).ToSvgPathData());
        Assert.Equal(inner.ToSvgPathData(), first.Combine(inner, PathCombineMode.Intersect).ToSvgPathData());
        Assert.True(inner.Combine(first, PathCombineMode.Exclude).IsEmpty);

        // Paths that share a side, entirely or in part, and paths that only touch
        var beside = new VectorPathBuilder().AddRectangle(new RectangleD(10, 2, 5, 4)).Build();
        Assert.Equal("M0 0 L10 0 L10 2 L15 2 L15 6 L10 6 L10 10 L0 10 Z", first.Combine(beside, PathCombineMode.Union).ToSvgPathData());
        Assert.True(first.Combine(beside, PathCombineMode.Intersect).IsEmpty);
        Assert.Equal(first.ToSvgPathData(), first.Combine(beside, PathCombineMode.Exclude).ToSvgPathData());
        Assert.Equal(first.ToSvgPathData(), first.Combine(first, PathCombineMode.Union).ToSvgPathData());
        Assert.Equal(first.ToSvgPathData(), first.Combine(first, PathCombineMode.Intersect).ToSvgPathData());
        Assert.True(first.Combine(first, PathCombineMode.Xor).IsEmpty);

        // Paths that do not meet, and paths without area
        var away = new VectorPathBuilder().AddRectangle(new RectangleD(20, 0, 5, 5)).Build();
        var empty = new VectorPathBuilder().Build();
        Assert.Equal("M0 0 L10 0 L10 10 L0 10 Z M20 0 L25 0 L25 5 L20 5 Z", first.Combine(away, PathCombineMode.Union).ToSvgPathData());
        Assert.True(first.Combine(away, PathCombineMode.Intersect).IsEmpty);
        Assert.Equal(first.ToSvgPathData(), first.Combine(empty, PathCombineMode.Union).ToSvgPathData());
        Assert.Equal(first.ToSvgPathData(), empty.Combine(first, PathCombineMode.Xor).ToSvgPathData());
        Assert.True(empty.Combine(first, PathCombineMode.Exclude).IsEmpty);
        Assert.True(first.Combine(new VectorPathBuilder().MoveTo(2, 2).LineTo(8, 8).Build(), PathCombineMode.Intersect).IsEmpty);

        // The fill rule says what each path fills: two squares in one path leave a hole with the even-odd rule
        var nested = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 10, 10)).AddRectangle(new RectangleD(3, 3, 4, 4)).Build();
        Assert.Equal(first.ToSvgPathData(), nested.Combine(empty, PathCombineMode.Union).ToSvgPathData());
        Assert.Equal(ring.ToSvgPathData(), nested.Combine(empty, PathCombineMode.Union, FillRule.EvenOdd).ToSvgPathData());

        // Each path can have a rule of its own: the square with the ring that the even-odd rule makes of the nested
        // squares, and with the whole square that the non-zero rule makes of them
        Assert.Equal(ring.ToSvgPathData(), first.Combine(nested, PathCombineMode.Intersect, FillRule.NonZero, FillRule.EvenOdd).ToSvgPathData());
        Assert.Equal("M3 3 L7 3 L7 7 L3 7 Z", first.Combine(nested, PathCombineMode.Exclude, FillRule.NonZero, FillRule.EvenOdd).ToSvgPathData());
        Assert.True(first.Combine(nested, PathCombineMode.Exclude, FillRule.EvenOdd, FillRule.NonZero).IsEmpty);
        Assert.Equal(ring.ToSvgPathData(), nested.Combine(first, PathCombineMode.Intersect, FillRule.EvenOdd, FillRule.NonZero).ToSvgPathData());

        // A star of five branches drawn as one contour that crosses itself: its center is filled with the non-zero rule
        // and not with the even-odd one, whatever the rule of the square it is combined with
        var star = new VectorPathBuilder().AddPolygon([new PointD(50, 5), new PointD(79, 95), new PointD(3, 39), new PointD(97, 39), new PointD(21, 95)]).Build();
        var center = new VectorPathBuilder().AddRectangle(new RectangleD(45, 50, 10, 10)).Build();
        var centerPoint = new PointD(50, 55);
        Assert.True(star.Combine(center, PathCombineMode.Xor, FillRule.EvenOdd, FillRule.NonZero).Contains(centerPoint));
        Assert.False(star.Combine(center, PathCombineMode.Xor, FillRule.NonZero, FillRule.EvenOdd).Contains(centerPoint));
        Assert.True(center.Combine(star, PathCombineMode.Exclude, FillRule.NonZero, FillRule.EvenOdd).Contains(centerPoint));
        Assert.False(center.Combine(star, PathCombineMode.Exclude, FillRule.EvenOdd, FillRule.NonZero).Contains(centerPoint));
        Assert.True(star.Combine(center, PathCombineMode.Union, FillRule.EvenOdd, FillRule.NonZero).Contains(new PointD(30, 45)));
        Assert.Equal(star.Combine(center, PathCombineMode.Union, FillRule.EvenOdd, FillRule.EvenOdd).ToSvgPathData(), star.Combine(center, PathCombineMode.Union, FillRule.EvenOdd, FillRule.NonZero).ToSvgPathData());

        Assert.Throws<ArgumentNullException>(() => first.Combine(null!, PathCombineMode.Union));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Combine(second, (PathCombineMode)4));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Combine(second, PathCombineMode.Union, (FillRule)2));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Combine(second, PathCombineMode.Union, (FillRule)2, FillRule.NonZero));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Combine(second, PathCombineMode.Union, FillRule.NonZero, (FillRule)2));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Combine(second, (PathCombineMode)4, FillRule.NonZero, FillRule.NonZero));
        Assert.Throws<ArgumentNullException>(() => first.Combine(null!, PathCombineMode.Union, FillRule.NonZero, FillRule.EvenOdd));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Simplify((FillRule)2));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => first.Combine(second, PathCombineMode.Union, cancellationToken: canceled.Token));
        Assert.Throws<OperationCanceledException>(() => first.Simplify(cancellationToken: canceled.Token));
    }

    [Fact]
    public void ASimplifiedPathIsTheOutlineOfWhatThePathFills()
    {
        // A contour that crosses itself: two triangles that touch at the crossing
        var bow = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(10, 10), new PointD(10, 0), new PointD(0, 10)]).Build();
        Assert.Equal("M0 0 L5 5 L0 10 Z M10 0 L10 10 L5 5 Z", bow.Simplify().ToSvgPathData());

        // A star of five branches: one outline of ten vertices, or five branches around a hole with the even-odd rule
        var star = new VectorPathBuilder().AddPolygon([new PointD(50, 5), new PointD(79, 95), new PointD(3, 39), new PointD(97, 39), new PointD(21, 95)]).Build();
        var outline = star.Simplify();
        Assert.Equal([PathSegmentKind.MoveTo, .. Enumerable.Repeat(PathSegmentKind.LineTo, 9), PathSegmentKind.Close], outline.EnumerateSegments().Select(segment => segment.Kind));
        Assert.True(outline.Contains(new PointD(50, 50), FillRule.EvenOdd));
        var branches = star.Simplify(FillRule.EvenOdd);
        Assert.Equal(5, branches.EnumerateSegments().Count(segment => segment.Kind == PathSegmentKind.Close));
        Assert.False(branches.Contains(new PointD(50, 50)));
        Assert.True(branches.Contains(new PointD(50, 15)));

        // Contours that overlap become one, and what is already an outline stays as it is
        var overlapping = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 10, 10)).AddRectangle(new RectangleD(5, 5, 10, 10)).Build();
        Assert.Equal("M0 0 L10 0 L10 5 L15 5 L15 15 L5 15 L5 10 L0 10 Z", overlapping.Simplify().ToSvgPathData());
        Assert.Equal("M0 0 L10 0 L10 5 L15 5 L15 15 L5 15 L5 10 L0 10 Z", overlapping.Simplify().Simplify().ToSvgPathData());

        // The outer contours turn clockwise on the image whatever the path does, and an open contour is closed
        Assert.Equal("M0 0 L10 0 L10 10 L0 10 Z", new VectorPathBuilder().MoveTo(0, 0).LineTo(0, 10).LineTo(10, 10).LineTo(10, 0).Build().Simplify().ToSvgPathData());
        Assert.True(new VectorPathBuilder().Build().Simplify().IsEmpty);
        Assert.True(new VectorPathBuilder().MoveTo(1, 1).LineTo(5, 5).Build().Simplify().IsEmpty);

        // The vertices are on a grid of 1/1024 of a pixel
        Assert.Equal("M0 0 L1.0009765625 0 L0 1 Z", new VectorPathBuilder().AddPolygon([new PointD(0.0003, 0), new PointD(1.0012, 0.0001), new PointD(0, 1)]).Build().Simplify().ToSvgPathData());

        // The pieces of a stroke become its outline: one contour for an open line, two around a closed one
        var pen = new Pen(White, 2) { LineJoin = LineJoin.Round, LineCap = LineCap.Round };
        var line = new VectorPathBuilder().MoveTo(1.5, 6).CubicBezierTo(2, -3, 5, 9, 6.5, 2).Build();
        var widened = line.Widen(pen);
        Assert.True(widened.EnumerateSegments().Count(segment => segment.Kind == PathSegmentKind.Close) > 3);
        var stroke = widened.Simplify();
        Assert.Equal(1, stroke.EnumerateSegments().Count(segment => segment.Kind == PathSegmentKind.Close));
        AssertClosePixels(canvas => canvas.Stroke(pen, line), canvas => canvas.Fill(White, stroke));
        AssertClosePixels(canvas => canvas.Stroke(pen, line), canvas => canvas.Fill(White, stroke, FillRule.EvenOdd));
        var circle = new VectorPathBuilder().AddCircle(new PointD(4, 4), 2.5).Build();
        Assert.Equal(2, circle.Widen(pen).Simplify().EnumerateSegments().Count(segment => segment.Kind == PathSegmentKind.Close));
    }

    [Fact]
    public void APathThatCrossesItselfEverywhereIsCutUntilNoSegmentsCross()
    {
        // 200 sides that cross in thousands of places: the first pass makes many more segments than the path has, and
        // its cuts, moved to the grid, make segments meet that the next pass cuts
        var random = new Random(0);
        var path = new VectorPathBuilder().AddPolygon([.. Enumerable.Range(0, 200).Select(_ => new PointD(random.NextDouble() * 100, random.NextDouble() * 100))]).Build();
        var outline = path.Simplify(cancellationToken: Ct);
        Assert.Equal(outline.ToSvgPathData(), outline.Simplify(cancellationToken: Ct).ToSvgPathData());
        AssertOutlineFillsThePath(path, outline);
        Assert.Equal(outline.ToSvgPathData(), Internals.PathCombiner.Combine(path, second: null, PathCombineMode.Union, FillRule.NonZero, maxPasses: 2, Ct).ToSvgPathData());

        // Snapped when the passes are spent: an outline that does not cross either
        AssertOutlineFillsThePath(path, Internals.PathCombiner.Combine(path, second: null, PathCombineMode.Union, FillRule.NonZero, maxPasses: 1, Ct));
        AssertOutlineFillsThePath(path, Internals.PathCombiner.Combine(path, second: null, PathCombineMode.Union, FillRule.NonZero, maxPasses: 0, Ct));
        var bow = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(10, 10), new PointD(10, 0), new PointD(0, 10)]).Build();
        Assert.Equal("M0 0 L5 5 L0 10 Z M10 0 L10 10 L5 5 Z", Internals.PathCombiner.Combine(bow, second: null, PathCombineMode.Union, FillRule.NonZero, maxPasses: 0, Ct).ToSvgPathData());
    }

    [Fact]
    public void SegmentsAlmostOnTheSameLineAreSnappedWhenCuttingThemDoesNotEnd()
    {
        // Thin triangles whose long sides are almost on the same line: each cut turns a piece across its neighbors far
        // from the cut, and every pass makes more cuts than the one before
        var random = new Random(0);
        var builder = new VectorPathBuilder();
        for (var i = 0; i < 50; i++)
        {
            var start = 50 + (random.NextDouble() * 0.01);
            builder.MoveTo(0, start).LineTo(100, start + (random.NextDouble() * 0.2)).LineTo(100, 60 + (i * 0.001)).Close();
        }

        var path = builder.Build();
        AssertOutlineFillsThePath(path, path.Simplify(cancellationToken: Ct));
        AssertOutlineFillsThePath(path, path.Simplify(FillRule.EvenOdd, Ct), FillRule.EvenOdd);
        var other = new VectorPathBuilder().AddRectangle(new RectangleD(20, 40, 60, 15)).Build();
        var expected = RenderFill(path, FillRule.NonZero);
        var inOther = RenderFill(other, FillRule.NonZero);
        var intersection = RenderFill(path.Combine(other, PathCombineMode.Intersect, cancellationToken: Ct), FillRule.EvenOdd);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(intersection[i], Math.Min(expected[i], inOther[i]) - 2, Math.Min(expected[i], inOther[i]) + 2, message: $"pixel ({i % 100}, {i / 100})");
        }
    }

    [Theory]
    [InlineData(PathCombineMode.Union)]
    [InlineData(PathCombineMode.Intersect)]
    [InlineData(PathCombineMode.Exclude)]
    [InlineData(PathCombineMode.Xor)]
    public void ACombinedPathContainsThePointsItsModeKeeps(PathCombineMode mode)
    {
        var random = new Random(20261013 + (int)mode);
        for (var iteration = 0; iteration < 60; iteration++)
        {
            var fillRule = iteration % 3 == 0 ? FillRule.EvenOdd : FillRule.NonZero;

            // Each path can have a rule of its own
            var otherFillRule = iteration % 4 == 1 ? FillRule.EvenOdd : iteration % 4 == 2 ? FillRule.NonZero : fillRule;
            var first = CreateShape(random);
            var second = CreateShape(random);
            var combined = otherFillRule == fillRule ? first.Combine(second, mode, fillRule, Ct) : first.Combine(second, mode, fillRule, otherFillRule, Ct);

            // The outline does not cross itself: both fill rules agree, and it is its own outline
            var simplified = combined.Simplify(cancellationToken: Ct);
            Assert.Equal(combined.ToSvgPathData(), simplified.ToSvgPathData());
            for (var sample = 0; sample < 200; sample++)
            {
                var point = new PointD((random.NextDouble() * 44) - 2, (random.NextDouble() * 44) - 2);

                // A point next to an outline can be on either side of its chords once they are on the grid
                if (IsNearOutline(first, point, fillRule) || IsNearOutline(second, point, otherFillRule))
                    continue;

                var inFirst = first.Contains(point, fillRule);
                var inSecond = second.Contains(point, otherFillRule);
                var expected = mode switch
                {
                    PathCombineMode.Union => inFirst || inSecond,
                    PathCombineMode.Intersect => inFirst && inSecond,
                    PathCombineMode.Exclude => inFirst && !inSecond,
                    _ => inFirst != inSecond,
                };
                Assert.Equal(expected, combined.Contains(point), $"iteration {iteration}, point ({point.X}, {point.Y})");
                Assert.Equal(expected, combined.Contains(point, FillRule.EvenOdd), $"iteration {iteration}, point ({point.X}, {point.Y}), even-odd");
            }
        }

        static bool IsNearOutline(VectorPath path, PointD point, FillRule fillRule)
        {
            var inside = path.Contains(point, fillRule);
            for (var i = 0; i < 8; i++)
            {
                var (sin, cos) = Math.SinCos(i * Math.PI / 4);
                if (path.Contains(new PointD(point.X + (0.01 * cos), point.Y + (0.01 * sin)), fillRule) != inside)
                    return true;
            }

            return false;
        }

        static VectorPath CreateShape(Random random)
        {
            // Polygons that cross themselves, curves, ellipses and rounded rectangles, on halves and on any position
            var builder = new VectorPathBuilder();
            var contours = random.Next(1, 4);
            for (var contour = 0; contour < contours; contour++)
            {
                switch (random.Next(5))
                {
                    case 0:
                        builder.AddEllipse(Next(random), 2 + (random.NextDouble() * 12), 2 + (random.NextDouble() * 12), random.Next(180));
                        break;

                    case 1:
                        builder.AddRoundedRectangle(new RectangleD(random.Next(30), random.Next(30), 4 + random.Next(20), 4 + random.Next(20)), new CornerRadii(random.Next(6), random.Next(6), random.Next(6), random.Next(6)));
                        break;

                    case 2:
                        builder.MoveTo(Next(random)).CubicBezierTo(Next(random), Next(random), Next(random)).QuadraticBezierTo(Next(random), Next(random)).Close();
                        break;

                    case 3:
                        // Whole coordinates: sides on the same lines, vertices on sides
                        builder.AddPolygon([.. Enumerable.Range(0, random.Next(3, 8)).Select(_ => new PointD(random.Next(8) * 5, random.Next(8) * 5))]);
                        break;

                    default:
                        builder.AddPolygon([.. Enumerable.Range(0, random.Next(3, 9)).Select(_ => Next(random))]);
                        break;
                }
            }

            return builder.Build();
        }

        static PointD Next(Random random) => new(random.NextDouble() * 40, random.NextDouble() * 40);
    }

    [Fact]
    public void CombinedCurvesKeepTheAreasOfTheirShapes()
    {
        // Two circles of radius 20 whose centers are 20 apart: what they share is two segments of a circle
        var first = new VectorPathBuilder().AddCircle(new PointD(30, 30), 20).Build();
        var second = new VectorPathBuilder().AddCircle(new PointD(50, 30), 20).Build();
        var disk = Math.PI * 400;
        var lens = (800 * Math.Acos(0.5)) - (10 * Math.Sqrt(1200));
        Assert.Equal(lens, GetArea(first.Combine(second, PathCombineMode.Intersect)), 3.0);
        Assert.Equal((2 * disk) - lens, GetArea(first.Combine(second, PathCombineMode.Union)), 8.0);
        Assert.Equal(disk - lens, GetArea(first.Combine(second, PathCombineMode.Exclude)), 6.0);
        Assert.Equal(2 * (disk - lens), GetArea(first.Combine(second, PathCombineMode.Xor)), 12.0);

        // What is kept and what is left out make the first path
        var kept = GetArea(first.Combine(second, PathCombineMode.Intersect)) + GetArea(first.Combine(second, PathCombineMode.Exclude));
        Assert.Equal(GetArea(first.Simplify()), kept, 0.01);

        static double GetArea(VectorPath path)
        {
            using var image = new Image<Gray16>(80, 60, new Gray16(0));
            image.Draw(canvas => canvas.Fill(White, path), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]).Sum(sample => (long)sample) / 65535.0;
        }
    }

    [Fact]
    public void TheClosestPointOfAPathIsOnItsChordsWithItsDistanceAlongThePath()
    {
        // Two sides of a square: the nearest point of a side, of the corner, or of an end
        var corner = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Build();
        AssertClosest(corner, new PointD(4, 3), new PointD(4, 0), 4);
        AssertClosest(corner, new PointD(4, -3), new PointD(4, 0), 4);
        AssertClosest(corner, new PointD(12, 5), new PointD(10, 5), 15);
        AssertClosest(corner, new PointD(13, -4), new PointD(10, 0), 10);
        AssertClosest(corner, new PointD(-3, -4), new PointD(0, 0), 0);
        AssertClosest(corner, new PointD(20, 20), new PointD(10, 10), 20);
        AssertClosest(corner, new PointD(10, 7), new PointD(10, 7), 17);

        // As near to both sides: the first one along the path
        AssertClosest(corner, new PointD(5, 5), new PointD(5, 0), 5);
        AssertClosest(corner.Reverse(), new PointD(5, 5), new PointD(10, 5), 5);

        // A closed contour has the segment that closes it, and the contours follow each other without the gap
        // between them: 20 for the two sides, then the diagonal from its end
        var triangle = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Close().Build();
        AssertClosest(triangle, new PointD(4, 5), new PointD(4.5, 4.5), 20 + (5.5 * Math.Sqrt(2)));
        var two = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).MoveTo(0, 5).LineTo(10, 5).Build();
        AssertClosest(two, new PointD(3, 4), new PointD(3, 5), 13);
        AssertClosest(two, new PointD(3, 2), new PointD(3, 0), 3);

        // A curve is its chords: the point is within 1/16 of a pixel of the circle, and the distance of the arc
        var circle = new VectorPathBuilder().AddCircle(new PointD(50, 50), 40).Build();
        Assert.True(circle.TryGetClosestPoint(new PointD(50, 150), out var bottom, out var quarter));
        Assert.Equal(50, bottom.X, 0.0625);
        Assert.Equal(90, bottom.Y, 0.0625);
        Assert.Equal(Math.PI * 20, quarter, 0.1);
        Assert.True(circle.TryGetClosestPoint(new PointD(50, 52), out var inside, out _));
        Assert.InRange(inside.DistanceTo(new PointD(50, 50)), 40 - 0.0625, 40);

        // The point at a distance is the nearest point to itself, at that distance
        var curve = new VectorPathBuilder().MoveTo(2, 9).CubicBezierTo(3, -4, 9, 16, 13, 2).LineTo(20, 2).Build();
        var length = curve.GetLength();
        for (var i = 0; i <= 16; i++)
        {
            Assert.True(curve.TryGetPointAtDistance(length * i / 16, out var point, out _));
            Assert.True(curve.TryGetClosestPoint(point, out var closest, out var distance));
            Assert.Equal(point.X, closest.X, 1e-9);
            Assert.Equal(point.Y, closest.Y, 1e-9);
            Assert.Equal(length * i / 16, distance, 1e-9);
        }

        // A path without length has no point; a position that is not one is rejected
        Assert.False(new VectorPathBuilder().Build().TryGetClosestPoint(new PointD(1, 1), out var none, out var zero));
        Assert.Equal(default, none);
        Assert.Equal(0, zero);
        Assert.False(new VectorPathBuilder().MoveTo(3, 3).LineTo(3, 3).Build().TryGetClosestPoint(new PointD(1, 1), out _, out _));
        Assert.Throws<ArgumentException>(() => corner.TryGetClosestPoint(new PointD(double.NaN, 0), out _, out _));
        Assert.Throws<ArgumentException>(() => corner.TryGetClosestPoint(new PointD(0, double.PositiveInfinity), out _, out _));

        static void AssertClosest(VectorPath path, PointD point, PointD expected, double expectedDistance)
        {
            Assert.True(path.TryGetClosestPoint(point, out var closest, out var distance));
            Assert.Equal(expected.X, closest.X, 1e-12);
            Assert.Equal(expected.Y, closest.Y, 1e-12);
            Assert.Equal(expectedDistance, distance, 1e-12);
        }
    }

    [Fact]
    public void RoundedCornersAreArcsOfACircleThatTouchesBothSides()
    {
        // A rectangle gets the corners of a rounded rectangle: each corner is cut 2 from its vertex, and the contour
        // starts where the arc of its first corner ends
        var rectangle = new VectorPathBuilder().AddRectangle(new RectangleD(1, 1, 6, 5)).Build();
        var rounded = rectangle.RoundCorners(2);
        Assert.Equal(new RectangleD(1, 1, 6, 5), rounded.GetTightBounds());
        Assert.Equal(
            ["MoveTo (3, 1)", "LineTo (5, 1)", "ArcTo (7, 3)", "LineTo (7, 4)", "ArcTo (5, 6)", "LineTo (3, 6)", "ArcTo (1, 4)", "LineTo (1, 3)", "ArcTo (3, 1)", "Close"],
            Describe(rounded));
        Assert.All(rounded.EnumerateSegments().Where(segment => segment.Kind == PathSegmentKind.ArcTo), arc =>
        {
            Assert.Equal(2, arc.RadiusX, 1e-12);
            Assert.Equal(2, arc.RadiusY, 1e-12);
            Assert.True(arc.IsClockwise);
            Assert.False(arc.IsLargeArc);
        });
        AssertClosePixels(canvas => canvas.Fill(White, new VectorPathBuilder().AddRoundedRectangle(new RectangleD(1, 1, 6, 5), 2).Build()), canvas => canvas.Fill(White, rounded));

        // The other way around, the arcs turn the other way
        Assert.All(rectangle.Reverse().RoundCorners(2).EnumerateSegments().Where(segment => segment.Kind == PathSegmentKind.ArcTo), arc => Assert.False(arc.IsClockwise));
        AssertClosePixels(canvas => canvas.Fill(White, rounded), canvas => canvas.Fill(White, rectangle.Reverse().RoundCorners(2)));

        // A radius that is too large for a side is the one that reaches the middle of the shorter side: a square
        // becomes a circle, and the two corners of a side share its middle, exactly
        var square = new VectorPathBuilder().AddRectangle(new RectangleD(1, 1, 6, 6)).Build();
        Assert.Equal(["MoveTo (4, 1)", "ArcTo (7, 4)", "ArcTo (4, 7)", "ArcTo (1, 4)", "ArcTo (4, 1)", "Close"], Describe(square.RoundCorners(100)));
        AssertClosePixels(canvas => canvas.FillCircle(White, new PointD(4, 4), 3), canvas => canvas.Fill(White, square.RoundCorners(100)));
        AssertClosePixels(canvas => canvas.Fill(White, new VectorPathBuilder().AddRoundedRectangle(new RectangleD(1, 1, 6, 5), 2.5).Build()), canvas => canvas.Fill(White, rectangle.RoundCorners(100)));

        // The circle touches both sides at r tan(turn / 2) from the vertex: 3 for a radius of 4 in the corner of a
        // 3-4-5 triangle whose sides make an angle of 2 atan(4 / 3) there... the right angle is cut at 4, and the
        // center of each arc is at the radius from both sides
        var triangle = new VectorPathBuilder().MoveTo(0, 0).LineTo(40, 0).LineTo(0, 30).Close().Build();
        var soft = triangle.RoundCorners(4);
        var segments = soft.EnumerateSegments().ToArray();
        Assert.Equal(PathSegmentKind.MoveTo, segments[0].Kind);
        Assert.Equal(new PointD(4, 0), segments[0].Point);
        var arcs = segments.Where(segment => segment.Kind == PathSegmentKind.ArcTo).ToArray();
        Assert.All(arcs, arc => Assert.Equal(4, arc.RadiusX, 1e-9));

        // Inside the triangle, the circle of radius 4 that touches the two sides of the right angle has its center
        // at (4, 4): the arc of that corner ends at (4, 0) and starts at (0, 4)
        Assert.Equal(new PointD(4, 0), arcs[^1].Point);
        Assert.Contains(segments, segment => segment.Kind == PathSegmentKind.LineTo && segment.Point == new PointD(0, 4));
        Assert.True(soft.Contains(new PointD(2, 2)));
        Assert.False(soft.Contains(new PointD(0.5, 0.5)));
        Assert.False(soft.Contains(new PointD(38, 0.5)));
        Assert.True(soft.Contains(new PointD(20, 1)));

        // Every point of the result is in the triangle, and at the radius from its three corners' circles or on its sides
        Assert.All(Flatten(soft), point =>
        {
            Assert.InRange(point.X, -1e-9, 40);
            Assert.InRange(point.Y, -1e-9, 30);
            Assert.InRange((point.X / 40) + (point.Y / 30), 0, 1 + 1e-9);
        });

        // An open contour keeps its ends, and what is not a corner between two straight segments is kept: a curve,
        // a turn back, a segment that goes on
        var open = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Build();
        Assert.Equal(["MoveTo (0, 0)", "LineTo (7, 0)", "ArcTo (10, 3)"], Describe(open.RoundCorners(3))[..3]);
        Assert.Equal(new PointD(10, 10), open.RoundCorners(3).EnumerateSegments().Last().Point);
        var kept = new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).QuadraticBezierTo(15, 0, 15, 5).LineTo(15, 10).LineTo(15, 20).LineTo(15, 12).Build();
        Assert.Equal(Describe(kept), Describe(kept.RoundCorners(2)));
        var ellipse = new VectorPathBuilder().AddEllipse(new PointD(5, 5), 4, 2).AddEllipse(new PointD(5, 5), 4, 2, 30).AddArc(new PointD(3, 3), 2, 2, 0, 200).Build();
        Assert.Equal(ellipse.ToSvgPathData(), ellipse.RoundCorners(1).ToSvgPathData());

        // A contour that ends where it started, then is closed, has a corner there too
        var explicitly = new VectorPathBuilder().MoveTo(1, 1).LineTo(7, 1).LineTo(7, 6).LineTo(1, 6).LineTo(1, 1).Close().Build();
        AssertClosePixels(canvas => canvas.Fill(White, rounded), canvas => canvas.Fill(White, explicitly.RoundCorners(2)));

        // The stroke of the result has no spike where two arcs meet
        using (var image = new Image<Gray8>(16, 16, new Gray8(0)))
        {
            var pen = new Pen(White, 2);
            image.Draw(canvas => canvas.Stroke(pen, new VectorPathBuilder().AddRectangle(new RectangleD(4, 4, 8, 8)).Build().RoundCorners(100)), Ct);
            var samples = DrawingTestSupport.ReadSamples(image.Frames[0]);
            Assert.Equal(0, samples[0]);
            Assert.Equal(0, samples[(1 * 16) + 8]);
            Assert.Equal(0, samples[(8 * 16) + 1]);
            Assert.Equal(Math.PI * (25 - 9), samples.Sum() / 255.0, 0.5);
        }

        // Nothing to round
        Assert.Same(rectangle, rectangle.RoundCorners(0));
        var empty = new VectorPathBuilder().Build();
        Assert.Same(empty, empty.RoundCorners(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.RoundCorners(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.RoundCorners(16777217));
        Assert.Throws<ArgumentException>(() => rectangle.RoundCorners(double.NaN));

        static string[] Describe(VectorPath path) => [.. path.EnumerateSegments().Select(segment => segment.Kind is PathSegmentKind.Close ? "Close" : string.Create(CultureInfo.InvariantCulture, $"{segment.Kind} ({Math.Round(segment.Point.X, 9, MidpointRounding.AwayFromZero)}, {Math.Round(segment.Point.Y, 9, MidpointRounding.AwayFromZero)})"))];
    }

    [Fact]
    public void AMeasureOfAPathAnswersAsThePathDoes()
    {
        VectorPath[] paths =
        [
            new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Build(),
            new VectorPathBuilder().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Close().MoveTo(20, 0).LineTo(20, 0).LineTo(25, 5).Build(),
            new VectorPathBuilder()
                .MoveTo(2, 2).LineTo(14, 3).LineTo(5, 9).QuadraticBezierTo(15, 12, 13, 17).Close()
                .MoveTo(1, 14).CubicBezierTo(4, 10, 6, 20, 9, 15)
                .AddArc(new PointD(10, 10), 4, 3, 200, 120)
                .AddEllipse(new PointD(30, 20), 12, 5, 30)
                .MoveTo(19, 19)
                .Build(),

            // Many chords, in many groups, that go back over each other: the nearest points of a position are found
            // in several groups, and the first one along the path is given
            CreateZigzag(),
        ];

        var random = new Random(20261030);
        foreach (var path in paths)
        {
            var measure = new VectorPathMeasure(path);
            Assert.Same(path, measure.Path);
            Assert.Equal(path.GetLength(), measure.Length);
            double[] distances = [double.NegativeInfinity, -1, 0, 5, 10, 10.5, 20, measure.Length, measure.Length + 1, double.PositiveInfinity, .. Enumerable.Range(0, 500).Select(_ => random.NextDouble() * measure.Length)];
            foreach (var distance in distances)
            {
                Assert.Equal(path.TryGetPointAtDistance(distance, out var expectedPoint, out var expectedTangent), measure.TryGetPointAtDistance(distance, out var point, out var tangent));
                Assert.Equal(expectedPoint, point, $"distance {distance}");
                Assert.Equal(expectedTangent, tangent, $"distance {distance}");
            }

            for (var i = 0; i < 500; i++)
            {
                // Positions anywhere, and positions on whole and half coordinates, which are as near to several chords
                var position = i % 2 == 0 ? new PointD((random.NextDouble() * 60) - 10, (random.NextDouble() * 40) - 10) : new PointD(random.Next(-4, 60) / 2.0, random.Next(-4, 40) / 2.0);
                Assert.Equal(path.TryGetClosestPoint(position, out var expectedClosest, out var expectedDistance), measure.TryGetClosestPoint(position, out var closest, out var distance));
                Assert.Equal(expectedClosest, closest, $"position ({position.X}, {position.Y})");
                Assert.Equal(expectedDistance, distance, $"position ({position.X}, {position.Y})");
            }

            Assert.Throws<ArgumentException>(() => measure.TryGetPointAtDistance(double.NaN, out _, out _));
            Assert.Throws<ArgumentException>(() => measure.TryGetClosestPoint(new PointD(double.NaN, 0), out _, out _));
        }

        // The first of two nearest points that are in different groups of chords
        var zigzag = new VectorPathMeasure(paths[^1]);
        Assert.True(zigzag.TryGetClosestPoint(new PointD(30.25, 3), out var first, out var along));
        Assert.Equal(new PointD(30.25, 0), first);
        Assert.Equal(30.25, along);

        // A path without length has no point
        foreach (var empty in new[] { new VectorPathBuilder().Build(), new VectorPathBuilder().MoveTo(1, 1).LineTo(1, 1).Build() })
        {
            var measure = new VectorPathMeasure(empty);
            Assert.Equal(0d, measure.Length);
            Assert.False(measure.TryGetPointAtDistance(0, out _, out _));
            Assert.False(measure.TryGetClosestPoint(new PointD(1, 1), out _, out _));
        }

        Assert.Throws<ArgumentNullException>(() => new VectorPathMeasure(null!));

        static VectorPath CreateZigzag()
        {
            // Along the x axis to 100 and back, one unit at a time
            var builder = new VectorPathBuilder().MoveTo(0, 0);
            for (var x = 1; x <= 100; x++)
            {
                builder.LineTo(x, 0);
            }

            for (var x = 99; x >= 0; x--)
            {
                builder.LineTo(x, 0);
            }

            return builder.Build();
        }
    }

    [Fact]
    public void AnOffsetPathIsTheOutlineOfItsAreaMovedByADistance()
    {
        // With miter corners, a rectangle stays a rectangle, larger or smaller by the distance on every side
        var rectangle = new VectorPathBuilder().AddRectangle(new RectangleD(10, 10, 20, 10)).Build();
        Assert.Equal("M8 8 L32 8 L32 22 L8 22 Z", rectangle.Offset(2).ToSvgPathData());
        Assert.Equal("M12 12 L28 12 L28 18 L12 18 Z", rectangle.Offset(-2).ToSvgPathData());
        Assert.Equal("M10 10 L30 10 L30 20 L10 20 Z", rectangle.Offset(0).ToSvgPathData());
        Assert.Equal("M8 8 L32 8 L32 22 L8 22 Z", rectangle.Reverse().Offset(2).ToSvgPathData());

        // Bevel corners are cut, and round ones are within the distance of the corner
        Assert.Equal("M10 8 L30 8 L32 10 L32 20 L30 22 L10 22 L8 20 L8 10 Z", rectangle.Offset(2, LineJoin.Bevel).ToSvgPathData());
        var round = rectangle.Offset(4, LineJoin.Round);
        Assert.Equal(RectangleD.FromLTRB(6, 6, 34, 24), round.GetTightBounds());
        Assert.Equal((28 * 18) - ((4 - Math.PI) * 16), GetArea(round), 0.5);
        Assert.False(round.Contains(new PointD(6.5, 6.5)));
        Assert.True(round.Contains(new PointD(7.5, 7.5)));

        // A miter longer than the limit allows is beveled: a corner of 30 degrees has a miter of 1 / sin(15 degrees) =
        // 3.86 times the width. The tip of the miter is 2 / sin(15 degrees) from the corner, along its bisector; the
        // bevel goes from 2 above the corner to 2 along the normal of the other side, (1, sqrt(3)) from the corner.
        var wedge = new VectorPathBuilder().MoveTo(0, 0).LineTo(40, 0).LineTo(40 - (40 * Math.Cos(Math.PI / 6)), 40 * Math.Sin(Math.PI / 6)).Close().Build().Transform(AffineTransform.CreateTranslation(20, 20));
        Assert.Equal(60 + (2 / Math.Tan(Math.PI / 12)), wedge.Offset(2, LineJoin.Miter, miterLimit: 4).GetTightBounds().Right, 0.01);
        Assert.Equal(61, wedge.Offset(2, LineJoin.Miter, miterLimit: 3).GetTightBounds().Right, 0.01);
        Assert.Equal(61, wedge.Offset(2, LineJoin.Bevel).GetTightBounds().Right, 0.01);

        // A hole gets smaller when the shape grows and larger when it shrinks, whatever the rule that makes it a hole
        var frame = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 30, 30)).AddRectangle(new RectangleD(10, 10, 10, 10)).Build().Transform(AffineTransform.CreateTranslation(5, 5));
        Assert.Equal("M3 3 L37 3 L37 37 L3 37 Z M17 17 L17 23 L23 23 L23 17 Z", frame.Offset(2, fillRule: FillRule.EvenOdd).ToSvgPathData());
        Assert.Equal("M7 7 L33 7 L33 33 L7 33 Z M13 13 L13 27 L27 27 L27 13 Z", frame.Offset(-2, fillRule: FillRule.EvenOdd).ToSvgPathData());
        Assert.Equal("M0 0 L40 0 L40 40 L0 40 Z", frame.Offset(5, fillRule: FillRule.EvenOdd).ToSvgPathData());
        Assert.True(frame.Offset(-5, fillRule: FillRule.EvenOdd).IsEmpty);

        // The contours inside the area are no part of its boundary: with the non-zero rule, the inner square is
        // filled, and the shape shrinks as the outer square does
        Assert.Equal("M7 7 L33 7 L33 33 L7 33 Z", frame.Offset(-2).ToSvgPathData());

        // Two shapes that overlap are moved as the one shape they fill: the notch between them has no band cut in it
        var cross = new VectorPathBuilder().AddRectangle(new RectangleD(20, 10, 10, 30)).AddRectangle(new RectangleD(10, 20, 30, 10)).Build();
        Assert.Equal("M22 12 L28 12 L28 22 L38 22 L38 28 L28 28 L28 38 L22 38 L22 28 L12 28 L12 22 L22 22 Z", cross.Offset(-2).ToSvgPathData());
        Assert.Equal(cross.Simplify().Offset(3).ToSvgPathData(), cross.Offset(3).ToSvgPathData());

        // Each bar is 16 by 36 once it has grown by 3, and they share a square of 16
        Assert.Equal(500, GetArea(cross), 0.01);
        Assert.Equal((2 * 16 * 36) - (16 * 16), GetArea(cross.Offset(3)), 0.01);

        // A shape that grows, then shrinks by as much, comes back; one that shrinks loses what is too narrow
        Assert.Equal(cross.Simplify().ToSvgPathData(), cross.Offset(3).Offset(-3).ToSvgPathData());
        Assert.Equal("M24 14 L26 14 L26 24 L36 24 L36 26 L26 26 L26 36 L24 36 L24 26 L14 26 L14 24 L24 24 Z", cross.Offset(-4).ToSvgPathData());
        Assert.True(cross.Offset(-5).IsEmpty);
        Assert.True(cross.Offset(-7).IsEmpty);

        // With round corners, what is left of the middle of the cross is what is further than the distance from
        // the four notches: the bars are gone, the middle is not
        var middle = cross.Offset(-5, LineJoin.Round);
        Assert.True(middle.Contains(new PointD(25, 25)));
        Assert.False(middle.Contains(new PointD(25, 15)));
        Assert.InRange(GetArea(middle), 1, 100 - (25 * Math.PI) + 1);

        // A curve stays round, whatever the join: a small circle that grows a lot is a circle, not the polygon of
        // its chords with pointed corners
        var dot = new VectorPathBuilder().AddCircle(new PointD(40, 40), 2).Build();
        foreach (var join in new[] { LineJoin.Miter, LineJoin.Round, LineJoin.Bevel })
        {
            var grown = dot.Offset(30, join);
            Assert.All(Flatten(grown), point => Assert.InRange(Math.Sqrt(((point.X - 40) * (point.X - 40)) + ((point.Y - 40) * (point.Y - 40))), 32 - 0.13, 32 + 0.01));
            Assert.Equal(Math.PI * 32 * 32, GetArea(grown), 16.0);
        }

        Assert.Equal(Math.PI * 15 * 15, GetArea(new VectorPathBuilder().AddCircle(new PointD(40, 40), 20).Build().Offset(-5)), 3.0);

        // A rounded rectangle keeps its round corners and gets the corner radius plus the distance
        var card = new VectorPathBuilder().AddRoundedRectangle(new RectangleD(20, 20, 30, 20), 4).Build();
        Assert.Equal(GetArea(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(14, 14, 42, 32), 10).Build()), GetArea(card.Offset(6)), 1.5);
        Assert.Equal(GetArea(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(22, 22, 26, 16), 2).Build()), GetArea(card.Offset(-2)), 1.0);

        // An open contour is closed as when it is filled; a path that fills nothing has no outline
        Assert.Equal("M8 8 L32 8 L32 22 L8 22 Z", new VectorPathBuilder().MoveTo(10, 10).LineTo(30, 10).LineTo(30, 20).LineTo(10, 20).Build().Offset(2).ToSvgPathData());
        Assert.True(new VectorPathBuilder().MoveTo(2, 2).LineTo(8, 8).Build().Offset(3).IsEmpty);
        Assert.True(new VectorPathBuilder().Build().Offset(3).IsEmpty);

        Assert.Throws<ArgumentException>(() => rectangle.Offset(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Offset(16777217));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Offset(-16777217));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Offset(16777216));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Offset(1, (LineJoin)3));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Offset(1, miterLimit: 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Offset(1, miterLimit: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => rectangle.Offset(1, fillRule: (FillRule)2));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => rectangle.Offset(1, cancellationToken: canceled.Token));

        static double GetArea(VectorPath path)
        {
            using var image = new Image<Gray16>(80, 80, new Gray16(0));
            image.Draw(canvas => canvas.Fill(White, path), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]).Sum(sample => (long)sample) / 65535.0;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AnOffsetPathContainsWhatIsWithinTheDistanceOfTheShape(int seed)
    {
        // Random polygons, moved outward and inward with round corners: a point is inside the result when it is
        // within the distance of the shape, or further than the distance from what is outside the shape
        var random = new Random(seed);
        var builder = new VectorPathBuilder();
        for (var shape = 0; shape < 3; shape++)
        {
            var points = new PointD[3 + random.Next(4)];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = new PointD(10 + (random.NextDouble() * 40), 10 + (random.NextDouble() * 40));
            }

            builder.AddPolygon(points);
        }

        var path = builder.Build();
        var outline = path.Simplify();
        const double Distance = 3;
        var larger = path.Offset(Distance, LineJoin.Round);
        var smaller = path.Offset(-Distance, LineJoin.Round);
        var edges = Flatten(outline);
        Assert.NotEmpty(edges);
        for (var i = 0; i < 2000; i++)
        {
            var point = new PointD(random.NextDouble() * 60, random.NextDouble() * 60);
            var isInside = path.Contains(point);
            Assert.True(outline.TryGetClosestPoint(point, out var closest, out _));
            var distance = point.DistanceTo(closest);

            // A point too near the boundary of the result is left out: the chords of its round corners are within
            // 1/16 of a pixel of them
            if (Math.Abs(distance - Distance) < 0.07)
                continue;

            Assert.Equal(isInside || distance < Distance, larger.Contains(point));
            Assert.Equal(isInside && distance > Distance, smaller.Contains(point));
        }
    }

    private static void AssertRectangle(RectangleD expected, RectangleD actual, double tolerance)
    {
        Assert.Equal(expected.Left, actual.Left, tolerance);
        Assert.Equal(expected.Top, actual.Top, tolerance);
        Assert.Equal(expected.Right, actual.Right, tolerance);
        Assert.Equal(expected.Bottom, actual.Bottom, tolerance);
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

    [Fact]
    public void ACurveThroughPointsIsACubicCurveBetweenEachPointAndTheNextOne()
    {
        // The control points of the curve from P1 to P2 are P1 + tension (P2 - P0) / 3 and P2 - tension (P3 - P1) / 3;
        // the first point is its own previous point, and the last one its own next point.
        //   from (0, 0) to (3, 3): (0, 0) + ((3, 3) - (0, 0)) / 6 = (0.5, 0.5), (3, 3) - ((6, 0) - (0, 0)) / 6 = (2, 3)
        //   from (3, 3) to (6, 0): (3, 3) + ((6, 0) - (0, 0)) / 6 = (4, 3), (6, 0) - ((6, 0) - (3, 3)) / 6 = (5.5, 0.5)
        PointD[] points = [new(0, 0), new(3, 3), new(6, 0)];
        var curve = new VectorPathBuilder().AddCurve(points).Build();
        Assert.Equal(
            [
                new PathSegment(PathSegmentKind.MoveTo, new PointD(0, 0)),
                new PathSegment(PathSegmentKind.CubicBezierTo, new PointD(3, 3), new PointD(0.5, 0.5), new PointD(2, 3)),
                new PathSegment(PathSegmentKind.CubicBezierTo, new PointD(6, 0), new PointD(4, 3), new PointD(5.5, 0.5)),
            ],
            GetSegments(curve));
        Assert.Equal(RectangleD.FromLTRB(0, 0, 6, 3), curve.Bounds);

        // A closed curve goes around: the point before the first one is the last one
        //   from (6, 0) to (0, 0): (6, 0) + ((0, 0) - (3, 3)) / 6 = (5.5, -0.5), (0, 0) - ((3, 3) - (6, 0)) / 6 = (0.5, -0.5)
        var closed = GetSegments(new VectorPathBuilder().AddClosedCurve(points).Build());
        Assert.HasCount(5, closed);
        Assert.Equal(new PathSegment(PathSegmentKind.CubicBezierTo, new PointD(3, 3), new PointD(-0.5, 0.5), new PointD(2, 3)), closed[1]);
        Assert.Equal(new PathSegment(PathSegmentKind.CubicBezierTo, new PointD(0, 0), new PointD(5.5, -0.5), new PointD(0.5, -0.5)), closed[3]);
        Assert.Equal(PathSegmentKind.Close, closed[4].Kind);

        // The tension scales the distance of the control points; without tension, they are the points
        var tight = GetSegments(new VectorPathBuilder().AddCurve(points, 0).Build());
        Assert.Equal(new PathSegment(PathSegmentKind.CubicBezierTo, new PointD(3, 3), new PointD(0, 0), new PointD(3, 3)), tight[1]);
        var loose = GetSegments(new VectorPathBuilder().AddCurve(points, 1).Build());
        Assert.Equal(new PathSegment(PathSegmentKind.CubicBezierTo, new PointD(3, 3), new PointD(1, 1), new PointD(1, 3)), loose[1]);

        // A curve continues the builder as a contour of its own, and a single point is a contour without length
        var builder = new VectorPathBuilder().MoveTo(9, 9).LineTo(8, 8);
        Assert.Same(builder, builder.AddCurve([new PointD(1, 1)]));
        Assert.Equal([new PathSegment(PathSegmentKind.MoveTo, new PointD(9, 9)), new PathSegment(PathSegmentKind.LineTo, new PointD(8, 8)), new PathSegment(PathSegmentKind.MoveTo, new PointD(1, 1))], GetSegments(builder.Build()));
        Assert.HasCount(2, GetSegments(new VectorPathBuilder().AddClosedCurve([new PointD(1, 1)]).Build()));

        // A curve from the current point continues its contour: the same curve as through that point and the others
        var continued = GetSegments(new VectorPathBuilder().MoveTo(0, 5).LineTo(0, 0).CurveTo(points.AsSpan(1)).LineTo(6, 5).Close().Build());
        Assert.HasCount(6, continued);
        Assert.Equal(GetSegments(curve)[1], continued[2]);
        Assert.Equal(GetSegments(curve)[2], continued[3]);
        Assert.Equal(new PathSegment(PathSegmentKind.LineTo, new PointD(6, 5)), continued[4]);
        Assert.Throws<InvalidOperationException>(() => new VectorPathBuilder().CurveTo(points));
        Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).CurveTo([]));
        Assert.Throws<ArgumentException>(() => new VectorPathBuilder().MoveTo(0, 0).CurveTo([new PointD(double.NaN, 0)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().MoveTo(0, 0).CurveTo(points, 2));

        Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddCurve([]));
        Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddClosedCurve([]));
        Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddCurve([new PointD(0, double.PositiveInfinity)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddCurve([new PointD(0, 16777217)]));
        Assert.Throws<ArgumentException>(() => new VectorPathBuilder().AddCurve(points, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddCurve(points, -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddClosedCurve(points, 1.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VectorPathBuilder().AddClosedCurve([new PointD(-16777216, 0), new PointD(16777216, 0), new PointD(16777216, 5)], 1));

        static List<PathSegment> GetSegments(VectorPath path)
        {
            var segments = new List<PathSegment>();
            foreach (var segment in path.EnumerateSegments())
            {
                segments.Add(segment);
            }

            return segments;
        }
    }

    /// <summary>Gets the pixels a drawing gives on a black image of 8 by 8 pixels.</summary>
    private static int[] Render(DrawingAction action)
    {
        using var image = new Image<Gray8>(8, 8, new Gray8(0));
        image.Draw(action, Ct);
        return DrawingTestSupport.ReadSamples(image.Frames[0]);
    }

    /// <summary>Checks that two drawings give the same pixels, to one level of gray: the same shape described in two ways.</summary>
    /// <summary>Asserts that an outline paints what a path paints, with both fill rules, and that its edges only meet at their ends.</summary>
    private static void AssertOutlineFillsThePath(VectorPath path, VectorPath outline, FillRule fillRule = FillRule.NonZero)
    {
        AssertEdgesOnlyMeetAtTheirEnds(outline);
        var expected = RenderFill(path, fillRule);
        var nonZero = RenderFill(outline, FillRule.NonZero);
        var evenOdd = RenderFill(outline, FillRule.EvenOdd);
        Assert.Contains(expected, sample => sample != 0);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(nonZero[i], expected[i] - 2, expected[i] + 2, message: $"pixel ({i % 100}, {i / 100})");
            Assert.InRange(evenOdd[i], expected[i] - 2, expected[i] + 2, message: $"pixel ({i % 100}, {i / 100}), even-odd");
        }
    }

    /// <summary>Asserts that no two edges of an outline, whose vertices are on the grid of 1/1024 of a pixel, cross, overlap, or have an end on the other one.</summary>
    private static void AssertEdgesOnlyMeetAtTheirEnds(VectorPath outline)
    {
        var edges = new List<(long X0, long Y0, long X1, long Y1)>();
        (long X, long Y) start = default;
        (long X, long Y) current = default;
        foreach (var segment in outline.EnumerateSegments())
        {
            var point = ((long)Math.Round(segment.Point.X * 1024, MidpointRounding.AwayFromZero), (long)Math.Round(segment.Point.Y * 1024, MidpointRounding.AwayFromZero));
            switch (segment.Kind)
            {
                case PathSegmentKind.MoveTo:
                    start = current = point;
                    break;

                case PathSegmentKind.LineTo:
                    edges.Add((current.X, current.Y, point.Item1, point.Item2));
                    current = point;
                    break;

                case PathSegmentKind.Close:
                    edges.Add((current.X, current.Y, start.X, start.Y));
                    current = start;
                    break;
            }
        }

        edges.Sort((a, b) => Math.Min(a.X0, a.X1).CompareTo(Math.Min(b.X0, b.X1)));
        for (var i = 0; i < edges.Count; i++)
        {
            var a = edges[i];
            for (var j = i + 1; j < edges.Count && Math.Min(edges[j].X0, edges[j].X1) <= Math.Max(a.X0, a.X1); j++)
            {
                var b = edges[j];
                if (Math.Min(b.Y0, b.Y1) > Math.Max(a.Y0, a.Y1) || Math.Max(b.Y0, b.Y1) < Math.Min(a.Y0, a.Y1))
                    continue;

                var isSame = (a.X0, a.Y0, a.X1, a.Y1) == (b.X0, b.Y0, b.X1, b.Y1) || (a.X0, a.Y0, a.X1, a.Y1) == (b.X1, b.Y1, b.X0, b.Y0);
                var crosses = Side(a, b.X0, b.Y0) * Side(a, b.X1, b.Y1) < 0 && Side(b, a.X0, a.Y0) * Side(b, a.X1, a.Y1) < 0;
                Assert.False(isSame || crosses || IsInside(a, b.X0, b.Y0) || IsInside(a, b.X1, b.Y1) || IsInside(b, a.X0, a.Y0) || IsInside(b, a.X1, a.Y1), $"edges {a} and {b}");
            }
        }

        static int Side((long X0, long Y0, long X1, long Y1) edge, long x, long y)
            => (((Int128)(edge.X1 - edge.X0) * (y - edge.Y0)) - ((Int128)(edge.Y1 - edge.Y0) * (x - edge.X0))).CompareTo(Int128.Zero);

        static bool IsInside((long X0, long Y0, long X1, long Y1) edge, long x, long y)
        {
            if (Side(edge, x, y) != 0 || (x, y) == (edge.X0, edge.Y0) || (x, y) == (edge.X1, edge.Y1))
                return false;

            return Math.Min(edge.X0, edge.X1) <= x && x <= Math.Max(edge.X0, edge.X1) && Math.Min(edge.Y0, edge.Y1) <= y && y <= Math.Max(edge.Y0, edge.Y1);
        }
    }

    private static int[] RenderFill(VectorPath path, FillRule fillRule)
    {
        using var image = new Image<Gray8>(100, 100, new Gray8(0));
        image.Draw(canvas => canvas.Fill(White, path, fillRule), Ct);
        return DrawingTestSupport.ReadSamples(image.Frames[0]);
    }

    private static void AssertClosePixels(DrawingAction first, DrawingAction second)
    {
        var expected = Render(first);
        var actual = Render(second);
        Assert.Contains(expected, sample => sample != 0);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(actual[i], expected[i] - 1, expected[i] + 1, message: $"pixel ({i % 8}, {i / 8})");
        }
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
