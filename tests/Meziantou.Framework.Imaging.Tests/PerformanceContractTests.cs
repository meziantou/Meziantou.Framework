using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Memory contracts of the library: static row callbacks allocate nothing per row in steady state,
/// and sequential processing keeps the live pixel storage independent of the total frame count when results are disposed.
/// These are deterministic (allocation counts and accounted bytes), unlike timings, which are measured by the benchmarks.
/// </summary>
public sealed class PerformanceContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void StaticRowCallbacksAllocateNothingWhateverTheRowCount(int height)
    {
        using var image = new Image<Rgba32>(16, height);
        using var decoded = Image.Load<Rgba32>(EncodePng(16, height));
        var box = new StrongBox<long>();
        foreach (var frame in new[] { image.Frames[0], decoded.Frames[0] })
        {
            Assert.Equal(0, MeasureSteadyStateAllocations(() => Run(frame, box)));
        }

        Assert.True(box.Value > 0);

        static void Run(ImageFrame<Rgba32> frame, StrongBox<long> box)
        {
            frame.ProcessPixelRows(static pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    foreach (ref var pixel in pixels.GetRowSpan(y))
                    {
                        pixel = new Rgba32((byte)(255 - pixel.R), pixel.G, pixel.B, pixel.A);
                    }
                }
            });

            frame.ProcessPixelRows(box, static (pixels, state) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    state.Value += pixels.GetRowSpan(y)[0].R + 1;
                }
            });

            frame.ProcessPixelBytes(box, static (pixels, state) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    state.Value += pixels.GetRowSpan(y)[1];
                }
            });
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.RgbaFloat)]
    [InlineData(PixelFormat.RgbaHalf)]
    public void DrawingCallsAllocateNothingOnceTheScratchOfTheSessionIsRented(PixelFormat format)
    {
        // A session allocates a bounded number of small objects (the session, its scratch handles); the shapes drawn in
        // it allocate nothing once the scratch has grown to the largest of them, whatever their number
        using var image = DrawingTestSupport.Create(format, 64, 48);
        ((Image)image).AppendFrame();
        using var sprite = new Image<Rgba64>(24, 16, new Rgba64(60000, 30000, 1000, 40000));
        var state = new DrawingState(
            new SolidBrush(new Rgba32(10, 200, 30, 120)) { BlendMode = BlendMode.Hue },
            new LinearGradientBrush(new PointD(0, 0), new PointD(64, 48), [new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(0.5, new Rgba32(0, 255, 0, 80)), new GradientStop(1, new Rgba32(0, 0, 255))]) { SpreadMode = GradientSpreadMode.Reflect, BlendMode = BlendMode.SoftLight },
            new Pen(new RadialGradientBrush(new PointD(30, 20), 25, [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(255, 255, 255))]) { ColorSpace = GradientColorSpace.LinearSrgb }, 3.5) { LineJoin = LineJoin.Round, LineCap = LineCap.Round },
            new VectorPathBuilder().MoveTo(4, 40).CubicBezierTo(20, -10, 40, 60, 60, 6).QuadraticBezierTo(30, 30, 8, 8).AddCircle(new PointD(32, 24), 15).Build(),
            TextLayout.Create("OPQ\nR O", FontTestSupport.Load(FontSamples.Curves()), 14.5),
            TextLayout.Create("ABCDEFG", FontTestSupport.Load(FontSamples.Charstrings()), 9.25),
            TextLayout.Create("AB\u00C1H", FontTestSupport.Load(FontSamples.Boxes()), 12),
            CreateRichLayout(),
            TextLayout.Create("ABCDE FGH", FontTestSupport.WithVariations(FontTestSupport.Load(FontSamples.Variable(withAdvanceVariations: false)), "wght=775,wdth=150"), 8.5),
            TextLayout.Create("ABCD", FontTestSupport.WithVariations(FontTestSupport.Load(FontSamples.VariableCharstrings()), "wght=650"), 11),
            CreateColorLayout(),
            TextLayout.Create("AB CA", FontTestSupport.Load(FontSamples.BitmapBoxes(useColorBitmapTables: true)), 16),
            TextLayout.Create("ABCA", FontTestSupport.Load(FontSamples.BitmapBoxes()), 11.5),
            new TextPathOptions { Side = TextPathSide.Right, Alignment = TextPathAlignment.Center, StartOffset = -7.5, BaselineOffset = 2.25, Overflow = TextPathOverflow.Clip },
            new Pen(new SolidBrush(new Rgba32(0, 0, 0, 200)), 2.5) { DashPattern = [5, 2.5, 0, 2.5], DashOffset = 1.25, LineCap = LineCap.Round },
            new SweepGradientBrush(new PointD(30, 22), 15, 300, [new GradientStop(0, new Rgba32(255, 255, 0, 40)), new GradientStop(1, new Rgba32(0, 255, 255))]) { SpreadMode = GradientSpreadMode.Reflect, Transform = AffineTransform.CreateSkew(20, 0), ColorSpace = GradientColorSpace.Oklab },
            new RadialGradientBrush(new PointD(30, 20), 25, [new GradientStop(0, new Rgba32(255, 255, 255)), new GradientStop(1, new Rgba32(0, 0, 0, 0))]) { FocalPoint = new PointD(22, 14) },
            new RadialGradientBrush(new PointD(25, 14), 12, [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 0, 0))]) { CompositeMode = CompositeMode.DestinationOut },
            sprite.Frames[0],
            new ImageBrush(sprite.Frames[0]) { Transform = AffineTransform.CreateRotation(20).Then(AffineTransform.CreateScale(1.5, 0.75)), WrapMode = ImageWrapMode.Reflect, Opacity = 0.75, CompositeMode = CompositeMode.DestinationOver },
            new PathGradientBrush([new PointD(4, 2), new PointD(58, 6), new PointD(50, 44), new PointD(30, 30), new PointD(6, 40)], [new Rgba32(255, 0, 0), new Rgba32(0, 255, 0, 90), new Rgba32(0, 0, 255), new Rgba32(255, 255, 0), new Rgba32(0, 255, 255)], new Rgba32(255, 255, 255)) { Transform = AffineTransform.CreateRotation(10, new PointD(32, 24)), Opacity = 0.5 },
            new StrongBox<long>(long.MaxValue));

        // Tiered compilation running concurrently can attribute a few bytes to this thread, and the many methods behind a
        // drawing take a while to reach their final tier: best of many rounds
        for (var round = 0; round < 40 && state.Allocated.Value != 0; round++)
        {
            image.Draw(state, static (canvas, state) =>
            {
                for (var i = 0; i < 3; i++)
                {
                    Paint(canvas, state);
                }

                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 20; i++)
                {
                    Paint(canvas, state);
                }

                state.Allocated.Value = Math.Min(state.Allocated.Value, GC.GetAllocatedBytesForCurrentThread() - before);
            });
        }

        Assert.Equal(0, state.Allocated.Value);

        static void Paint(DrawingCanvas canvas, DrawingState state)
        {
            canvas.Clear(new Rgba32(10, 20, 30, 40));
            canvas.Fill(state.Gradient);
            canvas.FillRectangle(state.Eraser, new RectangleD(10.5, 4, 30, 20.25));
            canvas.FillRectangle(state.Sweep, new RectangleD(2, 2, 50, 40));
            canvas.FillEllipse(state.Focal, new PointD(30, 20), 25, 15);
            canvas.FillRectangle(state.PathGradient, new RectangleD(1.5, 0.25, 60, 46));
            canvas.Fill(state.Solid, state.Path, FillRule.EvenOdd);
            canvas.Stroke(state.Pen, state.Path);
            canvas.FillRectangle(state.Solid, new RectangleD(3.5, 2.25, 40, 30));
            canvas.StrokeRectangle(state.Pen, new Rectangle(5, 5, 20, 20));
            canvas.FillEllipse(state.Gradient, new PointD(20, 20), 18, 9);
            canvas.StrokeCircle(state.Pen, new PointD(40, 30), 12);
            canvas.StrokeLine(state.Pen, new PointD(1, 1), new PointD(60, 44));
            canvas.StrokeBezier(state.Pen, new PointD(2, 2), new PointD(30, 46), new PointD(62, 3));
            canvas.StrokeBezier(state.Pen, new PointD(2, 46), new PointD(20, 0), new PointD(40, 47), new PointD(62, 20));
            canvas.FillRoundedRectangle(state.Gradient, new RectangleD(3.5, 2.25, 40, 30), 6.5);
            canvas.StrokeRoundedRectangle(state.Pen, new Rectangle(5, 5, 20, 20), 100);
            canvas.FillRoundedRectangle(state.Solid, new RectangleD(2.5, 3, 30, 20), new CornerRadii(4, 0, 9.5, 2));
            canvas.StrokeRoundedRectangle(state.Pen, new Rectangle(5, 5, 20, 20), new CornerRadii(30, 1, 30, 1));
            canvas.FillPie(state.Solid, new PointD(30, 20), 17.5, 20, 250);
            canvas.StrokeArc(state.Pen, new PointD(30, 20), 17.5, -30, -400);
            canvas.Stroke(state.DashedPen, state.Path);
            canvas.StrokeRectangle(state.DashedPen, new RectangleD(-20, 5.5, 200, 20));
            canvas.StrokeText(state.DashedPen, state.Text, new PointD(1, 10.5));
            canvas.DrawImage(state.Sprite, new Point(3, 4));
            canvas.DrawImage(state.Sprite, new PointD(30.5, 20.25), 0.5);
            canvas.DrawImage(state.Sprite, new Rectangle(4, 2, 10, 8), new RectangleD(-5, 6.5, 80, 30), 0.9);
            canvas.DrawImage(state.Sprite, AffineTransform.CreateRotation(30, new PointD(5, 5)).Then(AffineTransform.CreateTranslation(20, 10)), 0.8);
            canvas.DrawImage(state.Sprite, new RectangleD(2.5, 3, 5, 4), ImageSampling.Smooth);
            canvas.DrawImage(state.Sprite, AffineTransform.CreateRotation(30, new PointD(5, 5)).Then(AffineTransform.CreateScale(0.4, 0.3)), ImageSampling.Smooth, 0.8);
            canvas.Fill(state.Tile, state.Path);
            canvas.FillText(state.Tile, state.Text, new PointD(2.5, 3));
            canvas.Layer(0.6, state, static (layer, state) =>
            {
                layer.Fill(state.Gradient, state.Path);
                layer.FillText(state.Solid, state.ColorText, new PointD(1, 20));
                layer.Clip(new RectangleD(8.5, 4, 40, 30), state, static (inner, state) => inner.Layer(1, state, static (mask, state) => mask.Stroke(state.Pen, state.Path), CompositeMode.DestinationIn));
            });
            canvas.Layer(0.9, state, static (layer, state) => layer.FillEllipse(state.Focal, new PointD(30, 20), 25, 15), CompositeMode.SourceAtop, BlendMode.Overlay);
            canvas.Shadow(new Rgba32(0, 0, 0, 100), new PointD(2.5, 3), 1.75, state, static (shadowed, state) => shadowed.FillText(state.Solid, state.Text, new PointD(4, 6)));
            canvas.Transform(AffineTransform.CreateRotation(20, new PointD(32, 24)).Then(AffineTransform.CreateScale(1.25, 0.8)), state, static (turned, state) =>
            {
                turned.Fill(state.Gradient, state.Path);
                turned.Stroke(state.Pen, state.Path);
                turned.Stroke(state.DashedPen, state.Path);
                turned.FillRoundedRectangle(state.Sweep, new RectangleD(3.5, 2.25, 40, 30), 6.5);
                turned.FillText(state.Solid, state.RichText, new PointD(2, 3));
                turned.FillText(state.Gradient, state.ColorText, new PointD(1, 20));
                turned.StrokeText(state.Pen, state.CffText, new PointD(3, 30));
                turned.FillText(state.Solid, state.Text, state.Path, state.PathOptions);
                turned.DrawImage(state.Sprite, new RectangleD(-5, 6.5, 30, 20), ImageSampling.Smooth, 0.9);
                turned.Fill(state.Tile, state.Path);
                turned.Clip(state.Path, state, static (clipped, state) => clipped.Transform(AffineTransform.CreateTranslation(2.5, -1), state, static (moved, state) => moved.Fill(state.Focal)));
                turned.Shadow(new Rgba32(0, 0, 0, 100), new PointD(2, 1), 0.75, state, static (shadowed, state) => shadowed.FillCircle(state.Solid, new PointD(20, 20), 6));
            });
            // In perspective: the shapes are cut where they have no image, and their curves cut for what is seen of them
            canvas.Transform(new ProjectiveTransform(1.1, 0.05, 0.004, -0.1, 0.9, 0.002, 3, 2, 1), state, static (plane, state) =>
            {
                plane.Fill(state.Gradient, state.Path);
                plane.Stroke(state.Pen, state.Path);
                plane.Stroke(state.DashedPen, state.Path);
                plane.FillRoundedRectangle(state.Sweep, new RectangleD(3.5, 2.25, 40, 30), 6.5);
                plane.FillText(state.Solid, state.RichText, new PointD(2, 3));
                plane.StrokeText(state.Pen, state.CffText, new PointD(3, 30));
                plane.DrawImage(state.Sprite, new RectangleD(-5, 6.5, 30, 20), ImageSampling.Smooth, 0.9);
                plane.Fill(state.Tile, state.Path);
                plane.Clip(state.Path, state, static (clipped, state) => clipped.Fill(state.Focal));
            });
            canvas.Clip(new Rectangle(10, 8, 30, 24), state, static (clipped, state) => clipped.Shadow(new Rgba32(255, 255, 0), default, 0.8, state, static (glow, state) => glow.Stroke(state.Pen, state.Path)));
            canvas.Clip(state.Path, state, static (clipped, state) =>
            {
                clipped.DrawImage(state.Sprite, new RectangleD(1.5, 2, 60, 44), 0.6);
                clipped.Fill(state.Gradient);
                clipped.FillText(state.Gradient, state.ColorText, new PointD(1, 20));
                clipped.Clip(new Rectangle(8, 4, 40, 30), state, static (inner, state) => inner.Stroke(state.Pen, state.Path));
                clipped.Clip(new RectangleD(8.5, 4.25, 40, 30), state, static (inner, state) => inner.FillText(state.Solid, state.RichText, new PointD(1.25, 0.5)));
            });
            ReadOnlySpan<PointD> points = [new(2, 3), new(60, 10), new(20, 44), new(40, 1), new(5, 40)];
            canvas.FillPolygon(state.Solid, points, FillRule.EvenOdd);
            canvas.StrokePolygon(state.Pen, points);
            canvas.StrokePolyline(state.Pen, points);

            // Text: quadratic outlines, charstrings with subroutines, composite glyphs and kerning
            canvas.FillText(state.Gradient, state.Text, new PointD(2.5, 3));
            canvas.StrokeText(state.Pen, state.Text, new PointD(1, 10.5));
            canvas.FillText(state.Solid, state.CffText, new PointD(4, 20));
            canvas.StrokeText(state.Pen, state.CffText, new PointD(4, 30));
            canvas.FillText(state.Solid, state.CompositeText, new PointD(0.5, 30.25));

            // A wrapped and justified paragraph of several paints, a gradient among them, with decorations and two fonts
            canvas.FillText(state.Solid, state.RichText, new PointD(1.25, 0.5));
            canvas.StrokeText(state.Pen, state.RichText, new PointD(3, 2));

            // Instances of variable fonts: the points of the glyphs are varied, and the charstrings blended, at each draw
            canvas.FillText(state.Solid, state.VariableText, new PointD(1, 2));
            canvas.StrokeText(state.Pen, state.VariableText, new PointD(2, 20));
            canvas.FillText(state.Gradient, state.BlendedText, new PointD(3, 30));

            // Color glyphs: layers, gradients, a clip within a clip and a composition, with the color of the text from a
            // solid brush and from a gradient, at a point and along a path
            canvas.FillText(state.Solid, state.ColorText, new PointD(2, 1.5));
            canvas.FillText(state.Gradient, state.ColorText, new PointD(1, 20));
            canvas.FillText(state.Gradient, state.ColorText, state.Path, state.PathOptions);

            // Glyphs that are images: at the size of a strike and scaled, at a point and along a path
            canvas.FillText(state.Solid, state.BitmapText, new PointD(3, 2));
            canvas.FillText(state.Gradient, state.ScaledBitmapText, new PointD(1.25, 14.5));
            canvas.FillText(state.Solid, state.ScaledBitmapText, state.Path, state.PathOptions);

            // Along a path of curves and of two contours, on both sides: the path is measured in the scratch of the session
            canvas.FillText(state.Solid, state.Text, state.Path);
            canvas.FillText(state.Gradient, state.RichText, state.Path, state.PathOptions);
            canvas.StrokeText(state.Pen, state.CffText, state.Path, state.PathOptions);
        }

        static TextLayout CreateColorLayout()
        {
            var line = new FontColorLine(1, (0, 0, 1), (0.5, 0xFFFF, 0.5), (1, 1, 1));
            var font = FontTestSupport.Load(FontSamples.ColorPaints(
                ('D', new FontColorPaint.Composite(
                    new FontColorPaint.Glyph(3, new FontColorPaint.Sweep(line, 512, 384, 30, 300)),
                    14,
                    new FontColorPaint.Glyph(4, new FontColorPaint.Radial(line, 400, 300, 20, 600, 400, 300)))),
                ('E', new FontColorPaint.Glyph(3, new FontColorPaint.Rotate(new FontColorPaint.Glyph(4, new FontColorPaint.Linear(line, 128, 0, 600, 200, 300, 700)), 30, 512, 384)))));
            return TextLayout.Create("ADBE C", new TextStyle(font, 10.5) { ColorPaletteIndex = 1 });
        }

        static TextLayout CreateRichLayout()
        {
            var boxes = FontTestSupport.Load(FontSamples.Boxes());
            var curves = FontTestSupport.Load(FontSamples.Curves());
            var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(60, 40), [new GradientStop(0, new Rgba32(0, 0, 255)), new GradientStop(1, new Rgba32(255, 255, 0, 100))]);
            var text = new RichTextBuilder()
                .Append("AB A", new TextStyle(boxes, 9) { Brush = new SolidBrush(new Rgba32(200, 0, 0, 180)), Decorations = TextDecorations.Underline })
                .Append("B OP ", new TextStyle(new FontStack(boxes, curves), 7.5) { Brush = gradient, Decorations = TextDecorations.Strikethrough, DecorationBrush = new SolidBrush(new Rgba32(0, 0, 0)) })
                .Append("QA BA", new TextStyle(curves, 11) { LetterSpacing = 0.5 })
                .Build();
            return TextLayout.Create(text, new TextLayoutOptions { MaxWidth = 50, Alignment = TextAlignment.Justify });
        }
    }

    public static TheoryData<string> AnimationFormats() => ["apng", "gif"];

    [Theory]
    [MemberData(nameof(AnimationFormats))]
    public void SequentialTransformsKeepLivePixelStorageIndependentOfTheFrameCount(string format)
    {
        // Read a frame, resize it, write it, dispose it: the accounted peak (reader, returned images, resize scratch and writer)
        // must be the same for 4 and 40 frames
        var few = MeasureSequentialPeak(CreateAnimation(format, frames: 4), format, reuseDestination: false);
        var many = MeasureSequentialPeak(CreateAnimation(format, frames: 40), format, reuseDestination: false);
        Assert.Equal(few, many);

        // Same with one reused destination image (ReadFrameInto, processed in place)
        Assert.Equal(MeasureSequentialPeak(CreateAnimation(format, frames: 4), format, reuseDestination: true), MeasureSequentialPeak(CreateAnimation(format, frames: 40), format, reuseDestination: true));

        // The measure is meaningful: eager decoding retains every frame, so its peak grows with the frame count
        Assert.True(MeasureEagerPeak(CreateAnimation(format, frames: 40)) > 5 * MeasureEagerPeak(CreateAnimation(format, frames: 4)));
    }

    [Theory]
    [InlineData(PixelFormat.La16, PixelFormat.Rgba32, 1, 2)]
    [InlineData(PixelFormat.La32, PixelFormat.Rgba64, 1, 2)]
    [InlineData(PixelFormat.Rgb48, PixelFormat.Rgba64, 3, 4)]
    [InlineData(PixelFormat.A8, PixelFormat.Rgba32, 1, 4)]
    [InlineData(PixelFormat.Bgr565, PixelFormat.Rgb24, 2, 3)]
    [InlineData(PixelFormat.Rgba1010102, PixelFormat.Rgba64, 1, 2)]
    public void CompactFormatsStoreFramesInAFractionOfTheMemory(PixelFormat compact, PixelFormat wide, int numerator, int denominator)
    {
        // The pixel storage of a frame is its pixels, whatever the format: gray with alpha takes half the memory of RGBA,
        // 16-bit RGB three quarters, an alpha mask a quarter, 5-6-5 bits two thirds of Rgb24 and 10-bit colors half of
        // Rgba64 (the slabs round the storage up a little)
        using var compactImage = DrawingTestSupport.Create(compact, 256, 192);
        using var wideImage = DrawingTestSupport.Create(wide, 256, 192);
        Assert.Equal(256 * PixelFormats.GetBytesPerPixel(compact), compactImage.Frames[0].Storage.RowLength);
        var compactBytes = compactImage.Owner.Scope.GetLiveBytes(AllocationKind.ImagePixels);
        var wideBytes = wideImage.Owner.Scope.GetLiveBytes(AllocationKind.ImagePixels);
        Assert.InRange(compactBytes, 256L * 192 * PixelFormats.GetBytesPerPixel(compact), (long)(wideBytes * numerator / denominator * 1.15));
    }

    [Theory]
    [InlineData(PixelFormat.La16, PixelFormat.Rgba32)]
    [InlineData(PixelFormat.La32, PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Rgb48, PixelFormat.Rgba64)]
    public void LoadingIntoTheDefaultCompactFormatPeaksLowerThanLoadingAsRgba(PixelFormat compact, PixelFormat wide)
    {
        // A file whose default is a compact format is decoded into it, not into RGBA first: the peak of the load is the
        // image plus a few rows, well below the one of a load as RGBA
        using var source = DrawingTestSupport.CreateRandom(compact, 512, 384, new Random(42).Next);
        using var stream = new MemoryStream();
        source.Save(stream, new PngEncoder());
        var data = stream.ToArray();

        var compactPeak = MeasurePeak(() => Image.Load(data), compact);
        var widePeak = MeasurePeak(() => wide == PixelFormat.Rgba32 ? Image.Load<Rgba32>(data) : Image.Load<Rgba64>(data), wide);
        var expectedRatio = (double)PixelFormats.GetBytesPerPixel(compact) / PixelFormats.GetBytesPerPixel(wide);
        Assert.InRange((double)compactPeak / widePeak, 0, expectedRatio + 0.1);

        static long MeasurePeak(Func<Image> load, PixelFormat expected)
        {
            using var tracking = new AllocationTracking();
            using (var image = load())
            {
                Assert.Equal(expected, image.PixelFormat);
            }

            return tracking.Scopes.Sum(scope => scope.GetDiagnostics().PeakLiveBytes);
        }
    }

    [Theory]
    [InlineData(PixelFormat.RgbaFloat)]
    [InlineData(PixelFormat.RgbaHalf)]
    public void FloatingPointFilteringAllocatesNothingPerPixel(PixelFormat format)
    {
        // A resize, a convolution and a blur allocate their plans, a few objects whatever the size of the frames; the
        // pixels, the rows and the scratch come from the pool. Frames 63 times larger (2,976 more pixels, 10 runs) allocate
        // less than one byte per 8 more pixels: an allocation per pixel or per row would be far more. The bytes are not
        // compared exactly, because the parallel work of the larger frames can allocate a few bytes more on a loaded machine.
        var (small, large) = format == PixelFormat.RgbaFloat ? (Measure<RgbaFloat>(8, 6), Measure<RgbaFloat>(64, 48)) : (Measure<RgbaHalf>(8, 6), Measure<RgbaHalf>(64, 48));
        Assert.InRange(large - small, long.MinValue, 10 * ((64 * 48) - (8 * 6)) / 8);

        static long Measure<TPixel>(int width, int height)
            where TPixel : unmanaged
        {
            // Translucent pixels, so that the colors are filtered premultiplied
            using var source = new Image<Rgba64>(width, height);
            source.Frames[0].ProcessPixelRows(static pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba64((ushort)(x * 1000), (ushort)(y * 1300), (ushort)((x + y) * 500), (ushort)(30000 + (x * 100)));
                    }
                }
            });

            // A pool of its own, so that the buffers that other tests return to the shared pool do not change what it keeps.
            // Labeled as linear light: the linear working space filters the values as they are, whatever the ringing does
            using var converted = source.CloneAs<TPixel>();
            var pixels = new TPixel[width * height];
            converted.Frames[0].CopyPixelDataTo(pixels);
            using var image = new Image<TPixel>(ImageConfiguration.Default, new Size(width, height), new AllocationScope(ImageConfiguration.Default.Limits, new SlabPool()), layoutOptions: null);
            image.Frames[0].ProcessPixelRows(pixels, static (accessor, pixels) =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    pixels.AsSpan(y * accessor.Width, accessor.Width).CopyTo(accessor.GetRowSpan(y));
                }
            });

            image.Metadata.TransferFunction = Metadata.ColorTransferFunction.Linear;
            var up = new ResizeOptions(width * 2, height + 3) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Lanczos3, WorkingSpace = ResizeWorkingSpace.LinearSrgb };
            var down = new ResizeOptions(width, height) { Mode = ResizeMode.Stretch, Filter = ResamplingFilter.Bicubic };
            var sharpen = new ConvolutionOptions(new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]));
            for (var i = 0; i < 3; i++)
            {
                Run(image, up, down, sharpen);
            }

            // The fewest bytes of several rounds: tiered compilation can attribute a few bytes to this thread
            var allocated = long.MaxValue;
            for (var round = 0; round < 10; round++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 10; i++)
                {
                    Run(image, up, down, sharpen);
                }

                allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            return allocated;
        }

        static void Run(Image image, ResizeOptions up, ResizeOptions down, ConvolutionOptions sharpen)
        {
            image.Resize(up, Ct);
            image.Resize(down, Ct);
            image.Convolve(sharpen, Ct);
            image.GaussianBlur(1.5, cancellationToken: Ct);
        }
    }

    [Fact]
    public void ColorTransformsAllocateNothingPerConversion()
    {
        // A transform is built once; converting samples allocates nothing, whatever the sample type, the profile model
        // (matrix-based with the 8-bit input tables, table-based with black point compensation) or the number of colors
        // (enough for the 8-bit output tables, which are built once)
        var cmyk = IccTestProfiles.CmykLabLut16();
        var toDisplayP3 = IccColorTransform.Create(Metadata.IccProfile.Srgb, IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve()));
        var toCmyk = IccColorTransform.Create(Metadata.IccProfile.Srgb, cmyk);
        var fromCmyk = IccColorTransform.Create(cmyk, Metadata.IccProfile.SrgbGray);

        // Gamut mapping (matrix-based and table-based destinations) and a sequence with an abstract profile, on saturated
        // colors that the mapping changes
        var mapping = new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma };
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var mappedToSrgb = IccColorTransform.Create(displayP3, Metadata.IccProfile.Srgb, mapping);
        var mappedToCmyk = IccColorTransform.Create([displayP3, IccTestProfiles.LabAbstractLutAToB(), cmyk], mapping);
        foreach (var colors in new[] { 1, 5000, IccPipeline.ByteOutputTablesThreshold })
        {
            var bytes = new byte[colors * 3];
            var words = new ushort[colors * 3];
            var floats = new float[colors * 3];
            var inks = new byte[colors * 4];
            var gray = new byte[colors];
            var saturated = new byte[colors * 3];
            var mapped = new byte[colors * 3];
            for (var i = 0; i < saturated.Length; i++)
            {
                saturated[i] = (byte)(i % 3 == i / 3 % 3 ? 255 : 0);
            }

            Assert.Equal(0, MeasureSteadyStateAllocations(() =>
            {
                toDisplayP3.Convert(bytes, bytes);
                toDisplayP3.Convert(words, words);
                toDisplayP3.Convert(floats, floats);
                toCmyk.Convert(bytes, inks);
                fromCmyk.Convert(inks, gray);
                mappedToSrgb.Convert(saturated, mapped);
                mappedToCmyk.Convert(saturated, inks);
            }));
        }
    }

    [Fact]
    public void FloatingPointConversionsAllocateNothingPerRow()
    {
        // Every pair with a floating-point format, with the policies that clip and tone map: nothing per pixel or per row
        var integers = new Rgba64[512];
        var floats = new RgbaFloat[512];
        var halves = new RgbaHalf[512];
        var bytes = new Rgb24[512];
        for (var i = 0; i < floats.Length; i++)
        {
            floats[i] = new RgbaFloat(i / 64f, 0.5f, -0.25f, 0.75f);
        }

        var toneMapping = FloatSampleConversion.Create(new PixelConversionOptions { ToneMapping = HdrToneMappingOptions.Default, OutOfRangeSamples = OutOfRangeSampleHandling.Clip }, peak: 8);
        var background = new Rgba64(0, 0, 0);
        Assert.Equal(0, MeasureSteadyStateAllocations(() =>
        {
            PixelConverter.ConvertRow<RgbaFloat, Rgba64>(floats, integers, floats: toneMapping);
            PixelConverter.ConvertRow<RgbaFloat, Rgb24>(floats, bytes, background, floats: FloatSampleConversion.Clipping);
            PixelConverter.ConvertRow<RgbaFloat, RgbaHalf>(floats, halves);
            PixelConverter.ConvertRow<RgbaHalf, RgbaFloat>(halves, floats);
            PixelConverter.ConvertRow<Rgba64, RgbaFloat>(integers, floats);
            PixelConverter.ConvertRow<Rgb24, RgbaHalf>(bytes, halves);
        }));
    }

    [Fact]
    public void ColorValueOperationsAllocateNothing()
    {
        // The value types, their conversions through the three kinds of color spaces, the differences, the interpolations
        // and the parsing of CSS notations: everything but the formatting, which returns a string
        var displayP3 = RgbColorSpace.FromCicp(new Metadata.CicpColorDescription(12, 13, 0, fullRange: true));
        var profile = RgbColorSpace.FromIccProfile(Metadata.IccProfile.Srgb);
        var color = new Rgba32(201, 37, 119);
        var wide = new Rgba64(1234, 54321, 32100, 65535);
        var sum = 0.0;
        Assert.Equal(0, MeasureSteadyStateAllocations(() =>
        {
            ReadOnlySpan<RgbColorSpace> spaces = [RgbColorSpace.Srgb, displayP3, profile];
            foreach (var space in spaces)
            {
                var xyz = CieXyz.FromRgb(color, space);
                var lab = CieLab.FromRgb(wide, space);
                var lch = CieLch.FromRgb(color, space);
                var luv = CieLuv.FromRgb(color, space);
                var oklab = Oklab.FromRgb(color, space);
                var oklch = Oklch.FromRgb(wide, space);
                sum += xyz.ToRgba32(space).R + lab.ToRgba64(space).G + lch.ToRgba32(space).B + luv.ToRgba64(space).R + oklab.ToRgba32(space).G + oklch.ToRgba64(space).B;
                sum += ColorDifference.DeltaE76(lab, lch.ToLab()) + ColorDifference.DeltaE94(lab, CieLab.FromXyz(xyz)) + ColorDifference.DeltaE2000(lab, CieLab.FromXyz(luv.ToXyz()));
                sum += ColorDifference.DeltaEOK(oklab, oklch.ToOklab()) + Oklab.Lerp(oklab, Oklab.FromXyz(xyz), 0.3).L + Oklch.Lerp(oklch, oklab.ToOklch(), 0.7, HueInterpolation.Longer).H;
                sum += xyz.Adapt(CieXyz.D50, CieXyz.D65).X + lab.ToXyz(CieXyz.D65).Y + CieLuv.FromXyz(xyz, CieXyz.D65).U;
            }

            sum += Hsl.FromRgb(color).ToRgba32().R + Hsv.FromRgb(wide).ToRgba64().G + Hwb.FromRgb(color).ToRgba32().B + Cmyk.FromRgb(wide).ToRgba64().R;
            sum += CieLab.TryParse("lab(52.2345% 40.1645 59.9971 / 0.5)", out var parsed, out var alpha) ? parsed.L + alpha : 0;
            sum += Oklch.TryParse("oklch(70% 0.1 0.5turn)", out var parsedOklch, out _) ? parsedOklch.H : 0;
            sum += Hsl.TryParse("hsla(120, 100%, 25%, 0.5)", out var parsedHsl, out _) ? parsedHsl.L : 0;
            sum += Rgba32.TryParseCss("hwb(120 20% 30%)", out var parsedRgba) ? parsedRgba.G : 0;
        }));
        Assert.True(sum > 0);
    }

    private static long MeasureSteadyStateAllocations(Action action)
    {
        for (var i = 0; i < 3; i++)
        {
            action();
        }

        // Tiered compilation running concurrently can occasionally attribute a few bytes to this thread: best of several rounds
        var allocated = long.MaxValue;
        for (var round = 0; round < 5 && allocated != 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20; i++)
            {
                action();
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return allocated;
    }

    private static long MeasureSequentialPeak(byte[] input, string format, bool reuseDestination)
    {
        using var tracking = new AllocationTracking();

        // The read buffer of a seekable stream is bounded by its length, so a shorter input buffers less: a non-seekable
        // stream keeps the same buffer whatever the frame count
        using (var source = new TestInputStream(input) { Seekable = false })
        using (var output = new MemoryStream())
        using (var reader = Image.OpenReader<Rgba32>(source))
        {
            // The images returned by the reader share its scope; the reused destination has its own (one scope per role, so
            // the sum of the scope peaks bounds the simultaneous live bytes)
            var info = reader.Info;
            var size = reuseDestination ? info.Size : new Size(info.Width / 2, info.Height / 2);
            using var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(size)
            {
                Encoder = format == "gif" ? new GifEncoder() : new PngEncoder { AnimationMode = PngAnimationMode.Animated },
                Animation = info.Animation,
                ExpectedFrameCount = info.FrameCount,
            });

            if (reuseDestination)
            {
                using var destination = new Image<Rgba32>(info.Width, info.Height);
                while (reader.ReadFrameInto(destination))
                {
                    destination.Grayscale(Ct);
                    writer.WriteFrame(destination.Frames[0]);
                }
            }
            else
            {
                var resize = new ResizeOptions(size) { Mode = ResizeMode.Stretch };
                while (reader.ReadFrame() is { } frame)
                {
                    using (frame)
                    {
                        frame.Resize(resize, Ct);
                        writer.WriteFrame(frame.Frames[0]);
                    }
                }
            }

            writer.Complete();
        }

        // Everything was disposed: every scope is back to zero
        Assert.Equal(0, tracking.LiveBytes);
        return tracking.Scopes.Sum(scope => scope.GetDiagnostics().PeakLiveBytes);
    }

    private static long MeasureEagerPeak(byte[] input)
    {
        using var tracking = new AllocationTracking();
        using (var image = Image.Load<Rgba32>(input))
        {
            Assert.True(image.Frames.Count > 1);
        }

        return tracking.Scopes.Sum(scope => scope.GetDiagnostics().PeakLiveBytes);
    }

    private static byte[] CreateAnimation(string format, int frames)
    {
        using var image = new Image<Rgba32>(64, 48);
        for (var i = 0; i < frames; i++)
        {
            var frame = i == 0 ? image.Frames[0] : image.AppendFrame();
            frame.Metadata.Duration = FrameDuration.FromMilliseconds(40);
            frame.ProcessPixelRows(i, static (pixels, index) =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)((x * 4) + index), (byte)(y * 5), (byte)(index * 9));
                    }
                }
            });
        }

        using var stream = new MemoryStream();
        image.Save(stream, format == "gif" ? new GifEncoder() : new PngEncoder { AnimationMode = PngAnimationMode.Animated });
        return stream.ToArray();
    }

    private static byte[] EncodePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(10, 20, 30));
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    [Fact]
    public void GlyphRunsAllocateNothingOnceTheirScratchAndTheirCacheAreWarm()
    {
        // The masks of the glyphs on the pixels, uncached (computed in the scratch of the session) and cached (with a
        // synthetic bold), the shapes of glyphs on a turned canvas, a stroke, and the glyphs of colors and of images,
        // whose display lists only a cache keeps
        using var image = DrawingTestSupport.Create(PixelFormat.Rgba32, 64, 48);
        var state = new GlyphState(
            FontTestSupport.Load(FontSamples.Boxes()),
            FontTestSupport.Load(FontSamples.ColorBoxes()),
            FontTestSupport.Load(FontSamples.BitmapBoxes()),
            [new(FontSamples.BoxA, new PointD(2.3, 14)), new(FontSamples.TriangleD, new PointD(18.6, 14.5)), new(FontSamples.FrameE, new PointD(27, 30.2)), new(FontSamples.BoxA, new PointD(40.3, 14))],
            [new(FontSamples.ColorA, new PointD(2, 30)), new(FontSamples.ColorB, new PointD(20.5, 30))],
            [new(FontSamples.BitmapA, new PointD(30, 45)), new(FontSamples.BitmapB, new PointD(46, 45))],
            new GlyphRunOptions { Cache = new GlyphCache(1 << 20), Synthesis = FontSynthesis.Bold },
            new SolidBrush(new Rgba32(10, 200, 30, 120)),
            new LinearGradientBrush(new PointD(0, 0), new PointD(64, 48), [new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 255))]),
            new Pen(new SolidBrush(new Rgba32(0, 0, 0)), 1.25),
            new StrongBox<long>(long.MaxValue));
        for (var round = 0; round < 40 && state.Allocated.Value != 0; round++)
        {
            image.Draw(state, static (canvas, state) =>
            {
                for (var i = 0; i < 3; i++)
                {
                    Paint(canvas, state);
                }

                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 20; i++)
                {
                    Paint(canvas, state);
                }

                state.Allocated.Value = Math.Min(state.Allocated.Value, GC.GetAllocatedBytesForCurrentThread() - before);
            });
        }

        Assert.Equal(0, state.Allocated.Value);

        static void Paint(DrawingCanvas canvas, GlyphState state)
        {
            canvas.FillGlyphs(state.Solid, state.Boxes, 16, state.Glyphs);
            canvas.FillGlyphs(state.Gradient, state.Boxes, 16, state.Glyphs, state.Options);
            canvas.FillGlyphs(state.Gradient, state.Colors, 16, state.ColorGlyphs, state.Options);
            canvas.FillGlyphs(state.Solid, state.Bitmaps, 16, state.BitmapGlyphs, state.Options);
            canvas.StrokeGlyphs(state.Pen, state.Boxes, 16, state.Glyphs);
            canvas.Transform(AffineTransform.CreateRotation(20, new PointD(32, 24)), state, static (turned, state) => turned.FillGlyphs(state.Solid, state.Boxes, 16, state.Glyphs, state.Options));
        }
    }

    private sealed record GlyphState(Font Boxes, Font Colors, Font Bitmaps, PositionedGlyph[] Glyphs, PositionedGlyph[] ColorGlyphs, PositionedGlyph[] BitmapGlyphs, GlyphRunOptions Options, SolidBrush Solid, GradientBrush Gradient, Pen Pen, StrongBox<long> Allocated);

    private sealed record DrawingState(SolidBrush Solid, GradientBrush Gradient, Pen Pen, VectorPath Path, TextLayout Text, TextLayout CffText, TextLayout CompositeText, TextLayout RichText, TextLayout VariableText, TextLayout BlendedText, TextLayout ColorText, TextLayout BitmapText, TextLayout ScaledBitmapText, TextPathOptions PathOptions, Pen DashedPen, SweepGradientBrush Sweep, RadialGradientBrush Focal, Brush Eraser, ImageFrame Sprite, ImageBrush Tile, PathGradientBrush PathGradient, StrongBox<long> Allocated);
}
