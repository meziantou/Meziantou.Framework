using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The <c>Draw</c> entry points: the scope of the canvas, the lease held during the callback, the frames an image-level
/// call paints, argument validation, cancellation, the working storage and the metadata side effects.
/// </summary>
public sealed class DrawingSessionTests
{
    private static readonly Brush White = new SolidBrush(new Rgba32(255, 255, 255));
    private static readonly Pen WhitePen = new(White, 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // -----------------------------------------------------------------------------------------------------------------
    // The canvas and the callback
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheCallbackRunsOnceWithACanvasOfTheSizeOfTheImage()
    {
        using var image = new Image<Rgba32>(7, 3);
        image.AppendFrame();
        var calls = 0;
        image.Frames[1].Draw(
            canvas =>
            {
                calls++;
                Assert.Equal(7, canvas.Width);
                Assert.Equal(3, canvas.Height);
            },
            Ct);
        image.Draw(
            canvas =>
            {
                calls++;
                Assert.Equal(7, canvas.Width);
                Assert.Equal(3, canvas.Height);
            },
            Ct);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void TheStateOverloadsPassTheStateToTheCallback()
    {
        using var image = new Image<Gray8>(2, 1, new Gray8(0));
        image.Frames[0].Draw((Brush: White, Rectangle: new Rectangle(0, 0, 1, 1)), static (canvas, state) => canvas.FillRectangle(state.Brush, state.Rectangle), Ct);
        Assert.Equal(new Gray8(255), image.Frames[0][0, 0]);
        Assert.Equal(new Gray8(0), image.Frames[0][1, 0]);

        image.Draw((Brush: White, Rectangle: new Rectangle(1, 0, 1, 1)), static (canvas, state) => canvas.FillRectangle(state.Brush, state.Rectangle), Ct);
        Assert.Equal(new Gray8(255), image.Frames[0][1, 0]);

        // The state may be a ref struct
        Span<byte> calls = [0];
        image.Draw(calls, static (_, state) => state[0]++, Ct);
        image.Frames[0].Draw(calls, static (_, state) => state[0]++, Ct);
        Assert.Equal(2, calls[0]);
    }

    [Fact]
    public void ACanvasIsOnlyValidDuringItsCallback()
    {
        using var image = new Image<Gray8>(2, 2, new Gray8(0));
        DrawingCanvas none = default;
        Assert.Equal(0, none.Width);
        Assert.Equal(0, none.Height);
        AssertNotActive(none);

        // A canvas is a ref struct that cannot be stored anywhere by safe code; the session it wraps ends with the
        // callback, which is what makes a canvas kept by other means unusable
        var frame = image.Frames[0];
        var session = DrawingSession.Begin(image, [frame], [frame.GetStorage()], Ct);
        var canvas = new DrawingCanvas(session);
        canvas.FillRectangle(White, new Rectangle(0, 0, 1, 1));
        session.Dispose();
        Assert.Equal(2, canvas.Width);
        AssertNotActive(canvas);
        Assert.Equal(new Gray8(255), frame[0, 0]);
        Assert.Equal(new Gray8(0), frame[1, 1]);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);

        static void AssertNotActive(DrawingCanvas canvas)
        {
            var path = new VectorPathBuilder().AddRectangle(new Rectangle(0, 0, 1, 1)).Build();
            AssertThrows<InvalidOperationException>(canvas, static c => c.Fill(White));
            AssertThrows<InvalidOperationException>(canvas, c => c.Fill(White, path));
            AssertThrows<InvalidOperationException>(canvas, c => c.Stroke(WhitePen, path));
            AssertThrows<InvalidOperationException>(canvas, static c => c.FillRectangle(White, new Rectangle(0, 0, 1, 1)));
            AssertThrows<InvalidOperationException>(canvas, static c => c.StrokeRectangle(WhitePen, new Rectangle(0, 0, 1, 1)));
            AssertThrows<InvalidOperationException>(canvas, static c => c.FillEllipse(White, new PointD(1, 1), 1, 1));
            AssertThrows<InvalidOperationException>(canvas, static c => c.StrokeEllipse(WhitePen, new PointD(1, 1), 1, 1));
            AssertThrows<InvalidOperationException>(canvas, static c => c.FillCircle(White, new PointD(1, 1), 1));
            AssertThrows<InvalidOperationException>(canvas, static c => c.StrokeCircle(WhitePen, new PointD(1, 1), 1));
            AssertThrows<InvalidOperationException>(canvas, static c => c.StrokeLine(WhitePen, default, new PointD(1, 1)));
            AssertThrows<InvalidOperationException>(canvas, static c => c.StrokeBezier(WhitePen, default, new PointD(1, 0), new PointD(1, 1)));
            AssertThrows<InvalidOperationException>(canvas, static c => c.StrokeBezier(WhitePen, default, new PointD(1, 0), new PointD(0, 1), new PointD(1, 1)));
        }
    }

    [Fact]
    public void PixelsAreLeasedDuringTheCallback()
    {
        using var image = new Image<Rgba32>(4, 4);
        image.AppendFrame();
        var first = image.Frames[0];
        var second = image.Frames[1];

        // Frame level: only that frame is leased
        first.Draw(
            outer =>
            {
                Assert.Equal(1, image.Owner.ActiveLeaseCount);
                Assert.Throws<InvalidOperationException>(() => first.ProcessPixelRows(_ => { }));
                Assert.Throws<InvalidOperationException>(() => first.Draw(_ => { }, Ct));
                Assert.Throws<InvalidOperationException>(() => _ = first[0, 0]);
                Assert.Throws<InvalidOperationException>(() => image.Draw(_ => { }, Ct));
                Assert.Throws<InvalidOperationException>(() => image.Resize(new ResizeOptions(2, 2), Ct));
                Assert.Throws<InvalidOperationException>(() => image.Grayscale(Ct));
                Assert.Throws<InvalidOperationException>(image.Dispose);
                second.ProcessPixelRows(_ => { });
                second.Draw(canvas => canvas.Fill(White), Ct);
            },
            Ct);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.Equal(new Rgba32(255, 255, 255), second[0, 0]);

        // Image level: every frame is leased
        image.Draw(
            outer =>
            {
                Assert.Equal(2, image.Owner.ActiveLeaseCount);
                Assert.Throws<InvalidOperationException>(() => first.ProcessPixelRows(_ => { }));
                Assert.Throws<InvalidOperationException>(() => second.Draw(_ => { }, Ct));
                Assert.Throws<InvalidOperationException>(() => image.AppendFrame());
            },
            Ct);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);

        // From inside a pixel callback
        first.ProcessPixelRows(outer =>
        {
            Assert.Throws<InvalidOperationException>(() => first.Draw(_ => { }, Ct));
            Assert.Throws<InvalidOperationException>(() => image.Draw(_ => { }, Ct));
        });
        first.Draw(canvas => canvas.Fill(White), Ct);
    }

