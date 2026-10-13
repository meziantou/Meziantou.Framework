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

        // With an opacity
        using var faded = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        using var half = DrawingTestSupport.CreateRandom(PixelFormat.Rgba32, 8, 8, new Random(78).Next);
        faded.Draw(canvas => canvas.Fill(new ImageBrush(source.Frames[0]) { Opacity = 0.5 }), Ct);
        half.Draw(canvas => canvas.DrawImage(source.Frames[0], default(PointD), 0.5), Ct);
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
                    var expected = Sample(source, region, position, sampling, wrapMode);
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

        static Rgba64 Sample(Image source, Rectangle region, PointD position, ImageSampling sampling, ImageWrapMode wrapMode)
        {
            if (sampling == ImageSampling.Nearest)
                return Read(source, region, (int)Math.Floor(position.X), (int)Math.Floor(position.Y), wrapMode) ?? default;

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
                AssertThrows<ObjectDisposedException>(canvas, canvas => canvas.Fill(brush));
            },
            Ct);

        Assert.Throws<ArgumentNullException>(() => new ImageBrush(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(image.Frames[0]) { WrapMode = (ImageWrapMode)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(image.Frames[0]) { Sampling = (ImageSampling)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(image.Frames[0]) { Opacity = 1.01 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageBrush(image.Frames[0]) { Opacity = double.NaN });

        // The region of a brush is checked when it paints
        var outside = new ImageBrush(image.Frames[1]) { SourceRegion = new Rectangle(2, 2, 3, 3) };
        image.Frames[0].Draw(canvas => AssertThrows<ArgumentOutOfRangeException>(canvas, canvas => canvas.Fill(outside)), Ct);
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
