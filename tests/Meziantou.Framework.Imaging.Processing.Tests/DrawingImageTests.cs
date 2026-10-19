using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.TestHarness.Drawing;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Drawing an image on another one: <c>DrawImage</c> and <see cref="ImageBrush"/>. The expected samples are literals
/// derived by hand (the position each pixel center comes from, the pixels around it and their weights are in the
/// comments), the reference compositor of the test harness for what is composited, and the definition of the
/// interpolation written again with its own arithmetic for random placements.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class DrawingImageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AnImageDrawnOnWholePixelsKeepsItsPixels()
    {
        using var source = new Image<Rgba32>(2, 2);
        source.Frames[0][0, 0] = new Rgba32(10, 20, 30);
        source.Frames[0][1, 0] = new Rgba32(40, 50, 60);
        source.Frames[0][0, 1] = new Rgba32(70, 80, 90);
        source.Frames[0][1, 1] = new Rgba32(100, 110, 120);
        using var image = new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 4));
        image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Point(1, 2)), Ct);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var expected = x is 1 or 2 && y is 2 or 3 ? source.Frames[0][x - 1, y - 2] : new Rgba32(1, 2, 3, 4);
                Assert.Equal(expected, image.Frames[0][x, y]);
            }
        }

        // Partly outside the canvas, and entirely
        using var corner = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 4));
        corner.Draw(
            canvas =>
            {
                canvas.DrawImage(source.Frames[0], new Point(-1, -1));
                canvas.DrawImage(source.Frames[0], new Point(5, 0));
                canvas.DrawImage(source.Frames[0], new Point(0, -2));
            },
            Ct);
        Assert.Equal(new Rgba32(100, 110, 120), corner.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(1, 2, 3, 4), corner.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(1, 2, 3, 4), corner.Frames[0][0, 1]);

        // The source is only read, and is free again when the call returns
        Assert.Equal(new Rgba32(40, 50, 60), source.Frames[0][1, 0]);
        Assert.Equal(0, source.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void AnImageMovedByWholePixelsPaintsThePixelsOfItsRegionInEveryWrapMode()
    {
        // Each pixel of the canvas gets the pixel of the region its center comes from, wrapped, as a solid brush of
        // that color would paint it
        var random = new Random(20261018);
        ImageWrapMode[] wrapModes = [ImageWrapMode.Transparent, ImageWrapMode.Pad, ImageWrapMode.Repeat, ImageWrapMode.Reflect];
        ImageSampling[] samplings = [ImageSampling.Nearest, ImageSampling.Bilinear, ImageSampling.Smooth, ImageSampling.Box];
        (PixelFormat Source, PixelFormat Canvas)[] formats = [(PixelFormat.Rgba32, PixelFormat.Rgba32), (PixelFormat.Bgra32, PixelFormat.Bgra32), (PixelFormat.Rgba64, PixelFormat.Rgba64), (PixelFormat.Gray8, PixelFormat.Gray8), (PixelFormat.Gray16, PixelFormat.Gray16), (PixelFormat.Rgb24, PixelFormat.Rgb24), (PixelFormat.Rgba32, PixelFormat.Rgba64), (PixelFormat.Rgba64, PixelFormat.Rgb24), (PixelFormat.Gray8, PixelFormat.Bgra32)];
        foreach (var (sourceFormat, canvasFormat) in formats)
        {
            foreach (var wrapMode in wrapModes)
            {
                Check(random, sourceFormat, canvasFormat, wrapMode, samplings[random.Next(samplings.Length)]);
            }
        }

        static void Check(Random random, PixelFormat sourceFormat, PixelFormat canvasFormat, ImageWrapMode wrapMode, ImageSampling sampling)
        {
            using var source = DrawingTestSupport.CreateRandom(sourceFormat, random.Next(1, 30), random.Next(1, 6), random.Next);
            using var colors = source.CloneAs<Rgba64>();
            var region = new Rectangle(random.Next(source.Width), random.Next(source.Height), 1, 1);
            region = new Rectangle(region.X, region.Y, random.Next(1, source.Width - region.X + 1), random.Next(1, source.Height - region.Y + 1));
            var (shiftX, shiftY) = (random.Next(-40, 40), random.Next(-8, 8));
            var brush = new ImageBrush(source.Frames[0]) { SourceRegion = region, Transform = AffineTransform.CreateTranslation(shiftX, shiftY), WrapMode = wrapMode, Sampling = sampling, CompositeMode = CompositeMode.Source };

            // A rectangle of whole pixels: runs that start anywhere
            var left = random.Next(0, 20);
            var right = random.Next(left + 1, 41);
            using var expected = DrawingTestSupport.CreateRandom(canvasFormat, 40, 9, new Random(1).Next);
            using var actual = DrawingTestSupport.CreateRandom(canvasFormat, 40, 9, new Random(1).Next);
            expected.Draw(
                canvas =>
                {
                    for (var y = 0; y < 9; y++)
                    {
                        for (var x = left; x < right; x++)
                        {
                            var column = Wrap(x - shiftX - region.X, region.Width, wrapMode);
                            var row = Wrap(y - shiftY - region.Y, region.Height, wrapMode);
                            var color = column is { } c && row is { } r ? colors.Frames[0][region.X + c, region.Y + r] : default;
                            canvas.FillRectangle(new SolidBrush(color) { CompositeMode = CompositeMode.Source }, new RectangleD(x, y, 1, 1));
                        }
                    }
                },
                Ct);
            actual.Draw(canvas => canvas.FillRectangle(brush, new RectangleD(left, 0, right - left, 9)), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));
        }

        static int? Wrap(int offset, int size, ImageWrapMode wrapMode)
        {
            if (offset >= 0 && offset < size)
                return offset;

            switch (wrapMode)
            {
                case ImageWrapMode.Pad:
                    return Math.Clamp(offset, 0, size - 1);

                case ImageWrapMode.Repeat:
                    return ((offset % size) + size) % size;

                case ImageWrapMode.Reflect:
                    var folded = ((offset % (2 * size)) + (2 * size)) % (2 * size);
                    return folded < size ? folded : (2 * size) - 1 - folded;

                default:
                    return null;
            }
        }
    }

    [Fact]
    public void AnImageIsCompositedOverThePixelsInEveryPairOfPixelFormats()
    {
        // The colors of the source become 16-bit colors by the rules of pixel conversion (v * 257 from 8 bits, a gray
        // level for the three colors, an opaque alpha when there is none), then a paint like any brush color
        var random = new Random(4242);
        foreach (var sourceFormat in PixelFormats.All)
        {
            foreach (var format in PixelFormats.All)
            {
                var layout = ReferencePixelLayout.FromPixelFormat(format);
                using var source = DrawingTestSupport.CreateRandom(sourceFormat, 3, 3, random.Next);
                foreach (var (opacity, coverage) in new[] { (1.0, 65536), (0.5, 32768), (0.25, 16384), (1.0 / 3, 21845) })
                {
                    using var image = DrawingTestSupport.CreateRandom(format, 5, 4, random.Next);
                    var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
                    image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Point(1, 1), opacity), Ct);
                    var after = DrawingTestSupport.ReadSamples(image.Frames[0]);
                    var channels = layout.ChannelCount;
                    for (var pixel = 0; pixel < 20; pixel++)
                    {
                        var (x, y) = ((pixel % 5) - 1, (pixel / 5) - 1);
                        var destination = before.AsSpan(pixel * channels, channels).ToArray();
                        var expected = destination;
                        if (x is >= 0 and < 3 && y is >= 0 and < 3)
                        {
                            var (red, green, blue, alpha) = ReadColor(source.Frames[0], x, y);
                            expected = ReferenceCompositor.Blend(destination, ReferencePaint.Convert(red, green, blue, alpha, layout), coverage, layout);
                        }

                        Assert.True(expected.AsSpan().SequenceEqual(after.AsSpan(pixel * channels, channels)), $"{sourceFormat} on {format} at {opacity}, pixel ({pixel % 5}, {pixel / 5}): before [{string.Join(' ', destination)}], expected [{string.Join(' ', expected)}], actual [{string.Join(' ', after.AsSpan(pixel * channels, channels).ToArray())}]");
                    }
                }
            }
        }
    }

    [Fact]
    public void AnImageBetweenPixelsIsInterpolatedAndItsEdgesAreAntialiased()
    {
        // A white pixel half a pixel to the right: it covers half of two pixels of the canvas, and its color is extended
        // to the centers of both
        using (var source = new Image<Gray8>(1, 1, new Gray8(255)))
        using (var image = new Image<Gray8>(3, 1, new Gray8(0)))
        {
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new PointD(0.5, 0)), Ct);
            Assert.Equal([128, 128, 0], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // Two pixels, black and white, stretched to four: the centers of the canvas come from 0.25, 0.75, 1.25 and 1.75,
        // that is 0.25 before the center of the first pixel (the edge is extended), then 1/4 and 3/4 of the way between
        // the two centers (63.75 -> 64 and 191.25 -> 191), then 0.25 after the center of the second one
        using (var source = new Image<Gray8>(2, 1))
        using (var image = new Image<Gray8>(4, 1, new Gray8(7)))
        {
            source.Frames[0][1, 0] = new Gray8(255);
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 4, 1)), Ct);
            Assert.Equal([0, 64, 191, 255], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // Four pixels reduced to two: each center is between two pixels, (0 + 100) / 2 and (200 + 50) / 2
        using (var source = new Image<Gray8>(4, 1))
        using (var image = new Image<Gray8>(2, 1, new Gray8(7)))
        {
            DrawingTestSupport.WriteSamples(source.Frames[0], [0, 100, 200, 50]);
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 2, 1)), Ct);
            Assert.Equal([50, 125], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // The colors multiplied by their alpha are interpolated: half way between opaque red and transparent blue is
        // red at half its opacity (32767.5 -> 32768 at 16 bits, 128 at 8 bits), not purple
        using (var source = new Image<Rgba32>(2, 1))
        using (var image = new Image<Rgba32>(4, 1))
        {
            source.Frames[0][0, 0] = new Rgba32(255, 0, 0);
            source.Frames[0][1, 0] = new Rgba32(0, 0, 255, 0);
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new RectangleD(0.5, 0, 2, 1)), Ct);

            // The image starts half a pixel to the right: the centers of the canvas come from 0 (the left edge of the red
            // pixel, on half of a pixel of the canvas), 1 (between the two pixels), 2 (the right edge of the transparent
            // pixel), and outside the image
            Assert.Equal(new Rgba32(255, 0, 0, 128), image.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(255, 0, 0, 128), image.Frames[0][1, 0]);
            Assert.Equal(new Rgba32(0, 0, 0, 0), image.Frames[0][2, 0]);
            Assert.Equal(new Rgba32(0, 0, 0, 0), image.Frames[0][3, 0]);
        }
    }

    [Fact]
    public void AnImageIsPlacedByATransformation()
    {
        using var source = new Image<Rgba32>(2, 2);
        var a = source.Frames[0][0, 0] = new Rgba32(10, 20, 30);
        var b = source.Frames[0][1, 0] = new Rgba32(40, 50, 60);
        var c = source.Frames[0][0, 1] = new Rgba32(70, 80, 90);
        var d = source.Frames[0][1, 1] = new Rgba32(100, 110, 120);
        var frame = source.Frames[0];
        var background = new Rgba32(1, 2, 3, 4);

        // Without transformation, or moved by whole pixels: the image at that position; scaled: the image stretched
        Assert.Equal(Draw(canvas => canvas.DrawImage(frame, default(PointD))), Draw(canvas => canvas.DrawImage(frame, AffineTransform.Identity)));
        Assert.Equal(Draw(canvas => canvas.DrawImage(frame, new Point(1, 2))), Draw(canvas => canvas.DrawImage(frame, AffineTransform.CreateTranslation(1, 2))));
        Assert.Equal(Draw(canvas => canvas.DrawImage(frame, new PointD(0.25, 1.5), 0.5)), Draw(canvas => canvas.DrawImage(frame, AffineTransform.CreateTranslation(0.25, 1.5), 0.5)));
        Assert.Equal(
            Draw(canvas => canvas.DrawImage(frame, new RectangleD(0.5, 1, 3, 2.5))),
            Draw(canvas => canvas.DrawImage(frame, AffineTransform.CreateScale(1.5, 1.25).Then(AffineTransform.CreateTranslation(0.5, 1)))));

        // A quarter turn around the center of the image, clockwise on the image: the pixels themselves, turned
        var center = new PointD(1, 1);
        var turned = Draw(canvas => canvas.DrawImage(frame, AffineTransform.CreateRotation(90, center).Then(AffineTransform.CreateTranslation(1, 1))));
        Assert.Equal(c, At(turned, 1, 1));
        Assert.Equal(a, At(turned, 2, 1));
        Assert.Equal(d, At(turned, 1, 2));
        Assert.Equal(b, At(turned, 2, 2));
        Assert.Equal(background, At(turned, 0, 1));
        Assert.Equal(background, At(turned, 3, 2));

        // Mirrored from left to right, and turned upside down
        var mirrored = Draw(canvas => canvas.DrawImage(frame, AffineTransform.CreateScale(-1, 1, center)));
        Assert.Equal(b, At(mirrored, 0, 0));
        Assert.Equal(a, At(mirrored, 1, 0));
        Assert.Equal(d, At(mirrored, 0, 1));
        Assert.Equal(c, At(mirrored, 1, 1));
        var upsideDown = Draw(canvas => canvas.DrawImage(frame, AffineTransform.CreateRotation(180, center).Then(AffineTransform.CreateTranslation(2, 2))));
        Assert.Equal(d, At(upsideDown, 2, 2));
        Assert.Equal(a, At(upsideDown, 3, 3));

        // A transformation that flattens the image draws nothing
        Assert.Equal(Draw(static _ => { }), Draw(canvas => canvas.DrawImage(frame, AffineTransform.CreateScale(0, 1))));
        Assert.Equal(Draw(static _ => { }), Draw(canvas => canvas.DrawImage(frame, AffineTransform.CreateTranslation(1, 1), 0)));

        // Turned by any angle: the edges are antialiased, and the image keeps its area
        using var white = new Image<Rgba32>(6, 4, new Rgba32(255, 255, 255));
        using var canvasImage = new Image<Rgba32>(16, 16);
        canvasImage.Draw(canvas => canvas.DrawImage(white.Frames[0], AffineTransform.CreateRotation(33, new PointD(3, 2)).Then(AffineTransform.CreateTranslation(5, 6))), Ct);
        var samples = DrawingTestSupport.ReadSamples(canvasImage.Frames[0]);
        var area = 0;
        var partial = 0;
        for (var i = 0; i < samples.Length; i += 4)
        {
            area += samples[i + 3];
            partial += samples[i + 3] is > 0 and < 255 ? 1 : 0;
            if (samples[i + 3] != 0)
            {
                Assert.Equal([255, 255, 255], samples.AsSpan(i, 3).ToArray());
            }
        }

        Assert.Equal(24, area / 255.0, 0.05);
        Assert.InRange(partial, 12, 30);

        // The opacity and the clip apply as to any image
        using var gray = new Image<Gray8>(4, 4, new Gray8(0));
        using var dot = new Image<Gray8>(2, 2, new Gray8(255));
        gray.Draw(
            canvas => canvas.Clip(new Rectangle(0, 0, 4, 2), clipped => clipped.DrawImage(dot.Frames[0], AffineTransform.CreateScale(2, 2), 0.5)),
            Ct);
        Assert.Equal([128, 128, 128, 128, 128, 128, 128, 128, 0, 0, 0, 0, 0, 0, 0, 0], DrawingTestSupport.ReadSamples(gray.Frames[0]));

        Rgba32[] Draw(DrawingAction action)
        {
            using var image = new Image<Rgba32>(4, 4, background);
            image.Draw(action, Ct);
            var pixels = new Rgba32[16];
            image.Frames[0].CopyPixelDataTo(pixels);
            return pixels;
        }

        static Rgba32 At(Rgba32[] pixels, int x, int y) => pixels[(y * 4) + x];
    }

    [Fact]
    public void AReducedImageCanBeReadWithoutSkippingPixels()
    {
        // Four pixels reduced to two. The centers of the canvas come from 1 and 3, and each covers two pixels of the
        // image: the pixels whose centers are less than 2 away count, with the weights 1 - distance / 2, that is 1/4,
        // 3/4, 3/4 and 1/4 of the pixels -1 to 2, then of the pixels 1 to 4. The pixels beyond the edges are the pixels
        // of the edges: (0 * 4 + 100 * 3 + 200) / 8 = 62.5 -> 63 and (100 + 200 * 3 + 50 * 4) / 8 = 112.5 -> 113
        using (var source = new Image<Gray8>(4, 1))
        using (var image = new Image<Gray8>(2, 1, new Gray8(7)))
        {
            DrawingTestSupport.WriteSamples(source.Frames[0], [0, 100, 200, 50]);
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 2, 1), ImageSampling.Smooth), Ct);
            Assert.Equal([63, 113], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // A white pixel then seven black ones, reduced to one pixel. The interpolation reads the two pixels in the
        // middle, and misses the white one. Every pixel counts instead: the weights 1 - distance / 8 of the 16 pixels
        // around the center add up to 8, and the white pixel, with the four it is extended to on its left, has
        // (9 + 7 + 5 + 3 + 1) / 16 of them: 255 * 25 / 128 = 49.8 -> 50
        using (var source = new Image<Gray8>(8, 1))
        using (var image = new Image<Gray8>(1, 1, new Gray8(7)))
        {
            source.Frames[0][0, 0] = new Gray8(255);
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 1, 1)), Ct);
            Assert.Equal([0], DrawingTestSupport.ReadSamples(image.Frames[0]));
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 1, 1), ImageSampling.Smooth), Ct);
            Assert.Equal([50], DrawingTestSupport.ReadSamples(image.Frames[0]));

            // A region is read the same way, and the pixels around it are not
            source.Frames[0][4, 0] = new Gray8(255);
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 4, 1), new Rectangle(0, 0, 1, 1), ImageSampling.Smooth), Ct);

            // Weights 1 - distance / 4 of the 8 pixels around the center, which add up to 4: (5 + 3 + 1) / 8 of them are
            // white, 255 * 9 / 32 = 71.7 -> 72
            Assert.Equal([72], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // Around a transparent image, nothing counts for the color, and the alpha fades: a brush of two opaque pixels
        // reduced to one has, on the pixel it is centered on, 3/4 of the weights (1/4 + 1/2 + 1/2 + 1/4 of 4 pixels,
        // two of them outside)
        using (var source = new Image<Rgba32>(2, 1, new Rgba32(200, 100, 50)))
        using (var image = new Image<Rgba32>(3, 1))
        {
            var brush = new ImageBrush(source.Frames[0]) { Transform = AffineTransform.CreateScale(0.5, 1).Then(AffineTransform.CreateTranslation(1, 0)), Sampling = ImageSampling.Smooth };
            image.Draw(canvas => canvas.Fill(brush), Ct);
            Assert.Equal(new Rgba32(200, 100, 50, 191), image.Frames[0][1, 0]);
            Assert.Equal(new Rgba32(200, 100, 50, 32), image.Frames[0][0, 0]);
            Assert.Equal(new Rgba32(200, 100, 50, 32), image.Frames[0][2, 0]);
        }

        // An image of one color keeps it whatever its size, and an image that is not reduced is interpolated, bit for bit
        var random = new Random(77);
        using var flat = new Image<Rgba64>(9, 7, new Rgba64(12345, 60000, 321, 40000));
        using var noise = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 6, 5, random.Next);
        for (var i = 0; i < 20; i++)
        {
            var rotation = i % 2 == 0 ? AffineTransform.Identity : AffineTransform.CreateRotation(random.NextDouble() * 360, new PointD(4, 3));
            var reduction = rotation.Then(AffineTransform.CreateScale(0.1 + (random.NextDouble() * 0.8), 0.1 + (random.NextDouble() * 0.8))).Then(AffineTransform.CreateTranslation(random.NextDouble() * 3, random.NextDouble() * 3));
            using var reduced = new Image<Rgba64>(8, 8);
            reduced.Draw(canvas => canvas.Fill(new ImageBrush(flat.Frames[0]) { Transform = reduction, WrapMode = ImageWrapMode.Repeat, Sampling = ImageSampling.Smooth, CompositeMode = CompositeMode.Source }), Ct);
            Assert.All(DrawingTestSupport.ReadSamples(reduced.Frames[0]).Chunk(4), pixel => Assert.Equal([12345, 60000, 321, 40000], pixel));

            var enlargement = rotation.Then(AffineTransform.CreateScale(1 + (random.NextDouble() * 2), 1 + random.NextDouble())).Then(AffineTransform.CreateTranslation(random.NextDouble() * 3, random.NextDouble() * 3));
            using var smooth = new Image<Rgba32>(12, 10);
            using var bilinear = new Image<Rgba32>(12, 10);
            smooth.Draw(canvas => canvas.DrawImage(noise.Frames[0], enlargement, ImageSampling.Smooth), Ct);
            bilinear.Draw(canvas => canvas.DrawImage(noise.Frames[0], enlargement), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(bilinear.Frames[0]), DrawingTestSupport.ReadSamples(smooth.Frames[0]));
        }

        // The nearest pixel, for an image enlarged with its pixels as squares
        using (var source = new Image<Gray8>(2, 1))
        using (var image = new Image<Gray8>(4, 1, new Gray8(7)))
        {
            source.Frames[0][1, 0] = new Gray8(255);
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 4, 1), ImageSampling.Nearest), Ct);
            Assert.Equal([0, 0, 255, 255], DrawingTestSupport.ReadSamples(image.Frames[0]));
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], AffineTransform.CreateScale(2, 1), ImageSampling.Nearest), Ct);
            Assert.Equal([0, 0, 255, 255], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }
    }

    [Fact]
    public void AReducedImageGivesAPixelTheSameColorWhateverTheRunItIsPartOf()
    {
        // A wide image reduced 143 times: the pixels of a whole row of the canvas read more columns of the image than
        // the evaluator keeps at once, so the row is evaluated in parts. Each pixel drawn on its own gets the same bits
        var random = new Random(4242);
        using var source = DrawingTestSupport.CreateRandom(PixelFormat.Rgba64, 20000, 3, random.Next);
        foreach (var sampling in new[] { ImageSampling.Smooth, ImageSampling.Box })
        {
            foreach (var wrapMode in new[] { ImageWrapMode.Transparent, ImageWrapMode.Pad, ImageWrapMode.Repeat, ImageWrapMode.Reflect })
            {
                var brush = new ImageBrush(source.Frames[0]) { Transform = AffineTransform.CreateScale(0.007, 0.9).Then(AffineTransform.CreateTranslation(13.25, 0.5)), WrapMode = wrapMode, Sampling = sampling, CompositeMode = CompositeMode.Source };
                using var whole = new Image<Rgba64>(300, 2);
                using var alone = new Image<Rgba64>(300, 2);
                whole.Draw(canvas => canvas.Fill(brush), Ct);
                alone.Draw(canvas =>
                {
                    for (var x = 0; x < whole.Width; x++)
                    {
                        canvas.FillRectangle(brush, new RectangleD(x, 0, 1, 2));
                    }
                }, Ct);
                Assert.Equal(DrawingTestSupport.ReadSamples(whole.Frames[0]), DrawingTestSupport.ReadSamples(alone.Frames[0]));
            }
        }
    }

    [Fact]
    public void ABoxAveragesWhatAPixelOfTheCanvasCoversOfTheImage()
    {
        // Four pixels reduced to two: each pixel of the canvas is the average of the two it covers. Six reduced to four:
        // each covers one and a half, the first one [0, 1.5): (0 + 90 / 2) / 1.5 = 30, then (90 / 2 + 180) / 1.5 = 150,
        // (30 + 60 / 2) / 1.5 = 40 and (60 / 2 + 255) / 1.5 = 190
        Assert.Equal([50, 125], Reduce([0, 100, 200, 50], 2));
        Assert.Equal([30, 150, 40, 190], Reduce([0, 90, 180, 30, 60, 255], 4));

        // Stripes of 24 pixels drawn at a third of their size are stripes of 8 pixels, without the edges the wider
        // filter of smooth sampling blends
        using var stripes = new Image<Gray8>(96, 2);
        DrawingTestSupport.WriteSamples(stripes.Frames[0], [.. Enumerable.Range(0, 192).Select(i => i % 96 / 24 % 2 == 0 ? 0 : 255)]);
        Assert.Equal([.. Enumerable.Range(0, 32).Select(i => i / 8 % 2 == 0 ? 0 : 255)], Draw(stripes, ImageSampling.Box));
        Assert.Equal(227, Draw(stripes, ImageSampling.Smooth)[8]);

        // An image that is not reduced is interpolated, bit for bit
        var random = new Random(23);
        using var noise = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 6, 5, random.Next);
        var enlargement = AffineTransform.CreateRotation(25, new PointD(3, 2)).Then(AffineTransform.CreateScale(1.7, 1.2));
        using var box = new Image<Rgba32>(12, 10);
        using var bilinear = new Image<Rgba32>(12, 10);
        box.Draw(canvas => canvas.DrawImage(noise.Frames[0], enlargement, ImageSampling.Box), Ct);
        bilinear.Draw(canvas => canvas.DrawImage(noise.Frames[0], enlargement), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(bilinear.Frames[0]), DrawingTestSupport.ReadSamples(box.Frames[0]));

        static int[] Reduce(int[] samples, int width)
        {
            using var source = new Image<Gray8>(samples.Length, 1);
            DrawingTestSupport.WriteSamples(source.Frames[0], samples);
            return Draw(source, ImageSampling.Box, width);
        }

        static int[] Draw(Image source, ImageSampling sampling, int width = 32)
        {
            using var image = new Image<Gray8>(width, 1);
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new RectangleD(0, 0, width, 1), sampling), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void APositionOnTheEdgeBetweenTwoPixelsWeighsThemEqually()
    {
        // drawimage_canvas.html of the web-platform-tests, scenario 12: the region (0, 0, 30, 30) of an image whose
        // border of 5 pixels is blue around black, enlarged 2.5 times at (70, 70). The center of the pixel (82, 90)
        // comes from (5, 8.2): on the edge between a blue column and a black one, half of each. The center of (82, 82)
        // comes from (5, 5), the corner of the black square: three of the four pixels around it are blue.
        using var source = new Image<Rgba32>(50, 50, new Rgba32(0, 0, 255));
        source.Draw(canvas => canvas.FillRectangle(new Rgba32(0, 0, 0), new RectangleD(5, 5, 40, 40)), Ct);
        using var image = new Image<Rgba32>(100, 100);
        image.Draw(canvas => canvas.DrawImage(source.Frames[0], new RectangleD(0, 0, 30, 30), new RectangleD(70, 70, 75, 75), ImageSampling.Bilinear), Ct);
        Assert.Equal(new Rgba32(0, 0, 128), image.Frames[0][82, 90]);
        Assert.Equal(new Rgba32(0, 0, 191), image.Frames[0][82, 82]);
        Assert.Equal(new Rgba32(0, 0, 255), image.Frames[0][80, 80]);

        // The nearest pixel of a position on an edge is the one after it
        image.Draw(canvas => canvas.DrawImage(source.Frames[0], new RectangleD(0, 0, 30, 30), new RectangleD(70, 70, 75, 75), ImageSampling.Nearest), Ct);
        Assert.Equal(new Rgba32(0, 0, 0), image.Frames[0][82, 90]);
        Assert.Equal(new Rgba32(0, 0, 255), image.Frames[0][81, 90]);
    }

    [Fact]
    public void ARegionOfAnImageIsDrawnWithoutThePixelsAroundIt()
    {
        using var source = new Image<Gray8>(4, 2);
        DrawingTestSupport.WriteSamples(source.Frames[0], [0, 100, 200, 50, 9, 9, 9, 9]);
        using (var image = new Image<Gray8>(2, 1, new Gray8(7)))
        {
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(1, 0, 2, 1), new Rectangle(0, 0, 2, 1)), Ct);
            Assert.Equal([100, 200], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        // Stretched to four pixels: 0.25 before the center of the first pixel of the region and 0.25 after the center of
        // the last one, the edges of the region are extended (100 and 200), not the pixels next to it (0 and 50)
        using (var image = new Image<Gray8>(4, 1, new Gray8(7)))
        {
            image.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(1, 0, 2, 1), new Rectangle(0, 0, 4, 1)), Ct);
            Assert.Equal([100, 125, 175, 200], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }

        using (var image = new Image<Gray8>(2, 1, new Gray8(7)))
        {
            image.Draw(
                canvas =>
                {
                    // A region without pixels and a rectangle without area draw nothing
                    canvas.DrawImage(source.Frames[0], new Rectangle(1, 0, 0, 1), new Rectangle(0, 0, 2, 1));
                    canvas.DrawImage(source.Frames[0], new Rectangle(4, 2, 0, 0), new Rectangle(0, 0, 2, 1));
                    canvas.DrawImage(source.Frames[0], new Rectangle(1, 0, 2, 1), new RectangleD(0, 0, 0, 1));
                    canvas.DrawImage(source.Frames[0], new RectangleD(0, 0.5, 2, 0));

                    // A region must be inside the frame
                    AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(source.Frames[0], new Rectangle(3, 0, 2, 1), new Rectangle(0, 0, 2, 1)));
                    AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 1, 1, 2), new Rectangle(0, 0, 2, 1)));
                    AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(source.Frames[0], new Rectangle(-1, 0, 2, 1), new Rectangle(0, 0, 2, 1)));
                },
                Ct);
            Assert.Equal([7, 7], DrawingTestSupport.ReadSamples(image.Frames[0]));
        }
    }

    [Fact]
    public void AnImageBrushPaintsAnyShapeWithAnImage()
    {
        using var source = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(77).Next);
        var circle = new VectorPathBuilder().AddCircle(new PointD(4.25, 3.5), 3.1).Build();
        var brush = new ImageBrush(source.Frames[0]);
        Assert.Same(source.Frames[0], brush.Source);
        Assert.Equal((null, ImageWrapMode.Transparent, ImageSampling.Bilinear, 1d, CompositeMode.SourceOver), (brush.SourceRegion, brush.WrapMode, brush.Sampling, brush.Opacity, brush.CompositeMode));
        Assert.True(brush.Transform.IsIdentity);

        // Filling a shape with an image is drawing the image in the clip of the shape
        using var expected = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        using var actual = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        expected.Draw(canvas => canvas.Clip(circle, inner => inner.DrawImage(source.Frames[0], default(PointD))), Ct);
        actual.Draw(canvas => canvas.Fill(brush, circle), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));

        // And a stroke, a rectangle on the whole image, the whole canvas
        using var whole = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        using var drawn = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        whole.Draw(canvas => canvas.Fill(brush), Ct);
        drawn.Draw(canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 8, 8)), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(drawn.Frames[0]), DrawingTestSupport.ReadSamples(whole.Frames[0]));

        // With an opacity, which multiplies the alpha of the paint at 8 bits: the image whose alphas are halved, rounded
        using var faded = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        using var half = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        faded.Draw(canvas => canvas.Fill(new ImageBrush(source.Frames[0]) { Opacity = 0.5 }), Ct);
        using var halved = source.CloneAs<Rgba32>();
        halved.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (ref var pixel in pixels.GetRowSpan(y))
                {
                    pixel = new Rgba32(pixel.R, pixel.G, pixel.B, (byte)Math.Floor((pixel.A * 0.5) + 0.5 + (1.0 / 1048576)));
                }
            }
        });
        half.Draw(canvas => canvas.Fill(new ImageBrush(halved.Frames[0])), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(half.Frames[0]), DrawingTestSupport.ReadSamples(faded.Frames[0]));
        Assert.NotEqual(DrawingTestSupport.ReadSamples(whole.Frames[0]), DrawingTestSupport.ReadSamples(faded.Frames[0]));

        // Without opacity, nothing
        using var untouched = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        var before = DrawingTestSupport.ReadSamples(untouched.Frames[0]);
        untouched.Draw(
            canvas =>
            {
                canvas.Fill(new ImageBrush(source.Frames[0]) { Opacity = 0 });
                canvas.DrawImage(source.Frames[0], default(PointD), 0);
            },
            Ct);
        Assert.Equal(before, DrawingTestSupport.ReadSamples(untouched.Frames[0]));
    }

    [Fact]
    public void TheWrapModeGivesTheColorsAroundTheImage()
    {
        // Two pixels, 10 and 20, placed two pixels to the right on a row of six
        using var source = new Image<Gray8>(2, 1);
        DrawingTestSupport.WriteSamples(source.Frames[0], [10, 20]);
        var placement = AffineTransform.CreateTranslation(2, 0);
        Assert.Equal([0, 0, 10, 20, 0, 0], Fill(new ImageBrush(source.Frames[0]) { Transform = placement }));
        Assert.Equal([10, 10, 10, 20, 20, 20], Fill(new ImageBrush(source.Frames[0]) { Transform = placement, WrapMode = ImageWrapMode.Pad }));
        Assert.Equal([10, 20, 10, 20, 10, 20], Fill(new ImageBrush(source.Frames[0]) { Transform = placement, WrapMode = ImageWrapMode.Repeat }));
        Assert.Equal([20, 10, 10, 20, 20, 10], Fill(new ImageBrush(source.Frames[0]) { Transform = placement, WrapMode = ImageWrapMode.Reflect }));

        // A region is wrapped as an image of its own: the second pixel only
        Assert.Equal([0, 0, 0, 20, 0, 0], Fill(new ImageBrush(source.Frames[0]) { Transform = placement, SourceRegion = new Rectangle(1, 0, 1, 1) }));
        Assert.Equal([20, 20, 20, 20, 20, 20], Fill(new ImageBrush(source.Frames[0]) { Transform = placement, SourceRegion = new Rectangle(1, 0, 1, 1), WrapMode = ImageWrapMode.Repeat }));

        // Vertically too: the rows above and under the image
        using var column = new Image<Gray8>(1, 2);
        DrawingTestSupport.WriteSamples(column.Frames[0], [10, 20]);
        using var image = new Image<Gray8>(1, 6, new Gray8(0));
        image.Draw(canvas => canvas.Fill(new ImageBrush(column.Frames[0]) { Transform = AffineTransform.CreateTranslation(0, 2), WrapMode = ImageWrapMode.Reflect }), Ct);
        Assert.Equal([20, 10, 10, 20, 20, 10], DrawingTestSupport.ReadSamples(image.Frames[0]));

        static int[] Fill(ImageBrush brush)
        {
            using var row = new Image<Gray8>(6, 1, new Gray8(0));
            row.Draw(canvas => canvas.Fill(brush), Ct);
            return DrawingTestSupport.ReadSamples(row.Frames[0]);
        }
    }

    [Fact]
    public void EachAxisHasAWrapMode()
    {
        // Two pixels, 10 and 20, at the origin of a canvas of 5 by 3: repeated along x, nothing above or below them
        // (background-repeat: repeat-x), then the other way around
        using var source = new Image<Gray8>(2, 1);
        DrawingTestSupport.WriteSamples(source.Frames[0], [10, 20]);
        var brush = new ImageBrush(source.Frames[0]) { WrapModeX = ImageWrapMode.Repeat, Sampling = ImageSampling.Nearest };
        Assert.Equal(ImageWrapMode.Repeat, brush.WrapMode);
        Assert.Equal(ImageWrapMode.Transparent, brush.WrapModeY);
        Assert.Equal([10, 20, 10, 20, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], Fill(brush));
        Assert.Equal([10, 20, 0, 0, 0, 10, 20, 0, 0, 0, 10, 20, 0, 0, 0], Fill(new ImageBrush(source.Frames[0]) { WrapModeY = ImageWrapMode.Repeat }));
        Assert.Equal([10, 20, 20, 20, 20, 10, 20, 20, 20, 20, 10, 20, 20, 20, 20], Fill(new ImageBrush(source.Frames[0]) { WrapModeX = ImageWrapMode.Pad, WrapModeY = ImageWrapMode.Reflect }));

        // WrapMode sets both
        var both = new ImageBrush(source.Frames[0]) { WrapMode = ImageWrapMode.Reflect };
        Assert.Equal((ImageWrapMode.Reflect, ImageWrapMode.Reflect), (both.WrapModeX, both.WrapModeY));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(source.Frames[0]) { WrapModeY = (ImageWrapMode)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(source.Frames[0]) { WrapModeX = (ImageWrapMode)(-1) });

        static int[] Fill(ImageBrush brush)
        {
            using var image = new Image<Gray8>(5, 3, new Gray8(0));
            image.Draw(canvas => canvas.Fill(brush), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void APatternHasAWrapModePerAxisAndASpacing()
    {
        // A tile of 2 by 1 filled with 200, then a gap of 1: the period is 3 along x
        var tile = new Rectangle(0, 0, 2, 1);
        DrawingAction content = canvas => canvas.FillRectangle(new Rgba32(200, 200, 200), new Rectangle(-5, -5, 20, 20));
        Assert.Equal([200, 200, 0, 200, 200, 0, 200, 200, 0, 200, 200, 0], Fill(new PatternBrush(tile, content) { SpacingX = 1 }, 1));
        Assert.Equal([200, 200, 0, 200, 200, 0, 200, 200, 0, 200, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], Fill(new PatternBrush(tile, content) { SpacingX = 1, SpacingY = 1 }, 2));

        // Once along x, repeated along y; once along y, repeated along x
        Assert.Equal([200, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], Fill(new PatternBrush(tile, content) { WrapModeX = ImageWrapMode.Transparent }, 2));
        Assert.Equal([200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], Fill(new PatternBrush(tile, content) { WrapModeY = ImageWrapMode.Transparent }, 2));

        // A spacing on whole pixels of the canvas at a scale of 2: the tile is 4 pixels wide and the gap 2
        var scaled = new PatternBrush(tile, content) { SpacingX = 1, Transform = AffineTransform.CreateScale(2, 2) };
        Assert.Equal([200, 200, 200, 200, 0, 0, 200, 200, 200, 200, 0, 0], Fill(scaled, 1));

        var brush = new PatternBrush(tile, content);
        Assert.Equal((ImageWrapMode.Repeat, ImageWrapMode.Repeat, 0d, 0d), (brush.WrapModeX, brush.WrapModeY, brush.SpacingX, brush.SpacingY));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternBrush(tile, content) { SpacingX = -1 });
        Assert.Throws<ArgumentException>(() => new PatternBrush(tile, content) { SpacingY = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternBrush(tile, content) { WrapModeY = (ImageWrapMode)4 });

        static int[] Fill(PatternBrush brush, int height)
        {
            using var image = new Image<Gray8>(12, height, new Gray8(0));
            image.Draw(canvas => canvas.Fill(brush), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Fact]
    public void ARectangleOfAnImageThatIsNotMadeOfWholePixelsKeepsItsSamplesInside()
    {
        // A rectangle of whole pixels: the positions brought to [left + 1/2, right - 1/2] read what the extended pixels of
        // the edges give, so the pixels are those of the region of whole pixels
        using var photo = new Image<Rgba32>(8, 8);
        photo.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < 8; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < 8; x++)
                {
                    row[x] = new Rgba32((byte)(x * 31), (byte)(y * 29), (byte)((x * y) % 256), (byte)(128 + (x * 15)));
                }
            }
        });
        Assert.Equal(
            Render(16, 16, canvas => canvas.DrawImage(photo.Frames[0], new Rectangle(2, 2, 4, 4), new RectangleD(0, 0, 16, 16), ImageSampling.Bilinear)),
            Render(16, 16, canvas => canvas.DrawImage(photo.Frames[0], new RectangleD(2, 2, 4, 4), new RectangleD(0, 0, 16, 16), ImageSampling.Bilinear)));

        // From the middle of black to the middle of white: one pixel wide, so every position is brought to its middle,
        // half way between them (128). Narrower than a pixel: the middle of the rectangle, in black. With the nearest
        // pixel, the positions stay in [0.5, 1.5): black, then white.
        using var pair = new Image<Gray8>(2, 1);
        pair.Frames[0][1, 0] = new Gray8(255);
        Assert.Equal([128, 128, 128, 128], Draw(new RectangleD(0.5, 0, 1, 1), ImageSampling.Bilinear));
        Assert.Equal([128, 128, 128, 128], Draw(new RectangleD(0.5, 0, 1, 1), ImageSampling.Bicubic));
        Assert.Equal([0, 0, 0, 0], Draw(new RectangleD(0.25, 0, 0.5, 1), ImageSampling.Bilinear));
        Assert.Equal([0, 0, 255, 255], Draw(new RectangleD(0.5, 0, 1, 1), ImageSampling.Nearest));

        using var image = new Image<Gray8>(2, 2);
        image.Draw(canvas =>
        {
            AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(pair.Frames[0], new RectangleD(1.5, 0, 1, 1), new RectangleD(0, 0, 1, 1), ImageSampling.Bilinear));
            AssertThrows<ArgumentException>(canvas, canvas => canvas.DrawImage(pair.Frames[0], new RectangleD(double.NaN, 0, 1, 1), new RectangleD(0, 0, 1, 1), ImageSampling.Bilinear));
            AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(pair.Frames[0], new RectangleD(0, 0, 1, 1), new RectangleD(0, 0, 1, 1), (ImageSampling)5));
        }, Ct);

        int[] Draw(RectangleD region, ImageSampling sampling)
        {
            using var canvas = new Image<Gray8>(4, 1);
            canvas.Draw(c => c.DrawImage(pair.Frames[0], region, new RectangleD(0, 0, 4, 1), sampling), Ct);
            return [.. Enumerable.Range(0, 4).Select(x => (int)canvas.Frames[0][x, 0].Value)];
        }

        static int[] Render(int width, int height, DrawingAction action)
        {
            using var canvas = new Image<Rgba64>(width, height);
            canvas.Draw(action, Ct);
            return DrawingTestSupport.ReadSamples(canvas.Frames[0]);
        }
    }

    [Fact]
    public void BicubicSamplingWeighsSixteenPixelsWithTheFilterOfMitchellAndNetravali()
    {
        // At the center of a pixel, the weights of the columns around it are k(1) = 1/18, k(0) = 8/9 and k(1) = 1/18:
        // a white pixel between black ones keeps 8/9 of its white (227), and gives 1/18 of it to each of them (14). The
        // edges of the image are extended.
        using var line = new Image<Gray8>(4, 1);
        line.Frames[0][1, 0] = new Gray8(255);
        using var canvas = new Image<Gray8>(4, 1);
        canvas.Draw(c => c.DrawImage(line.Frames[0], new RectangleD(0, 0, 4, 1), ImageSampling.Bicubic), Ct);
        Assert.Equal([14, 227, 14, 0], Enumerable.Range(0, 4).Select(x => (int)canvas.Frames[0][x, 0].Value));

        // The weights add up to one: an image of one color keeps it, enlarged, turned, or in perspective
        using var flat = new Image<Rgba32>(3, 3, new Rgba32(200, 100, 50));
        using var target = new Image<Rgba32>(20, 20);
        target.Draw(c => c.DrawImage(flat.Frames[0], AffineTransform.CreateScale(4, 4).Then(AffineTransform.CreateRotation(20, new PointD(6, 6))).Then(AffineTransform.CreateTranslation(4, 2)), ImageSampling.Bicubic), Ct);
        Assert.Equal(new Rgba32(200, 100, 50), target.Frames[0][9, 8]);
        target.Draw(c => c.Clear(Colors.Transparent), Ct);
        target.Draw(c => c.DrawImage(flat.Frames[0], new PointD(2, 2), new PointD(18, 3), new PointD(16, 17), new PointD(3, 15), ImageSampling.Bicubic), Ct);
        Assert.Equal(new Rgba32(200, 100, 50), target.Frames[0][10, 9]);

        // A brush too, and the negative lobes of the filter never take a color beyond its range: white to black
        // enlarged eight times stays between them, and is white and black far from the edge
        using var steps = new Image<Gray8>(2, 1);
        steps.Frames[0][0, 0] = new Gray8(255);
        using var enlarged = new Image<Gray8>(16, 1);
        enlarged.Draw(c => c.Fill(new ImageBrush(steps.Frames[0]) { Transform = AffineTransform.CreateScale(8, 1), Sampling = ImageSampling.Bicubic, WrapMode = ImageWrapMode.Pad }), Ct);
        Assert.Equal(255, enlarged.Frames[0][0, 0].Value);
        Assert.Equal(0, enlarged.Frames[0][15, 0].Value);
        var samples = Enumerable.Range(0, 16).Select(x => (int)enlarged.Frames[0][x, 0].Value).ToArray();
        Assert.Equal(samples.OrderByDescending(sample => sample), samples);
    }

    [Fact]
    public void TheSamplingAndTheTransformOfAnImageBrush()
    {
        using var source = new Image<Gray8>(2, 1);
        DrawingTestSupport.WriteSamples(source.Frames[0], [0, 255]);

        // Twice as wide: with the nearest pixel, each pixel becomes two; interpolated, the image fades into the
        // transparent pixels around it (the first center is 0.25 before the center of the black pixel, three quarters
        // of it and a quarter of nothing: black at 3/4, which changes nothing on black; the last one is white at 3/4)
        var scale = AffineTransform.CreateScale(2, 1);
        Assert.Equal([0, 0, 255, 255], Fill(4, 1, new ImageBrush(source.Frames[0]) { Transform = scale, Sampling = ImageSampling.Nearest }));
        Assert.Equal([0, 64, 191, 191], Fill(4, 1, new ImageBrush(source.Frames[0]) { Transform = scale }));
        Assert.Equal([0, 64, 191, 255], Fill(4, 1, new ImageBrush(source.Frames[0]) { Transform = scale, WrapMode = ImageWrapMode.Pad }));

        // Half a pixel to the right: a white pixel is half on each of two pixels, at half its opacity on both
        using var white = new Image<Gray8>(1, 1, new Gray8(255));
        Assert.Equal([128, 128, 0], Fill(3, 1, new ImageBrush(white.Frames[0]) { Transform = AffineTransform.CreateTranslation(0.5, 0) }));
        Assert.Equal([0, 255, 0], Fill(3, 1, new ImageBrush(white.Frames[0]) { Transform = AffineTransform.CreateTranslation(1.25, 0), Sampling = ImageSampling.Nearest }));

        // Turned by a quarter turn (clockwise on the image) around the origin, then moved back onto the canvas: the row
        // becomes a column, read from the top
        var turned = AffineTransform.CreateRotation(90).Then(AffineTransform.CreateTranslation(1, 0));
        Assert.Equal([0, 255], Fill(1, 2, new ImageBrush(source.Frames[0]) { Transform = turned }));
        Assert.Equal([0, 255], Fill(1, 2, new ImageBrush(source.Frames[0]) { Transform = turned, Sampling = ImageSampling.Nearest }));

        // A transformation that flattens the plane paints nothing
        Assert.Equal([7, 7], Fill(2, 1, new ImageBrush(source.Frames[0]) { Transform = AffineTransform.CreateScale(0, 1), WrapMode = ImageWrapMode.Repeat }, background: 7));

        int[] Fill(int width, int height, ImageBrush brush, byte background = 0)
        {
            using var image = new Image<Gray8>(width, height, new Gray8(background));
            image.Draw(canvas => canvas.Fill(brush), Ct);
            return DrawingTestSupport.ReadSamples(image.Frames[0]);
        }
    }

    [Theory]
    [InlineData(ImageSampling.Bilinear, ImageWrapMode.Pad)]
    [InlineData(ImageSampling.Bilinear, ImageWrapMode.Transparent)]
    [InlineData(ImageSampling.Bilinear, ImageWrapMode.Repeat)]
    [InlineData(ImageSampling.Bilinear, ImageWrapMode.Reflect)]
    [InlineData(ImageSampling.Nearest, ImageWrapMode.Reflect)]
    [InlineData(ImageSampling.Nearest, ImageWrapMode.Transparent)]
    [InlineData(ImageSampling.Smooth, ImageWrapMode.Transparent)]
    [InlineData(ImageSampling.Smooth, ImageWrapMode.Pad)]
    [InlineData(ImageSampling.Smooth, ImageWrapMode.Repeat)]
    [InlineData(ImageSampling.Smooth, ImageWrapMode.Reflect)]
    [InlineData(ImageSampling.Box, ImageWrapMode.Transparent)]
    [InlineData(ImageSampling.Box, ImageWrapMode.Pad)]
    [InlineData(ImageSampling.Box, ImageWrapMode.Repeat)]
    [InlineData(ImageSampling.Box, ImageWrapMode.Reflect)]
    public void AnyPlacementFollowsTheDefinitionOfTheInterpolation(ImageSampling sampling, ImageWrapMode wrapMode)
    {
        // The image replaces transparent 16-bit pixels, so that each pixel of the canvas is the color the brush gave it.
        // The definition is written again below, with decimal arithmetic; a sample may differ by one unit where the two
        // computations do not round the same way
        var random = new Random(9100 + ((int)sampling * 10) + (int)wrapMode);
        for (var index = 0; index < 60; index++)
        {
            using var source = DrawingTestSupport.CreateRandom(PixelFormat.Rgba64, random.Next(1, 6), random.Next(1, 6), random.Next);
            var region = index % 3 == 0 ? new Rectangle(random.Next(source.Width), random.Next(source.Height), 1, 1) : new Rectangle(0, 0, source.Width, source.Height);
            region = new Rectangle(region.X, region.Y, random.Next(1, source.Width - region.X + 1), random.Next(1, source.Height - region.Y + 1));

            // No rotation for two cases out of three: the rows of the canvas are then rows of the image
            var transform = AffineTransform.CreateScale(0.25 + (random.NextDouble() * 3), 0.25 + (random.NextDouble() * 3)).Then(AffineTransform.CreateTranslation((random.NextDouble() * 6) - 2, (random.NextDouble() * 6) - 2));
            if (index % 3 == 1)
            {
                transform = AffineTransform.CreateRotation(random.NextDouble() * 360, new PointD(2, 2)).Then(transform);
            }
            else if (index % 6 == 0)
            {
                transform = AffineTransform.CreateScale(random.Next(1, 3), random.Next(1, 3)).Then(AffineTransform.CreateTranslation(random.Next(-2, 3), random.Next(-2, 3)));
            }

            var brush = new ImageBrush(source.Frames[0]) { SourceRegion = region, Transform = transform, WrapMode = wrapMode, Sampling = sampling, CompositeMode = CompositeMode.Source };
            using var image = new Image<Rgba64>(7, 6);
            image.Draw(canvas => canvas.Fill(brush), Ct);
            Assert.True(transform.TryInvert(out var inverse));
            for (var y = 0; y < 6; y++)
            {
                for (var x = 0; x < 7; x++)
                {
                    var position = inverse.Transform(new PointD(x + 0.5, y + 0.5));
                    var expected = Sample(source, region, position, inverse, sampling, wrapMode);
                    var actual = image.Frames[0][x, y];
                    var description = $"case {index}, pixel ({x}, {y}) from {position} of region {region} with {transform}: expected {expected}, actual {actual}";
                    if (expected.A <= 1 || actual.A <= 1)
                    {
                        // Hardly any opacity: the color is not defined
                        Assert.True(Math.Abs(expected.A - actual.A) <= 1, description);
                        continue;
                    }

                    // A color multiplied by a small alpha has few significant digits left
                    var tolerance = 1 + (65535 / expected.A / 100);
                    Assert.True(Math.Abs(expected.A - actual.A) <= 1 && Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance, description);
                }
            }
        }

        static Rgba64 Sample(Image source, Rectangle region, PointD position, AffineTransform inverse, ImageSampling sampling, ImageWrapMode wrapMode)
        {
            if (sampling == ImageSampling.Nearest)
                return Read(source, region, (int)Math.Floor(position.X), (int)Math.Floor(position.Y), wrapMode) ?? default;

            // What a pixel of the canvas covers of the image, limited to the region: more than a pixel when the image is reduced
            var spanX = Math.Min(Math.Sqrt((inverse.M11 * inverse.M11) + (inverse.M21 * inverse.M21)), region.Width);
            var spanY = Math.Min(Math.Sqrt((inverse.M12 * inverse.M12) + (inverse.M22 * inverse.M22)), region.Height);
            if (sampling is ImageSampling.Smooth or ImageSampling.Box && (spanX > 1 || spanY > 1))
                return Reduce(source, region, position, (decimal)Math.Max(spanX, 1), (decimal)Math.Max(spanY, 1), wrapMode, sampling == ImageSampling.Box);

            var column = (int)Math.Floor(position.X - 0.5);
            var row = (int)Math.Floor(position.Y - 0.5);
            var fx = (decimal)(position.X - 0.5 - column);
            var fy = (decimal)(position.Y - 0.5 - row);
            decimal red = 0, green = 0, blue = 0, alpha = 0;
            foreach (var (dx, dy, weight) in new[] { (0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy) })
            {
                if (weight == 0 || Read(source, region, column + dx, row + dy, wrapMode) is not { } color)
                    continue;

                red += weight * color.R * color.A;
                green += weight * color.G * color.A;
                blue += weight * color.B * color.A;
                alpha += weight * color.A;
            }

            var roundedAlpha = Math.Round(alpha, MidpointRounding.AwayFromZero);
            if (roundedAlpha == 0)
                return default;

            return new Rgba64((ushort)Math.Round(red / alpha, MidpointRounding.AwayFromZero), (ushort)Math.Round(green / alpha, MidpointRounding.AwayFromZero), (ushort)Math.Round(blue / alpha, MidpointRounding.AwayFromZero), (ushort)roundedAlpha);
        }

        static Rgba64 Reduce(Image source, Rectangle region, PointD position, decimal spanX, decimal spanY, ImageWrapMode wrapMode, bool isBox)
        {
            // Every pixel whose center is within the span of the position, weighted by its distance to it; or, with a
            // box, every pixel weighted by the length of its part inside the span centered on the position
            var u = (decimal)position.X;
            var v = (decimal)position.Y;
            decimal red = 0, green = 0, blue = 0, alpha = 0, total = 0;
            for (var row = (int)Math.Floor(v - spanY) - 1; row <= (int)Math.Ceiling(v + spanY) + 1; row++)
            {
                var weightY = Weight(row, v, spanY, isBox);
                if (weightY <= 0)
                    continue;

                for (var column = (int)Math.Floor(u - spanX) - 1; column <= (int)Math.Ceiling(u + spanX) + 1; column++)
                {
                    var weight = weightY * Weight(column, u, spanX, isBox);
                    if (weight <= 0)
                        continue;

                    total += weight;
                    if (Read(source, region, column, row, wrapMode) is { } color)
                    {
                        red += weight * color.R * color.A;
                        green += weight * color.G * color.A;
                        blue += weight * color.B * color.A;
                        alpha += weight * color.A;
                    }
                }
            }

            var roundedAlpha = Math.Round(alpha / total, MidpointRounding.AwayFromZero);
            if (roundedAlpha == 0)
                return default;

            return new Rgba64((ushort)Math.Round(red / alpha, MidpointRounding.AwayFromZero), (ushort)Math.Round(green / alpha, MidpointRounding.AwayFromZero), (ushort)Math.Round(blue / alpha, MidpointRounding.AwayFromZero), (ushort)roundedAlpha);
        }

        static decimal Weight(int index, decimal position, decimal span, bool isBox)
            => isBox ? Math.Min(index + 1, position + (span / 2)) - Math.Max(index, position - (span / 2)) : 1 - (Math.Abs(index + 0.5m - position) / span);

        static Rgba64? Read(Image source, Rectangle region, int column, int row, ImageWrapMode wrapMode)
        {
            if (Wrap(column - region.X, region.Width, wrapMode) is not { } x || Wrap(row - region.Y, region.Height, wrapMode) is not { } y)
                return null;

            return ((Image<Rgba64>)source).Frames[0][region.X + x, region.Y + y];
        }

        static int? Wrap(int offset, int size, ImageWrapMode wrapMode)
        {
            if (offset >= 0 && offset < size)
                return offset;

            switch (wrapMode)
            {
                case ImageWrapMode.Pad:
                    return Math.Clamp(offset, 0, size - 1);

                case ImageWrapMode.Repeat:
                    return ((offset % size) + size) % size;

                case ImageWrapMode.Reflect:
                    var folded = ((offset % (2 * size)) + (2 * size)) % (2 * size);
                    return folded < size ? folded : (2 * size) - 1 - folded;

                default:
                    return null;
            }
        }
    }

    [Fact]
    public void AnImageBrushCanMaskAndEraseWithAnOperator()
    {
        // The alpha of an image as a mask: the pixels are kept where the image is opaque
        using var mask = new Image<Rgba32>(2, 1);
        mask.Frames[0][0, 0] = new Rgba32(0, 0, 0);
        mask.Frames[0][1, 0] = new Rgba32(9, 9, 9, 51);
        using var image = new Image<Rgba32>(3, 1, new Rgba32(255, 0, 0, 200));
        image.Draw(canvas => canvas.FillRectangle(new ImageBrush(mask.Frames[0]) { CompositeMode = CompositeMode.DestinationIn }, new Rectangle(0, 0, 3, 1)), Ct);

        // 200 kept, 200 * 51 / 255 = 40, and erased where the image is not (it is transparent around its pixels)
        Assert.Equal(new Rgba32(255, 0, 0, 200), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 40), image.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), image.Frames[0][2, 0]);

        // Replacing: the pixels of the image, alpha included
        image.Draw(canvas => canvas.FillRectangle(new ImageBrush(mask.Frames[0]) { CompositeMode = CompositeMode.Source }, new Rectangle(0, 0, 2, 1)), Ct);
        Assert.Equal(new Rgba32(0, 0, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(9, 9, 9, 51), image.Frames[0][1, 0]);
    }

    [Fact]
    public void AnImageCannotBeDrawnOnItselfAndMustBeAlive()
    {
        using var image = new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 4));
        var other = new Image<Rgba32>(2, 2, new Rgba32(200, 100, 50));
        var frame = other.Frames[0];
        var second = image.AppendFrame();
        image.Draw(
            canvas =>
            {
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.DrawImage(null!, default(PointD)));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.DrawImage(null!, new Rectangle(0, 0, 1, 1)));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.DrawImage(null!, new Rectangle(0, 0, 1, 1), new Rectangle(0, 0, 1, 1)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, default(PointD), 1.5));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, default(PointD), -0.1));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, default(PointD), double.NaN));
                AssertThrows<ArgumentException>(canvas, canvas => canvas.DrawImage(frame, new PointD(double.NaN, 0)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, new RectangleD(16777216, 0, 1, 1)));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.DrawImage(null!, AffineTransform.Identity));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.DrawImage(null!, new Rectangle(0, 0, 1, 1), ImageSampling.Smooth));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.DrawImage(null!, new Rectangle(0, 0, 1, 1), new Rectangle(0, 0, 1, 1), ImageSampling.Smooth));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.DrawImage(null!, AffineTransform.Identity, ImageSampling.Smooth));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, new Rectangle(0, 0, 1, 1), (ImageSampling)5));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, new Rectangle(0, 0, 1, 1), new Rectangle(0, 0, 1, 1), (ImageSampling)(-1)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, AffineTransform.Identity, (ImageSampling)5));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, new Rectangle(0, 0, 1, 1), ImageSampling.Smooth, 2));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, new Rectangle(0, 0, 3, 3), new Rectangle(0, 0, 1, 1), ImageSampling.Smooth));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, AffineTransform.Identity, 1.5));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, AffineTransform.CreateTranslation(16777216, 0)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, AffineTransform.CreateScale(1e7, 1)));
                AssertThrows<InvalidOperationException>(canvas, canvas => canvas.DrawImage(image.Frames[0], AffineTransform.Identity));

                // The frames that are drawn on are leased by the drawing
                AssertThrows<InvalidOperationException>(canvas, canvas => canvas.DrawImage(image.Frames[0], default(PointD)));
                AssertThrows<InvalidOperationException>(canvas, canvas => canvas.Fill(new ImageBrush(second)));

                // Nothing was drawn, and the canvas still draws
                canvas.DrawImage(frame, new Point(2, 2));
            },
            Ct);
        Assert.Equal(new Rgba32(1, 2, 3, 4), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(200, 100, 50), image.Frames[0][2, 2]);
        Assert.Equal(new Rgba32(200, 100, 50), image.Frames[1][3, 3]);
        Assert.Equal(0, other.Owner.ActiveLeaseCount);

        // A frame of the same image can be drawn on another one of its frames
        image.Frames[0].Draw(canvas => canvas.DrawImage(image.Frames[1], new Point(-2, -2)), Ct);
        Assert.Equal(new Rgba32(200, 100, 50), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(200, 100, 50), image.Frames[0][1, 1]);
        Assert.Equal(new Rgba32(1, 2, 3, 4), image.Frames[0][2, 0]);

        // A source whose pixels are being accessed
        frame.ProcessPixelRows(_ => image.Draw(canvas => AssertThrows<InvalidOperationException>(canvas, canvas => canvas.DrawImage(frame, default(PointD))), Ct));

        var brush = new ImageBrush(frame);
        other.Dispose();
        image.Draw(
            canvas =>
            {
                AssertThrows<ObjectDisposedException>(canvas, canvas => canvas.DrawImage(frame, default(PointD)));
                AssertThrows<ObjectDisposedException>(canvas, canvas => canvas.DrawImage(frame, AffineTransform.Identity));
                AssertThrows<ObjectDisposedException>(canvas, canvas => canvas.Fill(brush));
            },
            Ct);

        Assert.Throws<ArgumentNullException>(() => new ImageBrush(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(image.Frames[0]) { WrapMode = (ImageWrapMode)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(image.Frames[0]) { Sampling = (ImageSampling)5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(image.Frames[0]) { Opacity = 1.01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(image.Frames[0]) { Opacity = double.NaN });

        // The region of a brush is checked when it paints
        var outside = new ImageBrush(image.Frames[1]) { SourceRegion = new Rectangle(2, 2, 3, 3) };
        image.Frames[0].Draw(canvas => AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.Fill(outside)), Ct);
    }

    [Fact]
    public void AnImageOnAParallelogramIsPlacedByAnAffineTransformation()
    {
        var random = new Random(8086);
        using var source = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 6, 5, random.Next);
        var frame = source.Frames[0];

        // The corners of the image where it is: its pixels, exactly
        using var expected = new Image<Rgba32>(12, 10, new Rgba32(9, 9, 9));
        using var actual = expected.Clone();
        expected.Draw(canvas => canvas.DrawImage(frame, new Point(3, 2)), Ct);
        actual.Draw(canvas => canvas.DrawImage(frame, new Point(3, 2), new Point(9, 2), new Point(9, 7), new Point(3, 7)), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));

        // Any parallelogram, with any sampling and opacity: the transformation that maps the corners
        foreach (var sampling in new[] { ImageSampling.Nearest, ImageSampling.Bilinear, ImageSampling.Smooth })
        {
            using var transformed = new Image<Rgba32>(12, 10, new Rgba32(9, 9, 9));
            using var cornered = transformed.Clone();
            transformed.Draw(canvas => canvas.DrawImage(frame, new AffineTransform(1.5, 0.25, -0.5, 1, 4, 1), sampling, 0.75), Ct);
            cornered.Draw(canvas => canvas.DrawImage(frame, new PointD(4, 1), new PointD(13, 2.5), new PointD(10.5, 7.5), new PointD(1.5, 6), sampling, 0.75), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(transformed.Frames[0]), DrawingTestSupport.ReadSamples(cornered.Frames[0]));
        }
    }

    [Fact]
    public void AnImageOnAQuadrilateralIsAPlaneSeenInPerspective()
    {
        // An image whose pixels tell where they are: red is ten times the column, green ten times the row
        using var source = new Image<Rgba32>(20, 20);
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                source.Frames[0][x, y] = new Rgba32((byte)(x * 10), (byte)(y * 10), 77);
            }
        }

        var frame = source.Frames[0];
        var random = new Random(1999);
        PointD[][] quadrilaterals =
        [
            [new(10, 6), new(54, 14), new(60, 58), new(4, 40)],
            [new(20, 4), new(44, 4), new(62, 60), new(2, 60)],
            [new(8, 8), new(56, 20), new(50, 44), new(12, 56)],

            // The other way around: the image is mirrored
            [new(54, 14), new(10, 6), new(4, 40), new(60, 58)],
        ];
        foreach (var corners in quadrilaterals)
        {
            // The transformation that brings the corners back to those of the image, from its definition: the eight
            // equations of the four corners, solved here
            var inverse = SolveProjection(corners, [new(0, 0), new(20, 0), new(20, 20), new(0, 20)]);
            using var image = new Image<Rgba32>(64, 64);
            image.Draw(canvas => canvas.DrawImage(frame, corners[0], corners[1], corners[2], corners[3], ImageSampling.Nearest), Ct);
            var inside = 0;
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var pixel = image.Frames[0][x, y];
                    if (pixel.A != 255)
                        continue;

                    var w = (inverse[6] * (x + 0.5)) + (inverse[7] * (y + 0.5)) + 1;
                    var u = ((inverse[0] * (x + 0.5)) + (inverse[1] * (y + 0.5)) + inverse[2]) / w;
                    var v = ((inverse[3] * (x + 0.5)) + (inverse[4] * (y + 0.5)) + inverse[5]) / w;

                    // The pixel of the image that contains the position, or its neighbor when the position is on its edge
                    Assert.True(Math.Abs((pixel.R / 10) + 0.5 - Math.Clamp(u, 0.5, 19.5)) <= 0.5 + 1e-6, $"pixel ({x}, {y}): column {pixel.R / 10} for {u}");
                    Assert.True(Math.Abs((pixel.G / 10) + 0.5 - Math.Clamp(v, 0.5, 19.5)) <= 0.5 + 1e-6, $"pixel ({x}, {y}): row {pixel.G / 10} for {v}");
                    Assert.Equal(77, pixel.B);
                    inside++;
                }
            }

            Assert.InRange(inside, 900, 4096);

            // A transformation of the canvas moves the quadrilateral with everything else
            var (dx, dy) = (random.Next(-3, 4), random.Next(-3, 4));
            using var moved = new Image<Rgba32>(64, 64);
            using var transformed = new Image<Rgba32>(64, 64);
            moved.Draw(canvas => canvas.DrawImage(frame, corners[0] + new PointD(dx, dy), corners[1] + new PointD(dx, dy), corners[2] + new PointD(dx, dy), corners[3] + new PointD(dx, dy)), Ct);
            transformed.Draw(canvas => canvas.Transform(AffineTransform.CreateTranslation(dx, dy), inner => inner.DrawImage(frame, corners[0], corners[1], corners[2], corners[3])), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(moved.Frames[0]), DrawingTestSupport.ReadSamples(transformed.Frames[0]));
        }

        // The opacity fades the image, and its edges are antialiased
        using var faded = new Image<Rgba32>(64, 64);
        faded.Draw(canvas => canvas.DrawImage(frame, new PointD(10, 6), new PointD(54, 14), new PointD(60, 58), new PointD(4, 40), ImageSampling.Bilinear, 0.5), Ct);
        Assert.Equal(128, faded.Frames[0][30, 30].A);
        Assert.Equal(0, faded.Frames[0][2, 2].A);
        Assert.Contains(DrawingTestSupport.ReadSamples(faded.Frames[0]).Chunk(4), pixel => pixel[3] is > 0 and < 128);
    }

    [Fact]
    public void AReducedImageInPerspectiveCanBeReadWithoutSkippingPixels()
    {
        // A checkerboard of single pixels, drawn eight times smaller: one pixel of it for each pixel of the canvas is
        // black or white, and all the pixels a pixel of the canvas covers are gray
        using var source = new Image<Gray8>(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                source.Frames[0][x, y] = new Gray8((byte)(((x + y) & 1) * 255));
            }
        }

        using var skipped = new Image<Gray8>(16, 16, new Gray8(7));
        using var smooth = new Image<Gray8>(16, 16, new Gray8(7));
        PointD[] corners = [new(3, 3), new(12, 4), new(13, 13), new(2, 11)];
        skipped.Draw(canvas => canvas.DrawImage(source.Frames[0], corners[0], corners[1], corners[2], corners[3], ImageSampling.Nearest), Ct);
        smooth.Draw(canvas => canvas.DrawImage(source.Frames[0], corners[0], corners[1], corners[2], corners[3], ImageSampling.Smooth), Ct);
        for (var y = 6; y < 10; y++)
        {
            for (var x = 6; x < 10; x++)
            {
                Assert.True(skipped.Frames[0][x, y].Value is 0 or 255);
                Assert.InRange(smooth.Frames[0][x, y].Value, 118, 138);
            }
        }

        Assert.Equal(new Gray8(7), smooth.Frames[0][0, 0]);
    }

    [Fact]
    public void TheCornersOfAQuadrilateralMustBeThoseOfAConvexOne()
    {
        using var image = new Image<Rgba32>(16, 16, new Rgba32(1, 2, 3, 4));
        using var other = new Image<Rgba32>(4, 4, new Rgba32(200, 100, 50));
        var frame = other.Frames[0];
        var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
        image.Draw(
            canvas =>
            {
                // Two sides that cross, a corner that points inward, three corners on a line
                AssertThrows<ArgumentException>(canvas, canvas => canvas.DrawImage(frame, new PointD(2, 2), new PointD(12, 2), new PointD(2, 12), new PointD(12, 12)));
                AssertThrows<ArgumentException>(canvas, canvas => canvas.DrawImage(frame, new PointD(2, 2), new PointD(12, 2), new PointD(5, 5), new PointD(2, 12)));
                AssertThrows<ArgumentException>(canvas, canvas => canvas.DrawImage(frame, new PointD(2, 2), new PointD(7, 2), new PointD(12, 2), new PointD(7, 9)));
                AssertThrows<ArgumentException>(canvas, canvas => canvas.DrawImage(frame, new PointD(double.NaN, 2), new PointD(12, 2), new PointD(12, 12), new PointD(2, 12)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, new PointD(2, 2), new PointD(12, 2), new PointD(16777217, 12), new PointD(2, 12)));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, new PointD(2, 2), new PointD(12, 2), new PointD(12, 12), new PointD(2, 12), (ImageSampling)5));
                AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.DrawImage(frame, new PointD(2, 2), new PointD(12, 3), new PointD(12, 12), new PointD(2, 12), ImageSampling.Smooth, 1.5));
                AssertThrows<ArgumentNullException>(canvas, static canvas => canvas.DrawImage(null!, new PointD(2, 2), new PointD(12, 3), new PointD(12, 12), new PointD(2, 12)));
                AssertThrows<InvalidOperationException>(canvas, canvas => canvas.DrawImage(image.Frames[0], new PointD(2, 2), new PointD(12, 3), new PointD(12, 12), new PointD(2, 12)));

                // Corners that a transformation sends beyond the range of the coordinates draw what the canvas shows:
                // nothing here
                canvas.Transform(AffineTransform.CreateScale(1e7, 1), inner => inner.DrawImage(frame, new PointD(2, 2), new PointD(12, 3), new PointD(12, 12), new PointD(2, 12)));

                // Corners that are all on a line draw nothing, as an image that a transformation flattens
                canvas.DrawImage(frame, new PointD(2, 2), new PointD(6, 6), new PointD(12, 12), new PointD(4, 4));
                canvas.DrawImage(frame, new PointD(3, 3), new PointD(3, 3), new PointD(3, 3), new PointD(3, 3));
                canvas.Transform(AffineTransform.CreateScale(1, 0), inner => inner.DrawImage(frame, new PointD(2, 2), new PointD(12, 3), new PointD(12, 12), new PointD(2, 12)));
            },
            Ct);
        Assert.Equal(before, DrawingTestSupport.ReadSamples(image.Frames[0]));
        Assert.Equal(0, other.Owner.ActiveLeaseCount);

        other.Dispose();
        image.Draw(canvas => AssertThrows<ObjectDisposedException>(canvas, canvas => canvas.DrawImage(frame, new PointD(2, 2), new PointD(12, 3), new PointD(12, 12), new PointD(2, 12))), Ct);
    }

    [Fact]
    public void APatternBrushRepeatsTheDrawingOfItsTile()
    {
        // A tile of 4 by 4 with a red square of 2 by 2 in its corner, on the pixels of the canvas: exactly that
        var red = new Rgba32(255, 0, 0);
        var calls = 0;
        var sizes = new List<Size>();
        var brush = new PatternBrush(new Rectangle(0, 0, 4, 4), tile =>
        {
            // The canvas of a tile is its image: as many pixels as the tile has on the canvas that is painted
            calls++;
            sizes.Add(new Size(tile.Width, tile.Height));
            tile.FillRectangle(red, new Rectangle(0, 0, 2, 2));
        });
        Assert.Equal(new RectangleD(0, 0, 4, 4), brush.Tile);
        Assert.True(brush.Transform.IsIdentity);
        using var image = new Image<Rgba32>(10, 9);
        image.Draw(
            canvas =>
            {
                canvas.FillRectangle(brush, new Rectangle(0, 0, 10, 5));
                canvas.FillRectangle(brush, new Rectangle(0, 5, 10, 4));
            },
            Ct);
        AssertPattern(image, (x, y) => x % 4 < 2 && y % 4 < 2);

        // The tile is drawn once for the shapes that follow each other with the same brush
        Assert.Equal(1, calls);

        // The transformation of the brush places the pattern: one pixel to the right, two down
        using var moved = new Image<Rgba32>(10, 9);
        moved.Draw(canvas => canvas.Fill(new PatternBrush(brush.Tile, brush.Content) { Transform = AffineTransform.CreateTranslation(1, 2) }), Ct);
        AssertPattern(moved, (x, y) => (x + 3) % 4 < 2 && (y + 2) % 4 < 2);

        // A tile that is not at the origin repeats from where it is, and what is drawn outside it is not painted
        using var anchored = new Image<Rgba32>(10, 9);
        anchored.Draw(
            canvas => canvas.Fill(new PatternBrush(new Rectangle(2, 1, 4, 4), tile =>
            {
                tile.FillRectangle(red, new Rectangle(2, 1, 2, 2));
                tile.FillRectangle(new Rgba32(0, 255, 0), new Rectangle(6, 1, 20, 20));
                tile.FillRectangle(new Rgba32(0, 255, 0), new Rectangle(-20, -20, 22, 21));
            })),
            Ct);
        AssertPattern(anchored, (x, y) => (x + 2) % 4 < 2 && (y + 3) % 4 < 2);

        // Under a transformation of the canvas, the tile is drawn at the size it has there: twice as large, as sharp
        using var scaled = new Image<Rgba32>(20, 18);
        scaled.Draw(canvas => canvas.Transform(AffineTransform.CreateScale(2, 2), inner => inner.Fill(brush)), Ct);
        AssertPattern(scaled, (x, y) => x % 8 < 4 && y % 8 < 4);
        Assert.Equal([new Size(4, 4), new Size(4, 4), new Size(8, 8)], sizes);

        // The tile is drawn as the shapes it paints are: again, with hard edges, when antialiasing is turned off
        var half = new PatternBrush(new Rectangle(0, 0, 2, 1), tile =>
        {
            calls++;
            tile.FillRectangle(red, new RectangleD(0, 0, 0.75, 1));
        });
        using var hard = new Image<Rgba32>(4, 1);
        calls = 0;
        hard.Draw(
            canvas =>
            {
                canvas.FillRectangle(half, new Rectangle(0, 0, 2, 1));
                canvas.Antialiasing(enabled: false, inner => inner.FillRectangle(half, new Rectangle(2, 0, 2, 1)));
            },
            Ct);
        Assert.Equal([191, 0, 255, 0], Enumerable.Range(0, 4).Select(x => (int)hard.Frames[0][x, 0].A));
        Assert.Equal(2, calls);

        // Between pixels, the image of the tile is interpolated, across the edges of the tiles too
        using var between = new Image<Rgba32>(8, 1);
        between.Draw(canvas => canvas.Fill(new PatternBrush(brush.Tile, brush.Content) { Transform = AffineTransform.CreateTranslation(0.5, 0) }), Ct);
        Assert.Equal([128, 255, 128, 0, 128, 255, 128, 0], Enumerable.Range(0, 8).Select(x => (int)between.Frames[0][x, 0].A));
        Assert.Equal(new Rgba32(255, 0, 0, 128), between.Frames[0][4, 0]);

        static void AssertPattern(Image<Rgba32> image, Func<int, int, bool> isRed)
        {
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    Assert.Equal(isRed(x, y) ? new Rgba32(255, 0, 0) : default, image.Frames[0][x, y], $"pixel ({x}, {y})");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void APatternBrushPaintsAsTheImageOfItsTile(PixelFormat format)
    {
        // Whatever the canvas, the shape, the operator and the blend mode: what an image brush paints with the image
        // of the tile, drawn by the same callback. The pattern is turned and moved, which keeps the size of its tile.
        var random = new Random(31337 + (int)format);
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(5, 3), [new GradientStop(0, new Rgba32(250, 220, 10)), new GradientStop(1, new Rgba32(20, 40, 200, 60))]);
        void Tile(DrawingCanvas canvas)
        {
            canvas.Fill(gradient);
            canvas.FillCircle(new Rgba32(255, 255, 255, 200), new PointD(2.5, 1.5), 1.2);
        }

        var isWide = format is not (PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24);
        using Image tile = isWide ? new Image<Rgba64>(5, 3) : new Image<Rgba32>(5, 3);
        tile.Draw(Tile, Ct);
        var placement = AffineTransform.CreateRotation(30, new PointD(3, 2)).Then(AffineTransform.CreateTranslation(2.5, -1));
        foreach (var (mode, blend) in new[] { (CompositeMode.SourceOver, BlendMode.Normal), (CompositeMode.DestinationOut, BlendMode.Normal), (CompositeMode.SourceAtop, BlendMode.Multiply) })
        {
            using var expected = DrawingTestSupport.CreateRandom(format, 14, 11, random.Next);
            using var actual = expected.Clone();
            var imageBrush = new ImageBrush(tile.Frames[0]) { WrapMode = ImageWrapMode.Repeat, Transform = placement, CompositeMode = mode, BlendMode = blend };
            var patternBrush = new PatternBrush(new Rectangle(0, 0, 5, 3), Tile) { Transform = placement, CompositeMode = mode, BlendMode = blend };
            void Draw(DrawingCanvas canvas, Brush brush)
            {
                canvas.FillEllipse(brush, new PointD(7, 5.5), 6.2, 4.8);
                canvas.StrokeLine(new Pen(brush, 2.5), new PointD(0, 0), new PointD(14, 11));
            }

            expected.Draw(canvas => Draw(canvas, imageBrush), Ct);
            actual.Draw(canvas => Draw(canvas, patternBrush), Ct);
            Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));
        }
    }

    [Theory]
    [MemberData(nameof(DrawingTestSupport.AllFormats), MemberType = typeof(DrawingTestSupport))]
    public void APatternBrushWithEightBitTilesPaintsAsTheImageOfAnEightBitTile(PixelFormat format)
    {
        // Whatever the canvas: what an image brush paints with an Rgba32 image of the tile, drawn by the same callback
        var random = new Random(4242 + (int)format);
        var gradient = new LinearGradientBrush(new PointD(0, 0), new PointD(5, 3), [new GradientStop(0, new Rgba64(0x1234, 0xFEDC, 0x0101, 0xFFFF)), new GradientStop(1, new Rgba64(0x8081, 0x2223, 0xCDEF, 0x7777))]);
        void Tile(DrawingCanvas canvas)
        {
            canvas.Fill(gradient);
            canvas.FillCircle(new Rgba64(0xFFFF, 0x7FFF, 0x3333, 0xC8C8), new PointD(2.5, 1.5), 1.2);
        }

        using var tile = new Image<Rgba32>(5, 3);
        tile.Draw(Tile, Ct);
        var placement = AffineTransform.CreateRotation(30, new PointD(3, 2)).Then(AffineTransform.CreateTranslation(2.5, -1));
        using var expected = DrawingTestSupport.CreateRandom(format, 14, 11, random.Next);
        using var actual = expected.Clone();
        var imageBrush = new ImageBrush(tile.Frames[0]) { WrapMode = ImageWrapMode.Repeat, Transform = placement };
        var patternBrush = new PatternBrush(new Rectangle(0, 0, 5, 3), Tile) { Transform = placement, TilePrecision = PatternTilePrecision.EightBit };
        expected.Draw(canvas => canvas.FillEllipse(imageBrush, new PointD(7, 5.5), 6.2, 4.8), Ct);
        actual.Draw(canvas => canvas.FillEllipse(patternBrush, new PointD(7, 5.5), 6.2, 4.8), Ct);
        Assert.Equal(DrawingTestSupport.ReadSamples(expected.Frames[0]), DrawingTestSupport.ReadSamples(actual.Frames[0]));
    }

    [Fact]
    public void ThePrecisionOfTheTileOfAPatternIsTheOneOfTheCanvasByDefault()
    {
        // On a 16-bit canvas, a tile of whole pixels placed on whole pixels keeps the 16-bit color of its drawing; with
        // 8-bit samples, the color is rounded to 8 bits, (v 255 + 32767) / 65535, then read back as v 257:
        // 0x1234 = 4660 -> 18 -> 4626, 0x5678 = 22136 -> 86 -> 22102, 0x9ABC = 39612 -> 154 -> 39578, 0x8000 -> 128 -> 32896
        var color = new Rgba64(0x1234, 0x5678, 0x9ABC, 0x8000);
        var canvasPrecision = new PatternBrush(new Rectangle(0, 0, 2, 2), tile => tile.Fill(color));
        var eightBit = new PatternBrush(new Rectangle(0, 0, 2, 2), tile => tile.Fill(color)) { TilePrecision = PatternTilePrecision.EightBit };
        Assert.Equal(PatternTilePrecision.Canvas, canvasPrecision.TilePrecision);
        Assert.Equal(PatternTilePrecision.EightBit, eightBit.TilePrecision);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternBrush(new Rectangle(0, 0, 2, 2), static _ => { }) { TilePrecision = (PatternTilePrecision)2 });

        // Both in one drawing, at the same size of tile: the image of one precision is not reused for the other
        using var image = new Image<Rgba64>(4, 1);
        image.Draw(
            canvas =>
            {
                canvas.FillRectangle(canvasPrecision, new Rectangle(0, 0, 1, 1));
                canvas.FillRectangle(eightBit, new Rectangle(1, 0, 1, 1));
                canvas.FillRectangle(canvasPrecision, new Rectangle(2, 0, 1, 1));
            },
            Ct);
        Assert.Equal(color, image.Frames[0][0, 0]);
        Assert.Equal(new Rgba64(4626, 22102, 39578, 32896), image.Frames[0][1, 0]);
        Assert.Equal(color, image.Frames[0][2, 0]);

        // On an 8-bit color canvas, both are the same
        using var narrow = new Image<Rgba32>(2, 1);
        narrow.Draw(
            canvas =>
            {
                canvas.FillRectangle(canvasPrecision, new Rectangle(0, 0, 1, 1));
                canvas.FillRectangle(eightBit, new Rectangle(1, 0, 1, 1));
            },
            Ct);
        Assert.Equal(new Rgba32(18, 86, 154, 128), narrow.Frames[0][0, 0]);
        Assert.Equal(narrow.Frames[0][0, 0], narrow.Frames[0][1, 0]);
    }

    [Fact]
    public void APatternBrushValidatesItsTileAndDrawsNothingWhenItsCallbackFails()
    {
        Assert.Throws<ArgumentNullException>(() => new PatternBrush(new Rectangle(0, 0, 1, 1), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternBrush(new Rectangle(0, 0, 0, 1), static _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternBrush(new RectangleD(0, 0, 1, -1), static _ => { }));
        Assert.Throws<ArgumentException>(() => new PatternBrush(new RectangleD(double.NaN, 0, 1, 1), static _ => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PatternBrush(new RectangleD(16777217, 0, 1, 1), static _ => { }));

        using var image = new Image<Rgba32>(8, 8, new Rgba32(1, 2, 3, 4));
        var before = DrawingTestSupport.ReadSamples(image.Frames[0]);
        var white = new Rgba32(255, 255, 255);
        PatternBrush? self = null;
        self = new PatternBrush(new Rectangle(0, 0, 4, 4), tile => tile.Fill(self!));
        var failing = new PatternBrush(new Rectangle(0, 0, 4, 4), tile =>
        {
            tile.Fill(white);
            throw new InvalidOperationException("tile");
        });
        var flat = new PatternBrush(new Rectangle(0, 0, 4, 4), static _ => Assert.Fail("The callback ran.")) { Transform = AffineTransform.CreateScale(1, 0) };
        var nested = new PatternBrush(new Rectangle(0, 0, 4, 4), tile => tile.Fill(new PatternBrush(new Rectangle(0, 0, 2, 2), inner => inner.FillRectangle(white, new Rectangle(0, 0, 1, 1)))));
        image.Draw(
            canvas =>
            {
                // A pattern that paints itself, a callback that fails
                AssertThrows<InvalidOperationException>(canvas, canvas => canvas.Fill(self));
                AssertThrows<InvalidOperationException>(canvas, canvas => canvas.Fill(failing));

                // A pattern that a transformation flattens paints nothing
                canvas.Fill(flat);
                canvas.Transform(AffineTransform.CreateScale(0, 0), inner => inner.Fill(nested));
            },
            Ct);
        Assert.Equal(before, DrawingTestSupport.ReadSamples(image.Frames[0]));

        // A pattern can be painted with another one
        image.Draw(canvas => canvas.Fill(nested), Ct);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.Equal((x & 1) == 0 && (y & 1) == 0 ? white : new Rgba32(1, 2, 3, 4), image.Frames[0][x, y]);
            }
        }

        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    [Fact]
    public void ATileLargerThanWhatTheCanvasShowsIsDrawnWhereTheCanvasShowsIt()
    {
        // A tile of 400 by 300 seen through a clip of 40 by 40 across the corner of four tiles: its image would have 30
        // times the pixels of the clip, so the callback draws the four tiles the clip shows, on an image of the clip.
        // The pixels are those of the whole tile repeated, which the canvas of 1000 by 700 draws.
        static void Content(DrawingCanvas tile)
        {
            tile.FillRectangle(new Rgba32(30, 90, 200), new RectangleD(0, 0, 400, 300));
            tile.FillCircle(new Rgba32(250, 200, 0), new PointD(380.3, 290.6), 35.5);
            tile.Stroke(new Pen(new Rgba32(200, 20, 60), 7), new VectorPathBuilder().MoveTo(-20, 330).LineTo(420, -10).Build());
            tile.FillRectangle(new Rgba32(0, 0, 0, 128), new RectangleD(10.5, 5.25, 30, 20));
        }

        var clip = new Rectangle(380, 280, 40, 40);
        foreach (var (wrapX, wrapY, spacing) in new[] { (ImageWrapMode.Repeat, ImageWrapMode.Repeat, 0d), (ImageWrapMode.Repeat, ImageWrapMode.Transparent, 12.5), (ImageWrapMode.Pad, ImageWrapMode.Repeat, 0d), (ImageWrapMode.Reflect, ImageWrapMode.Reflect, 0d) })
        {
            var brush = new PatternBrush(new RectangleD(0, 0, 400, 300), Content) { WrapModeX = wrapX, WrapModeY = wrapY, SpacingX = spacing, Transform = AffineTransform.CreateTranslation(-17, -9.5) };
            using var whole = new Image<Rgba32>(1000, 700);
            whole.Draw(canvas => canvas.Fill(brush), Ct);
            using var seen = new Image<Rgba32>(1000, 700);
            var runs = 0;
            var counted = new PatternBrush(new RectangleD(0, 0, 400, 300), tile =>
            {
                runs++;
                Content(tile);
            })
            { WrapModeX = wrapX, WrapModeY = wrapY, SpacingX = spacing, Transform = AffineTransform.CreateTranslation(-17, -9.5) };
            seen.Draw(canvas => canvas.Clip(clip, clipped => clipped.Fill(counted)), Ct);
            Assert.Equal(wrapX is ImageWrapMode.Pad ? 2 : wrapY is ImageWrapMode.Transparent ? 2 : 4, runs);

            // A tile a reflection turns is drawn mirrored instead of read mirrored: its edges may differ by one
            var tolerance = wrapX == ImageWrapMode.Reflect ? 1 : 0;
            for (var y = clip.Top; y < clip.Bottom; y++)
            {
                for (var x = clip.Left; x < clip.Right; x++)
                {
                    var expected = whole.Frames[0][x, y];
                    var actual = seen.Frames[0][x, y];
                    Assert.InRange(actual.R, expected.R - tolerance, expected.R + tolerance, message: $"{wrapX} ({x}, {y})");
                    Assert.InRange(actual.G, expected.G - tolerance, expected.G + tolerance, message: $"{wrapX} ({x}, {y})");
                    Assert.InRange(actual.B, expected.B - tolerance, expected.B + tolerance, message: $"{wrapX} ({x}, {y})");
                    Assert.InRange(actual.A, expected.A - tolerance, expected.A + tolerance, message: $"{wrapX} ({x}, {y})");
                }
            }
        }

        // A tile wider than the largest image the limits accept: the canvas shows its first 40 columns
        using var small = new Image<Rgba32>(40, 40);
        var wide = new PatternBrush(new RectangleD(0, 0, 40000, 40), static tile => tile.FillRectangle(new Rgba32(255, 0, 0), new RectangleD(0, 0, 20, 20)));
        small.Draw(canvas => canvas.Fill(wide), Ct);
        Assert.Equal(new Rgba32(255, 0, 0), small.Frames[0][5, 5]);
        Assert.Equal(default, small.Frames[0][25, 5]);

        // A tile of 4e6 pixels covers the canvas with the square at its origin. In perspective, the canvas has no window
        // on the tile: its image is drawn at the resolution the limits allow, which a tile of one color does not show.
        var huge = new PatternBrush(new Rectangle(0, 0, 4, 4), static tile => tile.FillRectangle(new Rgba32(0, 0, 255), new Rectangle(0, 0, 1, 1))) { Transform = AffineTransform.CreateScale(1e6, 1e6) };
        small.Draw(canvas => canvas.Fill(huge), Ct);
        Assert.Equal(new Rgba32(0, 0, 255), small.Frames[0][39, 39]);
        small.Draw(canvas => canvas.Clear(Colors.Transparent), Ct);
        var plain = new PatternBrush(new Rectangle(0, 0, 4, 4), static tile => tile.Fill(new Rgba32(0, 0, 255))) { Transform = AffineTransform.CreateScale(1e6, 1e6) };
        small.Draw(canvas => canvas.Transform(ProjectiveTransform.CreateFromQuadrilateral(new RectangleD(0, 0, 40, 40), new PointD(5, 5), new PointD(35, 2), new PointD(38, 38), new PointD(2, 30)), inner => inner.Fill(plain)), Ct);
        Assert.Equal(new Rgba32(0, 0, 255), small.Frames[0][20, 20]);
    }

    /// <summary>
    /// Solves the eight equations of the projective transformation that maps four points to four others: the numbers
    /// <c>a</c> to <c>h</c> of <c>((a x + b y + c) / (g x + h y + 1), (d x + e y + f) / (g x + h y + 1))</c>.
    /// </summary>
    private static double[] SolveProjection(PointD[] from, PointD[] to)
    {
        var matrix = new double[8][];
        for (var i = 0; i < 4; i++)
        {
            var (x, y, u, v) = (from[i].X, from[i].Y, to[i].X, to[i].Y);
            matrix[2 * i] = [x, y, 1, 0, 0, 0, -x * u, -y * u, u];
            matrix[(2 * i) + 1] = [0, 0, 0, x, y, 1, -x * v, -y * v, v];
        }

        for (var column = 0; column < 8; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < 8; row++)
            {
                if (Math.Abs(matrix[row][column]) > Math.Abs(matrix[pivot][column]))
                {
                    pivot = row;
                }
            }

            (matrix[column], matrix[pivot]) = (matrix[pivot], matrix[column]);
            for (var row = 0; row < 8; row++)
            {
                if (row == column)
                    continue;

                var factor = matrix[row][column] / matrix[column][column];
                for (var k = column; k < 9; k++)
                {
                    matrix[row][k] -= factor * matrix[column][k];
                }
            }
        }

        var result = new double[8];
        for (var i = 0; i < 8; i++)
        {
            result[i] = matrix[i][8] / matrix[i][i];
        }

        return result;
    }

    [Fact]
    public void DrawingAnImageBeyondTheAllocationLimitChangesNothing()
    {
        using var source = new Image<Rgba32>(64, 64, new Rgba32(200, 100, 50));
        using var probe = new Image<Gray8>(4, 4, new Gray8(0), ImageConfiguration.Default);
        long session = 0;
        probe.Draw(_ => session = probe.Owner.Scope.LiveBytes, Ct);

        // Room for the pixels and for the scratch of the frame size only: the call fails before it changes a pixel, and
        // the source is not left leased
        using var image = new Image<Gray8>(4, 4, new Gray8(0), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = session } });
        var before = image.Owner.Scope.LiveBytes;
        image.Draw(canvas => AssertThrows<ImageResourceLimitException>(canvas, canvas => canvas.DrawImage(source.Frames[0], new Rectangle(0, 0, 4, 4))), Ct);
        Assert.Equal(new int[16], DrawingTestSupport.ReadSamples(image.Frames[0]));
        Assert.Equal(before, image.Owner.Scope.LiveBytes);
        Assert.Equal(0, source.Owner.ActiveLeaseCount);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
    }

    /// <summary>Reads a pixel as a 16-bit color, by the rules of pixel conversion written from their definition.</summary>
    private static (int Red, int Green, int Blue, int Alpha) ReadColor(ImageFrame frame, int x, int y)
    {
        var layout = ReferencePixelLayout.FromPixelFormat(frame.PixelFormat);
        var samples = DrawingTestSupport.ReadSamples(frame).AsSpan(((y * frame.Width) + x) * layout.ChannelCount, layout.ChannelCount);
        int Widen(int value) => layout.MaxValue == 255 ? value * 257 : value;
        var alpha = layout.HasAlpha ? Widen(samples[layout.ColorChannels]) : 65535;
        if (layout.ColorChannels == 1)
            return (Widen(samples[0]), Widen(samples[0]), Widen(samples[0]), alpha);

        return layout.BlueFirst ? (Widen(samples[2]), Widen(samples[1]), Widen(samples[0]), alpha) : (Widen(samples[0]), Widen(samples[1]), Widen(samples[2]), alpha);
    }

    private static void AssertThrows<TException>(DrawingCanvas canvas, DrawingAction action)
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
}
