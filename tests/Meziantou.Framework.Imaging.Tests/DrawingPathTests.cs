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
}