    [Fact]
    public void AnExceptionOfTheCallbackReleasesEverything()
    {
        using var image = new Image<Gray8>(4, 4, new Gray8(0));
        var live = image.Owner.Scope.LiveBytes;
        Assert.Throws<FormatException>(() => image.Frames[0].Draw(
            canvas =>
            {
                canvas.FillRectangle(White, new Rectangle(0, 0, 1, 1));
                throw new FormatException("callback");
            },
            Ct));
        Assert.Throws<FormatException>(() => image.Draw(_ => throw new FormatException("callback"), Ct));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.Equal(live, image.Owner.Scope.LiveBytes);

        // What was drawn before the exception stays
        Assert.Equal(new Gray8(255), image.Frames[0][0, 0]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Frames
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AFrameLevelCallPaintsThatFrameOnly()
    {
        using var image = NewAnimation();
        var before = CopyAll(image);
        image.Frames[1].Draw(canvas => canvas.FillRectangle(new SolidBrush(new Rgba32(9, 8, 7)), new Rectangle(1, 1, 2, 1)), Ct);
        var after = CopyAll(image);
        Assert.Equal(before[0], after[0]);
        Assert.NotEqual(before[1], after[1]);
        Assert.Equal(before[2], after[2]);
        Assert.Equal(new Rgba32(9, 8, 7), image.Frames[1][1, 1]);
        Assert.Equal(new Rgba32(9, 8, 7), image.Frames[1][2, 1]);

        // The poster is a frame like the others
        image.PosterFrame!.Draw(canvas => canvas.Fill(new SolidBrush(new Rgba32(1, 1, 1))), Ct);
        Assert.Equal(new Rgba32(1, 1, 1), image.PosterFrame[3, 2]);
        Assert.Equal(after[0], CopyAll(image)[0]);
        Assert.Equal(after[1], CopyAll(image)[1]);
    }

    [Fact]
    public void AnImageLevelCallPaintsEveryFrameAndThePosterLikeAFrameLevelCall()
    {
        var path = new VectorPathBuilder().MoveTo(0.3, 0.2).CubicBezierTo(1.5, 3.5, 2.5, -1, 3.7, 2.6).Build();
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(4, 3), [new GradientStop(0, new Rgba32(255, 0, 0, 200)), new GradientStop(1, new Rgba32(0, 0, 255, 40))]);
        var pen = new Pen(gradient, 0.8) { LineCap = LineCap.Round };

        static void Paint(DrawingCanvas canvas, (VectorPath Path, Pen Pen) state)
        {
            canvas.FillEllipse(new SolidBrush(new Rgba32(0, 200, 0, 99)), new PointD(2, 1.5), 1.7, 1.1);
            canvas.Stroke(state.Pen, state.Path);
        }

        using var whole = NewAnimation();
        whole.Draw((path, pen), Paint, Ct);

        using var separate = NewAnimation();
        separate.Frames[0].Draw((path, pen), Paint, Ct);
        separate.Frames[1].Draw((path, pen), Paint, Ct);
        separate.PosterFrame!.Draw((path, pen), Paint, Ct);

        using var untouched = NewAnimation();
        var expected = CopyAll(separate);
        var actual = CopyAll(whole);
        var original = CopyAll(untouched);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(expected[i], actual[i]);
            Assert.NotEqual(original[i], actual[i]);
        }
    }

