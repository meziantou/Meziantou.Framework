using System.Runtime.CompilerServices;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
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
    public void DrawingCallsAllocateNothingOnceTheScratchOfTheSessionIsRented(PixelFormat format)
    {
        // A session allocates a bounded number of small objects (the session, its scratch handles); the shapes drawn in
        // it allocate nothing once the scratch has grown to the largest of them, whatever their number
        using var image = DrawingTestSupport.Create(format, 64, 48);
        ((Image)image).AppendFrame();
        var state = new DrawingState(
            new SolidBrush(new Rgba32(10, 200, 30, 120)),
            new LinearGradientBrush(new PointD(0, 0), new PointD(64, 48), [new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(0.5, new Rgba32(0, 255, 0, 80)), new GradientStop(1, new Rgba32(0, 0, 255))]) { SpreadMode = GradientSpreadMode.Reflect },
            new Pen(new RadialGradientBrush(new PointD(30, 20), 25, [new GradientStop(0, new Rgba32(0, 0, 0)), new GradientStop(1, new Rgba32(255, 255, 255))]), 3.5) { LineJoin = LineJoin.Round, LineCap = LineCap.Round },
            new VectorPathBuilder().MoveTo(4, 40).CubicBezierTo(20, -10, 40, 60, 60, 6).QuadraticBezierTo(30, 30, 8, 8).AddCircle(new PointD(32, 24), 15).Build(),
            TextLayout.Create("OPQ\nR O", FontTestSupport.Load(FontSamples.Curves()), 14.5),
            TextLayout.Create("ABCDEFG", FontTestSupport.Load(FontSamples.Charstrings()), 9.25),
            TextLayout.Create("AB\u00C1H", FontTestSupport.Load(FontSamples.Boxes()), 12),
            CreateRichLayout(),
            TextLayout.Create("ABCDE FGH", FontTestSupport.WithVariations(FontTestSupport.Load(FontSamples.Variable(withAdvanceVariations: false)), "wght=775,wdth=150"), 8.5),
            TextLayout.Create("ABCD", FontTestSupport.WithVariations(FontTestSupport.Load(FontSamples.VariableCharstrings()), "wght=650"), 11),
            new TextPathOptions { Side = TextPathSide.Right, Alignment = TextPathAlignment.Center, StartOffset = -7.5, BaselineOffset = 2.25, Overflow = TextPathOverflow.Clip },
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
            canvas.Fill(state.Gradient);
            canvas.Fill(state.Solid, state.Path, FillRule.EvenOdd);
            canvas.Stroke(state.Pen, state.Path);
            canvas.FillRectangle(state.Solid, new RectangleD(3.5, 2.25, 40, 30));
            canvas.StrokeRectangle(state.Pen, new Rectangle(5, 5, 20, 20));
            canvas.FillEllipse(state.Gradient, new PointD(20, 20), 18, 9);
            canvas.StrokeCircle(state.Pen, new PointD(40, 30), 12);
            canvas.StrokeLine(state.Pen, new PointD(1, 1), new PointD(60, 44));
            canvas.StrokeBezier(state.Pen, new PointD(2, 2), new PointD(30, 46), new PointD(62, 3));
            canvas.StrokeBezier(state.Pen, new PointD(2, 46), new PointD(20, 0), new PointD(40, 47), new PointD(62, 20));

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

            // Along a path of curves and of two contours, on both sides: the path is measured in the scratch of the session
            canvas.FillText(state.Solid, state.Text, state.Path);
            canvas.FillText(state.Gradient, state.RichText, state.Path, state.PathOptions);
            canvas.StrokeText(state.Pen, state.CffText, state.Path, state.PathOptions);
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

    [Fact]
    public void ColorTransformsAllocateNothingPerConversion()
    {
        // A transform is built once; converting samples allocates nothing, whatever the sample type, the profile model
        // (matrix-based with the 8-bit input tables, table-based with black point compensation) or the number of colors
        var cmyk = IccTestProfiles.CmykLabLut16();
        var toDisplayP3 = IccColorTransform.Create(Metadata.IccProfile.Srgb, IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve()));
        var toCmyk = IccColorTransform.Create(Metadata.IccProfile.Srgb, cmyk);
        var fromCmyk = IccColorTransform.Create(cmyk, Metadata.IccProfile.SrgbGray);
        foreach (var colors in new[] { 1, 5000 })
        {
            var bytes = new byte[colors * 3];
            var words = new ushort[colors * 3];
            var floats = new float[colors * 3];
            var inks = new byte[colors * 4];
            var gray = new byte[colors];
            Assert.Equal(0, MeasureSteadyStateAllocations(() =>
            {
                toDisplayP3.Convert(bytes, bytes);
                toDisplayP3.Convert(words, words);
                toDisplayP3.Convert(floats, floats);
                toCmyk.Convert(bytes, inks);
                fromCmyk.Convert(inks, gray);
            }));
        }
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
        using (var source = new MemoryStream(input, writable: false))
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

    private sealed record DrawingState(SolidBrush Solid, GradientBrush Gradient, Pen Pen, VectorPath Path, TextLayout Text, TextLayout CffText, TextLayout CompositeText, TextLayout RichText, TextLayout VariableText, TextLayout BlendedText, TextPathOptions PathOptions, StrongBox<long> Allocated);
}
