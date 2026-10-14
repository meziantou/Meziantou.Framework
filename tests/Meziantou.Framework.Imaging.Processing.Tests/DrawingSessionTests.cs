using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The <c>Draw</c> entry points: the scope of the canvas, the lease held during the callback, the frames an image-level
/// call paints, argument validation, cancellation, the working storage and the metadata side effects.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearGradientBrush(default, default, stops) { SpreadMode = (GradientSpreadMode)4 });

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
                    AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Layer(1, static _ => Assert.Fail("The callback ran."), (CompositeMode)12));

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

    // -----------------------------------------------------------------------------------------------------------------
    // Shadows
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AShadowHasTheShapeOfTheDrawingMovedByItsOffset()
    {
        // A white square of 2 pixels and its black shadow, 2 pixels to the right and 1 down, without blur
        var black = new Rgba32(0, 0, 0);
        var white = new Rgba32(255, 255, 255);
        using var image = new Image<Rgba32>(6, 5);
        image.Draw(canvas => canvas.Shadow(black, new PointD(2, 1), 0, layer => layer.FillRectangle(White, new Rectangle(1, 1, 2, 2))), Ct);
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                var expected = x is 1 or 2 && y is 1 or 2 ? white : x is 3 or 4 && y is 2 or 3 ? black : default;
                Assert.Equal(expected, image.Frames[0][x, y]);
            }
        }

        // The drawing is over its shadow, and the shadow of several shapes is one shadow: where two translucent
        // shapes overlap, the alpha of the drawing is 0.5 + 0.5 * 0.5 = 0.75, and so is its shadow (191.75 -> 192)
        using var overlapping = new Image<Rgba32>(4, 3);
        var half = new SolidBrush(new Rgba32(255, 255, 255, 128));
        overlapping.Draw(
            canvas => canvas.Shadow(black, new PointD(0, 1), 0, layer =>
            {
                layer.FillRectangle(half, new Rectangle(0, 1, 2, 1));
                layer.FillRectangle(half, new Rectangle(1, 1, 2, 1));
            }),
            Ct);
        Assert.Equal([0, 0, 0, 0], Enumerable.Range(0, 4).Select(x => (int)overlapping.Frames[0][x, 0].A));
        Assert.Equal([128, 192, 128, 0], Enumerable.Range(0, 4).Select(x => (int)overlapping.Frames[0][x, 1].A));
        Assert.Equal([128, 192, 128, 0], Enumerable.Range(0, 4).Select(x => (int)overlapping.Frames[0][x, 2].A));
        Assert.Equal(new Rgba32(0, 0, 0, 192), overlapping.Frames[0][1, 2]);

        // An offset that is not whole: the shadow is between two pixels, half on each
        using var between = new Image<Rgba32>(6, 4);
        between.Draw(canvas => canvas.Shadow(new Rgba32(200, 0, 0), new PointD(0.5, 1), 0, layer => layer.FillRectangle(White, new Rectangle(1, 1, 1, 1))), Ct);
        Assert.Equal([default, new Rgba32(200, 0, 0, 128), new Rgba32(200, 0, 0, 128), default, default, default], Enumerable.Range(0, 6).Select(x => between.Frames[0][x, 2]));
        between.Draw(canvas => canvas.Clear(Colors.Transparent), Ct);
        between.Draw(canvas => canvas.Shadow(new Rgba32(200, 0, 0), new PointD(-1.25, 0.5), 0, layer => layer.FillRectangle(White, new Rectangle(3, 1, 2, 1))), Ct);

        // 1.25 pixel to the left: a quarter of the pixel 1, the pixel 2 and three quarters of the pixel 3; half a pixel
        // down: half of the rows 1 and 2. The drawing is over its shadow.
        Assert.Equal([0, 0, 0, 0, 0, 0], Enumerable.Range(0, 6).Select(x => (int)between.Frames[0][x, 0].A));
        Assert.Equal([default, new Rgba32(200, 0, 0, 32), new Rgba32(200, 0, 0, 128), white, white, default], Enumerable.Range(0, 6).Select(x => between.Frames[0][x, 1]));
        Assert.Equal([default, new Rgba32(200, 0, 0, 32), new Rgba32(200, 0, 0, 128), new Rgba32(200, 0, 0, 96), default, default], Enumerable.Range(0, 6).Select(x => between.Frames[0][x, 2]));
        Assert.Equal([0, 0, 0, 0, 0, 0], Enumerable.Range(0, 6).Select(x => (int)between.Frames[0][x, 3].A));

        // A drawing that reaches an edge of the canvas is taken to continue beyond it: its shadow comes from there too
        using var edge = new Image<Rgba32>(2, 2);
        edge.Draw(canvas => canvas.Shadow(black, new PointD(1, 1), 0, layer => layer.FillRectangle(new SolidBrush(new Rgba32(255, 255, 255, 128)), new Rectangle(0, 0, 1, 1))), Ct);
        Assert.Equal([192, 128, 128, 128], DrawingTestSupport.ReadSamples(edge.Frames[0]).Where((_, index) => index % 4 == 3));

        // The alpha of the color is the opacity of the shadow of an opaque drawing; without it there is no shadow
        using var faint = new Image<Rgba32>(2, 1);
        faint.Draw(canvas => canvas.Shadow(new Rgba32(0, 0, 0, 51), new PointD(1, 0), 0, layer => layer.FillRectangle(White, new Rectangle(0, 0, 1, 1))), Ct);
        Assert.Equal(new Rgba32(0, 0, 0, 51), faint.Frames[0][1, 0]);
        faint.Draw(canvas => canvas.Shadow(new Rgba32(9, 9, 9, 0), new PointD(1, 0), 3, layer => layer.FillRectangle(new Rgba32(1, 2, 3), new Rectangle(0, 0, 1, 1))), Ct);
        Assert.Equal(new Rgba32(1, 2, 3), faint.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 51), faint.Frames[0][1, 0]);
    }

    [Fact]
    public void AShadowIsBlurredByTheWeightsOfABell()
    {
        // One opaque pixel and a glow of deviation 1: the alpha of the glow is the product of the weights of its
        // column and of its row, 0.004433, 0.054006, 0.242036 and 0.399050. In the row of the pixel, 255 times
        // 0.399050 times each: 0.45, 5.5, 24.6, 40.6; on the diagonal, the squares: 0.005, 0.74, 14.9, 40.6
        var red = new Rgba32(255, 0, 0);
        using var image = new Image<Rgba32>(7, 7);
        image.Draw(canvas => canvas.Shadow(red, default, 1, layer => layer.FillRectangle(new Rgba32(0, 0, 255), new Rectangle(3, 3, 1, 1))), Ct);
        Assert.Equal([0, 5, 25, 255, 25, 5, 0], Enumerable.Range(0, 7).Select(x => (int)image.Frames[0][x, 3].A));
        Assert.Equal([0, 5, 25, 255, 25, 5, 0], Enumerable.Range(0, 7).Select(y => (int)image.Frames[0][3, y].A));
        Assert.Equal([0, 1, 15, 255, 15, 1, 0], Enumerable.Range(0, 7).Select(i => (int)image.Frames[0][i, i].A));
        Assert.Equal(new Rgba32(255, 0, 0, 25), image.Frames[0][2, 3]);
        Assert.Equal(new Rgba32(0, 0, 255), image.Frames[0][3, 3]);

        // Moved by 4 pixels: the whole glow, 41 at its center, beside the pixel
        using var moved = new Image<Rgba32>(12, 7);
        moved.Draw(canvas => canvas.Shadow(red, new PointD(5, 0), 1, 7, static (layer, _) => layer.FillRectangle(new Rgba32(0, 0, 255), new Rectangle(3, 3, 1, 1))), Ct);
        Assert.Equal([0, 5, 25, 41, 25, 5, 0], Enumerable.Range(5, 7).Select(x => (int)moved.Frames[0][x, 3].A));
        Assert.Equal(new Rgba32(0, 0, 255), moved.Frames[0][3, 3]);

        // A bar across the whole canvas and its shadow under it: the bar continues beyond the edges of the canvas, so
        // its shadow is the same along the whole row. Two rows down from the bar, the rows the blur reads are opaque
        // for the weights that add up to 0.9416, then 0.6995, 0.3005, 0.0584 and 0.0044
        using var bar = new Image<Rgba32>(5, 8);
        bar.Draw(canvas => canvas.Shadow(new Rgba32(0, 0, 0), new PointD(0, 2), 1, layer => layer.FillRectangle(White, new Rectangle(0, 0, 5, 2))), Ct);
        for (var x = 0; x < 5; x++)
        {
            Assert.Equal([255, 255, 240, 178, 77, 15, 1, 0], Enumerable.Range(0, 8).Select(y => (int)bar.Frames[0][x, y].A));
            Assert.Equal(new Rgba32(0, 0, 0, 178), bar.Frames[0][x, 3]);
        }
    }

    [Fact]
    public void AShadowCombinesWithClipsLayersAndFrames()
    {
        // What is drawn outside the clip is not drawn, and still casts its shadow in it
        var black = new Rgba32(0, 0, 0);
        using (var image = new Image<Rgba32>(4, 6))
        {
            image.Draw(
                canvas => canvas.Clip(new Rectangle(0, 3, 4, 3), clipped => clipped.Shadow(black, new PointD(0, 3), 0, layer => layer.FillRectangle(White, new Rectangle(1, 0, 2, 2)))),
                Ct);
            for (var y = 0; y < 6; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    Assert.Equal(x is 1 or 2 && y is 3 or 4 ? black : default, image.Frames[0][x, y]);
                }
            }
        }

        // In a layer, the drawing and its shadow are faded together; on a format without alpha, the shadow darkens
        using (var image = new Image<Gray8>(3, 1, new Gray8(200)))
        {
            image.Draw(canvas => canvas.Layer(0.5, layer => layer.Shadow(black, new PointD(1, 0), 0, inner => inner.FillRectangle(White, new Rectangle(0, 0, 1, 1)))), Ct);

            // (255 + 200) / 2 = 227.5 -> 228 and (0 + 200) / 2 = 100
            Assert.Equal([228, 100, 200], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // Every frame gets the same shadow
        using (var image = NewAnimation())
        {
            image.Draw(canvas => canvas.Shadow(black, new PointD(1, 1), 0, layer => layer.FillRectangle(new Rgba32(9, 8, 7), new Rectangle(1, 0, 1, 1))), Ct);
            foreach (var frame in new[] { image.Frames[0], image.Frames[1], image.PosterFrame! })
            {
                Assert.Equal(new Rgba32(9, 8, 7), frame[1, 0]);
                Assert.Equal(black, frame[2, 1]);
            }

            Assert.False(ExifTiff.HasThumbnail(image.Metadata.ExifProfile!.Data.Span));
        }

        // A callback that throws draws nothing, not even a shadow
        using (var image = new Image<Gray8>(2, 1, new Gray8(100)))
        {
            image.Draw(
                canvas =>
                {
                    try
                    {
                        canvas.Shadow(black, new PointD(1, 0), 0, layer =>
                        {
                            layer.Fill(White);
                            throw new FormatException("stop");
                        });
                    }
                    catch (FormatException)
                    {
                    }
                },
                Ct);
            Assert.Equal([100, 100], DrawingTestSupport.ReadSamples(image.Frames[0]));
            Assert.Equal(0, image.Owner.ActiveLeaseCount);
        }
    }

    [Fact]
    public void AShadowValidatesItsArgumentsAndIsChargedToTheImage()
    {
        using var image = new Image<Gray8>(16, 16, new Gray8(200), ImageConfiguration.Default);
        var before = image.Owner.Scope.LiveBytes;
        image.Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Shadow(default, default, 1, null!));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Shadow(default, default, 1, 1, null!));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.Shadow(default, new PointD(double.NaN, 0), 1, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Shadow(default, new PointD(0, 16777217), 1, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.Shadow(default, default, double.NaN, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Shadow(default, default, -1, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Shadow(default, default, 10001, 1, static (_, _) => Assert.Fail("The callback ran.")));

                // The storage of a shadow is kept for the next one
                canvas.Shadow(new Rgba32(0, 0, 0), new PointD(2, 2), 1.5, layer => layer.FillCircle(White, new PointD(6, 6), 3));
                var live = image.Owner.Scope.LiveBytes;
                Assert.True(live > before);
                canvas.Shadow(new Rgba32(0, 0, 0, 128), new PointD(-1, 3.5), 1, layer => layer.FillCircle(White, new PointD(9, 6), 2));
                Assert.Equal(live, image.Owner.Scope.LiveBytes);
            },
            Ct);
        Assert.Equal(before, image.Owner.Scope.LiveBytes);
        Assert.Equal(new Gray8(255), image.Frames[0][9, 6]);
        Assert.InRange(image.Frames[0][6, 6].Value, 201, 255);
        Assert.InRange(image.Frames[0][8, 10].Value, 1, 150);
        Assert.Equal(new Gray8(200), image.Frames[0][0, 15]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The transformation of the canvas
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<double, double, double, double, double, double> Transforms => new()
    {
        { 1, 0, 0, 1, 3, 2 },
        { 1, 0, 0, 1, 2.25, -1.5 },
        { 1.5, 0, 0, 1.5, 1, 1 },
        { 2, 0, 0, 0.5, 0, 6 },
        { 0, 1, -1, 0, 20, 2 },
        { -1, 0, 0, 1, 22, 1 },
        { 0.8, 0.6, -0.6, 0.8, 9, -3 },
        { 1.2, 0.3, 0.5, 0.9, -2, 1.5 },
        { 0.6, -0.9, 1.1, 0.4, 3, 12 },
    };

    [Theory]
    [MemberData(nameof(Transforms))]
    public void ATransformedDrawingIsTheDrawingOfTheTransformedShapes(double m11, double m12, double m21, double m22, double dx, double dy)
    {
        var transform = new AffineTransform(m11, m12, m21, m22, dx, dy);
        var polygon = new VectorPathBuilder().AddPolygon([new PointD(2, 1), new PointD(13.5, 3), new PointD(9, 12.25), new PointD(1.5, 8)]).AddRectangle(new RectangleD(4, 4, 3, 3)).Build();
        var curve = new VectorPathBuilder().MoveTo(2, 9).CubicBezierTo(3, -4, 9, 16, 13, 2).AddEllipse(new PointD(7, 7), 5, 3, 25).AddRoundedRectangle(new RectangleD(1, 1, 12, 9), new CornerRadii(2, 4, 0, 3)).Build();
        GradientStop[] stops = [new(0, new Rgba32(255, 0, 0)), new(1, new Rgba32(0, 0, 255, 60))];
        var gradient = new LinearGradientBrush(new PointD(1, 2), new PointD(12, 9), stops) { Transform = AffineTransform.CreateSkew(10, 0) };
        var radial = new RadialGradientBrush(new PointD(6, 6), 7, stops) { SpreadMode = GradientSpreadMode.Reflect };
        using var texture = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 5, 4, new Random(3).Next);
        var tile = new ImageBrush(texture.Frames[0]) { WrapMode = ImageWrapMode.Repeat, Transform = AffineTransform.CreateScale(1.5, 2) };

        // Straight sides: the vertices are transformed as those of a path are, and the pixels are the same, bit for
        // bit. The brushes are placed by their own transformation, then by the one of the canvas.
        AssertSame(canvas => canvas.Fill(White, polygon.Transform(transform)), canvas => canvas.Transform(transform, inner => inner.Fill(White, polygon)));
        AssertSame(canvas => canvas.Fill(White, polygon.Transform(transform), FillRule.EvenOdd), canvas => canvas.Transform(transform, inner => inner.Fill(White, polygon, FillRule.EvenOdd)));
        AssertSame(
            canvas => canvas.Fill(new LinearGradientBrush(new PointD(1, 2), new PointD(12, 9), stops) { Transform = AffineTransform.CreateSkew(10, 0).Then(transform) }, polygon.Transform(transform)),
            canvas => canvas.Transform(transform, inner => inner.Fill(gradient, polygon)));
        AssertSame(
            canvas => canvas.Fill(new RadialGradientBrush(new PointD(6, 6), 7, stops) { SpreadMode = GradientSpreadMode.Reflect, Transform = transform }, polygon.Transform(transform)),
            canvas => canvas.Transform(transform, inner => inner.Fill(radial, polygon)));
        AssertSame(
            canvas => canvas.Fill(new ImageBrush(texture.Frames[0]) { WrapMode = ImageWrapMode.Repeat, Transform = AffineTransform.CreateScale(1.5, 2).Then(transform) }, polygon.Transform(transform)),
            canvas => canvas.Transform(transform, inner => inner.Fill(tile, polygon)));
        AssertSame(
            canvas => canvas.FillPolygon(White, [transform.Transform(new PointD(1, 1)), transform.Transform(new PointD(9, 2)), transform.Transform(new PointD(4, 11))]),
            canvas => canvas.Transform(transform, 3, static (inner, _) => inner.FillPolygon(White, [new PointD(1, 1), new PointD(9, 2), new PointD(4, 11)])));
        AssertSame(
            canvas => canvas.Fill(White, new VectorPathBuilder().AddRectangle(new RectangleD(2.5, 1, 8, 6.25)).Build().Transform(transform)),
            canvas => canvas.Transform(transform, inner => inner.FillRectangle(White, new RectangleD(2.5, 1, 8, 6.25))));

        // An image, to a rectangle or with a transformation of its own
        AssertSame(
            canvas => canvas.DrawImage(texture.Frames[0], AffineTransform.CreateScale(2, 1.5).Then(AffineTransform.CreateTranslation(1, 2)).Then(transform), 0.75),
            canvas => canvas.Transform(transform, inner => inner.DrawImage(texture.Frames[0], new RectangleD(1, 2, 10, 6), 0.75)));
        AssertSame(
            canvas => canvas.DrawImage(texture.Frames[0], AffineTransform.CreateRotation(20).Then(transform), ImageSampling.Smooth),
            canvas => canvas.Transform(transform, inner => inner.DrawImage(texture.Frames[0], AffineTransform.CreateRotation(20), ImageSampling.Smooth)));

        // The stroke of a pen is the stroke of the path, transformed: the pen is transformed with it. With straight
        // joins and ends, the pieces are the same, bit for bit.
        var pen = new Pen(White, 2.5) { LineJoin = LineJoin.Miter, LineCap = LineCap.Square };
        var dashed = new Pen(White, 1.5) { LineJoin = LineJoin.Bevel, DashPattern = [3, 2, 1, 2], DashOffset = 1.25 };
        AssertSame(canvas => canvas.Fill(White, polygon.Widen(pen).Transform(transform)), canvas => canvas.Transform(transform, inner => inner.Stroke(pen, polygon)));
        AssertSame(canvas => canvas.Fill(White, polygon.Widen(dashed).Transform(transform)), canvas => canvas.Transform(transform, inner => inner.Stroke(dashed, polygon)));
        AssertSame(
            canvas => canvas.Fill(White, new VectorPathBuilder().MoveTo(2, 3).LineTo(11, 9).Build().Widen(pen).Transform(transform)),
            canvas => canvas.Transform(transform, inner => inner.StrokeLine(pen, new PointD(2, 3), new PointD(11, 9))));

        // A clip is the clip of the transformed path, and a rectangle is a path
        AssertSame(canvas => canvas.Fill(White, polygon.Transform(transform)), canvas => canvas.Transform(transform, inner => inner.Clip(polygon, clipped => clipped.Fill(White))));
        AssertSame(
            canvas => canvas.Fill(White, new VectorPathBuilder().AddRectangle(new Rectangle(2, 1, 8, 6)).Build().Transform(transform)),
            canvas => canvas.Transform(transform, inner => inner.Clip(new Rectangle(2, 1, 8, 6), clipped => clipped.Fill(White))));

        // Curves are flattened for the size the transformation gives them, before their vertices are transformed: the
        // chords are not those of the transformed path, and both stay within 1/16 of a pixel of the curve. Along an
        // edge that crosses a pixel, that is at most 2 * sqrt(2) / 16 of the pixel: 45 levels.
        var round = new Pen(White, 2) { LineJoin = LineJoin.Round, LineCap = LineCap.Round };
        AssertClose(canvas => canvas.Fill(White, curve.Transform(transform)), canvas => canvas.Transform(transform, inner => inner.Fill(White, curve)), 46);
        AssertClose(canvas => canvas.Stroke(round, curve.Transform(transform)), canvas => canvas.Transform(AffineTransform.CreateTranslation(dx, dy), inner => inner.Stroke(round, curve.Transform(new AffineTransform(m11, m12, m21, m22, 0, 0)))), 46);

        // The stroke of a curve with a round pen: the pieces of the stroke, transformed. The pieces of a widened path
        // are cut for the size the path has, so their chords are as far from the stroke as the transformation makes
        // them (up to 2.3 / 16 of a pixel here), and those of the canvas within 1/16
        AssertClose(canvas => canvas.Fill(White, curve.Widen(round).Transform(transform)), canvas => canvas.Transform(transform, inner => inner.Stroke(round, curve)), 96);
        AssertClose(
            canvas => canvas.Fill(White, new VectorPathBuilder().AddEllipse(new PointD(7, 6), 5, 3).Build().Transform(transform)),
            canvas => canvas.Transform(transform, inner => inner.FillEllipse(White, new PointD(7, 6), 5, 3)),
            46);

        // A transformation inside another one applies first
        var inner = AffineTransform.CreateRotation(15, new PointD(6, 6)).Then(AffineTransform.CreateScale(0.9, 1.1));
        AssertSame(
            canvas => canvas.Transform(inner.Then(transform), nested => nested.Fill(gradient, polygon)),
            canvas => canvas.Transform(transform, outer => outer.Transform(inner, nested => nested.Fill(gradient, polygon))));

        static void AssertSame(DrawingAction expected, DrawingAction actual) => AssertClose(expected, actual, 0);

        static void AssertClose(DrawingAction expected, DrawingAction actual, int tolerance)
        {
            using var first = new Image<Rgba32>(32, 28, new Rgba32(10, 20, 30, 200));
            using var second = new Image<Rgba32>(32, 28, new Rgba32(10, 20, 30, 200));
            first.Draw(expected, Ct);
            second.Draw(actual, Ct);
            var left = DrawingTestSupport.ReadSamples(first.Frames[0]);
            var right = DrawingTestSupport.ReadSamples(second.Frames[0]);
            if (tolerance == 0)
            {
                Assert.Equal(left, right);
                return;
            }

            for (var i = 0; i < left.Length; i++)
            {
                Assert.InRange(right[i], left[i] - tolerance, left[i] + tolerance, message: $"sample {i}");
            }
        }
    }

    [Fact]
    public void ATransformationAppliesToTheDrawingsOfItsCallbackOnly()
    {
        // Enlarged 40 times, a circle of radius 1 is the circle of radius 40: its curve is not the octagon a circle
        // of radius 1 is drawn with
        using (var direct = new Image<Gray8>(100, 100, new Gray8(0)))
        using (var scaled = new Image<Gray8>(100, 100, new Gray8(0)))
        {
            direct.Draw(canvas => canvas.FillCircle(White, new PointD(50, 50), 40), Ct);
            scaled.Draw(canvas => canvas.Transform(AffineTransform.CreateScale(40, 40).Then(AffineTransform.CreateTranslation(10, 10)), inner => inner.FillCircle(White, new PointD(1, 1), 1)), Ct);
            var expected = DrawingTestSupport.ReadSamples(direct.Frames[0]);
            var actual = DrawingTestSupport.ReadSamples(scaled.Frames[0]);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.InRange(actual[i], expected[i] - 2, expected[i] + 2, message: $"pixel {i}");
            }

            // The pen is enlarged with the path: a circle of radius 1 stroked with a pen of 0.1 is a ring 4 pixels wide
            scaled.Draw(
                canvas =>
                {
                    canvas.Clear(Colors.Black);
                    canvas.Transform(AffineTransform.CreateScale(40, 40).Then(AffineTransform.CreateTranslation(10, 10)), inner => inner.StrokeCircle(new Pen(White, 0.1), new PointD(1, 1), 1));
                },
                Ct);
            Assert.Equal(new Gray8(255), scaled.Frames[0][50, 10]);
            Assert.Equal(new Gray8(255), scaled.Frames[0][50, 11]);
            Assert.Equal(new Gray8(0), scaled.Frames[0][50, 14]);
            Assert.Equal(new Gray8(0), scaled.Frames[0][50, 6]);
            Assert.Equal(2 * Math.PI * 40 * 4, DrawingTestSupport.ReadSamples(scaled.Frames[0]).Sum() / 255.0, 3.0);
        }

        using var image = new Image<Gray8>(8, 4, new Gray8(0));
        image.Draw(
            canvas =>
            {
                canvas.Transform(AffineTransform.CreateTranslation(4, 0), inner => inner.FillRectangle(White, new Rectangle(0, 0, 1, 1)));

                // The transformation ended with its callback, also when the callback throws
                canvas.FillRectangle(White, new Rectangle(0, 1, 1, 1));
                try
                {
                    canvas.Transform(AffineTransform.CreateTranslation(4, 0), 1, static (_, _) => throw new FormatException("stop"));
                }
                catch (FormatException)
                {
                }

                canvas.FillRectangle(White, new Rectangle(0, 2, 1, 1));

                // The size of the canvas does not change, and filling the canvas fills all of it: only the brush moves
                canvas.Transform(AffineTransform.CreateScale(2, 2), inner =>
                {
                    Assert.Equal(8, inner.Width);
                    Assert.Equal(4, inner.Height);
                    inner.Clip(new Rectangle(0, 3, 8, 1), row => row.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(255, 255, 255))])));
                });
            },
            Ct);

        // The gradient goes from 0 to 4 before the transformation, so from 0 to 8 on the canvas: the parameters of the
        // centers are 1/16, 3/16, ... 15/16, and the clip of the row 3 is the rows 6 and 7 of the canvas, outside it
        Assert.Equal(
            [
                0, 0, 0, 0, 255, 0, 0, 0,
                255, 0, 0, 0, 0, 0, 0, 0,
                255, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0,
            ],
            DrawingTestSupport.ReadSamples(image.Frames[0]));
        image.Draw(
            canvas => canvas.Transform(AffineTransform.CreateScale(2, 2), inner => inner.Clip(new RectangleD(0, 1.5, 4, 0.5), row => row.Fill(new LinearGradientBrush(new PointD(0, 0), new PointD(4, 0), [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(255, 255, 255))])))),
            Ct);
        Assert.Equal([16, 48, 80, 112, 143, 175, 207, 239], DrawingTestSupport.ReadSamples(image.Frames[0])[24..]);

        // A transformation that flattens the plane draws nothing; one that sends a shape too far is rejected, and
        // the canvas can still be drawn on
        using var gray = new Image<Gray8>(4, 4, new Gray8(0));
        gray.Draw(
            canvas =>
            {
                canvas.Transform(AffineTransform.CreateScale(0, 1), inner => inner.FillRectangle(White, new Rectangle(0, 0, 4, 4)));
                canvas.Transform(AffineTransform.CreateScale(1, 0), inner => inner.Stroke(WhitePen, new VectorPathBuilder().AddCircle(new PointD(2, 2), 1).Build()));
                canvas.Transform(AffineTransform.CreateScale(1e6, 1e6), inner =>
                {
                    AssertThrows<ArgumentOutOfRangeException>(inner, static canvas => canvas.FillRectangle(White, new Rectangle(0, 0, 100, 100)));
                    AssertThrows<ArgumentOutOfRangeException>(inner, static canvas => canvas.StrokeLine(WhitePen, new PointD(0, 0), new PointD(100, 100)));
                    AssertThrows<ArgumentOutOfRangeException>(inner, static canvas => canvas.Shadow(Colors.Black, new PointD(100, 0), 1, static _ => Assert.Fail("The callback ran.")));
                    AssertThrows<ArgumentException>(inner, static canvas => canvas.FillRectangle(White, new RectangleD(double.NaN, 0, 1, 1)));
                });
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Transform(AffineTransform.Identity, null!));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Transform(AffineTransform.Identity, 1, null!));
                canvas.FillRectangle(White, new Rectangle(3, 3, 1, 1));
            },
            Ct);
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255], DrawingTestSupport.ReadSamples(gray.Frames[0]));
    }

    [Theory]
    [MemberData(nameof(Transforms))]
    public void APenThatDoesNotScaleStrokesThePathAsItIsOnTheCanvas(double m11, double m12, double m21, double m22, double dx, double dy)
    {
        var transform = new AffineTransform(m11, m12, m21, m22, dx, dy);
        var polygon = new VectorPathBuilder().AddPolygon([new PointD(2, 1), new PointD(13.5, 3), new PointD(9, 12.25), new PointD(1.5, 8)]).MoveTo(3, 4).LineTo(8, 5).LineTo(6, 9).Build();
        var curve = new VectorPathBuilder().MoveTo(2, 9).CubicBezierTo(3, -4, 9, 16, 13, 2).AddEllipse(new PointD(7, 7), 5, 3, 25).Build();
        Pen[] pens =
        [
            new(White, 2.5) { LineJoin = LineJoin.Miter, LineCap = LineCap.Square, ScalesWithTransform = false },
            new(White, 1.5) { LineJoin = LineJoin.Bevel, DashPattern = [3, 2, 1, 2], DashOffset = 1.25, ScalesWithTransform = false },
            new(White, 2) { StartMarker = LineMarker.Arrow, EndMarker = LineMarker.Diamond, ScalesWithTransform = false },
            new(White, 2) { Alignment = PenAlignment.Inside, ScalesWithTransform = false },
            new(White, 2) { Alignment = PenAlignment.Outside, DashPattern = [4, 1], ScalesWithTransform = false },
        ];

        // The path is transformed, then stroked: the width, the dashes and the markers are those of the pen. With
        // straight sides, the vertices are those of the transformed path, and the pixels are the same, bit for bit.
        foreach (var pen in pens)
        {
            AssertClose(canvas => canvas.Stroke(pen, polygon.Transform(transform)), canvas => canvas.Transform(transform, inner => inner.Stroke(pen, polygon)), 0);
        }

        AssertClose(
            canvas => canvas.StrokeLine(pens[0], transform.Transform(new PointD(2, 3)), transform.Transform(new PointD(11, 9))),
            canvas => canvas.Transform(transform, inner => inner.StrokeLine(pens[0], new PointD(2, 3), new PointD(11, 9))),
            0);
        AssertClose(
            canvas => canvas.StrokePolygon(pens[1], [transform.Transform(new PointD(1, 1)), transform.Transform(new PointD(9, 2)), transform.Transform(new PointD(4, 11))]),
            canvas => canvas.Transform(transform, 3, (inner, _) => inner.StrokePolygon(pens[1], [new PointD(1, 1), new PointD(9, 2), new PointD(4, 11)])),
            0);

        // Curves are flattened for the size the transformation gives them, before their vertices are transformed:
        // the chords are not those of the transformed path, and both stay within 1/16 of a pixel of the curve
        var round = new Pen(White, 2) { LineJoin = LineJoin.Round, LineCap = LineCap.Round, ScalesWithTransform = false };
        AssertClose(canvas => canvas.Stroke(round, curve.Transform(transform)), canvas => canvas.Transform(transform, inner => inner.Stroke(round, curve)), 46);

        // Inside a transformation that is inside another one, the path goes through both
        var first = AffineTransform.CreateRotation(15, new PointD(6, 6)).Then(AffineTransform.CreateScale(0.9, 1.1));
        AssertClose(
            canvas => canvas.Stroke(pens[1], polygon.Transform(first.Then(transform))),
            canvas => canvas.Transform(transform, outer => outer.Transform(first, nested => nested.Stroke(pens[1], polygon))),
            0);

        static void AssertClose(DrawingAction expected, DrawingAction actual, int tolerance)
        {
            using var first = new Image<Rgba32>(32, 28, new Rgba32(10, 20, 30, 200));
            using var second = new Image<Rgba32>(32, 28, new Rgba32(10, 20, 30, 200));
            first.Draw(expected, Ct);
            second.Draw(actual, Ct);
            var left = DrawingTestSupport.ReadSamples(first.Frames[0]);
            var right = DrawingTestSupport.ReadSamples(second.Frames[0]);
            if (tolerance == 0)
            {
                Assert.Equal(left, right);
                return;
            }

            for (var i = 0; i < left.Length; i++)
            {
                Assert.InRange(right[i], left[i] - tolerance, left[i] + tolerance, message: $"sample {i}");
            }
        }
    }

    [Fact]
    public void APenThatDoesNotScaleKeepsItsWidthAtEveryScale()
    {
        // Enlarged 40 times, a circle of radius 1 stroked with a pen of 4 pixels is a ring 4 pixels wide: the pen a
        // transformation applies to draws it 160 pixels wide, which covers the whole circle
        var enlarge = AffineTransform.CreateScale(40, 40).Then(AffineTransform.CreateTranslation(10, 10));
        var pen = new Pen(White, 4) { ScalesWithTransform = false };
        using var scaled = new Image<Gray8>(100, 100, new Gray8(0));
        using var direct = new Image<Gray8>(100, 100, new Gray8(0));
        scaled.Draw(canvas => canvas.Transform(enlarge, inner => inner.StrokeCircle(pen, new PointD(1, 1), 1)), Ct);
        direct.Draw(canvas => canvas.StrokeCircle(pen, new PointD(50, 50), 40), Ct);
        Assert.Equal(new Gray8(255), scaled.Frames[0][50, 9]);
        Assert.Equal(new Gray8(255), scaled.Frames[0][50, 10]);
        Assert.Equal(new Gray8(0), scaled.Frames[0][50, 13]);
        Assert.Equal(new Gray8(0), scaled.Frames[0][50, 6]);
        Assert.Equal(2 * Math.PI * 40 * 4, DrawingTestSupport.ReadSamples(scaled.Frames[0]).Sum() / 255.0, 3.0);
        var expected = DrawingTestSupport.ReadSamples(direct.Frames[0]);
        var actual = DrawingTestSupport.ReadSamples(scaled.Frames[0]);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(actual[i], expected[i] - 2, expected[i] + 2, message: $"pixel {i}");
        }

        // A scaling along one axis makes the stroke of a pen that scales wider in that direction only; the other pen
        // draws the two sides of a square as wide as each other
        using var stretched = new Image<Gray8>(40, 12, new Gray8(0));
        stretched.Draw(canvas => canvas.Transform(AffineTransform.CreateScale(4, 1), inner => inner.StrokeRectangle(new Pen(White, 2) { ScalesWithTransform = false }, new RectangleD(1, 3, 8, 6))), Ct);
        var samples = DrawingTestSupport.ReadSamples(stretched.Frames[0]);

        // The rectangle is from 4 to 36 and from 3 to 9: its left side covers the columns 3 and 4, its top side the rows 2 and 3
        Assert.Equal([0, 0, 0, 255, 255, 0, 0], samples.AsSpan(6 * 40, 7).ToArray());
        Assert.Equal([0, 0, 255, 255, 0, 0], Enumerable.Range(0, 6).Select(y => samples[(y * 40) + 20]).ToArray());

        // Without transformation the value changes nothing, and the pieces of a widened path do not depend on it
        var path = new VectorPathBuilder().MoveTo(2, 9).CubicBezierTo(3, -4, 9, 16, 13, 2).Build();
        var plain = new Pen(White, 3) { DashPattern = [2, 1.5], EndMarker = LineMarker.Arrow };
        var same = new Pen(White, 3) { DashPattern = [2, 1.5], EndMarker = LineMarker.Arrow, ScalesWithTransform = false };
        Assert.True(plain.ScalesWithTransform);
        Assert.False(same.ScalesWithTransform);
        using var one = new Image<Gray8>(16, 16, new Gray8(0));
        using var other = new Image<Gray8>(16, 16, new Gray8(0));
        one.Draw(canvas => canvas.Stroke(plain, path), Ct);
        other.Draw(canvas => canvas.Stroke(same, path), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(one.Frames[0]), DrawingTestSupport.ReadSamples(other.Frames[0]));
        Assert.Equal(path.Widen(plain).ToSvgPathData(), path.Widen(same).ToSvgPathData());

        // A transformation that flattens the plane leaves a line, which the pen strokes; one that sends the path too
        // far is rejected before a pixel changes
        using var flat = new Image<Gray8>(8, 8, new Gray8(0));
        flat.Draw(
            canvas =>
            {
                canvas.Transform(AffineTransform.CreateScale(1, 0).Then(AffineTransform.CreateTranslation(0, 4)), inner => inner.StrokeLine(new Pen(White, 2) { ScalesWithTransform = false }, new PointD(2, 1), new PointD(6, 7)));
                canvas.Transform(AffineTransform.CreateScale(1, 0).Then(AffineTransform.CreateTranslation(0, 4)), inner => inner.StrokeRectangle(new Pen(White, 2) { ScalesWithTransform = false, Alignment = PenAlignment.Outside }, new RectangleD(2, 1, 4, 5)));
                canvas.Transform(AffineTransform.CreateScale(1e6, 1e6), inner => AssertThrows<ArgumentOutOfRangeException>(inner, static canvas => canvas.StrokeLine(new Pen(White, 1) { ScalesWithTransform = false }, new PointD(0, 0), new PointD(100, 100))));
            },
            Ct);
        var line = DrawingTestSupport.ReadSamples(flat.Frames[0]);
        Assert.Equal([0, 0, 255, 255, 255, 255, 0, 0], line.AsSpan(3 * 8, 8).ToArray());
        Assert.Equal([0, 0, 255, 255, 255, 255, 0, 0], line.AsSpan(4 * 8, 8).ToArray());
    }

    [Fact]
    public void ATransformationCombinesWithLayersAndShadows()
    {
        // A layer keeps the pixels of the canvas: what is drawn in it is transformed, and the layer itself is not
        using (var image = new Image<Gray8>(6, 2, new Gray8(0)))
        {
            image.Draw(
                canvas => canvas.Transform(AffineTransform.CreateTranslation(2, 0), inner => inner.Layer(0.5, layer => layer.FillRectangle(White, new Rectangle(0, 0, 2, 1)))),
                Ct);
            Assert.Equal([0, 0, 128, 128, 0, 0, 0, 0, 0, 0, 0, 0], DrawingTestSupport.ReadSamples(image.Frames[0]));
            image.Draw(
                canvas => canvas.Layer(1, layer => layer.Transform(AffineTransform.CreateScale(2, 2), inner => inner.FillRectangle(White, new Rectangle(0, 0, 1, 1)))),
                Ct);
            Assert.Equal([255, 255, 128, 128, 0, 0, 255, 255, 0, 0, 0, 0], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // The offset and the blur of a shadow are distances of the drawing: twice as large when the drawing is
        using var plain = new Image<Rgba32>(24, 24);
        using var scaled = new Image<Rgba32>(24, 24);
        var black = new Rgba32(0, 0, 0);
        plain.Draw(canvas => canvas.Shadow(black, new PointD(6, 4), 2, layer => layer.FillRectangle(White, new Rectangle(4, 4, 8, 8))), Ct);
        scaled.Draw(
            canvas => canvas.Transform(AffineTransform.CreateScale(2, 2), inner => inner.Shadow(black, new PointD(3, 2), 1, layer => layer.FillRectangle(White, new Rectangle(2, 2, 4, 4)))),
            Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(plain.Frames[0]), DrawingTestSupport.ReadSamples(scaled.Frames[0]));
        Assert.Equal(new Rgba32(255, 255, 255), scaled.Frames[0][5, 5]);
        Assert.InRange(scaled.Frames[0][14, 13].A, 200, 255);

        // A quarter turn moves the shadow with the drawing
        using var turned = new Image<Rgba32>(6, 6);
        turned.Draw(
            canvas => canvas.Transform(AffineTransform.CreateRotation(90).Then(AffineTransform.CreateTranslation(6, 0)), inner => inner.Shadow(black, new PointD(2, 0), 0, layer => layer.FillRectangle(White, new Rectangle(1, 1, 1, 1)))),
            Ct);

        // The pixel (1, 1) goes to the column 6 - 2 = 4 and the row 1; its shadow, 2 to the right, goes 2 down
        Assert.Equal(new Rgba32(255, 255, 255), turned.Frames[0][4, 1]);
        Assert.Equal(black, turned.Frames[0][4, 3]);
        Assert.Equal(2, DrawingTestSupport.ReadSamples(turned.Frames[0]).Where((_, index) => index % 4 == 3).Count(alpha => alpha != 0));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Projective transformations and transformations of space
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AnAffineProjectiveTransformationDrawsWhatTheAffineOneDraws()
    {
        var transform = new AffineTransform(0.8, 0.3, -0.4, 1.1, 9, 2);
        var polygon = new VectorPathBuilder().AddPolygon([new PointD(2, 1), new PointD(13.5, 3), new PointD(9, 12.25), new PointD(1.5, 8)]).Build();
        var curve = new VectorPathBuilder().MoveTo(2, 9).CubicBezierTo(3, -4, 9, 16, 13, 2).AddEllipse(new PointD(7, 7), 5, 3, 25).Build();
        GradientStop[] stops = [new(0, new Rgba32(255, 0, 0)), new(1, new Rgba32(0, 0, 255, 60))];
        var gradient = new LinearGradientBrush(new PointD(1, 2), new PointD(12, 9), stops);
        using var texture = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 5, 4, new Random(3).Next);
        var round = new Pen(White, 2) { LineJoin = LineJoin.Round, LineCap = LineCap.Round, DashPattern = [3, 1] };
        var fixedPen = new Pen(gradient, 1.5) { ScalesWithTransform = false };
        DrawingAction drawing = canvas =>
        {
            canvas.Fill(gradient, curve);
            canvas.Stroke(round, polygon);
            canvas.Stroke(fixedPen, curve);
            canvas.DrawImage(texture.Frames[0], new RectangleD(1, 2, 10, 6), ImageSampling.Smooth);
            canvas.Clip(polygon, clipped => clipped.Fill(new ImageBrush(texture.Frames[0]) { WrapMode = ImageWrapMode.Reflect }));
            canvas.Shadow(new Rgba32(0, 0, 0, 128), new PointD(2, 1), 1.5, shadowed => shadowed.FillCircle(gradient, new PointD(8, 8), 4));
            canvas.Blur(1, blurred => blurred.FillRectangle(White, new RectangleD(3, 3, 4, 2)));
        };

        var expected = Draw(canvas => canvas.Transform(transform, drawing));

        // The third column is (0, 0, 1): the transformation is the affine one, and so is a multiple of it by 2, or the
        // transformation of space it is
        Assert.Equal(expected, Draw(canvas => canvas.Transform((ProjectiveTransform)transform, drawing)));
        Assert.Equal(expected, Draw(canvas => canvas.Transform(new ProjectiveTransform(1.6, 0.6, 0, -0.8, 2.2, 0, 18, 4, 2), drawing)));
        Assert.Equal(expected, Draw(canvas => canvas.Transform((Transform3D)transform, drawing)));
        Assert.Equal(expected, Draw(canvas => canvas.Transform(Transform3D.CreateTranslation(0, 0, 40), outer => outer.Transform((Transform3D)transform, drawing))));

        static int[] Draw(DrawingAction action)
        {
            using var image = new Image<Rgba32>(32, 28, new Rgba32(10, 20, 30, 200));
            image.Draw(action, Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void APlaneInPerspectiveIsDrawnFromTheImagesOfItsPoints()
    {
        var transform = ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 10, 10), new PointD(3, 2), new PointD(27, 5), new PointD(22, 21), new PointD(6, 25));
        PointD[] points = [new(1, 1), new(9, 2), new(8.5, 9), new(4, 6.5), new(2, 8)];
        var projected = points.Select(point => transform.TryTransform(point, out var image) ? image : throw new InvalidOperationException()).ToArray();

        // A straight side stays straight: a polygon is the polygon of the images of its vertices, bit for bit, and so
        // is the stroke of a pen that does not scale. A pen that scales is the stroke of the plane, seen in perspective:
        // the pieces of the stroke, transformed.
        var miter = new Pen(White, 1.25) { LineJoin = LineJoin.Miter };
        var fixedPen = new Pen(White, 1.5) { ScalesWithTransform = false, LineJoin = LineJoin.Bevel, LineCap = LineCap.Square };
        var polygon = new VectorPathBuilder().AddPolygon(points).Build();
        AssertSame(canvas => canvas.FillPolygon(White, projected), canvas => canvas.Transform(transform, inner => inner.FillPolygon(White, points)));
        AssertSame(canvas => canvas.StrokePolygon(fixedPen, projected), canvas => canvas.Transform(transform, inner => inner.StrokePolygon(fixedPen, points)));
        AssertSame(canvas => canvas.Transform(transform, inner => inner.Fill(White, polygon.Widen(miter))), canvas => canvas.Transform(transform, inner => inner.Stroke(miter, polygon)));
        AssertSame(canvas => canvas.FillPolygon(White, projected), canvas => canvas.Transform(transform, inner => inner.Clip(polygon, clipped => clipped.Fill(White))));

        // An image on the rectangle is the image on the quadrilateral, up to the rounding of two ways of computing
        // the same transformation
        using var texture = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 5, 4, new Random(5).Next);
        AssertClose(
            canvas => canvas.DrawImage(texture.Frames[0], new PointD(3, 2), new PointD(27, 5), new PointD(22, 21), new PointD(6, 25), ImageSampling.Smooth),
            canvas => canvas.Transform(transform, inner => inner.DrawImage(texture.Frames[0], new RectangleD(0, 0, 10, 10), ImageSampling.Smooth)),
            1);
        AssertClose(
            canvas => canvas.DrawImage(texture.Frames[0], new PointD(3, 2), new PointD(27, 5), new PointD(22, 21), new PointD(6, 25)),
            canvas => canvas.DrawImage(texture.Frames[0], ((ProjectiveTransform)AffineTransform.CreateScale(2, 2.5)).Then(transform), ImageSampling.Bilinear),
            1);

        // The color of a brush is the one of the point of the plane the center of the pixel comes from
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(10, 0), [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(255, 255, 255))]);
        using var image = new Image<Rgba32>(32, 28, new Rgba32(10, 20, 30));
        image.Draw(canvas => canvas.Transform(transform, inner => inner.Fill(gradient)), Ct);
        Assert.True(transform.TryInvert(out var inverse));
        for (var y = 0; y < 28; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                Assert.True(inverse.TryTransform(new PointD(x + 0.5, y + 0.5), out var point));
                var level = 255 * Math.Clamp(point.X / 10, 0, 1);
                Assert.InRange(image.Frames[0][x, y].R, Math.Floor(level), Math.Ceiling(level));
            }
        }

        static void AssertSame(DrawingAction expected, DrawingAction actual) => AssertClose(expected, actual, 0);

        static void AssertClose(DrawingAction expected, DrawingAction actual, int tolerance)
        {
            using var first = new Image<Rgba32>(32, 28, new Rgba32(10, 20, 30, 200));
            using var second = new Image<Rgba32>(32, 28, new Rgba32(10, 20, 30, 200));
            first.Draw(expected, Ct);
            second.Draw(actual, Ct);
            var left = DrawingTestSupport.ReadSamples(first.Frames[0]);
            var right = DrawingTestSupport.ReadSamples(second.Frames[0]);
            Assert.NotEqual(Enumerable.Repeat<int[]>([10, 20, 30, 200], 32 * 28).SelectMany(pixel => pixel).ToArray(), left);
            for (var i = 0; i < left.Length; i++)
            {
                Assert.InRange(right[i], left[i] - tolerance, left[i] + tolerance, message: $"sample {i}");
            }
        }
    }

    [Fact]
    public void WhatIsBeyondTheHorizonOfThePlaneIsNotDrawn()
    {
        // W = 1 - x / 10, then a translation by 20: the plane goes to infinity at x = 10, whose image is the vertical
        // line x = 10 of the canvas, and x becomes (20 - x) / (1 - x / 10) on the canvas. What is on the other side of
        // the horizon (x > 10) is behind the viewer and has no image.
        var transform = new ProjectiveTransform(1, 0, -0.1, 0, 1, 0, 0, 0, 1).Then(AffineTransform.CreateTranslation(20, 0));
        var gray = new Rgba32(50, 50, 50);
        using var image = new Image<Rgba32>(32, 4, gray);

        // A rectangle that crosses the horizon is cut there, whatever the size of its image, and nothing is
        // rejected. Its left side, x = -100, is at 20 - 10 + 100 / 11 on the canvas: it covers 1 / 11 of the pixel 10.
        image.Draw(canvas => canvas.Transform(transform, inner => inner.FillRectangle(White, new RectangleD(-100, -1000, 200, 2000))), Ct);
        for (var x = 0; x < 32; x++)
        {
            var pixel = image.Frames[0][x, 1];
            if (x < 10)
            {
                Assert.Equal(gray, pixel);
            }
            else if (x == 10)
            {
                Assert.Equal(69, pixel.R);
            }
            else
            {
                Assert.Equal(new Rgba32(255, 255, 255), pixel);
            }
        }

        // A drawing that is entirely beyond the horizon draws nothing, and a brush has no color beyond it
        image.Draw(canvas => canvas.Clear(gray), Ct);
        image.Draw(canvas => canvas.Transform(transform, inner => inner.FillRectangle(White, new RectangleD(12, 0, 4, 4))), Ct);
        for (var x = 0; x < 32; x++)
        {
            Assert.Equal(gray, image.Frames[0][x, 1]);
        }

        var stops = new[] { new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 255)) };
        using var texture = new Image<Rgba32>(2, 2, new Rgba32(0, 255, 0));
        foreach (var brush in new Brush[] { new LinearGradientBrush(new PointD(0, 0), new PointD(1, 0), stops), new ImageBrush(texture.Frames[0]) { WrapMode = ImageWrapMode.Repeat } })
        {
            image.Draw(canvas => canvas.Clear(gray), Ct);
            image.Draw(canvas => canvas.Transform(transform, inner => inner.Fill(brush)), Ct);
            Assert.Equal(gray, image.Frames[0][9, 1]);
            Assert.NotEqual(gray, image.Frames[0][10, 1]);
            Assert.NotEqual(gray, image.Frames[0][31, 1]);
        }

        // Lines across the horizon, stroked with a pen that scales or not, and images: they go through the vanishing
        // point of the canvas, (10, 0)
        image.Draw(canvas => canvas.Clear(gray), Ct);
        image.Draw(
            canvas => canvas.Transform(transform, inner =>
            {
                inner.StrokeLine(new Pen(White, 1) { ScalesWithTransform = false }, new PointD(-100, 1.5), new PointD(100, 1.5));
                inner.StrokeLine(new Pen(White, 0.5), new PointD(-100, 3), new PointD(100, 3));
                inner.DrawImage(texture.Frames[0], new RectangleD(-100, 0, 200, 1));
                inner.DrawImage(texture.Frames[0], new ProjectiveTransform(1, 0, 0.5, 0, 1, 0, 0, 0, 1), ImageSampling.Bilinear);
            }),
            Ct);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                Assert.Equal(gray, image.Frames[0][x, y]);
            }
        }

        // At x = 0 of the plane, the canvas is the plane moved by 20: the image covers the rows from 0 to 1, the
        // line of the pen that does not scale is one pixel wide around 1.5 there
        Assert.Equal(new Rgba32(0, 255, 0), image.Frames[0][20, 0]);
        Assert.Equal(new Rgba32(0, 255, 0), image.Frames[0][25, 0]);
        Assert.NotEqual(gray, image.Frames[0][28, 2]);
    }

    [Fact]
    public void TransformationsOfSpaceInsideEachOtherKeepTheirDepth()
    {
        // A rotation around the vertical axis through the center of the canvas, inside a perspective seen from its
        // center: as the 3D transforms of two nested elements of CSS
        var toCenter = Transform3D.CreateTranslation(-16, -14, 0);
        var fromCenter = Transform3D.CreateTranslation(16, 14, 0);
        var perspective = toCenter.Then(Transform3D.CreatePerspective(40)).Then(fromCenter);
        var rotation = toCenter.Then(Transform3D.CreateRotationY(50)).Then(fromCenter);
        var square = new RectangleD(6, 4, 20, 20);
        var nested = Draw(canvas => canvas.Transform(perspective, outer => outer.Transform(rotation, inner => inner.FillRectangle(White, square))));

        // The two transformations of space are combined: the one of their product
        Assert.Equal(nested, Draw(canvas => canvas.Transform(rotation.Then(perspective), inner => inner.FillRectangle(White, square))));
        Assert.Equal(nested, Draw(canvas => canvas.Transform(rotation.Then(perspective).ToProjectiveTransform(), inner => inner.FillRectangle(White, square))));

        // Each flattened to the plane on its own, the rotation is a horizontal scaling that the perspective does not
        // see: the depth it gives to the right side is lost
        Assert.NotEqual(nested, Draw(canvas => canvas.Transform(perspective.ToProjectiveTransform(), outer => outer.Transform(rotation.ToProjectiveTransform(), inner => inner.FillRectangle(White, square)))));

        // The right side turns away from the viewer: it is smaller than the left one
        Assert.True(CountColumn(nested, 10) > CountColumn(nested, 21) + 2);

        static int CountColumn(int[] samples, int x)
        {
            var count = 0;
            for (var y = 0; y < 28; y++)
            {
                count += samples[(((y * 32) + x) * 4) + 3] == 255 ? 1 : 0;
            }

            return count;
        }

        static int[] Draw(DrawingAction action)
        {
            using var image = new Image<Rgba32>(32, 28);
            image.Draw(action, Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void ShadowsAndBlursInPerspectiveAreDrawnInThePlane()
    {
        // A transformation whose W changes by a billionth across the canvas: the shadow, the blur and the inner shadow
        // are drawn on an image of the plane, at the scale of the transformation, then that image in perspective. The
        // image is on the pixels of the canvas, so the result is the one of the affine transformation, up to rounding.
        var affine = new AffineTransform(2, 0, 0, 2, 4, 2);
        var projective = new ProjectiveTransform(2, 0, 1e-9, 0, 2, 0, 4, 2, 1);
        var black = new Rgba32(0, 0, 0, 160);
        var pattern = new PatternBrush(new RectangleD(0, 0, 2, 2), tile => tile.FillRectangle(new Rgba32(200, 30, 30), new RectangleD(0, 0, 1, 1)));
        DrawingAction drawing = canvas =>
        {
            canvas.Shadow(black, new PointD(1, 1.5), 1, shadowed => shadowed.FillRectangle(White, new RectangleD(2, 2, 5, 4)));
            canvas.InnerShadow(black, new PointD(0.5, 0.5), 0.75, shadowed => shadowed.FillRectangle(new Rgba32(30, 200, 30), new RectangleD(8, 1, 4, 6)));
            canvas.Blur(0.8, blurred => blurred.FillRectangle(new Rgba32(30, 30, 200), new RectangleD(2, 8, 4, 3)));
            canvas.FillRectangle(pattern, new RectangleD(8, 8, 4, 3));
        };

        using var expected = new Image<Rgba32>(32, 28, new Rgba32(240, 240, 240));
        using var actual = new Image<Rgba32>(32, 28, new Rgba32(240, 240, 240));
        expected.Draw(canvas => canvas.Transform(affine, drawing), Ct);
        actual.Draw(canvas => canvas.Transform(projective, drawing), Ct);
        var left = DrawingTestSupport.ReadSamples(expected.Frames[0]);
        var right = DrawingTestSupport.ReadSamples(actual.Frames[0]);
        for (var i = 0; i < left.Length; i++)
        {
            Assert.InRange(right[i], left[i] - 2, left[i] + 2, message: $"sample {i}");
        }

        // The canvas of the callback is the canvas, whatever it is drawn on
        actual.Draw(canvas => canvas.Transform(projective, inner => inner.Shadow(black, default, 1, shadowed => Assert.Equal((32, 28), (shadowed.Width, shadowed.Height)))), Ct);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Masks
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AMaskKeepsADrawingInProportionToItsAlphaOrToItsLuminance()
    {
        var red = new Rgba32(255, 0, 0);

        // By its alpha, whatever its colors: a blue at half its opacity keeps half of the red
        using var image = new Image<Rgba32>(4, 2);
        image.Draw(canvas => canvas.Mask(mask => mask.FillRectangle(new Rgba32(0, 0, 255, 128), new Rectangle(1, 0, 2, 2)), content => content.Fill(red)), Ct);
        Assert.Equal([default, new Rgba32(255, 0, 0, 128), new Rgba32(255, 0, 0, 128), default], Enumerable.Range(0, 4).Select(x => image.Frames[0][x, 1]));

        // By its luminance: the luma of each color times its alpha. White keeps, black removes; the gray 128 and a
        // white at half its opacity keep 128 of 255; red has the luma round(0.2126 * 255) = 54.
        Rgba32[] colors = [new(255, 255, 255), new(0, 0, 0), new(128, 128, 128), new(255, 255, 255, 128), default, new(255, 0, 0)];
        using var luminance = new Image<Rgba32>(6, 1);
        luminance.Draw(
            canvas => canvas.Mask(
                mask =>
                {
                    for (var x = 0; x < colors.Length; x++)
                    {
                        mask.FillRectangle(colors[x], new Rectangle(x, 0, 1, 1));
                    }
                },
                content => content.Fill(red),
                MaskMode.Luminance),
            Ct);
        Assert.Equal([255, 0, 128, 128, 0, 54], Enumerable.Range(0, 6).Select(x => (int)luminance.Frames[0][x, 0].A));
        Assert.Equal(new Rgba32(255, 0, 0, 54), luminance.Frames[0][5, 0]);
        Assert.Equal(default, luminance.Frames[0][1, 0]);

        // On a 16-bit canvas, the layers have 16-bit samples
        using var wide = new Image<Rgba64>(2, 1);
        wide.Draw(new Rgba64(65535, 0, 0), static (canvas, color) => canvas.Mask(
            color,
            static (mask, _) => mask.FillRectangle(new Rgba64(32768, 32768, 32768), new Rectangle(0, 0, 1, 1)),
            static (content, color) => content.Fill(color),
            MaskMode.Luminance), Ct);
        Assert.Equal(new Rgba64(65535, 0, 0, 32768), wide.Frames[0][0, 0]);
        Assert.Equal(default, wide.Frames[0][1, 0]);
    }

    [Fact]
    public void AMaskIsALayerKeptByAnotherLayer()
    {
        // A mask by its alpha is what two layers do: the content, and the mask drawn on it with DestinationIn
        var random = new Random(4321);
        var path = new VectorPathBuilder().AddCircle(new PointD(5.25, 4.5), 3.3).Build();
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(10, 0), [new GradientStop(0, new Rgba32(255, 255, 255)), new GradientStop(1, new Rgba32(0, 0, 0, 0))]);
        foreach (var format in PixelFormats.All)
        {
            using var expected = DrawingTestSupport.CreateRandom(format, 10, 8, random.Next);
            using var actual = expected.Clone();
            using var clipped = expected.Clone();
            void Content(DrawingCanvas canvas) => canvas.FillEllipse(new Rgba32(40, 200, 90, 220), new PointD(5, 4), 4.6, 3.1);
            void Shape(DrawingCanvas canvas)
            {
                canvas.Fill(gradient, path);
                canvas.StrokeLine(new Pen(new Rgba32(1, 2, 3, 99), 1.5), new PointD(0, 7), new PointD(10, 1));
            }

            expected.Draw(
                canvas => canvas.Layer(1, layer =>
                {
                    Content(layer);
                    layer.Layer(1, Shape, CompositeMode.DestinationIn);
                }),
                Ct);
            actual.Draw(canvas => canvas.Mask(Shape, Content), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));

            // A clip applies to the result, and a mask inside a mask to its content
            var before = DrawingTestSupport.ReadSamples(clipped.Frames[0]);
            clipped.Draw(canvas => canvas.Clip(new Rectangle(3, 2, 4, 4), inner => inner.Mask(Shape, content => content.Mask(Shape, Content, MaskMode.Luminance))), Ct);
            var after = DrawingTestSupport.ReadSamples(clipped.Frames[0]);
            var channels = after.Length / 80;
            for (var i = 0; i < after.Length; i++)
            {
                var (x, y) = (i / channels % 10, i / channels / 10);
                if (x is < 3 or >= 7 || y is < 2 or >= 6)
                {
                    Assert.Equal(before[i], after[i]);
                }
            }

            Assert.NotEqual(before, after);
        }
    }

    [Fact]
    public void AMaskValidatesItsArgumentsAndDrawsNothingWhenACallbackFails()
    {
        using var image = NewAnimation();
        var before = CopyAll(image);
        image.Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Mask(null!, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Mask(static _ => Assert.Fail("The callback ran."), null!));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Mask(1, null!, static (_, _) => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Mask(1, static (_, _) => Assert.Fail("The callback ran."), null!));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Mask(static _ => Assert.Fail("The callback ran."), static _ => Assert.Fail("The callback ran."), (MaskMode)2));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Mask(1, static (_, _) => Assert.Fail("The callback ran."), static (_, _) => Assert.Fail("The callback ran."), (MaskMode)(-1)));
                AssertThrows<InvalidOperationException>(canvas, static canvas => canvas.Mask(static _ => throw new InvalidOperationException("mask"), static content => content.Fill(White)));
                AssertThrows<InvalidOperationException>(canvas, static canvas => canvas.Mask(static mask => mask.Fill(White), static _ => throw new InvalidOperationException("content")));
            },
            Ct);
        Assert.Equal(before, CopyAll(image));

        // Nothing is left of the masks that failed: the next drawing is drawn on the frames
        image.Draw(static canvas => canvas.Mask(static mask => mask.FillRectangle(White, new Rectangle(0, 0, 1, 1)), static content => content.Fill(new Rgba32(9, 8, 7))), Ct);
        Assert.Equal(new Rgba32(9, 8, 7), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(9, 8, 7), image.Frames[1][0, 0]);
        Assert.Equal(new Rgba32(9, 8, 7), image.PosterFrame![0, 0]);
        Assert.Equal(before[0].AsSpan(4).ToArray(), DrawingTestSupport.ReadSamples(image.Frames[0]).AsSpan(4).ToArray());
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Blurs
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ABlurredDrawingIsSpreadByTheWeightsOfABell()
    {
        // One opaque pixel blurred with a deviation of 1: its alpha is spread by the products of the weights 0.004433,
        // 0.054006, 0.242036 and 0.399050 of its row and of its column. In its row, 255 times 0.399050 times each:
        // 0.45, 5.5, 24.6, 40.6; on the diagonal, the squares: 0.005, 0.74, 14.9, 40.6. The color is not spread thinner:
        // it is the blue of the pixel wherever something of it is.
        var blue = new Rgba32(0, 0, 255);
        using var image = new Image<Rgba32>(7, 7);
        image.Draw(canvas => canvas.Blur(1, layer => layer.FillRectangle(blue, new Rectangle(3, 3, 1, 1))), Ct);
        Assert.Equal([0, 5, 25, 41, 25, 5, 0], Enumerable.Range(0, 7).Select(x => (int)image.Frames[0][x, 3].A));
        Assert.Equal([0, 5, 25, 41, 25, 5, 0], Enumerable.Range(0, 7).Select(y => (int)image.Frames[0][3, y].A));
        Assert.Equal([0, 1, 15, 41, 15, 1, 0], Enumerable.Range(0, 7).Select(i => (int)image.Frames[0][i, i].A));
        Assert.Equal(new Rgba32(0, 0, 255, 25), image.Frames[0][2, 3]);
        Assert.Equal(default, image.Frames[0][0, 3]);

        // Without deviation, the drawing is drawn as it is; with the state overload too
        using var sharp = new Image<Rgba32>(7, 7);
        sharp.Draw(blue, static (canvas, color) => canvas.Blur(0, color, static (layer, color) => layer.FillRectangle(color, new Rectangle(3, 3, 1, 1))), Ct);
        Assert.Equal(blue, sharp.Frames[0][3, 3]);
        Assert.Single(DrawingTestSupport.ReadSamples(sharp.Frames[0]).Chunk(4), pixel => pixel[3] != 0);

        // A drawing that covers the canvas continues beyond its edges: nothing of it fades along them
        var random = new Random(77);
        foreach (var format in PixelFormats.All)
        {
            using var full = DrawingTestSupport.CreateRandom(format, 9, 5, random.Next);
            using var expected = full.Clone();
            var color = new Rgba64(40000, 12345, 999, 65535);
            full.Draw(canvas => canvas.Blur(2.5, layer => layer.Fill(color)), Ct);
            expected.Draw(canvas => canvas.Fill(color), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(full.Frames[0]));
        }

        // A clip applies to the result: what is drawn around it still spreads into it
        using var clipped = new Image<Rgba32>(7, 7);
        clipped.Draw(canvas => canvas.Clip(new Rectangle(0, 0, 3, 7), inner => inner.Blur(1, layer => layer.FillRectangle(blue, new Rectangle(3, 3, 1, 1)))), Ct);
        Assert.Equal([0, 5, 25, 0, 0, 0, 0], Enumerable.Range(0, 7).Select(x => (int)clipped.Frames[0][x, 3].A));

        // A transformation enlarges the blur with the drawing: twice the size, twice the deviation
        using var direct = new Image<Rgba32>(20, 20);
        direct.Draw(canvas => canvas.Blur(2, layer => layer.FillRectangle(blue, new Rectangle(8, 8, 4, 4))), Ct);
        using var scaled = new Image<Rgba32>(20, 20);
        scaled.Draw(canvas => canvas.Transform(AffineTransform.CreateScale(2, 2), inner => inner.Blur(1, layer => layer.FillRectangle(blue, new Rectangle(4, 4, 2, 2)))), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(direct.Frames[0]), DrawingTestSupport.ReadSamples(scaled.Frames[0]));
    }

    [Fact]
    public void TheBackdropIsBlurredWhereTheClipLetsADrawingThrough()
    {
        // On opaque pixels, the blur of the whole canvas is the Gaussian blur of the image, to one unit: both read the
        // pixels beyond the edges as the pixels on the edges
        var random = new Random(2468);
        foreach (var format in new[] { PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Gray16 })
        {
            using var image = DrawingTestSupport.CreateRandom(format, 16, 12, random.Next);
            using var reference = image.Clone();
            using var clipped = image.Clone();
            var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
            reference.GaussianBlur(1.5, cancellationToken: Ct);
            image.Draw(canvas => canvas.BlurBackdrop(1.5), Ct);
            var expected = DrawingTestSupport.ReadSamples(reference.Frames[0]);
            var actual = DrawingTestSupport.ReadSamples(image.Frames[0]);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.InRange(actual[i], expected[i] - 1, expected[i] + 1, message: $"{format}, sample {i}");
            }

            // Inside a clip, the pixels of the clip get the same blur, which reads the pixels around the clip, and the
            // others are untouched
            clipped.Draw(canvas => canvas.Clip(new Rectangle(4, 3, 7, 5), inner => inner.BlurBackdrop(1.5)), Ct);
            var partial = DrawingTestSupport.ReadSamples(clipped.Frames[0]);
            var channels = partial.Length / (16 * 12);
            for (var i = 0; i < partial.Length; i++)
            {
                var (x, y) = (i / channels % 16, i / channels / 16);
                Assert.Equal(x is >= 4 and < 11 && y is >= 3 and < 8 ? actual[i] : before[i], partial[i]);
            }
        }

        // The edge of a clip is antialiased: a pixel that is half inside is half way to its blur
        using var edge = new Image<Gray8>(8, 1);
        for (var x = 0; x < 8; x++)
        {
            edge.Frames[0][x, 0] = new Gray8((byte)(x < 4 ? 0 : 200));
        }

        using var whole = edge.Clone();
        whole.Draw(static canvas => canvas.BlurBackdrop(1), Ct);
        edge.Draw(static canvas => canvas.Clip(new RectangleD(0, 0, 4.5, 1), static inner => inner.BlurBackdrop(1)), Ct);
        Assert.Equal(whole.Frames[0][3, 0], edge.Frames[0][3, 0]);
        Assert.InRange(edge.Frames[0][4, 0].Value, ((200 + whole.Frames[0][4, 0].Value) / 2) - 1, ((200 + whole.Frames[0][4, 0].Value) / 2) + 1);
        Assert.Equal(new Gray8(200), edge.Frames[0][5, 0]);

        // Transparent pixels do not darken their neighbors, and every frame is blurred
        using var animation = new Image<Rgba32>(7, 7);
        animation.AppendFrame();
        animation.Frames[0][3, 3] = new Rgba32(255, 0, 0);
        animation.Frames[1][3, 3] = new Rgba32(0, 255, 0);
        animation.Draw(static canvas => canvas.BlurBackdrop(1), Ct);
        Assert.Equal([0, 5, 25, 41, 25, 5, 0], Enumerable.Range(0, 7).Select(x => (int)animation.Frames[0][x, 3].A));
        Assert.Equal(new Rgba32(255, 0, 0, 25), animation.Frames[0][2, 3]);
        Assert.Equal(new Rgba32(0, 255, 0, 25), animation.Frames[1][3, 2]);

        // In a layer, what is blurred is what the layer holds: blurring a layer as it ends is the same
        using var layered = new Image<Rgba32>(9, 9, new Rgba32(10, 20, 30));
        using var blurred = layered.Clone();
        layered.Draw(
            static canvas => canvas.Layer(1, static layer =>
            {
                layer.FillRectangle(new Rgba32(200, 100, 0), new Rectangle(3, 3, 3, 2));
                layer.BlurBackdrop(1.25);
            }),
            Ct);
        blurred.Draw(static canvas => canvas.Blur(1.25, static layer => layer.FillRectangle(new Rgba32(200, 100, 0), new Rectangle(3, 3, 3, 2))), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(blurred.Frames[0]), DrawingTestSupport.ReadSamples(layered.Frames[0]));

        // Uniform pixels stay what they are, and a blur without deviation changes nothing
        using var uniform = new Image<Rgba64>(6, 6, new Rgba64(1234, 40000, 65535, 30000));
        uniform.Draw(
            static canvas =>
            {
                canvas.BlurBackdrop(3);
                canvas.BlurBackdrop(0);
            },
            Ct);
        Assert.All(Enumerable.Range(0, 36), i => Assert.Equal(new Rgba64(1234, 40000, 65535, 30000), uniform.Frames[0][i % 6, i / 6]));
    }

    [Fact]
    public void ABlurValidatesItsArgumentsAndIsChargedToTheImage()
    {
        using var image = NewAnimation();
        var before = CopyAll(image);
        var live = image.Owner.Scope.LiveBytes;
        image.Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Blur(1, null!));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Blur(1, 1, null!));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.Blur(double.NaN, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Blur(-1, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Blur(10001, 1, static (_, _) => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.BlurBackdrop(double.PositiveInfinity));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.BlurBackdrop(-0.5));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.BlurBackdrop(10001));
                AssertThrows<InvalidOperationException>(canvas, static canvas => canvas.Blur(2, static layer =>
                {
                    layer.Fill(White);
                    throw new InvalidOperationException("layer");
                }));
            },
            Ct);
        Assert.Equal(before, CopyAll(image));
        Assert.Equal(live, image.Owner.Scope.LiveBytes);

        // A blur that would exceed the limit changes no pixel: room for the pixels and for the scratch of the frame
        // size, not for the rows a blur keeps
        using var probe = new Image<Rgba32>(64, 64, new Rgba32(1, 2, 3), ImageConfiguration.Default);
        long session = 0;
        probe.Draw(_ => session = probe.Owner.Scope.LiveBytes, Ct);
        using var limited = new Image<Rgba32>(64, 64, new Rgba32(1, 2, 3), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = session + 4096 } });
        limited.Frames[0][5, 5] = new Rgba32(255, 255, 255);
        var pixels = DrawingTestSupport.ReadSamples(limited.Frames[0]);
        Assert.Throws<ImageResourceLimitException>(() => limited.Draw(static canvas => canvas.BlurBackdrop(40), Ct));
        Assert.Throws<ImageResourceLimitException>(() => limited.Draw(static canvas => canvas.Blur(40, static layer => layer.Fill(White)), Ct));
        Assert.Equal(pixels, DrawingTestSupport.ReadSamples(limited.Frames[0]));
        Assert.Equal(0, limited.Owner.ActiveLeaseCount);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Inner shadows
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AnInnerShadowIsCastOnADrawingByWhatIsAroundIt()
    {
        var white = new Rgba32(255, 255, 255);
        var black = new Rgba32(0, 0, 0);

        // Without blur, the shadow is what is around the drawing, moved: two columns along the left side of the rectangle
        using var image = new Image<Rgba32>(10, 6);
        image.Draw(canvas => canvas.InnerShadow(black, new PointD(2, 0), 0, layer => layer.FillRectangle(white, new Rectangle(2, 1, 6, 4))), Ct);
        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                var expected = x is < 2 or >= 8 || y is < 1 or >= 5 ? default : x < 4 ? black : white;
                Assert.Equal(expected, image.Frames[0][x, y]);
            }
        }

        // With a blur of deviation 1, a pixel is shadowed by the weights of the columns of its row that are not in the
        // drawing: 0.3005 for the first column of a bar (255 * 0.6995 = 178), then 0.0584 (240) and 0.0044 (254). The
        // bar continues beyond the top and the bottom of the canvas: no shadow comes from there.
        using var bar = new Image<Rgba32>(14, 4);
        bar.Draw(canvas => canvas.InnerShadow(black, default, 1, 0, static (layer, _) => layer.FillRectangle(new Rgba32(255, 255, 255), new Rectangle(3, 0, 8, 4))), Ct);
        for (var y = 0; y < 4; y++)
        {
            Assert.Equal([0, 0, 0, 178, 240, 254, 255, 255, 254, 240, 178, 0, 0, 0], Enumerable.Range(0, 14).Select(x => (int)bar.Frames[0][x, y].R));
            Assert.Equal([0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0], Enumerable.Range(0, 14).Select(x => (int)bar.Frames[0][x, y].A));
        }

        // The alpha of the drawing is kept, and the color of the shadow is the one that is given, at its opacity: 128
        // of 255 where the shadow is whole. A translucent drawing is shadowed all over by what it lacks of opacity,
        // 155 of 255 here: the red is mixed at (155 / 255) (128 / 255) = 0.305 -> 77.8, and the blue is left at 177.2
        using var translucent = new Image<Rgba32>(10, 6);
        translucent.Draw(canvas => canvas.InnerShadow(new Rgba32(255, 0, 0, 128), new PointD(2, 0), 0, layer => layer.FillRectangle(new Rgba32(0, 0, 255, 100), new Rectangle(2, 1, 6, 4))), Ct);
        Assert.Equal(new Rgba32(128, 0, 127, 100), translucent.Frames[0][2, 2]);
        Assert.Equal(new Rgba32(78, 0, 177, 100), translucent.Frames[0][5, 2]);
        Assert.Equal(default, translucent.Frames[0][1, 2]);

        // A drawing that covers the canvas has nothing around it
        using var full = NewAnimation();
        full.Draw(canvas => canvas.InnerShadow(black, new PointD(1, 1), 2, layer => layer.Fill(white)), Ct);
        Assert.All(CopyAll(full), samples => Assert.All(samples, sample => Assert.Equal(255, sample)));

        // A shadow that fails draws nothing, and its arguments are those of a shadow
        using var failed = NewAnimation();
        var before = CopyAll(failed);
        failed.Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.InnerShadow(default, default, 1, null!));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.InnerShadow(default, default, 1, 1, null!));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.InnerShadow(default, new PointD(double.NaN, 0), 1, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.InnerShadow(default, default, -1, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.InnerShadow(default, default, 10001, 1, static (_, _) => Assert.Fail("The callback ran.")));
                AssertThrows<InvalidOperationException>(canvas, static canvas => canvas.InnerShadow(new Rgba32(0, 0, 0), new PointD(1, 0), 1, static layer =>
                {
                    layer.Fill(White);
                    throw new InvalidOperationException("layer");
                }));
            },
            Ct);
        Assert.Equal(before, CopyAll(failed));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Antialiasing and the space of compositing
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void WithoutAntialiasingAPixelIsPaintedEntirelyOrNotAtAll()
    {
        // A rectangle from 0.4 to 10.4: it covers 0.6 of the first pixel, which is painted, and 0.4 of the pixel 10,
        // which is not. From 0.5 to 10.5, it covers half of both: both are painted.
        using var image = new Image<Gray8>(12, 4, new Gray8(0));
        image.Draw(
            canvas =>
            {
                canvas.Antialiasing(enabled: false, hard =>
                {
                    hard.FillRectangle(White, new RectangleD(0.4, 0, 10, 1));
                    hard.FillRectangle(White, new RectangleD(0.5, 1, 10, 1));

                    // Antialiasing can be turned on again inside
                    hard.Antialiasing(enabled: true, 0.4, static (soft, left) => soft.FillRectangle(White, new RectangleD(left, 2, 10, 1)));
                });

                // And it is on again after the callback
                canvas.FillRectangle(White, new RectangleD(0.4, 3, 10, 1));
            },
            Ct);
        Assert.Equal([255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 0], Enumerable.Range(0, 12).Select(x => (int)image.Frames[0][x, 0].Value));
        Assert.Equal([255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0], Enumerable.Range(0, 12).Select(x => (int)image.Frames[0][x, 1].Value));
        Assert.Equal([153, 255, 255, 255, 255, 255, 255, 255, 255, 255, 102, 0], Enumerable.Range(0, 12).Select(x => (int)image.Frames[0][x, 2].Value));
        Assert.Equal([153, 255, 255, 255, 255, 255, 255, 255, 255, 255, 102, 0], Enumerable.Range(0, 12).Select(x => (int)image.Frames[0][x, 3].Value));

        // Any shape, stroke or clip: every pixel is the paint or what it was
        using var shapes = new Image<Rgb24>(40, 40, new Rgb24(10, 20, 30));
        var paint = new Rgba32(200, 100, 50);
        shapes.Draw(
            canvas => canvas.Antialiasing(enabled: false, hard =>
            {
                hard.FillEllipse(paint, new PointD(24.3, 17.6), 11.4, 6.7);
                hard.StrokeLine(new Pen(paint, 2.5) { LineCap = LineCap.Round }, new PointD(2.2, 36.1), new PointD(37.4, 33.3));
                hard.Clip(new VectorPathBuilder().AddCircle(new PointD(6.4, 6.2), 4.7).Build(), clipped => clipped.Fill(paint));
            }),
            Ct);
        var samples = DrawingTestSupport.ReadSamples(shapes.Frames[0]).Chunk(3).ToArray();
        Assert.All(samples, pixel => Assert.True(pixel is [200, 100, 50] or [10, 20, 30], string.Join(' ', pixel)));
        Assert.Contains(samples, pixel => pixel is [200, 100, 50]);

        // A disk has the pixels of its area, to a part of those its edge goes through
        using var disk = new Image<Gray8>(40, 40, new Gray8(0));
        disk.Draw(static canvas => canvas.Antialiasing(enabled: false, static hard => hard.FillCircle(White, new PointD(20.3, 19.6), 12.4)), Ct);
        var painted = DrawingTestSupport.ReadSamples(disk.Frames[0]);
        Assert.All(painted, sample => Assert.True(sample is 0 or 255));
        Assert.InRange(painted.Count(sample => sample == 255), (Math.PI * 12.4 * 12.4) - 12, (Math.PI * 12.4 * 12.4) + 12);

        // What is not an edge is unchanged: the opacity of a paint, and a clip that started with antialiasing
        using var opacity = new Image<Gray8>(3, 1, new Gray8(0));
        opacity.Draw(
            canvas => canvas.Clip(new RectangleD(0, 0, 1.6, 1), clipped => clipped.Antialiasing(enabled: false, hard =>
            {
                hard.Fill(new Rgba32(255, 255, 255, 128));

                // Thinner than half a pixel: no pixel is covered by half
                hard.StrokeLine(new Pen(White, 0.4), new PointD(0, 0.5), new PointD(3, 0.5));
            })),
            Ct);
        Assert.Equal([128, 77, 0], Enumerable.Range(0, 3).Select(x => (int)opacity.Frames[0][x, 0].Value));

        // A callback that fails leaves antialiasing as it was
        using var restored = new Image<Gray8>(2, 1, new Gray8(0));
        restored.Draw(
            canvas =>
            {
                AssertThrows<InvalidOperationException>(canvas, static canvas => canvas.Antialiasing(enabled: false, static _ => throw new InvalidOperationException("callback")));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Antialiasing(enabled: false, null!));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Antialiasing(enabled: false, 1, null!));
                canvas.FillRectangle(White, new RectangleD(0, 0, 1.2, 1));
            },
            Ct);
        Assert.Equal(new Gray8(51), restored.Frames[0][1, 0]);
    }

    [Fact]
    public void InLinearLightPaintsAreMixedAsLightIs()
    {
        // Half of a pixel in white over black. On the stored values: 127.5 -> 128. In linear light, half of the light
        // of white: 1.055 * 0.5^(1 / 2.4) - 0.055 = 0.7354 -> 187.5 -> 188
        using var image = new Image<Rgb24>(3, 1, new Rgb24(0, 0, 0));
        image.Draw(
            canvas =>
            {
                canvas.FillRectangle(White, new RectangleD(0, 0, 0.5, 1));
                canvas.Compositing(CompositingSpace.LinearSrgb, linear =>
                {
                    linear.FillRectangle(White, new RectangleD(1, 0, 0.5, 1));

                    // The stored values again, inside, and with the state overload
                    linear.Compositing(CompositingSpace.Encoded, 2, static (encoded, left) => encoded.FillRectangle(White, new RectangleD(left, 0, 0.5, 1)));
                });
            },
            Ct);
        Assert.Equal([new Rgb24(128, 128, 128), new Rgb24(188, 188, 188), new Rgb24(128, 128, 128)], Enumerable.Range(0, 3).Select(x => image.Frames[0][x, 0]));

        // A red at half its opacity over green: (128, 127, 0) on the stored values, and in linear light the light of
        // each, 128 / 255 of the red and 127 / 255 of the green: (188, 187, 0)
        using var colors = new Image<Rgba32>(2, 1, new Rgba32(0, 255, 0));
        colors.Draw(
            canvas =>
            {
                canvas.FillRectangle(new Rgba32(255, 0, 0, 128), new Rectangle(0, 0, 1, 1));
                canvas.Compositing(CompositingSpace.LinearSrgb, linear => linear.FillRectangle(new Rgba32(255, 0, 0, 128), new Rectangle(1, 0, 1, 1)));
            },
            Ct);
        Assert.Equal(new Rgba32(128, 127, 0), colors.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(188, 187, 0), colors.Frames[0][1, 0]);

        // With 16-bit samples: 0.735357 * 65535 = 48191.6
        using var wide = new Image<Gray16>(1, 1, new Gray16(0));
        wide.Draw(canvas => canvas.Compositing(CompositingSpace.LinearSrgb, linear => linear.FillRectangle(White, new RectangleD(0, 0, 0.5, 1))), Ct);
        Assert.InRange(wide.Frames[0][0, 0].Value, 48191, 48193);
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void InLinearLightTheExactCasesStayExact(PixelFormat format)
    {
        var random = new Random(1357 + (int)format);
        using var image = DrawingTestSupport.CreateRandom(format, 24, 16, random.Next);
        var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
        var layout = Meziantou.Framework.Imaging.TestHarness.Drawing.ReferencePixelLayout.FromPixelFormat(format);

        // A paint that leaves every pixel as it is writes the samples it read: decoding and encoding a sample gives it back
        image.Draw(
            canvas => canvas.Compositing(CompositingSpace.LinearSrgb, linear =>
            {
                linear.Fill(new SolidBrush(new Rgba32(1, 2, 3)) { CompositeMode = CompositeMode.DestinationIn });
                linear.Fill(new SolidBrush(new Rgba32(200, 100, 50, 0)));
                linear.Fill(new SolidBrush(new Rgba32(200, 100, 50, 0)) { BlendMode = BlendMode.Multiply });
                linear.FillRectangle(new Rgba32(200, 100, 50), new RectangleD(30, 30, 5, 5));
            }),
            Ct);
        Assert.Equal(before, DrawingTestSupport.ReadSamples(image.Frames[0]));

        // An opaque paint that covers pixels entirely writes its color, exactly; the pixels around are untouched
        using var expected = image.Clone();
        var color = new Rgba64(51400, 25700, 12850, 65535);
        expected.Draw(canvas => canvas.FillRectangle(color, new Rectangle(3, 2, 9, 7)), Ct);
        image.Draw(canvas => canvas.Compositing(CompositingSpace.LinearSrgb, linear => linear.FillRectangle(color, new Rectangle(3, 2, 9, 7))), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(image.Frames[0]));

        // Any paint, operator and blend mode stays within the samples of the format, and transparent pixels stay
        // transparent black or untouched
        image.Draw(
            canvas => canvas.Compositing(CompositingSpace.LinearSrgb, linear =>
            {
                foreach (var mode in Enum.GetValues<CompositeMode>())
                {
                    foreach (var blend in new[] { BlendMode.Normal, BlendMode.Multiply, BlendMode.SoftLight, BlendMode.Luminosity })
                    {
                        var brush = new SolidBrush(new Rgba64((ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536), (ushort)random.Next(65536))) { CompositeMode = mode, BlendMode = blend };
                        linear.FillEllipse(brush, new PointD(random.Next(24), random.Next(16)), 1 + (random.NextDouble() * 9), 1 + (random.NextDouble() * 7));
                    }
                }
            }),
            Ct);
        var after = DrawingTestSupport.ReadSamples(image.Frames[0]);
        Assert.All(after, sample => Assert.InRange(sample, 0, layout.MaxValue));
        if (layout.HasAlpha)
        {
            for (var i = 0; i < after.Length; i += layout.ChannelCount)
            {
                if (after[i + layout.ColorChannels] == 0 && before[i + layout.ColorChannels] != 0)
                {
                    Assert.Equal(new int[layout.ChannelCount], after.AsSpan(i, layout.ChannelCount).ToArray());
                }
            }
        }
    }

    [Fact]
    public void EverySampleIsItselfThroughLinearLight()
    {
        // Every 16-bit gray level, and every 8-bit one, rewritten by a paint that keeps the pixel: none changes
        using var wide = new Image<Gray16>(256, 256);
        using var narrow = new Image<Gray8>(256, 1);
        for (var i = 0; i < 65536; i++)
        {
            wide.Frames[0][i % 256, i / 256] = new Gray16((ushort)i);
        }

        for (var i = 0; i < 256; i++)
        {
            narrow.Frames[0][i, 0] = new Gray8((byte)i);
        }

        var keep = new SolidBrush(new Rgba32(9, 9, 9)) { CompositeMode = CompositeMode.DestinationIn };
        wide.Draw(canvas => canvas.Compositing(CompositingSpace.LinearSrgb, linear => linear.Fill(keep)), Ct);
        narrow.Draw(canvas => canvas.Compositing(CompositingSpace.LinearSrgb, linear => linear.Fill(keep)), Ct);
        Assert.Equal(Enumerable.Range(0, 65536), DrawingTestSupport.ReadSamples(wide.Frames[0]));
        Assert.Equal(Enumerable.Range(0, 256), DrawingTestSupport.ReadSamples(narrow.Frames[0]));
    }

    [Fact]
    public void LinearLightIsTheLightOfTheTransferFunctionOfTheImage()
    {
        // A profile that only has lookup tables has no function of one channel: no light to combine
        using var image = new Image<Rgb24>(1, 1, new Rgb24(0, 0, 0));
        image.Metadata.IccProfile = IccTestProfiles.RgbLabLut8();
        image.Draw(
            canvas =>
            {
                AssertThrows<UnsupportedImageFeatureException>(canvas, static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<UnsupportedImageFeatureException>(canvas, static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, 1, static (_, _) => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.Compositing((CompositingSpace)2, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, null!));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Compositing(CompositingSpace.Encoded, 1, null!));

                // The stored values never need the profile
                canvas.Compositing(CompositingSpace.Encoded, static encoded => encoded.FillRectangle(White, new RectangleD(0, 0, 0.5, 1)));
            },
            Ct);
        Assert.Equal(new Rgb24(128, 128, 128), image.Frames[0][0, 0]);

        // Samples that are linear light already are combined as they are
        using var linear = new Image<Rgb24>(1, 1, new Rgb24(0, 0, 0));
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        linear.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.FillRectangle(White, new RectangleD(0, 0, 0.5, 1))), Ct);
        Assert.Equal(new Rgb24(128, 128, 128), linear.Frames[0][0, 0]);

        // Without ICC profile, the transfer characteristics of a CICP description give the function, as for a resize:
        // a gamma of 2.2 encodes half of the light of white as 0.5^(1 / 2.2) = 0.7297 -> 186.08, linear
        // characteristics are light already, and reserved ones have no function
        foreach (var (transfer, expected) in new[] { ((byte)4, 186), ((byte)13, 188), ((byte)2, 188), ((byte)8, 128) })
        {
            using var described = new Image<Rgb24>(1, 1, new Rgb24(0, 0, 0));
            described.Metadata.Cicp = new CicpColorDescription(9, transfer, 0, fullRange: true);
            described.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.FillRectangle(White, new RectangleD(0, 0, 0.5, 1))), Ct);
            Assert.Equal(new Rgb24((byte)expected, (byte)expected, (byte)expected), described.Frames[0][0, 0]);
        }

        using var reserved = new Image<Rgb24>(1, 1, new Rgb24(0, 0, 0));
        reserved.Metadata.Cicp = new CicpColorDescription(1, 3, 0, fullRange: true);
        reserved.Draw(static canvas => AssertThrows<UnsupportedImageFeatureException>(canvas, static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static _ => Assert.Fail("The callback ran."))), Ct);
        Assert.Equal(new Rgb24(0, 0, 0), reserved.Frames[0][0, 0]);

        // A layer and a shadow are combined in linear light with what is under them. A layer at half its opacity:
        // white over black is 188, as a paint at half its opacity is
        using var layered = new Image<Rgb24>(2, 1, new Rgb24(0, 0, 0));
        layered.Draw(
            static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner =>
            {
                inner.Layer(32768 / 65536.0, static layer => layer.FillRectangle(White, new Rectangle(0, 0, 1, 1)));
                inner.Shadow(new Rgba32(255, 255, 255, 128), new PointD(1, 0), 0, static layer => layer.FillRectangle(new Rgba32(0, 0, 0), new Rectangle(0, 0, 1, 1)));
            }),
            Ct);
        Assert.Equal(new Rgb24(0, 0, 0), layered.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(188, 188, 188), layered.Frames[0][1, 0]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Color filters
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AColorMatrixGivesEachSampleASumOfTheSamplesOfItsPixel()
    {
        // R' = 0.5 R + 0.25 G + 0.1 A, G' = B, B' = 1 - R, A' = 0.5 A + 0.2, on 8-bit samples:
        //   (200, 100, 40, 255): 100 + 25 + 25.5 = 150.5 -> 151 (halves upward); 40; 255 - 200 = 55; 127.5 + 51 = 178.5 -> 179
        //   (10, 255, 0, 0): 5 + 63.75 = 68.75 -> 69; 0; 245; 51
        var matrix = new ColorMatrix(
        [
            0.5, 0.25, 0, 0.1, 0,
            0, 0, 1, 0, 0,
            -1, 0, 0, 0, 1,
            0, 0, 0, 0.5, 0.2,
        ]);
        Assert.Equal(0.25, matrix[0, 1]);
        Assert.Equal(0.2, matrix[3, 4]);
        Assert.False(matrix.IsIdentity);
        using (var image = new Image<Rgba32>(2, 1))
        {
            image.Frames[0][0, 0] = new Rgba32(200, 100, 40, 255);
            image.Frames[0][1, 0] = new Rgba32(10, 255, 0, 0);
            image.ApplyColorMatrix(matrix, Ct);
            Assert.Equal(new Rgba32(151, 40, 55, 179), image.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(69, 0, 245, 51), image.Frames[0][1, 0]);
        }

        // The same pixel in the other formats: blue first, 16-bit samples (25700 + 6425 + 6553.5 = 38678.5 -> 38679,
        // 10280, 65535 - 51400 = 14135, 32767.5 + 13107 = 45874.5 -> 45875), and without alpha (an alpha of 255, and the result over black:
        // 151 * 179 / 255 = 106, 40 * 179 / 255 = 28.08, 55 * 179 / 255 = 38.6)
        using (var bgra = new Image<Bgra32>(1, 1, new Bgra32(200, 100, 40, 255)))
        using (var wide = new Image<Rgba64>(1, 1, new Rgba64(51400, 25700, 10280, 65535)))
        using (var opaque = new Image<Rgb24>(1, 1, new Rgb24(200, 100, 40)))
        {
            bgra.ApplyColorMatrix(matrix, Ct);
            wide.ApplyColorMatrix(matrix, Ct);
            opaque.Frames[0].ApplyColorMatrix(matrix, Ct);
            Assert.Equal(new Bgra32(151, 40, 55, 179), bgra.Frames[0][0, 0]);
            Assert.Equal(new Rgba64(38679, 10280, 14135, 45875), wide.Frames[0][0, 0]);
            Assert.Equal(new Rgb24(106, 28, 39), opaque.Frames[0][0, 0]);
        }

        // A gray level is red, green and blue, with an alpha of 255, and gets the luma of the result over black:
        // (100, 100, 100, 255) gives (101, 100, 155, 179), whose luma is (2126 * 101 + 7152 * 100 + 722 * 155 + 5000) / 10000 = 104,
        // and 104 * 179 / 255 = 73
        using (var gray = new Image<Gray8>(1, 1, new Gray8(100)))
        using (var gray16 = new Image<Gray16>(1, 1, new Gray16(25700)))
        {
            gray.ApplyColorMatrix(matrix, Ct);
            gray16.ApplyColorMatrix(matrix, Ct);
            Assert.Equal(new Gray8(73), gray.Frames[0][0, 0]);
            Assert.InRange(gray16.Frames[0][0, 0].Value, (73 * 257) - 200, (73 * 257) + 200);
        }

        // The samples are kept between 0 and the largest one, and every pixel gets its samples: the color of a
        // pixel that is, or becomes, fully transparent too
        using (var image = new Image<Rgba32>(3, 1))
        {
            image.Frames[0][0, 0] = new Rgba32(200, 100, 40, 255);
            image.Frames[0][1, 0] = new Rgba32(100, 100, 40, 0);
            image.Frames[0][2, 0] = new Rgba32(3, 100, 250, 128);
            image.ApplyColorMatrix(new ColorMatrix([2, 0, 0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0.5, 0, 0, 0, 0, 0]), Ct);
            Assert.Equal(new Rgba32(255, 0, 168, 0), image.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(200, 0, 168, 0), image.Frames[0][1, 0]);
            Assert.Equal(new Rgba32(6, 0, 255, 0), image.Frames[0][2, 0]);
            image.ApplyColorMatrix(new ColorMatrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 3, 0.25]), Ct);
            Assert.Equal(new Rgba32(6, 0, 255, 64), image.Frames[0][2, 0]);
            image.ApplyColorMatrix(new ColorMatrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 30, 0]), Ct);
            Assert.Equal(new Rgba32(6, 0, 255, 255), image.Frames[0][2, 0]);
        }

        // The identity changes nothing, in any format, bit for bit
        var random = new Random(11);
        foreach (var format in PixelFormats.All)
        {
            using var image = DrawingTestSupport.CreateRandom(format, 9, 5, random.Next);
            var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
            image.ApplyColorMatrix(ColorMatrix.Identity, Ct);
            image.ApplyColorMatrix(new ColorMatrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0]), Ct);
            image.Draw(static canvas => canvas.Filter(ColorMatrix.Identity, static layer => layer.FilterBackdrop(ColorMatrix.Identity)), Ct);
            Assert.Equal(before, DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        Assert.True(ColorMatrix.Identity.IsIdentity);
        Assert.True(new ColorMatrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0]).IsIdentity);
        Assert.Throws<ArgumentException>(() => new ColorMatrix(new double[19]));
        Assert.Throws<ArgumentException>(() => new ColorMatrix(new double[21]));
        Assert.Throws<ArgumentException>(() => new ColorMatrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, double.NaN, 0]));
        Assert.Throws<ArgumentException>(() => new ColorMatrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, double.PositiveInfinity, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[4, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[-1, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[0, 5]);
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[0, -1]);
    }

    [Fact]
    public void TheReadyMadeColorMatricesAreTheFilterFunctionsOfCss()
    {
        // grayscale(): the luma of the library, with its rounding. The other functions keep the gray levels gray.
        var random = new Random(5);
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64 })
        {
            using var expected = DrawingTestSupport.CreateRandom(format, 64, 64, random.Next);
            using var actual = expected.Clone();
            expected.Grayscale(Ct);
            actual.ApplyColorMatrix(ColorMatrix.CreateGrayscale(), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));
        }

        // Every sum of the luma coefficients that is half way between two samples goes up, as the integers do
        using (var ties = new Image<Rgb24>(256, 256))
        using (var reference = new Image<Rgb24>(256, 256))
        {
            for (var y = 0; y < 256; y++)
            {
                for (var x = 0; x < 256; x++)
                {
                    ties.Frames[0][x, y] = reference.Frames[0][x, y] = new Rgb24((byte)x, (byte)y, (byte)((x * 7) + (y * 13)));
                }
            }

            reference.Grayscale(Ct);
            ties.ApplyColorMatrix(ColorMatrix.CreateGrayscale(), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(reference.Frames[0]), DrawingTestSupport.ReadSamples(ties.Frames[0]));
        }

        // The luma of (200, 100, 40) is 42.52 + 71.52 + 2.888 = 116.93, and half way to it is 158.46, 108.46, 78.46
        var pixel = new Rgba32(200, 100, 40, 128);
        Assert.Equal(new Rgba32(117, 117, 117, 128), Apply(ColorMatrix.CreateGrayscale(), pixel));
        Assert.Equal(new Rgba32(158, 108, 78, 128), Apply(ColorMatrix.CreateGrayscale(0.5), pixel));
        Assert.Equal(pixel, Apply(ColorMatrix.CreateGrayscale(0), pixel));

        // brightness(1.5): 300 is kept at 255, 150, 60. contrast(2): 2 c - 127.5 = 272.5, 72.5 -> 73, -47.5 -> 0
        Assert.Equal(new Rgba32(255, 150, 60, 128), Apply(ColorMatrix.CreateBrightness(1.5), pixel));
        Assert.Equal(new Rgba32(0, 0, 0, 128), Apply(ColorMatrix.CreateBrightness(0), pixel));
        Assert.Equal(new Rgba32(255, 73, 0, 128), Apply(ColorMatrix.CreateContrast(2), pixel));
        Assert.Equal(new Rgba32(128, 128, 128, 128), Apply(ColorMatrix.CreateContrast(0), pixel));

        // invert(): 255 - c; invert(0.25): 0.5 c + 63.75 = 163.75, 113.75, 83.75
        Assert.Equal(new Rgba32(55, 155, 215, 128), Apply(ColorMatrix.CreateInvert(), pixel));
        Assert.Equal(new Rgba32(164, 114, 84, 128), Apply(ColorMatrix.CreateInvert(0.25), pixel));
        Assert.Equal(new Rgba32(128, 128, 128, 128), Apply(ColorMatrix.CreateInvert(0.5), pixel));

        // opacity(0.5): 64; a tint keeps the alpha, multiplied by the one of its color: 128 * 128 / 255 = 64.25
        Assert.Equal(new Rgba32(200, 100, 40, 64), Apply(ColorMatrix.CreateOpacity(0.5), pixel));
        Assert.Equal(new Rgba32(1, 2, 3, 128), Apply(ColorMatrix.CreateTint(new Rgba32(1, 2, 3)), pixel));
        Assert.Equal(new Rgba32(1, 2, 3, 64), Apply(ColorMatrix.CreateTint(new Rgba32(1, 2, 3, 128)), pixel));

        // saturate(0) is the gray level of the coefficients of feColorMatrix: 0.213 * 200 + 0.715 * 100 + 0.072 * 40 = 116.98;
        // saturate(2) moves each color twice as far from it: 2 * 200 - 116.98 = 283, 83.02, -36.98
        Assert.Equal(new Rgba32(117, 117, 117, 128), Apply(ColorMatrix.CreateSaturation(0), pixel));
        Assert.Equal(new Rgba32(255, 83, 0, 128), Apply(ColorMatrix.CreateSaturation(2), pixel));
        Assert.True(ColorMatrix.CreateSaturation(1).IsIdentity);

        // sepia(): 0.393 * 200 + 0.769 * 100 + 0.189 * 40 = 163.06, 0.349 * 200 + 0.686 * 100 + 0.168 * 40 = 145.12,
        // 0.272 * 200 + 0.534 * 100 + 0.131 * 40 = 113.04
        Assert.Equal(new Rgba32(163, 145, 113, 128), Apply(ColorMatrix.CreateSepia(), pixel));
        Assert.Equal(pixel, Apply(ColorMatrix.CreateSepia(0), pixel));

        // hue-rotate(): a turn changes nothing, half a turn is 2 L - c for a luminance L that a gray level keeps, and
        // a third of a turn nearly sends red to green and green to blue (the matrix of the specification is not a
        // rotation of the colors, but of their hues around the gray axis)
        Assert.True(ColorMatrix.CreateHueRotation(0).IsIdentity);
        Assert.True(ColorMatrix.CreateHueRotation(360).IsIdentity);
        Assert.True(ColorMatrix.CreateHueRotation(-720).IsIdentity);
        Assert.Equal(new Rgba32(34, 134, 194, 128), Apply(ColorMatrix.CreateHueRotation(180), pixel));
        Assert.Equal(new Rgba32(90, 90, 90, 255), Apply(ColorMatrix.CreateHueRotation(77), new Rgba32(90, 90, 90)));
        var turned = Apply(ColorMatrix.CreateHueRotation(120), new Rgba32(255, 0, 0));
        Assert.True(turned.G > turned.R && turned.G > turned.B);
        Assert.Equal(Apply(ColorMatrix.CreateHueRotation(90), pixel), Apply(ColorMatrix.CreateHueRotation(450), pixel));

        // Two inks: black is the dark one, white the light one, and a gray is that far between them
        var duotone = ColorMatrix.CreateDuotone(new Rgba32(20, 40, 100), new Rgba32(250, 240, 200, 7));
        Assert.Equal(new Rgba32(20, 40, 100, 255), Apply(duotone, new Rgba32(0, 0, 0)));
        Assert.Equal(new Rgba32(250, 240, 200, 255), Apply(duotone, new Rgba32(255, 255, 255)));
        Assert.Equal(new Rgba32(135, 140, 150, 77), Apply(duotone, new Rgba32(128, 128, 128, 77)), new SampleComparer(1));
        Assert.Equal(Apply(duotone, new Rgba32(0, 255, 0)).R, (byte)Math.Round(20 + (230 * 0.7152), MidpointRounding.AwayFromZero));

        // One matrix made of two is both of them, without the rounding and the clamping in between
        var both = ColorMatrix.CreateBrightness(2).Then(ColorMatrix.CreateInvert());
        Assert.Equal(new Rgba32(0, 55, 175, 128), Apply(both, pixel));
        Assert.Equal(Apply(ColorMatrix.CreateInvert(), Apply(ColorMatrix.CreateBrightness(2), pixel)), Apply(both, pixel));
        Assert.Equal(new Rgba32(110, 255, 255, 128), Apply(ColorMatrix.CreateInvert().Then(ColorMatrix.CreateBrightness(2)), pixel));
        Assert.Same(both, both.Then(ColorMatrix.Identity));
        Assert.Same(both, ColorMatrix.Identity.Then(both));

        // Brighter then darker comes back where clamping in between would not
        Assert.Equal(pixel, Apply(ColorMatrix.CreateBrightness(4).Then(ColorMatrix.CreateBrightness(0.25)), pixel));
        Assert.Equal(new Rgba32(64, 64, 40, 128), Apply(ColorMatrix.CreateBrightness(0.25), Apply(ColorMatrix.CreateBrightness(4), pixel)));

        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMatrix.CreateBrightness(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMatrix.CreateBrightness(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMatrix.CreateContrast(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMatrix.CreateSaturation(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMatrix.CreateGrayscale(1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMatrix.CreateSepia(-0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMatrix.CreateInvert(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColorMatrix.CreateOpacity(1.01));
        Assert.Throws<ArgumentException>(() => ColorMatrix.CreateHueRotation(double.NaN));
        Assert.Throws<ArgumentNullException>(() => both.Then(null!));

        static Rgba32 Apply(ColorMatrix matrix, Rgba32 color)
        {
            using var image = new Image<Rgba32>(1, 1, color);
            image.ApplyColorMatrix(matrix, Ct);
            return image.Frames[0][0, 0];
        }
    }

    [Fact]
    public void AColorFilterAppliesToADrawingAsAWholeOrToWhatIsAlreadyDrawn()
    {
        // A layer whose pixels get a color: the drawing of the callback, with its antialiasing and its alpha, in
        // another color. The tint is the one of a solid paint of that color wherever the drawing is opaque.
        var circle = new VectorPathBuilder().AddCircle(new PointD(8, 8), 6).Build();
        var tint = ColorMatrix.CreateTint(new Rgba32(30, 90, 200));
        using (var tinted = new Image<Rgba32>(16, 16, new Rgba32(250, 250, 250)))
        using (var painted = new Image<Rgba32>(16, 16, new Rgba32(250, 250, 250)))
        {
            tinted.Draw(canvas => canvas.Filter(tint, layer => layer.Fill(new Rgba32(0, 0, 0), circle)), Ct);
            painted.Draw(canvas => canvas.Layer(1, layer => layer.Fill(new Rgba32(30, 90, 200), circle)), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(painted.Frames[0]), DrawingTestSupport.ReadSamples(tinted.Frames[0]));
            Assert.Equal(new Rgba32(30, 90, 200), tinted.Frames[0][8, 8]);
            Assert.Equal(new Rgba32(250, 250, 250), tinted.Frames[0][0, 0]);
        }

        // The matrix applies to the pixels of the layer, drawn or not: one that gives an alpha to transparent pixels
        // paints all of them, inside the clip
        using (var image = new Image<Rgb24>(4, 2, new Rgb24(0, 0, 0)))
        {
            var opaque = new ColorMatrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0.5, 0, 0, 0, 0, 1]);
            image.Draw(canvas => canvas.Clip(new Rectangle(1, 0, 2, 2), clipped => clipped.Filter(opaque, layer => layer.FillRectangle(new Rgba32(255, 0, 0), new Rectangle(0, 0, 2, 1)))), Ct);
            Assert.Equal([0, 0, 0, 255, 0, 128, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 128, 0, 0, 128, 0, 0, 0], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // What is already drawn: every pixel without a clip, the inside of the clip with one, and a pixel on its edge
        // in proportion to what the clip covers of it
        using (var image = new Image<Gray8>(4, 1, new Gray8(200)))
        {
            image.Draw(
                canvas =>
                {
                    canvas.Clip(new RectangleD(1, 0, 1.5, 1), clipped => clipped.FilterBackdrop(ColorMatrix.CreateInvert()));
                },
                Ct);

            // 255 - 200 = 55 where the clip is, and half way from 200 to 55 where it covers half of the pixel
            Assert.Equal([200, 55, 128, 200], DrawingTestSupport.ReadSamples(image.Frames[0]));
            image.Draw(canvas => canvas.FilterBackdrop(ColorMatrix.CreateBrightness(0.5)), Ct);
            Assert.Equal([100, 28, 64, 100], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // Inside a layer, the pixels of the layer; with clips and layers of any depth, on every frame and the poster
        using (var image = NewAnimation())
        {
            var before = image.Frames[1][2, 1];
            var poster = image.PosterFrame![2, 1];
            image.Draw(
                canvas =>
                {
                    canvas.Layer(1, layer =>
                    {
                        layer.FillRectangle(new Rgba32(10, 20, 30), new Rectangle(0, 0, 4, 3));
                        layer.Clip(new Rectangle(0, 0, 2, 3), left => left.FilterBackdrop(ColorMatrix.CreateInvert()));
                    });
                    canvas.Clip(new Rectangle(2, 1, 1, 1), one => one.FilterBackdrop(ColorMatrix.CreateOpacity(0)));
                },
                Ct);
            Assert.Equal(new Rgba32(245, 235, 225), image.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(245, 235, 225), image.Frames[1][1, 2]);
            Assert.Equal(new Rgba32(10, 20, 30), image.Frames[0][3, 0]);
            Assert.Equal(new Rgba32(10, 20, 30, 0), image.Frames[0][2, 1]);
            Assert.Equal(new Rgba32(10, 20, 30, 0), image.Frames[1][2, 1]);
            Assert.Equal(new Rgba32(10, 20, 30, 0), image.PosterFrame![2, 1]);
            Assert.NotEqual(before, image.Frames[1][2, 1]);
            Assert.NotEqual(poster, image.PosterFrame![2, 1]);
            Assert.False(ExifTiff.HasThumbnail(image.Metadata.ExifProfile!.Data.Span));
        }

        // A filter whose callback throws draws nothing, and the canvas can still be drawn on
        using (var image = new Image<Gray8>(2, 1, new Gray8(0)))
        {
            image.Draw(
                canvas =>
                {
                    try
                    {
                        canvas.Filter(ColorMatrix.CreateInvert(), layer =>
                        {
                            layer.Fill(White);
                            throw new FormatException("stop");
                        });
                    }
                    catch (FormatException)
                    {
                    }

                    canvas.Filter(ColorMatrix.CreateInvert(), 1, static (layer, _) => layer.FillRectangle(new Rgba32(55, 55, 55), new Rectangle(1, 0, 1, 1)));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Filter((ColorMatrix)null!, static _ => { }));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Filter(ColorMatrix.Identity, null!));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Filter((ColorMatrix)null!, 1, static (_, _) => { }));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Filter(ColorMatrix.Identity, 1, null!));
                    AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.FilterBackdrop(null!));
                },
                Ct);
            Assert.Equal([0, 200], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // The working storage is charged to the image: with room for the pixels and for the scratch of a session
        // only, a filter is rejected before a pixel changes
        using var probe = new Image<Rgba32>(40, 40);
        long session = 0;
        probe.Draw(_ => session = probe.Owner.Scope.LiveBytes, Ct);
        using var small = new Image<Rgba32>(40, 40, new Rgba32(255, 255, 255), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = session } });
        small.Draw(
            static canvas =>
            {
                AssertThrows<ImageResourceLimitException>(canvas, static canvas => canvas.Filter(ColorMatrix.CreateInvert(), static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ImageResourceLimitException>(canvas, static canvas => canvas.FilterBackdrop(ColorMatrix.CreateInvert()));
            },
            Ct);
        Assert.Throws<ImageResourceLimitException>(() => small.ApplyColorMatrix(ColorMatrix.CreateInvert(), Ct));
        Assert.All(DrawingTestSupport.ReadSamples(small.Frames[0]), sample => Assert.Equal(255, sample));
        Assert.Equal(0, small.Owner.ActiveLeaseCount);

        Assert.Throws<ArgumentNullException>(() => small.ApplyColorMatrix(null!, Ct));
        Assert.Throws<ArgumentNullException>(() => small.Frames[0].ApplyColorMatrix(null!, Ct));
        Assert.Throws<ArgumentNullException>("image", () => ((Image)null!).ApplyColorMatrix(ColorMatrix.Identity, Ct));
        Assert.Throws<ArgumentNullException>("frame", () => ((ImageFrame)null!).ApplyColorMatrix(ColorMatrix.Identity, Ct));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => probe.ApplyColorMatrix(ColorMatrix.CreateInvert(), canceled.Token));
        Assert.Equal(default, probe.Frames[0][5, 5]);
    }

    /// <summary>Compares two colors to a number of levels per sample.</summary>
    private sealed class SampleComparer(int tolerance) : IEqualityComparer<Rgba32>
    {
        public bool Equals(Rgba32 x, Rgba32 y)
            => Math.Abs(x.R - y.R) <= tolerance && Math.Abs(x.G - y.G) <= tolerance && Math.Abs(x.B - y.B) <= tolerance && Math.Abs(x.A - y.A) <= tolerance;

        public int GetHashCode(Rgba32 obj) => 0;
    }

    [Fact]
    public void LinearLightIsTheLightOfTheToneCurvesOfAProfileThatIsNotSrgb()
    {
        // The curves of the profile decode the samples: with the curve of sRGB, half of the light of white is 188,
        // as without profile; with a gamma of 2, it is sqrt(0.5) = 0.7071 -> 180.3
        foreach (var (curve, expected) in new[] { (IccTestProfiles.SrgbCurve(), 188), (IccTestProfiles.Gamma(0x0200), 180), (IccTestProfiles.Gamma(0x0100), 128) })
        {
            using var image = new Image<Rgb24>(1, 1, new Rgb24(0, 0, 0));
            image.Metadata.IccProfile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, curve);
            image.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.FillRectangle(White, new RectangleD(0, 0, 0.5, 1))), Ct);
            Assert.Equal(new Rgb24((byte)expected, (byte)expected, (byte)expected), image.Frames[0][0, 0]);
        }

        // Each channel has its curve, whatever the order of the samples: gammas of 1, 2 and 3 give 127.5 -> 128,
        // 180.3 and 0.5^(1 / 3) = 0.7937 -> 202.4; the alpha is not a color
        var perChannel = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.Gamma(0x0100), IccTestProfiles.Gamma(0x0200), IccTestProfiles.Gamma(0x0300));
        using (var rgba = new Image<Rgba32>(2, 1, new Rgba32(0, 0, 0)))
        using (var bgra = new Image<Bgra32>(1, 1, new Bgra32(0, 0, 0)))
        using (var wide = new Image<Rgba64>(1, 1, new Rgba64(0, 0, 0)))
        {
            rgba.Metadata.IccProfile = bgra.Metadata.IccProfile = wide.Metadata.IccProfile = perChannel;
            rgba.Frames[0][1, 0] = default;
            rgba.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.FillRectangle(White, new RectangleD(0, 0, 1.5, 1))), Ct);
            bgra.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.FillRectangle(White, new RectangleD(0, 0, 0.5, 1))), Ct);
            wide.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.FillRectangle(White, new RectangleD(0, 0, 0.5, 1))), Ct);
            Assert.Equal(new Rgba32(255, 255, 255), rgba.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(255, 255, 255, 128), rgba.Frames[0][1, 0]);
            Assert.Equal(new Bgra32(128, 180, 202), bgra.Frames[0][0, 0]);
            Assert.Equal(new Rgba64(32768, (ushort)Math.Floor((Math.Sqrt(0.5) * 65535) + 0.5), (ushort)Math.Floor((Math.Cbrt(0.5) * 65535) + 0.5)), wide.Frames[0][0, 0]);
        }

        // A monochrome profile has one curve
        using (var gray = new Image<Gray8>(1, 1, new Gray8(0)))
        {
            gray.Metadata.IccProfile = IccTestProfiles.Gray(IccTestProfiles.Gamma(0x0200));
            gray.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.FillRectangle(White, new RectangleD(0, 0, 0.5, 1))), Ct);
            Assert.Equal(new Gray8(180), gray.Frames[0][0, 0]);
        }

        // Layers, shadows and the tiles of the patterns hold samples of the image: they are combined with its curves
        using (var layered = new Image<Rgb24>(3, 1, new Rgb24(0, 0, 0)))
        {
            layered.Metadata.IccProfile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.Gamma(0x0200));
            var tile = new PatternBrush(new Rectangle(0, 0, 1, 1), static canvas =>
            {
                canvas.Fill(new Rgba32(0, 0, 0));
                canvas.FillRectangle(White, new RectangleD(0, 0, 0.5, 1));
            });
            layered.Draw(
                canvas => canvas.Compositing(CompositingSpace.LinearSrgb, inner =>
                {
                    inner.Layer(32768 / 65536.0, static layer => layer.FillRectangle(White, new Rectangle(0, 0, 1, 1)));
                    inner.Shadow(new Rgba32(255, 255, 255, 128), new PointD(1, 0), 0, static layer => layer.FillRectangle(new Rgba32(0, 0, 0), new Rectangle(0, 0, 1, 1)));
                    inner.FillRectangle(tile, new Rectangle(2, 0, 1, 1));
                }),
                Ct);
            // The shadow is white at 128 / 255 of its opacity: sqrt(0.50196) = 0.7085 -> 180.7
            Assert.Equal(new Rgb24(0, 0, 0), layered.Frames[0][0, 0]);
            Assert.Equal(new Rgb24(181, 181, 181), layered.Frames[0][1, 0]);

            // Half of the tile is white over black: its pixel is half the light of white, with the curve of the image
            // and not the function of sRGB, which would give 188
            Assert.Equal(new Rgb24(180, 180, 180), layered.Frames[0][2, 0]);
        }

        // The tables of the curves are charged to the image
        using var probe = new Image<Rgb24>(4, 4, new Rgb24(0, 0, 0));
        probe.Metadata.IccProfile = perChannel;
        long before = 0;
        long inside = 0;
        probe.Draw(
            canvas =>
            {
                before = probe.Owner.Scope.LiveBytes;
                canvas.Compositing(CompositingSpace.LinearSrgb, _ => inside = probe.Owner.Scope.LiveBytes);
            },
            Ct);
        Assert.True(inside >= before + (3 * 65792 * sizeof(double)));
        Assert.Equal(0, probe.Owner.ActiveLeaseCount);
        using var tight = new Image<Rgb24>(4, 4, new Rgb24(0, 0, 0), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = before + 1024 } });
        tight.Metadata.IccProfile = perChannel;
        tight.Draw(static canvas => AssertThrows<ImageResourceLimitException>(canvas, static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static _ => Assert.Fail("The callback ran."))), Ct);
    }

    [Fact]
    public void ABlurAddsLightWhenPaintsAreCombinedInLinearLight()
    {
        // Red next to green, blurred so much that every pixel is half of each: on the stored values, half way is the
        // dark olive (128, 128, 0); in linear light, it is the yellow that half of the light of each color makes, 188
        using var encoded = new Image<Rgb24>(4, 1);
        using var linear = new Image<Rgb24>(4, 1);
        foreach (var image in new[] { encoded, linear })
        {
            image.Draw(
                static canvas =>
                {
                    canvas.FillRectangle(new Rgba32(255, 0, 0), new Rectangle(0, 0, 2, 1));
                    canvas.FillRectangle(new Rgba32(0, 255, 0), new Rectangle(2, 0, 2, 1));
                },
                Ct);
        }

        encoded.Draw(static canvas => canvas.BlurBackdrop(60), Ct);
        linear.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.BlurBackdrop(60)), Ct);
        for (var x = 0; x < 4; x++)
        {
            Assert.InRange(encoded.Frames[0][x, 0].R, 124, 132);
            Assert.InRange(encoded.Frames[0][x, 0].G, 124, 132);
            Assert.InRange(linear.Frames[0][x, 0].R, 185, 191);
            Assert.InRange(linear.Frames[0][x, 0].G, 185, 191);
            Assert.Equal(0, linear.Frames[0][x, 0].B);
        }

        // The red side keeps a little more red, as on the stored values
        Assert.True(linear.Frames[0][0, 0].R > linear.Frames[0][3, 0].R);
        Assert.Equal(linear.Frames[0][0, 0].R, linear.Frames[0][3, 0].G);

        // The same for a drawing that is blurred as a whole, in every format: the light of each pixel is what the
        // blur of the stored values would give of white, w, decoded: with 16-bit samples, encode(w) to a few units
        using var white = new Image<Rgba64>(9, 1, new Rgba64(0, 0, 0));
        using var light = new Image<Rgba64>(9, 1, new Rgba64(0, 0, 0));
        white.Draw(static canvas => canvas.Blur(1.5, static layer => layer.Clear(Colors.Black)), Ct);
        white.Draw(static canvas => canvas.FillRectangle(White, new Rectangle(4, 0, 1, 1)), Ct);
        light.Draw(static canvas => canvas.FillRectangle(White, new Rectangle(4, 0, 1, 1)), Ct);
        white.Draw(static canvas => canvas.BlurBackdrop(1.5), Ct);
        light.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.BlurBackdrop(1.5)), Ct);
        for (var x = 0; x < 9; x++)
        {
            var weight = white.Frames[0][x, 0].R / 65535.0;
            var expected = (weight <= 0.0031308 ? 12.92 * weight : (1.055 * Math.Pow(weight, 1 / 2.4)) - 0.055) * 65535;
            Assert.InRange(light.Frames[0][x, 0].R, expected - 40, expected + 40);
            Assert.Equal(light.Frames[0][x, 0].R, light.Frames[0][x, 0].G);
            Assert.Equal(65535, light.Frames[0][x, 0].A);
        }

        Assert.True(light.Frames[0][3, 0].R > white.Frames[0][3, 0].R + 10000);

        // With the tone curves of a profile, and on a gray image: gamma 2, so half the light is 180
        using var gray = new Image<Gray8>(4, 1, new Gray8(0));
        gray.Metadata.IccProfile = IccTestProfiles.Gray(IccTestProfiles.Gamma(0x0200));
        gray.Draw(
            static canvas =>
            {
                canvas.FillRectangle(White, new Rectangle(0, 0, 2, 1));
                canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.BlurBackdrop(60));
            },
            Ct);
        Assert.All(DrawingTestSupport.ReadSamples(gray.Frames[0]), sample => Assert.InRange(sample, 177, 183));

        // A blurred layer of one color keeps that color: only its alpha is spread, and it is combined in linear light
        using var layer = new Image<Rgb24>(5, 1, new Rgb24(0, 0, 0));
        layer.Draw(static canvas => canvas.Compositing(CompositingSpace.LinearSrgb, static inner => inner.Blur(60, static blurred => blurred.FillRectangle(White, new Rectangle(0, 0, 5, 1)))), Ct);
        Assert.All(DrawingTestSupport.ReadSamples(layer.Frames[0]), sample => Assert.Equal(255, sample));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Filters and backdrops
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AFilteredDrawingIsBlurredByTheWeightsOfABellAlongEachAxis()
    {
        // One opaque white pixel, blurred with a deviation of 1: the weights are exp(-i^2 / 2) for i from -3 to 3,
        // divided by their sum. The middle one is 1 / 2.505964..., so the pixel keeps 0.159244... of its alpha (41 of
        // 255) and its neighbor along an axis 0.159244 * exp(-1/2) (25 of 255); the color stays white.
        var center = 1 / (1 + (2 * (Math.Exp(-0.5) + Math.Exp(-2) + Math.Exp(-4.5))));
        using var image = new Image<Rgba32>(9, 9);
        image.Draw(canvas => canvas.Filter(new BlurFilter(1), layer => layer.FillRectangle(White, new Rectangle(4, 4, 1, 1))), Ct);
        Assert.Equal(new Rgba32(255, 255, 255, (byte)Math.Floor((255 * center * center) + 0.5)), image.Frames[0][4, 4]);
        Assert.Equal(new Rgba32(255, 255, 255, (byte)Math.Floor((255 * center * center * Math.Exp(-0.5)) + 0.5)), image.Frames[0][5, 4]);
        Assert.Equal(41, image.Frames[0][4, 4].A);
        Assert.Equal(image.Frames[0][5, 4], image.Frames[0][4, 3]);

        // One deviation per axis: along x only, the rows above and below stay transparent
        image.Draw(canvas => canvas.Clear(Colors.Transparent), Ct);
        image.Draw(canvas => canvas.Filter(new BlurFilter(1, 0), layer => layer.FillRectangle(White, new Rectangle(4, 4, 1, 1))), Ct);
        Assert.Equal((byte)Math.Floor((255 * center) + 0.5), image.Frames[0][4, 4].A);
        Assert.Equal((byte)Math.Floor((255 * center * Math.Exp(-0.5)) + 0.5), image.Frames[0][5, 4].A);
        Assert.Equal(0, image.Frames[0][4, 3].A);

        // The edges of the canvas: transparent beyond them by default (a drawing that covers the canvas fades at its
        // edges), or the drawing continued, wrapped or mirrored
        using var full = new Image<Rgba32>(10, 10);
        full.Draw(canvas => canvas.Filter(new BlurFilter(1), layer => layer.Fill(White)), Ct);
        Assert.InRange(full.Frames[0][0, 0].A, 1, 254);
        Assert.Equal(255, full.Frames[0][5, 5].A);
        foreach (var mode in new[] { FilterEdgeMode.Duplicate, FilterEdgeMode.Wrap, FilterEdgeMode.Mirror })
        {
            full.Draw(canvas => canvas.Clear(Colors.Transparent), Ct);
            full.Draw(canvas => canvas.Filter(new BlurFilter(1) { EdgeMode = mode }, layer => layer.Fill(White)), Ct);
            Assert.Equal(new Rgba32(255, 255, 255), full.Frames[0][0, 0]);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new BlurFilter(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlurFilter(1, 10001));
        Assert.Throws<ArgumentException>(() => new BlurFilter(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlurFilter(1) { EdgeMode = (FilterEdgeMode)4 });
        Assert.Equal((2d, 3d, FilterEdgeMode.Transparent), (new BlurFilter(2, 3).SigmaX, new BlurFilter(2, 3).SigmaY, new BlurFilter(2, 3).EdgeMode));
    }

    [Fact]
    public void AFilterCanChangeColorsCastAShadowAndBeChained()
    {
        // A color matrix on the straight colors: the gray of the matrix of the canvas, up to the rounding of the
        // single precision values
        var photo = new LinearGradientBrush(new PointD(0, 0), new PointD(8, 0), [new GradientStop(0, new Rgba32(200, 30, 90)), new GradientStop(1, new Rgba32(20, 160, 240, 180))]);
        var expected = Draw(canvas => canvas.Filter(ColorMatrix.CreateGrayscale(), layer => layer.Fill(photo)));
        var actual = Draw(canvas => canvas.Filter(new ColorMatrixFilter(ColorMatrix.CreateGrayscale()), layer => layer.Fill(photo)));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(actual[i], expected[i] - 1, expected[i] + 1, message: $"sample {i}");
        }

        // Inverted twice: the colors back
        var twice = Draw(canvas => canvas.Filter(new FilterChain(new ColorMatrixFilter(ColorMatrix.CreateInvert()), new ColorMatrixFilter(ColorMatrix.CreateInvert())), layer => layer.Fill(photo)));
        var plain = Draw(canvas => canvas.Fill(photo));
        for (var i = 0; i < plain.Length; i++)
        {
            Assert.InRange(twice[i], plain[i] - 1, plain[i] + 1, message: $"sample {i}");
        }

        // A shadow without blur 3 pixels to the right, under the drawing: a white square of 2 at (2, 2), its black
        // shadow at (5, 2); with half a pixel, the shadow is half on its first and its last column
        using var image = new Image<Rgba32>(10, 6);
        var square = new Rectangle(2, 2, 2, 2);
        image.Draw(canvas => canvas.Filter(new DropShadowFilter(new Rgba32(0, 0, 0), new PointD(3, 0), 0), layer => layer.FillRectangle(White, square)), Ct);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][2, 2]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image.Frames[0][4, 2]);
        Assert.Equal(new Rgba32(0, 0, 0), image.Frames[0][5, 2]);
        Assert.Equal(new Rgba32(0, 0, 0), image.Frames[0][6, 3]);
        image.Draw(canvas => canvas.Clear(Colors.Transparent), Ct);
        image.Draw(canvas => canvas.Filter(new DropShadowFilter(new Rgba32(0, 0, 0), new PointD(2.5, 0), 0) { ShadowOnly = true }, layer => layer.FillRectangle(White, square)), Ct);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image.Frames[0][2, 2]);
        Assert.Equal(new Rgba32(0, 0, 0, 128), image.Frames[0][4, 2]);
        Assert.Equal(new Rgba32(0, 0, 0), image.Frames[0][5, 2]);
        Assert.Equal(new Rgba32(0, 0, 0, 128), image.Frames[0][6, 2]);

        Assert.Throws<ArgumentException>(() => new DropShadowFilter(default, new PointD(double.NaN, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DropShadowFilter(default, default, -1));
        Assert.Throws<ArgumentNullException>(() => new ColorMatrixFilter(null!));
        Assert.Throws<ArgumentNullException>(() => new FilterChain(new BlurFilter(1), null!));
        Assert.Equal(2, new FilterChain(new BlurFilter(1), new BlurFilter(2)).Filters.Count);

        static int[] Draw(DrawingAction action)
        {
            using var image = new Image<Rgba32>(8, 2);
            image.Draw(action, Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void TheLengthsOfAFilterAreLengthsOfTheDrawing()
    {
        // Scaled by 2: a blur of 1 is a blur of 2, and a shadow 1 pixel away is 2 pixels away
        var filter = new FilterChain(new BlurFilter(1), new DropShadowFilter(new Rgba32(0, 0, 0, 128), new PointD(1, 0.5), 0.5));
        var expected = Draw(canvas => canvas.Filter(new FilterChain(new BlurFilter(2), new DropShadowFilter(new Rgba32(0, 0, 0, 128), new PointD(2, 1), 1)), layer => layer.FillRectangle(White, new Rectangle(6, 6, 6, 6))));
        Assert.Equal(expected, Draw(canvas => canvas.Transform(AffineTransform.CreateScale(2, 2), scaled => scaled.Filter(filter, layer => layer.FillRectangle(White, new Rectangle(3, 3, 3, 3))))));

        // Turned by a quarter of a turn: the filter applies in the plane of the drawing, which is then turned. The
        // square is symmetric and its shadow is below it: turned, the shadow is on its left.
        var turned = Draw(canvas => canvas.Transform(AffineTransform.CreateRotation(90, new PointD(12, 12)), plane => plane.Filter(new DropShadowFilter(new Rgba32(0, 0, 0), new PointD(0, 4), 0), layer => layer.FillRectangle(White, new Rectangle(8, 8, 8, 8)))));
        Assert.Equal(255, turned[((12 * 24) + 6) * 4 + 3]);
        Assert.Equal(0, turned[((12 * 24) + 6) * 4]);
        Assert.Equal(0, turned[((12 * 24) + 18) * 4 + 3]);

        static int[] Draw(DrawingAction action)
        {
            using var image = new Image<Rgba32>(24, 24);
            image.Draw(action, Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void ABackdropLayerStartsWithWhatIsBehindItFiltered()
    {
        // Red on the left half, blue on the right one; the backdrop is the rectangle from x = 4 to x = 12
        var red = new Rgba32(255, 0, 0);
        var blue = new Rgba32(0, 0, 255);
        using var image = CreateHalves();
        image.Draw(canvas => canvas.BackdropLayer(new BlurFilter(0.5), new RectangleD(4, 0, 8, 4), 1, static _ => { }), Ct);

        // Outside the rectangle, nothing changes; on its edges, the backdrop is mirrored (a blur of 0.5 reaches 2
        // pixels): still red, still blue, and opaque. In the middle, red and blue are mixed.
        Assert.Equal(red, image.Frames[0][3, 1]);
        Assert.Equal(blue, image.Frames[0][12, 1]);
        Assert.Equal(red, image.Frames[0][4, 1]);
        Assert.Equal(blue, image.Frames[0][11, 1]);
        Assert.True(image.Frames[0][7, 1].B > 0 && image.Frames[0][8, 1].R > 0);
        Assert.Equal(image.Frames[0][7, 0], image.Frames[0][7, 3]);

        // The callback draws on the filtered backdrop, and the layer has an opacity: inverted at half its opacity,
        // red over red is (255, 0, 0) and (0, 255, 255) mixed half way
        using var inverted = CreateHalves();
        inverted.Draw(canvas => canvas.BackdropLayer(new ColorMatrixFilter(ColorMatrix.CreateInvert()), new RectangleD(0, 0, 16, 4), 0.5, layer => layer.FillRectangle(White, new Rectangle(0, 0, 1, 1))), Ct);
        Assert.Equal(new Rgba32(128, 128, 128), inverted.Frames[0][1, 1]);
        Assert.Equal(new Rgba32(255, 128, 128), inverted.Frames[0][0, 0]);

        // A clip applies to the layer: only the pixels it lets through change; without filter, the layer starts with
        // the backdrop as it is
        using var clipped = CreateHalves();
        clipped.Draw(canvas => canvas.Clip(new Rectangle(0, 0, 2, 2), inner => inner.BackdropLayer(new ColorMatrixFilter(ColorMatrix.CreateInvert()), new RectangleD(0, 0, 16, 4), 1, static _ => { })), Ct);
        Assert.Equal(new Rgba32(0, 255, 255), clipped.Frames[0][1, 1]);
        Assert.Equal(red, clipped.Frames[0][2, 2]);
        clipped.Draw(canvas => canvas.BackdropLayer(null, new RectangleD(0, 0, 16, 4), 1, layer => layer.Fill(new SolidBrush(red) { CompositeMode = CompositeMode.DestinationOut })), Ct);
        Assert.Equal(new Rgba32(0, 255, 255), clipped.Frames[0][1, 1]);

        // Each frame starts with its own backdrop
        using var animation = new Image<Rgba32>(2, 1, red);
        animation.AppendFrame();
        animation.Frames[1].Draw(canvas => canvas.Fill(blue), Ct);
        animation.Draw(canvas => canvas.BackdropLayer(new ColorMatrixFilter(ColorMatrix.CreateInvert()), new RectangleD(0, 0, 2, 1), 1, static _ => { }), Ct);
        Assert.Equal(new Rgba32(0, 255, 255), animation.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(255, 255, 0), animation.Frames[1][0, 0]);

        image.Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.BackdropLayer(null, new RectangleD(0, 0, 1, 1), 1, null!));
                AssertThrows<ArgumentException>(canvas, static canvas => canvas.BackdropLayer(null, new RectangleD(double.NaN, 0, 1, 1), 1, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.BackdropLayer(null, new RectangleD(0, 0, 1, 1), 2, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<ArgumentOutOfRangeException>(canvas, static canvas => canvas.BackdropLayer(null, new RectangleD(0, 0, 1, 1), 1, static _ => Assert.Fail("The callback ran."), (CompositeMode)12));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.Filter((Filter)null!, static _ => Assert.Fail("The callback ran.")));
            },
            Ct);

        Image<Rgba32> CreateHalves()
        {
            var halves = new Image<Rgba32>(16, 4, red);
            halves.Draw(canvas => canvas.FillRectangle(blue, new Rectangle(8, 0, 8, 4)), Ct);
            return halves;
        }
    }

    [Fact]
    public void AFilterGraphComputesItsPrimitivesFromTheDrawingAndEachOther()
    {
        var red = new Rgba32(255, 0, 0);
        var blue = new Rgba32(0, 0, 255);

        // A flood fills its subregion only, and nothing is drawn outside the region of the filter
        var pixels = DrawFiltered(new FilterGraph(new FloodPrimitive(new Rgba32(255, 0, 0, 128)) { Subregion = new RectangleD(1, 1, 2, 2) }));
        Assert.Equal(new Rgba32(255, 0, 0, 128), pixels[1, 1]);
        Assert.Equal(new Rgba32(255, 0, 0, 128), pixels[2, 2]);
        Assert.Equal(default, pixels[3, 2]);
        pixels = DrawFiltered(new FilterGraph(new FloodPrimitive(blue), new MergePrimitive(FilterInput.FromResult(0), FilterInput.SourceGraphic)) { Region = new RectangleD(0, 0, 3, 3) });
        Assert.Equal(blue, pixels[0, 0]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[1, 1]);
        Assert.Equal(default, pixels[3, 3]);

        // An offset moves the drawing (the white pixel at (1, 1)); by half a pixel, half of it is on each side
        pixels = DrawFiltered(new FilterGraph(new OffsetPrimitive(new PointD(2, 1))));
        Assert.Equal(new Rgba32(255, 255, 255), pixels[3, 2]);
        Assert.Equal(default, pixels[1, 1]);
        pixels = DrawFiltered(new FilterGraph(new OffsetPrimitive(new PointD(0.5, 0))));
        Assert.Equal(new Rgba32(255, 255, 255, 128), pixels[1, 1]);
        Assert.Equal(new Rgba32(255, 255, 255, 128), pixels[2, 1]);

        // A tile repeats the subregion of its input: the pixels of the drawing from (0, 0) to (2, 2), with the white
        // pixel at (1, 1), every 2 pixels
        pixels = DrawFiltered(new FilterGraph(new OffsetPrimitive(default) { Subregion = new RectangleD(0, 0, 2, 2) }, new TilePrimitive { Subregion = new RectangleD(0, 0, 6, 6) }));
        Assert.Equal(new Rgba32(255, 255, 255), pixels[3, 3]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[5, 1]);
        Assert.Equal(default, pixels[4, 3]);

        // The alpha of the drawing, painted green
        pixels = DrawFiltered(new FilterGraph(new FloodPrimitive(new Rgba32(0, 255, 0)), new CompositePrimitive(FilterCompositeOperator.In, FilterInput.SourceAlpha)));
        Assert.Equal(new Rgba32(0, 255, 0), pixels[1, 1]);
        Assert.Equal(default, pixels[0, 1]);

        // The operators, with red on the pixels 0 and 1 and blue on the pixels 1 and 2
        Assert.Equal([red, red, blue], Composite(FilterCompositeOperator.Over));
        Assert.Equal([default, red, default], Composite(FilterCompositeOperator.In));
        Assert.Equal([red, default, default], Composite(FilterCompositeOperator.Out));
        Assert.Equal([default, red, blue], Composite(FilterCompositeOperator.Atop));
        Assert.Equal([red, default, blue], Composite(FilterCompositeOperator.Xor));
        Assert.Equal([red, new Rgba32(255, 0, 255), blue], Composite(FilterCompositeOperator.Lighter));

        // Arithmetic, half of each: in sRGB, (0.5, 0, 0.5) where both are; by default in linear light, where 0.5 is
        // encoded as 1.055 * 0.5^(1 / 2.4) - 0.055 = 0.7354 (188). Where only red is, 0.5 of red and of the alpha.
        // With -A B + 0.3, every component is 0.3 where one input is (white), and the alpha is -0.7 where both are:
        // zero, and a color is never more than its alpha.
        Assert.Equal([new Rgba32(255, 0, 0, 128), new Rgba32(128, 0, 128), new Rgba32(0, 0, 255, 128)], Composite(FilterCompositeOperator.Arithmetic, FilterColorSpace.Srgb, k2: 0.5, k3: 0.5));
        var encoded = (byte)Math.Floor((255 * ((1.055 * Math.Pow(0.5, 1 / 2.4)) - 0.055)) + 0.5);
        Assert.Equal(188, encoded);
        Assert.Equal(new Rgba32(encoded, 0, encoded), Composite(FilterCompositeOperator.Arithmetic, FilterColorSpace.LinearRgb, k2: 0.5, k3: 0.5)[1]);
        Assert.Equal([new Rgba32(255, 255, 255, 77), default, new Rgba32(255, 255, 255, 77)], Composite(FilterCompositeOperator.Arithmetic, FilterColorSpace.Srgb, k1: -1, k4: 0.3));

        // Blend modes: the first input is the source, the second one the backdrop; red at 128 / 255 over blue leaves
        // 127 / 255 of the blue
        Assert.Equal(new Rgba32(128, 128, 0), Blend(BlendMode.Multiply, new Rgba32(255, 128, 0), new Rgba32(128, 255, 255)));
        Assert.Equal(new Rgba32(255, 255, 255), Blend(BlendMode.Screen, new Rgba32(255, 128, 0), new Rgba32(128, 255, 255)));
        Assert.Equal(new Rgba32(128, 0, 127), Blend(BlendMode.Normal, new Rgba32(255, 0, 0, 128), new Rgba32(0, 0, 255)));

        Assert.Throws<ArgumentException>(() => new FilterGraph());
        Assert.Throws<ArgumentNullException>(() => new FilterGraph(new FloodPrimitive(red), null!));
        Assert.Throws<ArgumentException>(() => new FilterGraph(new OffsetPrimitive(default) { Input = FilterInput.FromResult(0) }));
        Assert.Throws<ArgumentException>(() => new FilterGraph(new FloodPrimitive(red)) { Region = new RectangleD(double.NaN, 0, 1, 1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterInput.FromResult(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FloodPrimitive(red) { ColorSpace = (FilterColorSpace)2 });
        Assert.Throws<ArgumentException>(() => new FloodPrimitive(red) { Subregion = new RectangleD(0, double.PositiveInfinity, 1, 1) });
        Assert.Throws<ArgumentException>(() => new OffsetPrimitive(new PointD(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompositePrimitive((FilterCompositeOperator)7, FilterInput.SourceGraphic));
        Assert.Throws<ArgumentException>(() => new CompositePrimitive(FilterCompositeOperator.Arithmetic, FilterInput.SourceGraphic) { K1 = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BlendPrimitive((BlendMode)99, FilterInput.SourceGraphic));
        Assert.Throws<ArgumentNullException>(() => new ColorMatrixPrimitive(null!));
        Assert.Equal(FilterInput.Previous, default);
        Assert.Equal((FilterInputKind.Result, 3), (FilterInput.FromResult(3).Kind, FilterInput.FromResult(3).ResultIndex));
        Assert.NotEqual(FilterInput.SourceGraphic, FilterInput.SourceAlpha);

        Rgba32[] Composite(FilterCompositeOperator @operator, FilterColorSpace space = FilterColorSpace.Srgb, double k1 = 0, double k2 = 0, double k3 = 0, double k4 = 0)
        {
            var result = DrawFiltered(new FilterGraph(
                new FloodPrimitive(red) { Subregion = new RectangleD(0, 0, 2, 1) },
                new FloodPrimitive(blue) { Subregion = new RectangleD(1, 0, 2, 1) },
                new CompositePrimitive(@operator, FilterInput.FromResult(1)) { Input = FilterInput.FromResult(0), ColorSpace = space, K1 = k1, K2 = k2, K3 = k3, K4 = k4 }));
            return [result[0, 0], result[1, 0], result[2, 0]];
        }

        Rgba32 Blend(BlendMode mode, Rgba32 source, Rgba32 backdrop)
            => DrawFiltered(new FilterGraph(new FloodPrimitive(source), new FloodPrimitive(backdrop), new BlendPrimitive(mode, FilterInput.FromResult(1)) { Input = FilterInput.FromResult(0), ColorSpace = FilterColorSpace.Srgb }))[0, 0];
    }

    [Fact]
    public void AFilterGraphChangesColorsInTheColorsOfEachPrimitive()
    {
        // Luminance to alpha: a gray of 128 in sRGB is 0.502 of luminance, but 0.2159 in linear light (55), the default
        var gray = new Rgba32(128, 128, 128);
        Assert.Equal(new Rgba32(0, 0, 0, 128), DrawFiltered(new FilterGraph(new ColorMatrixPrimitive(ColorMatrix.LuminanceToAlpha) { ColorSpace = FilterColorSpace.Srgb }), gray)[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 55), DrawFiltered(new FilterGraph(new ColorMatrixPrimitive(ColorMatrix.LuminanceToAlpha)), gray)[0, 0]);

        // A matrix that changes nothing in linear light gives the colors back
        foreach (var color in new[] { new Rgba32(0, 1, 2), new Rgba32(10, 128, 254), new Rgba32(200, 30, 90, 77) })
        {
            Assert.Equal(color, DrawFiltered(new FilterGraph(new ColorMatrixPrimitive(ColorMatrix.CreateBrightness(1.0000001))), color)[0, 0]);
        }

        // Transfer functions, in sRGB: a table [0, 1, 0.5] takes 0.2 to 0.4 and 1 to 0.5; a discrete one [0.2, 0.6, 1]
        // takes 0.2 to 0.2 and 128 / 255 to 0.6; 0.5 C + 0.25 takes 1 to 0.75; C^2 takes 128 / 255 to 0.252 (64)
        var transfer = new ComponentTransferPrimitive
        {
            Red = TransferFunction.CreateTable(0, 1, 0.5),
            Green = TransferFunction.CreateDiscrete(0.2, 0.6, 1),
            Blue = TransferFunction.CreateLinear(0.5, 0.25),
            ColorSpace = FilterColorSpace.Srgb,
        };
        Assert.Equal(new Rgba32(102, 51, 191), DrawFiltered(new FilterGraph(transfer), new Rgba32(51, 51, 255))[0, 0]);
        Assert.Equal(new Rgba32(128, 153, 191), DrawFiltered(new FilterGraph(transfer), new Rgba32(255, 128, 255))[0, 0]);
        Assert.Equal(new Rgba32(64, 0, 0, 128), DrawFiltered(new FilterGraph(new ComponentTransferPrimitive { Red = TransferFunction.CreateGamma(1, 2, 0), Alpha = TransferFunction.CreateDiscrete(0.5), ColorSpace = FilterColorSpace.Srgb }), new Rgba32(128, 0, 0))[0, 0]);
        Assert.Equal(new Rgba32(1, 2, 3), DrawFiltered(new FilterGraph(new ComponentTransferPrimitive { Red = TransferFunction.CreateTable(), Green = TransferFunction.Identity }), new Rgba32(1, 2, 3))[0, 0]);

        Assert.Throws<ArgumentException>(() => TransferFunction.CreateTable(0, double.NaN));
        Assert.Throws<ArgumentException>(() => TransferFunction.CreateGamma(1, double.PositiveInfinity, 0));
        Assert.Throws<ArgumentNullException>(() => new ComponentTransferPrimitive { Alpha = null! });
        Assert.Equal((TransferFunctionKind.Linear, 0.5, 0.25), (transfer.Blue.Kind, transfer.Blue.Slope, transfer.Blue.Intercept));
        Assert.Equal([0, 1, 0.5], transfer.Red.Values);

        // A subregion is in the coordinates of the drawing: scaled by 2, the pixel (1, 1) is 4 pixels
        using var image = new Image<Rgba32>(4, 4);
        image.Draw(canvas => canvas.Transform(AffineTransform.CreateScale(2, 2), scaled => scaled.Filter(new FilterGraph(new FloodPrimitive(gray) { Subregion = new RectangleD(1, 1, 1, 1) }), static _ => { })), Ct);
        Assert.Equal(gray, image.Frames[0][2, 3]);
        Assert.Equal(default, image.Frames[0][1, 1]);
    }

    /// <summary>Filters a white pixel at (1, 1) of a canvas of 6 by 6 pixels, or the whole canvas painted with a color.</summary>
    private static FilteredPixels DrawFiltered(Filter filter, Rgba32? color = null)
    {
        using var image = new Image<Rgba32>(6, 6);
        image.Draw(canvas => canvas.Filter(filter, layer => layer.FillRectangle(color ?? new Rgba32(255, 255, 255), color is null ? new Rectangle(1, 1, 1, 1) : new Rectangle(0, 0, 6, 6))), Ct);
        var pixels = new Rgba32[36];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = image.Frames[0][i % 6, i / 6];
        }

        return new FilteredPixels(pixels);
    }

    private sealed class FilteredPixels(Rgba32[] pixels)
    {
        public Rgba32 this[int x, int y] => pixels[x + (6 * y)];
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Paint colors of a profile
    // -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void PaintsOfAProfileAreConvertedToTheColorSpaceOfTheImage()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var red = new Rgba64(65535, 0, 0);

        // The red of sRGB, on an image whose profile is Display P3: (0.9175, 0.2003, 0.1386) of its samples, where
        // the red (65535, 0, 0) of the image is a color sRGB does not have
        using (var image = new Image<Rgba64>(2, 1))
        {
            image.Metadata.IccProfile = displayP3;
            image.Draw(
                canvas =>
                {
                    canvas.FillRectangle(red, new Rectangle(0, 0, 1, 1));
                    canvas.PaintProfile(IccProfile.Srgb, inner => inner.FillRectangle(red, new Rectangle(1, 0, 1, 1)));
                },
                Ct);
            Assert.Equal(red, image.Frames[0][0, 0]);
            var converted = image.Frames[0][1, 0];
            Assert.InRange(converted.R, (0.9175 * 65535) - 80, (0.9175 * 65535) + 80);
            Assert.InRange(converted.G, (0.2003 * 65535) - 80, (0.2003 * 65535) + 80);
            Assert.InRange(converted.B, (0.1386 * 65535) - 80, (0.1386 * 65535) + 80);
            Assert.Equal(65535, converted.A);

            // It is the conversion of an ICC transform between the two profiles
            ushort[] expected = [65535, 0, 0];
            IccColorTransform.Create(IccProfile.Srgb, displayP3).Convert(expected, expected);
            Assert.Equal(new Rgba64(expected[0], expected[1], expected[2]), converted);
        }

        // The same image described by a CICP color description instead (the primaries of Display P3, the function
        // of sRGB): the same color, from the chromaticities of the primaries instead of the colorants of a profile
        using (var described = new Image<Rgba64>(1, 1))
        {
            described.Metadata.Cicp = new CicpColorDescription(12, 13, 0, fullRange: true);
            described.Draw(canvas => canvas.PaintProfile(IccProfile.Srgb, inner => inner.Fill(red)), Ct);
            var converted = described.Frames[0][0, 0];
            Assert.InRange(converted.R, (0.9175 * 65535) - 80, (0.9175 * 65535) + 80);
            Assert.InRange(converted.G, (0.2003 * 65535) - 80, (0.2003 * 65535) + 80);
            Assert.InRange(converted.B, (0.1386 * 65535) - 80, (0.1386 * 65535) + 80);
        }

        // On the wider primaries of BT.2020 with the function of BT.709: the light (0.6274, 0.0691, 0.0164), encoded
        // as 1.099 l^0.45 - 0.099, or 4.5 l under 0.018
        using (var wide = new Image<Rgba64>(1, 1))
        {
            wide.Metadata.Cicp = new CicpColorDescription(9, 1, 0, fullRange: true);
            wide.Draw(canvas => canvas.PaintProfile(IccProfile.Srgb, inner => inner.Fill(red)), Ct);
            var converted = wide.Frames[0][0, 0];
            Assert.InRange(converted.R, (((1.099 * Math.Pow(0.6274, 0.45)) - 0.099) * 65535) - 100, (((1.099 * Math.Pow(0.6274, 0.45)) - 0.099) * 65535) + 100);
            Assert.InRange(converted.G, (((1.099 * Math.Pow(0.0691, 0.45)) - 0.099) * 65535) - 100, (((1.099 * Math.Pow(0.0691, 0.45)) - 0.099) * 65535) + 100);
            Assert.InRange(converted.B, (4.5 * 0.0164 * 65535) - 100, (4.5 * 0.0164 * 65535) + 100);
        }

        // On an sRGB image, the colors of sRGB are not changed, bit for bit; those of Display P3 that sRGB does not
        // have are clipped, component by component, and its grays are the same grays
        using (var plain = new Image<Rgba32>(4, 1))
        {
            plain.Draw(
                canvas =>
                {
                    canvas.PaintProfile(IccProfile.Srgb, inner => inner.FillRectangle(new Rgba32(200, 100, 40, 128), new Rectangle(0, 0, 1, 1)));
                    canvas.PaintProfile(displayP3, inner =>
                    {
                        inner.FillRectangle(new Rgba32(255, 0, 0), new Rectangle(1, 0, 1, 1));
                        inner.FillRectangle(new Rgba32(128, 128, 128), new Rectangle(2, 0, 1, 1));
                        inner.FillRectangle(new Rgba32(200, 100, 40), new Rectangle(3, 0, 1, 1));
                    });
                },
                Ct);
            Assert.Equal(new Rgba32(200, 100, 40, 128), plain.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(255, 0, 0), plain.Frames[0][1, 0]);
            Assert.Equal(new Rgba32(128, 128, 128), plain.Frames[0][2, 0], new SampleComparer(1));

            // A color of Display P3 is more saturated than the same samples in sRGB: more red, less green and blue
            var orange = plain.Frames[0][3, 0];
            Assert.True(orange.R > 200 && orange.G < 100 && orange.B < 40);
        }

        // On a grayscale image, a color is the gray that has its luminance in the connection space of the profiles,
        // as a conversion to a gray profile gives it (0.2225 of the light of white for the red of sRGB once adapted
        // to D50, which the function of sRGB encodes as 0.5090), not the luma of its samples (0.2126 of white)
        using (var gray = new Image<Gray16>(2, 1))
        {
            gray.Draw(
                canvas =>
                {
                    canvas.FillRectangle(red, new Rectangle(0, 0, 1, 1));
                    canvas.PaintProfile(IccProfile.Srgb, inner => inner.FillRectangle(red, new Rectangle(1, 0, 1, 1)));
                },
                Ct);
            Assert.Equal(new Gray16(13933), gray.Frames[0][0, 0]);
            Assert.InRange(gray.Frames[0][1, 0].Value, (0.5090 * 65535) - 100, (0.5090 * 65535) + 100);
            ushort[] expected = [65535, 0, 0];
            var luminance = new ushort[1];
            IccColorTransform.Create(IccProfile.Srgb, IccProfile.SrgbGray).Convert(expected, luminance);
            Assert.Equal(new Gray16(luminance[0]), gray.Frames[0][1, 0]);
        }
    }

    [Fact]
    public void EveryColorThatIsGivenIsAColorOfTheProfileOfThePaints()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var red = new Rgba64(65535, 0, 0);
        var blue = new Rgba64(0, 0, 65535);
        using var image = new Image<Rgba64>(8, 4);
        image.Metadata.IccProfile = displayP3;
        using var texture = new Image<Rgba64>(1, 1, red);
        Rgba64 convertedRed = default;
        Rgba64 convertedBlue = default;
        image.Draw(
            canvas =>
            {
                canvas.PaintProfile(IccProfile.Srgb, inner =>
                {
                    inner.FillRectangle(red, new Rectangle(0, 0, 1, 1));
                    inner.FillRectangle(blue, new Rectangle(1, 0, 1, 1));
                });
            },
            Ct);
        convertedRed = image.Frames[0][0, 0];
        convertedBlue = image.Frames[0][1, 0];
        Assert.NotEqual(red, convertedRed);
        Assert.NotEqual(blue, convertedBlue);

        GradientStop[] stops = [new(0, red), new(1, blue)];
        image.Draw(
            canvas =>
            {
                canvas.Clear(Colors.Transparent);
                canvas.PaintProfile(IccProfile.Srgb, inner =>
                {
                    // The stops of a gradient, in the three spaces their colors are mixed in: at a stop, the color
                    // of the stop
                    inner.FillRectangle(new LinearGradientBrush(new PointD(0.5, 0), new PointD(7.5, 0), stops), new Rectangle(0, 0, 8, 1));
                    inner.FillRectangle(new LinearGradientBrush(new PointD(0.5, 0), new PointD(7.5, 0), stops) { ColorSpace = GradientColorSpace.LinearSrgb }, new Rectangle(0, 1, 8, 1));
                    inner.FillRectangle(new LinearGradientBrush(new PointD(0.5, 0), new PointD(7.5, 0), stops) { ColorSpace = GradientColorSpace.Oklab }, new Rectangle(0, 2, 8, 1));

                    // A layer and the tile of a pattern hold samples of the image: their paints are converted once
                    inner.Layer(1, layer => layer.FillRectangle(red, new Rectangle(0, 3, 1, 1)));
                    inner.FillRectangle(new PatternBrush(new Rectangle(0, 0, 1, 1), tile => tile.Fill(red)), new Rectangle(1, 3, 1, 1));

                    // The color of a shadow, whose drawing is erased here
                    inner.Shadow(red, new PointD(1, 0), 0, shadowed => shadowed.FillRectangle(blue, new Rectangle(2, 3, 1, 1)));

                    // An image that is drawn, or that a brush paints, is samples already: not converted
                    inner.DrawImage(texture.Frames[0], new PointD(4, 3));
                    inner.FillRectangle(new ImageBrush(texture.Frames[0]) { WrapMode = ImageWrapMode.Repeat }, new Rectangle(5, 3, 1, 1));

                    // A scope inside another one: the profile of the image changes nothing
                    inner.PaintProfile(displayP3, same => same.FillRectangle(red, new Rectangle(6, 3, 1, 1)));
                });

                // After the callback, the colors are samples of the image again, also when the callback throws
                try
                {
                    canvas.PaintProfile(IccProfile.Srgb, 1, static (_, _) => throw new FormatException("stop"));
                }
                catch (FormatException)
                {
                }

                canvas.FillRectangle(red, new Rectangle(7, 3, 1, 1));
            },
            Ct);
        var frame = image.Frames[0];
        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(convertedRed, frame[0, row]);
            Assert.Equal(convertedBlue, frame[7, row]);
        }

        // In the middle, the gradient that mixes light is lighter than the one that mixes the stored values
        Assert.True(frame[3, 1].R > frame[3, 0].R);
        Assert.NotEqual(frame[3, 1], frame[3, 2]);
        Assert.Equal(convertedRed, frame[0, 3]);
        Assert.Equal(convertedRed, frame[1, 3]);
        Assert.Equal(convertedBlue, frame[2, 3]);
        Assert.Equal(convertedRed, frame[3, 3]);
        Assert.Equal(red, frame[4, 3]);
        Assert.Equal(red, frame[5, 3]);
        Assert.Equal(red, frame[6, 3]);
        Assert.Equal(red, frame[7, 3]);

        // Clearing the canvas with a color of the profile, and 8-bit samples: the converted color, rounded
        using var narrow = new Image<Rgb24>(1, 1);
        narrow.Metadata.IccProfile = displayP3;
        narrow.Draw(static canvas => canvas.PaintProfile(IccProfile.Srgb, static inner => inner.Clear(new Rgba32(255, 0, 0))), Ct);
        Assert.Equal(new Rgb24((byte)((convertedRed.R + 128) / 257), (byte)((convertedRed.G + 128) / 257), (byte)((convertedRed.B + 128) / 257)), narrow.Frames[0][0, 0]);
    }

    [Fact]
    public void TheProfileOfThePaintsAndTheColorSpaceOfTheImageAreValidated()
    {
        using var image = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3));
        image.Draw(
            static canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.PaintProfile(null!, static _ => { }));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.PaintProfile(IccProfile.Srgb, null!));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.PaintProfile(null!, 1, static (_, _) => { }));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.PaintProfile(IccProfile.Srgb, 1, null!));

                // The colors of the paints are red, green and blue values
                AssertThrows<UnsupportedImageFeatureException>(canvas, static canvas => canvas.PaintProfile(IccProfile.SrgbGray, static _ => Assert.Fail("The callback ran.")));
                AssertThrows<UnsupportedImageFeatureException>(canvas, static canvas => canvas.PaintProfile(IccProfile.Cgats001CompatibleCmyk, 1, static (_, _) => Assert.Fail("The callback ran.")));

                // The options are those of a color conversion
                canvas.PaintProfile(IccProfile.Srgb, static inner => inner.Fill(new Rgba32(9, 8, 7)), new IccColorTransformOptions { Intent = IccRenderingIntent.RelativeColorimetric, BlackPointCompensation = false });
            },
            Ct);
        Assert.Equal(new Rgba32(9, 8, 7), image.Frames[0][0, 0]);

        // High dynamic range samples: the light of the white of a paint among them is not chosen implicitly
        foreach (var transfer in new byte[] { 16, 18 })
        {
            using var hdr = new Image<Rgba64>(1, 1);
            hdr.Metadata.Cicp = new CicpColorDescription(9, transfer, 0, fullRange: true);
            hdr.Draw(static canvas => AssertThrows<UnsupportedImageFeatureException>(canvas, static canvas => canvas.PaintProfile(IccProfile.Srgb, static _ => Assert.Fail("The callback ran."))), Ct);
            Assert.Equal(default, hdr.Frames[0][0, 0]);
        }

        // A profile that cannot label the pixels of the image, and one that cannot be a destination
        using var mislabeled = new Image<Rgba32>(1, 1);
        mislabeled.Metadata.IccProfile = IccProfile.SrgbGray;
        mislabeled.Draw(static canvas => AssertThrows<UnsupportedImageFeatureException>(canvas, static canvas => canvas.PaintProfile(IccProfile.Srgb, static _ => Assert.Fail("The callback ran."))), Ct);

        // A profile and the linear light label: the color space of the image is ambiguous
        using var ambiguous = new Image<Rgba32>(1, 1);
        ambiguous.Metadata.IccProfile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        ambiguous.Metadata.TransferFunction = ColorTransferFunction.Linear;
        ambiguous.Draw(static canvas => AssertThrows<UnsupportedImageFeatureException>(canvas, static canvas => canvas.PaintProfile(IccProfile.Srgb, static _ => Assert.Fail("The callback ran."))), Ct);

        // Samples labeled as linear sRGB: the red of sRGB at half its value is 0.2140 of the light of red
        using var linear = new Image<Rgba64>(1, 1);
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        linear.Draw(static canvas => canvas.PaintProfile(IccProfile.Srgb, static inner => inner.Fill(new Rgba64(32768, 0, 0))), Ct);
        Assert.InRange(linear.Frames[0][0, 0].R, (0.2140 * 65535) - 30, (0.2140 * 65535) + 30);
        Assert.Equal(0, linear.Frames[0][0, 0].G);
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