    [Fact]
    public void DetachedFramesAndDisposedImagesAreRejected()
    {
        var image = new Image<Rgba32>(2, 2);
        var removed = image.AppendFrame();
        image.RemoveFrame(1);
        Assert.Throws<ObjectDisposedException>(() => removed.Draw(_ => Assert.Fail("callback"), Ct));

        var frame = image.Frames[0];
        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => frame.Draw(_ => Assert.Fail("callback"), Ct));
        Assert.Throws<ObjectDisposedException>(() => image.Draw(_ => Assert.Fail("callback"), Ct));
    }

    [Fact]
    public void ContiguousAndSegmentedStoragesGiveTheSamePixels()
    {
        var path = new VectorPathBuilder().AddCircle(new PointD(6, 5), 4.3).AddPolygon([new PointD(0, 0), new PointD(12.5, 3), new PointD(2, 10.5)]).Build();
        var brush = new RadialGradientBrush(new PointD(5, 5), 6, [new GradientStop(0, new Rgba32(255, 255, 0)), new GradientStop(1, new Rgba32(0, 0, 255, 100))]);
        using var contiguous = new Image<Rgba32>(ImageConfiguration.Default, new Size(13, 11), AllocationScope.Create(configuration: null), layoutOptions: null);
        using var segmented = new Image<Rgba32>(ImageConfiguration.Default, new Size(13, 11), AllocationScope.Create(configuration: null), new PixelStorageLayoutOptions { RowAlignment = 64, TargetSlabBytes = 256 });
        Assert.HasCountGreaterThan(1, segmented.Frames[0].Storage.GetSlabCapacities());
        foreach (var image in new[] { contiguous, segmented })
        {
            image.Draw(
                canvas =>
                {
                    canvas.Fill(brush, path, FillRule.EvenOdd);
                    canvas.Stroke(new Pen(new Rgba32(0, 0, 0, 180), 1.5), path);
                },
                Ct);
        }

        Assert.Equal(DrawingTestSupport.ReadSamples(contiguous.Frames[0]), DrawingTestSupport.ReadSamples(segmented.Frames[0]));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Arguments
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void NullArgumentsAreRejected()
    {
        using var image = new Image<Gray8>(2, 2, new Gray8(0));
        Assert.Throws<ArgumentNullException>(() => image.Draw(null!, Ct));
        Assert.Throws<ArgumentNullException>(() => image.Draw(0, null!, Ct));
        Assert.Throws<ArgumentNullException>(() => image.Frames[0].Draw(null!, Ct));
        Assert.Throws<ArgumentNullException>(() => image.Frames[0].Draw(0, null!, Ct));
        image.Frames[0].Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static c => c.Fill(null!));
                AssertThrows<ArgumentNullException>(canvas, static c => c.Fill(null!, new VectorPathBuilder().Build()));
                AssertThrows<ArgumentNullException>(canvas, static c => c.Fill(White, null!));
                AssertThrows<ArgumentNullException>(canvas, static c => c.Stroke(null!, new VectorPathBuilder().Build()));
                AssertThrows<ArgumentNullException>(canvas, static c => c.Stroke(WhitePen, null!));
                AssertThrows<ArgumentNullException>(canvas, static c => c.FillRectangle(null!, default));
                AssertThrows<ArgumentNullException>(canvas, static c => c.StrokeRectangle(null!, default));
                AssertThrows<ArgumentNullException>(canvas, static c => c.FillEllipse(null!, default, 1, 1));
                AssertThrows<ArgumentNullException>(canvas, static c => c.StrokeEllipse(null!, default, 1, 1));
                AssertThrows<ArgumentNullException>(canvas, static c => c.FillCircle(null!, default, 1));
                AssertThrows<ArgumentNullException>(canvas, static c => c.StrokeCircle(null!, default, 1));
                AssertThrows<ArgumentNullException>(canvas, static c => c.StrokeLine(null!, default, default));
                AssertThrows<ArgumentNullException>(canvas, static c => c.StrokeBezier(null!, default, default, default));
                AssertThrows<ArgumentNullException>(canvas, static c => c.StrokeBezier(null!, default, default, default, default));
            },
            Ct);
        Assert.Equal(new Gray8(0), image.Frames[0][0, 0]);
        Assert.Throws<ArgumentNullException>(() => new Pen(null!, 1));
    }

    [Fact]
    public void CoordinatesMustBeFiniteAndBounded()
    {
        using var image = new Image<Gray8>(2, 2, new Gray8(0));
        image.Frames[0].Draw(
            canvas =>
            {
                foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    var point = new PointD(invalid, 0);
                    AssertThrows<ArgumentException>(canvas, c => c.StrokeLine(WhitePen, point, default));
                    AssertThrows<ArgumentException>(canvas, c => c.StrokeLine(WhitePen, default, new PointD(0, invalid)));
                    AssertThrows<ArgumentException>(canvas, c => c.FillCircle(White, point, 1));
                    AssertThrows<ArgumentException>(canvas, c => c.FillCircle(White, default, invalid));
                    AssertThrows<ArgumentException>(canvas, c => c.StrokeCircle(WhitePen, default, invalid));
                    AssertThrows<ArgumentException>(canvas, c => c.FillEllipse(White, default, 1, invalid));
                    AssertThrows<ArgumentException>(canvas, c => c.StrokeEllipse(WhitePen, default, invalid, 1));
                    AssertThrows<ArgumentException>(canvas, c => c.StrokeBezier(WhitePen, default, point, default));
                    AssertThrows<ArgumentException>(canvas, c => c.StrokeBezier(WhitePen, default, default, point, default));
                    if (!double.IsNaN(invalid))
                    {
                        // A rectangle rejects a size that is not a number when it is built
                        AssertThrows<ArgumentException>(canvas, c => c.FillRectangle(White, new RectangleD(invalid, 0, 1, 1)));
                        AssertThrows<ArgumentException>(canvas, c => c.StrokeRectangle(WhitePen, new RectangleD(0, invalid, 1, 1)));
                    }
                }

                const double TooFar = 16777217;
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.StrokeLine(WhitePen, new PointD(TooFar, 0), default));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.StrokeLine(WhitePen, default, new PointD(0, -TooFar)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.FillRectangle(White, new RectangleD(0, 0, TooFar, 1)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.FillRectangle(White, new RectangleD(16777216, 0, 1, 1)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.FillCircle(White, default, TooFar));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.FillCircle(White, default, -1));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.StrokeCircle(WhitePen, default, -1));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.FillEllipse(White, default, -1, 1));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.StrokeEllipse(WhitePen, default, 1, -1));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static c => c.Fill(White, new VectorPathBuilder().Build(), (FillRule)2));

                // The bounds themselves are accepted
                canvas.StrokeLine(WhitePen, new PointD(-16777216, -16777216), new PointD(16777216, 16777216));
                canvas.FillCircle(White, new PointD(16777216, 0), 16777216);
            },
            Ct);
    }

    [Fact]
    public void PensAndBrushesValidateTheirSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 16777217));
        Assert.Throws<ArgumentException>(() => new Pen(White, double.NaN));
        Assert.Throws<ArgumentException>(() => new Pen(White, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { LineJoin = (LineJoin)3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { LineCap = (LineCap)(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { MiterLimit = 0.99 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { MiterLimit = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pen(White, 1) { MiterLimit = 1048577 });
        Assert.Equal(1, new Pen(White, 1) { MiterLimit = 1 }.MiterLimit);
        Assert.Equal(16777216, new Pen(White, 16777216).Width);

        Assert.Throws<ArgumentOutOfRangeException>(() => new GradientStop(-0.01, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradientStop(1.01, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradientStop(double.NaN, default));

        GradientStop[] stops = [new(0, default), new(1, default)];
        GradientStop[] decreasing = [new(0.5, default), new(0.25, default)];
        Assert.Throws<ArgumentException>(() => new LinearGradientBrush(default, default, []));
        Assert.Throws<ArgumentException>(() => new RadialGradientBrush(default, 1, []));
        Assert.Throws<ArgumentException>(() => new LinearGradientBrush(default, default, decreasing));
        Assert.Throws<ArgumentException>(() => new RadialGradientBrush(default, 1, decreasing));
        Assert.Throws<ArgumentException>(() => new LinearGradientBrush(new PointD(double.NaN, 0), default, stops));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearGradientBrush(default, new PointD(0, 16777217), stops));
        Assert.Throws<ArgumentException>(() => new RadialGradientBrush(new PointD(0, double.PositiveInfinity), 1, stops));
        Assert.Throws<ArgumentException>(() => new RadialGradientBrush(default, double.NaN, stops));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RadialGradientBrush(default, -1, stops));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearGradientBrush(default, default, stops) { SpreadMode = (GradientSpreadMode)3 });

        // The stops are copied
        var brush = new LinearGradientBrush(new PointD(1, 2), new PointD(3, 4), stops);
        stops[0] = new GradientStop(0.5, new Rgba32(1, 2, 3));
        Assert.Equal(new GradientStop(0, default), brush.Stops.Span[0]);
        Assert.Equal(new PointD(1, 2), brush.Start);
        Assert.Equal(new PointD(3, 4), brush.End);
        var radial = new RadialGradientBrush(new PointD(1, 2), 3, stops);
        Assert.Equal(new PointD(1, 2), radial.Center);
        Assert.Equal(3, radial.Radius);
        Assert.Equal(2, radial.Stops.Length);

        // Colors convert to brushes
        Brush fromRgba32 = new Rgba32(1, 2, 3, 4);
        Brush fromRgba64 = new Rgba64(1, 2, 3, 4);
        Assert.Equal(new Rgba64(257, 514, 771, 1028), Assert.IsType<SolidBrush>(fromRgba32).Color);
        Assert.Equal(new Rgba64(1, 2, 3, 4), Assert.IsType<SolidBrush>(fromRgba64).Color);
        Assert.Equal(new Rgba64(1, 2, 3, 4), Assert.IsType<SolidBrush>(Brush.FromRgba64(new Rgba64(1, 2, 3, 4))).Color);
        Assert.Equal(new Rgba64(257, 514, 771, 1028), Assert.IsType<SolidBrush>(Brush.FromRgba32(new Rgba32(1, 2, 3, 4))).Color);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ACanceledTokenStopsBeforeTheCallback()
    {
        using var image = new Image<Gray8>(2, 2, new Gray8(0));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => image.Draw(_ => Assert.Fail("callback"), cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => image.Frames[0].Draw(_ => Assert.Fail("callback"), cancellation.Token));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void ACancellationBetweenTwoShapesKeepsTheFirstOne()
    {
        using var image = new Image<Gray8>(2, 1, new Gray8(0));
        using var cancellation = new CancellationTokenSource();
        var live = image.Owner.Scope.LiveBytes;
        Assert.Throws<OperationCanceledException>(() => image.Draw(
            canvas =>
            {
                canvas.FillRectangle(White, new Rectangle(0, 0, 1, 1));
                cancellation.Cancel();
                canvas.FillRectangle(White, new Rectangle(1, 0, 1, 1));
                Assert.Fail("not reached");
            },
            cancellation.Token));
        Assert.Equal(new Gray8(255), image.Frames[0][0, 0]);
        Assert.Equal(new Gray8(0), image.Frames[0][1, 0]);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.Equal(live, image.Owner.Scope.LiveBytes);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Working storage
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheScratchIsChargedToTheImageAndReleased()
    {
        var path = new VectorPathBuilder().MoveTo(0.5, 0.5).CubicBezierTo(3, 0, 1, 3, 3.5, 2.5).Build();
        var gradient = new RadialGradientBrush(new PointD(2, 1), 3, [new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 255))]);
        void Paint(DrawingCanvas canvas)
        {
            canvas.Fill(gradient);
            canvas.Stroke(new Pen(White, 0.75) { LineCap = LineCap.Round }, path);
        }

        long before, peak;
        using (var probe = NewAnimation(new SlabPool(), ImageConfiguration.Default))
        {
            before = probe.Owner.Scope.LiveBytes;
            probe.Draw(
                canvas =>
                {
                    Assert.True(probe.Owner.Scope.GetLiveBytes(AllocationKind.Temporary) > 0, "The scratch of the frame size is rented before the callback");
                    Paint(canvas);
                },
                Ct);
            peak = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.Equal(before, probe.Owner.Scope.LiveBytes);
            Assert.Equal(0, probe.Owner.Scope.GetLiveBytes(AllocationKind.Temporary));
        }

        Assert.True(peak > before, "The scratch storage is charged in addition to the pixel storages");

        // One byte short: the call that needs it fails before it changes a pixel, and everything is released
        var tight = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak - 1 } };
        using (var image = NewAnimation(new SlabPool(), tight))
        {
            var exception = Assert.Throws<ImageResourceLimitException>(() => image.Draw(Paint, Ct));
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            Assert.Equal(before, image.Owner.Scope.LiveBytes);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
        }

        var exact = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = peak } };
        using (var image = NewAnimation(new SlabPool(), exact))
        {
            image.Draw(Paint, Ct);
            Assert.Equal(before, image.Owner.Scope.LiveBytes);
        }
    }

    [Fact]
    public void ACallThatExceedsTheLimitChangesNoPixel()
    {
        // Room for the pixels and for the scratch of the frame size, not for the edges of a shape
        using var probe = new Image<Gray8>(4, 4, new Gray8(0), ImageConfiguration.Default);
        long session = 0;
        probe.Draw(_ => session = probe.Owner.Scope.LiveBytes, Ct);

        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = session } };
        using var image = new Image<Gray8>(4, 4, new Gray8(0), configuration);
        var thrown = false;
        image.Draw(
            canvas =>
            {
                try
                {
                    canvas.FillRectangle(White, new Rectangle(0, 0, 4, 4));
                }
                catch (ImageResourceLimitException)
                {
                    thrown = true;
                }
            },
            Ct);
        Assert.True(thrown);
        Assert.Equal(new int[16], DrawingTestSupport.ReadSamples(image.Frames[0]));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void AnAllocationFailureAtAnyRentalLeavesTheImageUnchanged()
    {
        var path = new VectorPathBuilder().MoveTo(0.5, 0.5).CubicBezierTo(3, 0, 1, 3, 3.5, 2.5).Build();
        var pen = new Pen(new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), [new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 255))]), 0.75);
        foreach (var frameLevel in new[] { false, true })
        {
            void Draw(Image<Rgba32> image)
            {
                if (frameLevel)
                {
                    image.Frames[1].Draw(canvas => canvas.Stroke(pen, path), Ct);
                }
                else
                {
                    image.Draw(canvas => canvas.Stroke(pen, path), Ct);
                }
            }

            // Fail the first rental, then the second one, and so on until the call goes through
            var completed = false;
            for (var failing = 0; !completed; failing++)
            {
                Assert.True(failing < 64, "Too many rentals for one shape");
                var pool = new SlabPool();
                using var image = NewAnimation(pool, ImageConfiguration.Default);
                var pixels = CopyAll(image);
                var exif = image.Metadata.ExifProfile;
                var live = image.Owner.Scope.LiveBytes;
                var rentals = 0;
                pool.RentFailureInjector = _ => rentals++ == failing ? new InjectedAllocationFailureException() : null;
                try
                {
                    Draw(image);
                    completed = true;
                }
                catch (InjectedAllocationFailureException)
                {
                    // Every rental of a shape happens before its first pixel
                    Assert.Equal(pixels, CopyAll(image));
                    Assert.Same(exif, image.Metadata.ExifProfile);
                }

                pool.RentFailureInjector = null;
                Assert.Equal(live, image.Owner.Scope.LiveBytes);
                Assert.Equal(0, image.Owner.ActiveLeaseCount);

                Draw(image); // still usable
                Assert.NotEqual(pixels, CopyAll(image));
                Assert.Equal(live, image.Owner.Scope.LiveBytes);
                if (completed)
                {
                    // The frame scratch, the edges (grown several times), the sweep and the gradient: more than a few rentals
                    Assert.True(failing >= 6, $"Only {failing} rentals were made");
                }
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Metadata
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheExifThumbnailIsRemovedByTheFirstShapeThatChangesPixels()
    {
        using var image = NewAnimation();
        var exif = image.Metadata.ExifProfile;
        Assert.True(ExifTiff.HasThumbnail(exif!.Data.Span));

        // No shape, a shape outside the canvas, a transparent paint, an empty path: nothing changes
        image.Draw(_ => { }, Ct);
        image.Draw(
            canvas =>
            {
                canvas.FillRectangle(White, new Rectangle(10, 10, 5, 5));
                canvas.Fill(new SolidBrush(new Rgba32(1, 2, 3, 0)));
                canvas.Fill(White, new VectorPathBuilder().Build());
                canvas.Stroke(WhitePen, new VectorPathBuilder().Build());
            },
            Ct);
        Assert.Same(exif, image.Metadata.ExifProfile);

        image.Frames[1].Draw(canvas => canvas.FillRectangle(White, new Rectangle(0, 0, 1, 1)), Ct);
        Assert.NotSame(exif, image.Metadata.ExifProfile);
        Assert.False(ExifTiff.HasThumbnail(image.Metadata.ExifProfile!.Data.Span));
    }

    [Fact]
    public void HotspotsAndOtherMetadataAreKept()
    {
        using var image = new Image<Rgba32>(4, 4);
        image.Frames[0].Metadata.Hotspot = new Point(2, 1);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Draw(canvas => canvas.Fill(White), Ct);
        Assert.Equal(new Point(2, 1), image.Frames[0].Metadata.Hotspot);
        Assert.Equal(ExifOrientation.RightTop, image.Metadata.Orientation);
        Assert.Equal(new Size(4, 4), image.Size);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Clipping
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AClipOfWholePixelsKeepsTheDrawingsInsideIt()
    {
        using var image = new Image<Gray8>(4, 4, new Gray8(0));
        image.Draw(
            canvas =>
            {
                canvas.Clip(new Rectangle(1, 1, 2, 2), inner =>
                {
                    inner.Fill(White);
                    inner.StrokeLine(new Pen(White, 2), new PointD(0, 0), new PointD(4, 0));
                    inner.FillCircle(White, new PointD(3.5, 3.5), 0.5);
                });

                // The clip is over when the callback returns
                canvas.FillRectangle(White, new Rectangle(3, 3, 1, 1));
            },
            Ct);
        Assert.Equal([0, 0, 0, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 0, 0, 255], DrawingTestSupport.ReadSamples(image.Frames[0]));

        // Beyond the canvas, and empty
        using var other = new Image<Gray8>(2, 2, new Gray8(0));
        other.Draw(
            canvas =>
            {
                canvas.Clip(new Rectangle(-5, 1, 6, 100), inner => inner.Fill(White));
                canvas.Clip(new Rectangle(1, 0, 0, 2), inner => inner.Fill(White));
                canvas.Clip(new Rectangle(5, 5, 2, 2), inner => inner.Fill(White));
            },
            Ct);
        Assert.Equal([0, 0, 255, 0], DrawingTestSupport.ReadSamples(other.Frames[0]));
    }

    [Fact]
    public void AClipMultipliesTheCoverageOfWhatIsDrawn()
    {
        // A rectangle that is not on whole pixels has an antialiased edge, like a shape: from x = 0.5 to x = 2.5
        using (var image = new Image<Gray8>(3, 1, new Gray8(0)))
        {
            image.Draw(canvas => canvas.Clip(new RectangleD(0.5, 0, 2, 1), inner => inner.Fill(White)), Ct);
            Assert.Equal([128, 255, 128], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // The left half of a pixel as a clip, the top half as a shape: a quarter, 63.75 -> 64
        Assert.Equal(64, PaintClipped(new RectangleD(0, 0, 0.5, 1), new RectangleD(0, 0, 1, 0.5)));

        // The coverages are multiplied, whatever the shapes have in common: the left half in the left half is a quarter too
        Assert.Equal(64, PaintClipped(new RectangleD(0, 0, 0.5, 1), new RectangleD(0, 0, 0.5, 1)));
        Assert.Equal(64, PaintClipped(new RectangleD(0, 0, 0.5, 1), new RectangleD(0.5, 0, 0.5, 1)));

        // A shape that covers the pixel has the coverage of the clip, and a clip that covers it changes nothing
        Assert.Equal(128, PaintClipped(new RectangleD(0, 0, 0.5, 1), new RectangleD(0, 0, 1, 1)));
        Assert.Equal(128, PaintClipped(new RectangleD(-3, -3, 8, 8.5), new RectangleD(0, 0, 0.5, 1)));
        Assert.Equal(128, PaintClipped(new RectangleD(-3.5, -3.5, 8, 8), new RectangleD(0, 0, 0.5, 1)));

        // Three quarters of three quarters: 0.5625 -> 143.4 -> 143
        Assert.Equal(143, PaintClipped(new RectangleD(0, 0, 0.75, 1), new RectangleD(0, 0, 1, 0.75)));

        static int PaintClipped(RectangleD clip, RectangleD shape)
        {
            using var image = new Image<Gray8>(1, 1, new Gray8(0));
            image.Draw(canvas => canvas.Clip(clip, inner => inner.FillRectangle(White, shape)), Ct);
            return image.Frames[0][0, 0].Value;
        }
    }

    [Fact]
    public void FillingInsideTheClipOfAPathIsFillingThePath()
    {
        var circle = new VectorPathBuilder().AddCircle(new PointD(4.25, 3.5), 3.1).Build();
        var rings = new VectorPathBuilder().AddRectangle(new RectangleD(0.5, 0.5, 7, 7)).AddRectangle(new RectangleD(2.25, 2.25, 3.5, 3.5)).AddCircle(new PointD(4, 4), 1.2).Build();
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(8, 8), [new GradientStop(0, new Rgba32(255, 0, 0, 40)), new GradientStop(1, new Rgba32(0, 0, 255))]);
        var translucent = new SolidBrush(new Rgba32(10, 200, 30, 120));
        foreach (var brush in new Brush[] { White, translucent, gradient })
        {
            foreach (var (path, fillRule) in new[] { (circle, FillRule.NonZero), (rings, FillRule.NonZero), (rings, FillRule.EvenOdd) })
            {
                var expected = Paint(canvas => canvas.Fill(brush, path, fillRule));
                Assert.Equal(expected, Paint(canvas => canvas.Clip(path, inner => inner.Fill(brush), fillRule)));

                // With a state, and from the whole canvas as a rectangle
                Assert.Equal(expected, Paint(canvas => canvas.Clip(path, brush, static (inner, brush) => inner.FillRectangle(brush, new Rectangle(0, 0, 8, 8)), fillRule)));
            }
        }

        static int[] Paint(DrawingAction action)
        {
            using var image = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(11).Next);
            image.Draw(action, Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void AClipInsideAnotherOneKeepsWhatBothLetThrough()
    {
        var circle = new VectorPathBuilder().AddCircle(new PointD(4.25, 3.5), 3.1).Build();
        using var whole = new Image<Gray8>(8, 8, new Gray8(0));
        whole.Draw(canvas => canvas.Fill(White, circle), Ct);
        var filled = DrawingTestSupport.ReadSamples(whole.Frames[0]);

        // A rectangle of whole pixels inside the clip of a path, and the other way around: the right half of the circle
        var expected = filled.Select((value, index) => index % 8 >= 4 ? value : 0).ToArray();
        using (var image = new Image<Gray8>(8, 8, new Gray8(0)))
        {
            image.Draw(canvas => canvas.Clip(circle, inner => inner.Clip(new Rectangle(4, 0, 4, 8), innermost => innermost.Fill(White))), Ct);
            Assert.Equal(expected, DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        using (var image = new Image<Gray8>(8, 8, new Gray8(0)))
        {
            image.Draw(canvas => canvas.Clip(new Rectangle(4, 0, 4, 8), inner => inner.Clip(circle, innermost => innermost.Fill(White))), Ct);
            Assert.Equal(expected, DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // Two rectangles of whole pixels: their intersection
        using (var image = new Image<Gray8>(4, 4, new Gray8(0)))
        {
            image.Draw(
                canvas => canvas.Clip(new Rectangle(0, 0, 2, 2), inner =>
                {
                    inner.Clip(new Rectangle(1, 1, 3, 3), innermost => innermost.Fill(White));

                    // The inner clip is over: the outer one applies again
                    inner.FillRectangle(new SolidBrush(new Rgba32(100, 100, 100)), new Rectangle(0, 0, 4, 1));
                }),
                Ct);
            Assert.Equal([100, 100, 0, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // Two halves of a pixel: a quarter (63.75 -> 64); three halves in each other: an eighth (31.875 -> 32)
        using (var image = new Image<Gray8>(1, 1, new Gray8(0)))
        {
            image.Draw(canvas => canvas.Clip(new RectangleD(0, 0, 0.5, 1), inner => inner.Clip(new RectangleD(0, 0, 1, 0.5), innermost => innermost.Fill(White))), Ct);
            Assert.Equal(new Gray8(64), image.Frames[0][0, 0]);
        }

        using (var image = new Image<Gray8>(1, 1, new Gray8(0)))
        {
            var half = new VectorPathBuilder().AddRectangle(new RectangleD(0, 0, 0.5, 1)).Build();
            image.Draw(canvas => canvas.Clip(half, inner => inner.Clip(new RectangleD(0, 0, 1, 0.5), innermost => innermost.Clip(half, last => last.Fill(White)))), Ct);
            Assert.Equal(new Gray8(32), image.Frames[0][0, 0]);
        }
    }

    [Fact]
    public void AClipAppliesToEveryDrawingAndToEveryFrame()
    {
        using var image = NewAnimation();
        var before = CopyAll(image);
        var triangle = new VectorPathBuilder().AddPolygon([new PointD(0, 0), new PointD(4, 0), new PointD(0, 3)]).Build();
        image.Draw(
            canvas => canvas.Clip(new Rectangle(1, 0, 2, 2), inner =>
            {
                inner.Clear(new Rgba32(1, 2, 3, 4));
                inner.Stroke(new Pen(White, 1), triangle);
                inner.StrokeRectangle(new Pen(new Rgba32(9, 9, 9), 3) { DashPattern = [1, 1] }, new Rectangle(-2, -2, 10, 10));
            }),
            Ct);
        var after = CopyAll(image);
        for (var frame = 0; frame < 3; frame++)
        {
            for (var pixel = 0; pixel < 12; pixel++)
            {
                var inside = pixel % 4 is 1 or 2 && pixel / 4 < 2;
                var changed = !before[frame].AsSpan(pixel * 4, 4).SequenceEqual(after[frame].AsSpan(pixel * 4, 4));
                Assert.Equal(inside, changed, $"frame {frame}, pixel {pixel}");
            }

            // The same pixels on every frame
            Assert.Equal(after[0].AsSpan(4, 8).ToArray(), after[frame].AsSpan(4, 8).ToArray());
        }
    }

    [Fact]
    public void AClipEndsWhenItsCallbackThrows()
    {
        using var image = new Image<Gray8>(2, 2, new Gray8(0));
        image.Draw(
            canvas =>
            {
                try
                {
                    canvas.Clip(new Rectangle(0, 0, 1, 1), _ => throw new FormatException("stop"));
                }
                catch (FormatException)
                {
                }

                try
                {
                    canvas.Clip(new VectorPathBuilder().AddCircle(default, 1).Build(), 5, static (_, _) => throw new FormatException("stop"));
                }
                catch (FormatException)
                {
                }

                canvas.Fill(White);
            },
            Ct);
        Assert.Equal([255, 255, 255, 255], DrawingTestSupport.ReadSamples(image.Frames[0]));
    }

    [Fact]
    public void AClipThatLetsNothingThroughDrawsNothing()
    {
        using var image = new Image<Gray8>(4, 4, new Gray8(0));
        var ran = 0;
        image.Draw(
            canvas =>
            {
                canvas.Clip(new VectorPathBuilder().Build(), inner => Fill(inner));
                canvas.Clip(new VectorPathBuilder().AddCircle(new PointD(50, 50), 3).Build(), inner => Fill(inner));
                canvas.Clip(new VectorPathBuilder().MoveTo(1, 1).LineTo(3, 3).Build(), inner => Fill(inner));
                canvas.Clip(new RectangleD(1.5, 1.5, 0, 2), inner => Fill(inner));
                canvas.Clip(new RectangleD(-9.5, -9.5, 2, 2), inner => Fill(inner));
            },
            Ct);
        Assert.Equal(5, ran);
        Assert.Equal(new int[16], DrawingTestSupport.ReadSamples(image.Frames[0]));

        void Fill(DrawingCanvas canvas)
        {
            ran++;
            canvas.Fill(White);
            canvas.Clear(Colors.White);
            canvas.Clip(new Rectangle(0, 0, 4, 4), inner => inner.Fill(White));
        }
    }

    [Fact]
    public void AClipValidatesItsArgumentsAndIsChargedToTheImage()
    {
        using (var image = new Image<Gray8>(4, 4, new Gray8(0)))
        {
            image.Draw(
                canvas =>
                {
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Clip((VectorPath)null!, static _ => { }));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Clip(new VectorPathBuilder().Build(), null!));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Clip(new VectorPathBuilder().Build(), 1, null!));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Clip(new Rectangle(0, 0, 1, 1), null!));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Clip(new Rectangle(0, 0, 1, 1), 1, null!));
                    AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Clip(new VectorPathBuilder().Build(), static _ => Assert.Fail("The callback ran."), (FillRule)2));
                    AssertThrows<ArgumentException>(canvas, static canvas => canvas.Clip(new RectangleD(double.NaN, 0, 1, 1), static _ => Assert.Fail("The callback ran.")));
                    AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Clip(new RectangleD(16777216, 0, 1, 1), static _ => Assert.Fail("The callback ran.")));

                    // A rejected clip leaves the canvas without clip
                    canvas.FillRectangle(White, new Rectangle(0, 0, 1, 1));
                },
                Ct);
            Assert.Equal(new Gray8(255), image.Frames[0][0, 0]);
        }

        // Room for the pixels and for the scratch of the frame size, not for the edges and the mask of a clip: the
        // callback is not run, and the canvas can still be drawn on once there is room
        using var probe = new Image<Gray8>(64, 64, new Gray8(0), ImageConfiguration.Default);
        long session = 0;
        probe.Draw(_ => session = probe.Owner.Scope.LiveBytes, Ct);
        var large = new VectorPathBuilder().AddCircle(new PointD(32, 32), 30).Build();
        var small = new VectorPathBuilder().AddCircle(new PointD(32, 32), 5).Build();
        long before;
        using (var image = new Image<Gray8>(64, 64, new Gray8(0), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = session } }))
        {
            before = image.Owner.Scope.LiveBytes;
            var thrown = false;
            image.Draw(
                canvas =>
                {
                    try
                    {
                        canvas.Clip(large, static _ => Assert.Fail("The callback ran."));
                    }
                    catch (ImageResourceLimitException)
                    {
                        thrown = true;
                    }
                },
                Ct);
            Assert.True(thrown);
            Assert.Equal(new int[64 * 64], DrawingTestSupport.ReadSamples(image.Frames[0]));
            Assert.Equal(before, image.Owner.Scope.LiveBytes);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
        }

        // The mask is released with the session
        using (var image = new Image<Gray8>(64, 64, new Gray8(0), ImageConfiguration.Default))
        {
            before = image.Owner.Scope.LiveBytes;
            image.Draw(canvas => canvas.Clip(large, inner => inner.Clip(small, innermost => innermost.Fill(White))), Ct);
            Assert.Equal(before, image.Owner.Scope.LiveBytes);
            Assert.Equal(new Gray8(255), image.Frames[0][32, 32]);
            Assert.Equal(new Gray8(0), image.Frames[0][40, 32]);
            Assert.Equal(new Gray8(0), image.Frames[0][2, 2]);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Layers
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ALayerFadesADrawingAsAWhole()
    {
        // Two white rectangles that overlap on two pixels, each at 128/255 of its opacity: where they overlap, the
        // second one is over the first, (255 * 128 + 128 * 127) / 255 = 191.75 -> 192
        var translucent = new SolidBrush(new Rgba32(255, 255, 255, 128));
        using var direct = new Image<Gray8>(4, 1, new Gray8(0));
        direct.Draw(
            canvas =>
            {
                canvas.FillRectangle(translucent, new Rectangle(0, 0, 3, 1));
                canvas.FillRectangle(translucent, new Rectangle(1, 0, 3, 1));
            },
            Ct);
        Assert.Equal([128, 192, 192, 128], DrawingTestSupport.ReadSamples(direct.Frames[0]));

        // In a layer, the rectangles are drawn as they are, then the whole is faded: 255 / 2 = 127.5 -> 128 everywhere
        using var layered = new Image<Gray8>(4, 1, new Gray8(0));
        layered.Draw(
            canvas => canvas.Layer(0.5, layer =>
            {
                layer.FillRectangle(White, new Rectangle(0, 0, 3, 1));
                layer.FillRectangle(White, new Rectangle(1, 0, 3, 1));
            }),
            Ct);
        Assert.Equal([128, 128, 128, 128], DrawingTestSupport.ReadSamples(layered.Frames[0]));

        // On transparent pixels, the layer keeps its colors and gets its opacity; a quarter is 63.75 -> 64
        using var transparent = new Image<Rgba32>(2, 1);
        transparent.Draw(canvas => canvas.Layer(0.25, layer => layer.FillRectangle(new Rgba32(10, 20, 30), new Rectangle(0, 0, 1, 1))), Ct);
        Assert.Equal(new Rgba32(10, 20, 30, 64), transparent.Frames[0][0, 0]);
        Assert.Equal(default, transparent.Frames[0][1, 0]);

        // A layer in a layer: black at half its opacity over white is 127.5 -> 128, and that gray at half its opacity
        // over the black of the canvas is 64. The state overloads pass their state.
        using var nested = new Image<Gray8>(1, 1, new Gray8(0));
        nested.Draw(
            canvas => canvas.Layer(0.5, White, static (outer, white) =>
            {
                outer.Fill(white);
                outer.Layer(0.5, new SolidBrush(new Rgba32(0, 0, 0)), static (inner, black) => inner.Fill(black));
            }),
            Ct);
        Assert.Equal([64], DrawingTestSupport.ReadSamples(nested.Frames[0]));

        // Without opacity nothing is drawn, and the callback still runs
        var ran = false;
        layered.Draw(
            canvas => canvas.Layer(0, layer =>
            {
                ran = true;
                layer.Clear(Colors.White);
            }),
            Ct);
        Assert.True(ran);
        Assert.Equal([128, 128, 128, 128], DrawingTestSupport.ReadSamples(layered.Frames[0]));
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void ALayerIsDrawnAsAnImageOfItsDrawings(PixelFormat format)
    {
        // Whole pixels and opaque colors: the layer is the drawing itself, in every pixel format
        var color = new Rgba64(0x1234, 0x5678, 0x9ABC);
        using var direct = DrawingTestSupport.Create(format, 4, 3);
        using var layered = DrawingTestSupport.Create(format, 4, 3);
        direct.Draw(canvas => Paint(canvas, color), Ct);
        layered.Draw(canvas => canvas.Layer(1, layer => Paint(layer, color)), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(direct.Frames[0]), DrawingTestSupport.ReadSamples(layered.Frames[0]));

        // Any drawing: the layer has the samples of the canvas and its own alpha, so each pixel is the drawing to one
        // unit of an 8-bit sample (the alpha of the layer is rounded before it is drawn)
        var path = new VectorPathBuilder().MoveTo(0.5, 2.25).CubicBezierTo(1, -3, 2.5, 5, 3.75, 0.5).Close().AddCircle(new PointD(2, 1.5), 1.2).Build();
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(4, 3), [new GradientStop(0, new Rgba32(255, 40, 0)), new GradientStop(1, new Rgba32(0, 90, 255))]);
        using var first = DrawingTestSupport.CreateRandom(format, 4, 3, new Random(5).Next);
        using var second = DrawingTestSupport.CreateRandom(format, 4, 3, new Random(5).Next);
        first.Draw(canvas => canvas.Fill(gradient, path), Ct);
        second.Draw(canvas => canvas.Layer(1, layer => layer.Fill(gradient, path)), Ct);
        var expected = DrawingTestSupport.ReadSamples(first.Frames[0]);
        var actual = DrawingTestSupport.ReadSamples(second.Frames[0]);
        var unit = PixelFormats.GetBitsPerComponent(format) == 8 ? 1 : 257;
        Assert.Contains(expected, sample => sample != 0);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(actual[i], expected[i] - (2 * unit), expected[i] + (2 * unit), message: $"sample {i}");
        }

        static void Paint(DrawingCanvas canvas, Rgba64 color)
        {
            canvas.FillRectangle(color, new Rectangle(1, 0, 2, 2));
            canvas.FillRectangle(new Rgba32(255, 255, 255), new Rectangle(2, 1, 2, 2));
        }
    }

    [Fact]
    public void ALayerIsCombinedWithTheCanvasByItsOperator()
    {
        // A mask made of two shapes: the canvas is kept where the layer is opaque, and erased elsewhere
        var red = new Rgba32(255, 0, 0);
        using var image = new Image<Rgba32>(5, 1, red);
        image.Draw(
            canvas => canvas.Layer(
                1,
                layer =>
                {
                    layer.FillRectangle(White, new Rectangle(1, 0, 1, 1));
                    layer.FillRectangle(new SolidBrush(new Rgba32(0, 0, 0, 128)), new Rectangle(3, 0, 1, 1));
                },
                CompositeMode.DestinationIn),
            Ct);
        Assert.Equal(default, image.Frames[0][0, 0]);
        Assert.Equal(red, image.Frames[0][1, 0]);
        Assert.Equal(default, image.Frames[0][2, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 128), image.Frames[0][3, 0]);
        Assert.Equal(default, image.Frames[0][4, 0]);

        // An eraser made of a drawing, and a drawing put behind the pixels
        using var erased = new Image<Rgba32>(3, 1, red);
        erased.Draw(canvas => canvas.Layer(1, layer => layer.FillRectangle(White, new Rectangle(1, 0, 1, 1)), CompositeMode.DestinationOut), Ct);
        Assert.Equal([red, default, red], [erased.Frames[0][0, 0], erased.Frames[0][1, 0], erased.Frames[0][2, 0]]);
        erased.Draw(canvas => canvas.Layer(1, new Rgba32(0, 0, 255), static (layer, blue) => layer.Fill(blue), CompositeMode.DestinationOver), Ct);
        Assert.Equal([red, new Rgba32(0, 0, 255), red], [erased.Frames[0][0, 0], erased.Frames[0][1, 0], erased.Frames[0][2, 0]]);

        // The operator applies inside the clip the layer started in, and nowhere else
        using var clipped = new Image<Rgba32>(4, 1, red);
        clipped.Draw(
            canvas => canvas.Clip(new Rectangle(0, 0, 2, 1), inner => inner.Layer(1, layer => layer.FillRectangle(White, new Rectangle(1, 0, 3, 1)), CompositeMode.DestinationIn)),
            Ct);
        Assert.Equal([default, red, red, red], [clipped.Frames[0][0, 0], clipped.Frames[0][1, 0], clipped.Frames[0][2, 0], clipped.Frames[0][3, 0]]);
    }

    [Fact]
    public void ClipsAndLayersCombine()
    {
        // The clip a layer starts in applies to the layer as a whole: its mask multiplies the coverage of the layer
        using (var image = new Image<Gray8>(4, 1, new Gray8(0)))
        {
            image.Draw(canvas => canvas.Clip(new RectangleD(0.5, 0, 2, 1), inner => inner.Layer(1, layer => layer.Fill(White))), Ct);
            Assert.Equal([128, 255, 128, 0], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // A clip started in a layer ends in it, and what the layer draws after it is not clipped
        using (var image = new Image<Gray8>(4, 1, new Gray8(0)))
        {
            image.Draw(
                canvas => canvas.Layer(0.5, layer =>
                {
                    layer.Clip(new Rectangle(0, 0, 1, 1), inner => inner.Fill(White));
                    layer.FillRectangle(White, new Rectangle(3, 0, 1, 1));
                }),
                Ct);
            Assert.Equal([128, 0, 0, 128], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // A clip around a layer around a clip: what both clips let through
        using (var image = new Image<Gray8>(4, 1, new Gray8(0)))
        {
            image.Draw(
                canvas => canvas.Clip(new Rectangle(1, 0, 3, 1), outer => outer.Layer(1, layer => layer.Clip(new Rectangle(0, 0, 3, 1), inner => inner.Fill(White)))),
                Ct);
            Assert.Equal([0, 255, 255, 0], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }
    }

    [Fact]
    public void ALayerIsDrawnOnEveryFrameAndDiscardedWhenItsCallbackThrows()
    {
        using var image = NewAnimation();
        image.Draw(canvas => canvas.Layer(1, layer => layer.FillRectangle(new Rgba32(9, 8, 7), new Rectangle(1, 1, 2, 1))), Ct);
        foreach (var frame in new[] { image.Frames[0], image.Frames[1], image.PosterFrame! })
        {
            Assert.Equal(new Rgba32(9, 8, 7), frame[1, 1]);
            Assert.Equal(new Rgba32(9, 8, 7), frame[2, 1]);
            Assert.NotEqual(new Rgba32(9, 8, 7), frame[0, 1]);
        }

        Assert.False(ExifTiff.HasThumbnail(image.Metadata.ExifProfile!.Data.Span));
        Assert.Equal(0, image.Owner.ActiveLeaseCount);

        // A callback that throws ends its layer, and the clips it started; nothing of the layer is drawn
        using var gray = new Image<Gray8>(2, 1, new Gray8(0));
        gray.Draw(
            canvas =>
            {
                try
                {
                    canvas.Layer(1, layer =>
                    {
                        layer.Fill(White);
                        layer.Clip(new Rectangle(0, 0, 1, 1), _ => throw new FormatException("stop"));
                    });
                }
                catch (FormatException)
                {
                }

                try
                {
                    canvas.Layer(1, 5, static (layer, _) => layer.Layer(1, static _ => throw new FormatException("stop")));
                }
                catch (FormatException)
                {
                }

                canvas.FillRectangle(White, new Rectangle(1, 0, 1, 1));
            },
            Ct);
        Assert.Equal([0, 255], DrawingTestSupport.ReadSamples(gray.Frames[0]));
        Assert.Equal(0, gray.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void ALayerValidatesItsArgumentsAndIsChargedToTheImage()
    {
        using (var image = new Image<Gray8>(4, 4, new Gray8(0)))
        {
            image.Draw(
                canvas =>
                {
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Layer(1, null!));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Layer(1, 1, null!));
                    AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Layer(-0.1, static _ => Assert.Fail("The callback ran.")));
                    AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Layer(1.5, static _ => Assert.Fail("The callback ran.")));
                    AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Layer(double.NaN, 1, static (_, _) => Assert.Fail("The callback ran.")));
                    AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Layer(1, static _ => Assert.Fail("The callback ran."), (CompositeMode)11));

                    // A rejected layer leaves the canvas as it was
                    canvas.FillRectangle(White, new Rectangle(0, 0, 1, 1));
                },
                Ct);
            Assert.Equal(new Gray8(255), image.Frames[0][0, 0]);
        }

        // Room for the pixels and for the scratch of a rectangle, not for the image of a layer: the callback is not
        // run, and the canvas can still be drawn on
        using var probe = new Image<Gray8>(64, 64, new Gray8(0), ImageConfiguration.Default);
        long session = 0;
        probe.Draw(
            canvas =>
            {
                canvas.FillRectangle(White, new Rectangle(0, 0, 1, 1));
                session = probe.Owner.Scope.LiveBytes;
            },
            Ct);
        long before;
        using (var image = new Image<Gray8>(64, 64, new Gray8(0), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = session } }))
        {
            before = image.Owner.Scope.LiveBytes;
            var thrown = false;
            image.Draw(
                canvas =>
                {
                    try
                    {
                        canvas.Layer(1, static _ => Assert.Fail("The callback ran."));
                    }
                    catch (ImageResourceLimitException)
                    {
                        thrown = true;
                    }

                    canvas.FillRectangle(White, new Rectangle(0, 0, 1, 1));
                },
                Ct);
            Assert.True(thrown);
            Assert.Equal(new Gray8(255), image.Frames[0][0, 0]);
            Assert.Equal(new Gray8(0), image.Frames[0][1, 0]);
            Assert.Equal(before, image.Owner.Scope.LiveBytes);
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
        }

        // The image of a layer is kept while the canvas lasts, used again by the next layer, and released with it
        using (var image = new Image<Gray8>(64, 64, new Gray8(0), ImageConfiguration.Default))
        {
            before = image.Owner.Scope.LiveBytes;
            image.Draw(
                canvas =>
                {
                    canvas.Layer(0.75, layer => layer.Layer(1, inner => inner.FillRectangle(White, new Rectangle(0, 0, 2, 2))));
                    var live = image.Owner.Scope.LiveBytes;
                    Assert.True(live >= before + (2 * 64 * 64 * 8));
                    canvas.Layer(0.5, layer => layer.Layer(1, inner => inner.FillRectangle(White, new Rectangle(1, 1, 2, 2))));
                    Assert.Equal(live, image.Owner.Scope.LiveBytes);
                },
                Ct);
            Assert.Equal(before, image.Owner.Scope.LiveBytes);

            // The second layer started transparent: what the first one drew is not drawn again. White at 3/4 is
            // 191.25 -> 191, and white at half over it (255 + 191) / 2 = 223
            Assert.Equal(new Gray8(191), image.Frames[0][0, 0]);
            Assert.Equal(new Gray8(223), image.Frames[0][1, 1]);
            Assert.Equal(new Gray8(128), image.Frames[0][2, 2]);
            Assert.Equal(new Gray8(0), image.Frames[0][3, 3]);
        }
    }

    private delegate void CanvasAction(DrawingCanvas canvas);

    private static void AssertThrows<TException>(DrawingCanvas canvas, CanvasAction action)
        where TException : Exception
    {
        try
        {
            action(canvas);
        }
        catch (TException exception) when (exception.GetType() == typeof(TException) || typeof(TException) == typeof(ArgumentException))
        {
            return;
        }

        Assert.Fail($"{typeof(TException).Name} was not thrown.");
    }

    /// <summary>A two-frame 4x3 Rgba32 image with a poster and an EXIF thumbnail; every frame has different pixels.</summary>
    private static Image<Rgba32> NewAnimation() => NewAnimation(pool: null, ImageConfiguration.Default);

    private static Image<Rgba32> NewAnimation(SlabPool? pool, ImageConfiguration configuration)
    {
        var image = new Image<Rgba32>(configuration, new Size(4, 3), new AllocationScope(configuration.Limits, pool), layoutOptions: null);
        for (var i = 0; i < 2; i++)
        {
            var frame = i == 0 ? image.Frames[0] : image.AppendFrame();
            for (var y = 0; y < 3; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    frame[x, y] = new Rgba32((byte)(x * 60), (byte)(y * 100), (byte)(i * 200), (byte)(255 - (x * 40) - i));
                }
            }
        }

        image.SetPosterFrame(image.Frames[1]);
        image.PosterFrame![0, 0] = new Rgba32(5, 6, 7, 8);
        image.Metadata.ExifProfile = new ExifProfile(MetadataBlob.FromOwnedArray(new TiffBuilder(bigEndian: false)
            .Ifd0(TiffBuilder.Short(ExifTiff.ImageWidthTag, 4), TiffBuilder.Short(ExifTiff.ImageLengthTag, 3))
            .Thumbnail("THUMB"u8.ToArray())
            .Build()));
        return image;
    }

    private static int[][] CopyAll(Image<Rgba32> image)
        => [DrawingTestSupport.ReadSamples(image.Frames[0]), DrawingTestSupport.ReadSamples(image.Frames[1]), DrawingTestSupport.ReadSamples(image.PosterFrame!)];
}
