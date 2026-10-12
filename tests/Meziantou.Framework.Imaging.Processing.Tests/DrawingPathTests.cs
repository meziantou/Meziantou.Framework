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
