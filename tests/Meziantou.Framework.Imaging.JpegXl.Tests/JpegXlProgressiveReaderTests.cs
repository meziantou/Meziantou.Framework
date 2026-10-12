using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// <see cref="ImageReader{TPixel}.ReadFrameRefinementInto"/> on JPEG XL files: the renderings a progressive frame gives
/// before it is complete. The pixels of an intermediate rendering are not defined by the format, so the tests check what
/// must hold whatever they are: the last rendering is the frame an eager load gives, byte for byte; the stages are the
/// ones the frame header declares; a rendering does not depend on how the input arrives, nor on what follows it in the
/// input; the renderings get closer to the frame; and an input that stops early gives its complete stages, then fails.
/// The inputs are files written by cjxl (fixtures of the corpus) and by the encoder of the library.
/// </summary>
public sealed class JpegXlProgressiveReaderTests
{
    [Theory]
    [InlineData(JpegXlCompression.Lossy, PixelFormat.Rgb24)]
    [InlineData(JpegXlCompression.Lossy, PixelFormat.Rgba32)]
    [InlineData(JpegXlCompression.Lossy, PixelFormat.Gray8)]
    [InlineData(JpegXlCompression.Lossy, PixelFormat.Rgba64)]
    [InlineData(JpegXlCompression.Lossless, PixelFormat.Rgb24)]
    [InlineData(JpegXlCompression.Lossless, PixelFormat.Rgba32)]
    [InlineData(JpegXlCompression.Lossless, PixelFormat.Gray16)]
    public void AProgressiveFrameIsRenderedAtEachOfItsStages(JpegXlCompression compression, PixelFormat format)
    {
        // One group and several groups
        foreach (var (width, height) in new[] { (40, 24), (300, 270) })
        {
            var data = Encode(width, height, format, new JpegXlEncoder { Compression = compression, Progressive = true, Quality = 90 });
            var renderings = ReadRenderings(new MemoryStream(data));
            var context = $"{compression} {format} {width}x{height}";

            // The LF image, then each of the three passes: the last one completes the frame. A lossless image codes the
            // resolutions that fit a group in its first section: it has no stage of its own for them, and none at all when
            // the image fits a group
            var stages = renderings.Select(rendering => (rendering.IsFinal, rendering.Downsampling, rendering.CompletedPasses)).ToArray();
            if (compression == JpegXlCompression.Lossy)
            {
                Assert.Equal([(false, 8, 0), (false, 4, 1), (false, 2, 2), (true, 1, 3)], stages);
            }
            else if (width <= 256 && height <= 256)
            {
                Assert.Equal([(true, 1, 3)], stages);
            }
            else
            {
                // The stages that have sections of their own depend on the channels: at least the one before the last pass
                Assert.Equal((true, 1, 3), stages[^1]);
                Assert.Contains((false, 2, 2), stages);
                Assert.All(stages[..^1], stage => !stage.IsFinal);
                Assert.All(stages.Zip(stages.Skip(1)), pair => pair.Second.Downsampling < pair.First.Downsampling && pair.Second.CompletedPasses > pair.First.CompletedPasses);
            }

            using var eager = Image.Load<Rgba64>(data);
            Assert.True(GetPixels(eager).AsSpan().SequenceEqual(renderings[^1].Pixels), $"{context}: the last rendering is not the frame.");

            // Each stage is closer to the frame than the one before; an alpha channel that is not squeezed is opaque until it is decoded
            var previous = double.MaxValue;
            for (var i = 0; i < renderings.Count - 1; i++)
            {
                var error = GetColorError(renderings[i].Pixels, renderings[^1].Pixels);
                Assert.True(error < previous, $"{context}: the rendering {i} has the error {error}, after {previous}.");
                previous = error;
            }
        }
    }

    [Fact]
    public void AFrameThatIsNotProgressiveIsRenderedOnceOrWithItsLfImage()
    {
        // One section: nothing can be shown before the frame
        using (var small = CreateImage(40, 24, PixelFormat.Rgb24))
        {
            foreach (var encoder in new[] { new JpegXlEncoder(), new JpegXlEncoder { Compression = JpegXlCompression.Lossy } })
            {
                var renderings = ReadRenderings(new MemoryStream(Save(small, encoder)));
                Assert.Equal([(true, 1, 1)], renderings.Select(rendering => (rendering.IsFinal, rendering.Downsampling, rendering.CompletedPasses)));
            }
        }

        // Several groups and one pass: a lossy frame has its LF image before its groups; a lossless frame that is not squeezed has nothing to show
        using var large = CreateImage(300, 270, PixelFormat.Rgb24);
        var lossy = ReadRenderings(new MemoryStream(Save(large, new JpegXlEncoder { Compression = JpegXlCompression.Lossy })));
        Assert.Equal([(false, 8, 0), (true, 1, 1)], lossy.Select(rendering => (rendering.IsFinal, rendering.Downsampling, rendering.CompletedPasses)));
        var lossless = ReadRenderings(new MemoryStream(Save(large, new JpegXlEncoder())));
        Assert.Equal([(true, 1, 1)], lossless.Select(rendering => (rendering.IsFinal, rendering.Downsampling, rendering.CompletedPasses)));
    }

    [Theory]
    [InlineData("jxl/lossy-progressive")]
    [InlineData("jxl/lossy-lf-frame")]
    [InlineData("jxl/lossless-squeeze")]
    [InlineData("jxl/lossless-groups")]
    public void FilesWrittenByTheReferenceEncoderEndWithTheirFrame(string id)
    {
        // A frame of its own for the LF image, a permuted table of contents, a responsive lossless image, groups without passes
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        var renderings = ReadRenderings(new MemoryStream(data));
        using var eager = Image.Load<Rgba64>(data);
        Assert.True(GetPixels(eager).AsSpan().SequenceEqual(renderings[^1].Pixels), $"{id}: the last rendering is not the frame.");
        Assert.Equal(renderings.Count - 1, renderings.FindIndex(rendering => rendering.IsFinal));
        for (var i = 1; i < renderings.Count; i++)
        {
            Assert.True(renderings[i].Downsampling <= renderings[i - 1].Downsampling && renderings[i].CompletedPasses >= renderings[i - 1].CompletedPasses, $"{id}: the rendering {i} is not finer than the one before.");
        }
    }

    [Theory]
    [InlineData(JpegXlCompression.Lossy)]
    [InlineData(JpegXlCompression.Lossless)]
    public async Task ARenderingDoesNotDependOnHowTheInputArrives(JpegXlCompression compression)
    {
        using var source = CreateImage(300, 270, PixelFormat.Rgba32);
        var data = Save(source, new JpegXlEncoder { Compression = compression, Progressive = true });
        var expected = ReadRenderings(new MemoryStream(data));
        foreach (var chunk in new[] { 1, 7, 4096 })
        {
            AssertSameRenderings(expected, ReadRenderingsInPieces(data, chunk), $"{compression}, {chunk} bytes per read");
        }

        using var asynchronousStream = CreateStream(data, 513, isAsynchronous: true);
        AssertSameRenderings(expected, await ReadRenderingsAsync(asynchronousStream), $"{compression}, asynchronous");
    }

    [Theory]
    [InlineData(JpegXlCompression.Lossy)]
    [InlineData(JpegXlCompression.Lossless)]
    public void AnInputThatStopsEarlyGivesItsCompleteStagesThenFails(JpegXlCompression compression)
    {
        using var source = CreateImage(300, 270, PixelFormat.Rgb24);
        var data = Save(source, new JpegXlEncoder { Compression = compression, Progressive = true });
        var complete = ReadRenderings(new MemoryStream(data));
        var previousCount = 0;
        foreach (var length in Enumerable.Range(1, 40).Select(i => data.Length * i / 41))
        {
            using var reader = OpenReaderOrNull(data.AsSpan(0, length).ToArray());
            if (reader is null)
                continue;

            using var image = new Image<Rgba64>(reader.Info.Width, reader.Info.Height);
            var count = 0;
            var exception = Assert.Throws<InvalidImageContentException>(() =>
            {
                while (reader.ReadFrameRefinementInto(image) is { } refinement)
                {
                    // A stage of a prefix is the stage of the whole file: what follows a stage does not change it
                    Assert.False(refinement.IsFinal, $"{length} bytes: the frame was returned.");
                    Assert.Equal((complete[count].Downsampling, complete[count].CompletedPasses), (refinement.Downsampling, refinement.CompletedPasses));
                    Assert.True(GetPixels(image).AsSpan().SequenceEqual(complete[count].Pixels), $"{length} bytes: the rendering {count} differs.");
                    count++;
                }
            });

            Assert.Equal(ImageFormat.JpegXl, exception.Format);
            Assert.True(count >= previousCount, $"{length} bytes: {count} renderings, after {previousCount}.");
            previousCount = count;

            // The reader is faulted: truncation is never a clean end
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrameRefinementInto(image));
        }

        Assert.Equal(complete.Count - 1, previousCount);
    }

    [Fact]
    public void TheFrameCanBeReadAtAnyStage()
    {
        using var source = CreateImage(300, 270, PixelFormat.Rgb24);
        var data = Save(source, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Progressive = true });
        using var eager = Image.Load<Rgba64>(data);
        var expected = GetPixels(eager);
        for (var skipped = 0; skipped <= 3; skipped++)
        {
            using var reader = Image.OpenReader<Rgba64>(new MemoryStream(data));
            using var image = new Image<Rgba64>(reader.Info.Width, reader.Info.Height);
            for (var i = 0; i < skipped; i++)
            {
                Assert.False(reader.ReadFrameRefinementInto(image)!.IsFinal);
                Assert.Equal(0, reader.FramesRead);
            }

            // The frame itself, into another image, then the end of the input
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Equal(1, reader.FramesRead);
            Assert.True(expected.AsSpan().SequenceEqual(GetPixels(frame)), $"after {skipped} renderings: the frame differs.");
            Assert.Null(reader.ReadFrameRefinementInto(image));
            Assert.Null(reader.ReadFrame());
        }
    }

    [Fact]
    public void EveryFrameOfAnAnimationIsRefined()
    {
        using var first = CreateImage(300, 270, PixelFormat.Rgb24);
        using var second = CreateImage(300, 270, PixelFormat.Rgb24, seed: 7);
        using var animation = (Image<Rgb24>)first.CloneAs<Rgb24>();
        using (var next = second.CloneAs<Rgb24>())
        {
            animation.AppendFrame(next.Frames[0]);
        }

        animation.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        animation.Frames[1].Metadata.Duration = new FrameDuration(1, 5);
        var data = Save(animation, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Progressive = true });
        using var eager = Image.Load<Rgba64>(data);
        using var reader = Image.OpenReader<Rgba64>(new MemoryStream(data));
        using var image = new Image<Rgba64>(reader.Info.Width, reader.Info.Height);
        for (var frame = 0; frame < 2; frame++)
        {
            for (var stage = 0; stage < 4; stage++)
            {
                var refinement = reader.ReadFrameRefinementInto(image);
                Assert.NotNull(refinement);
                Assert.Equal((stage == 3, stage), (refinement.IsFinal, refinement.CompletedPasses));
                Assert.Equal(stage == 3 ? frame + 1 : frame, reader.FramesRead);
                Assert.Equal(eager.Frames[frame].Metadata.Duration, image.Frames[0].Metadata.Duration);
            }

            using var expected = eager.CloneFrame(frame);
            Assert.True(GetPixels((Image<Rgba64>)expected).AsSpan().SequenceEqual(GetPixels(image)), $"frame {frame}: the last rendering is not the frame.");
        }

        Assert.Null(reader.ReadFrameRefinementInto(image));
    }

    [Fact]
    public void AnotherFormatGivesItsFramesAtOnce()
    {
        using var source = CreateImage(40, 24, PixelFormat.Rgba32);
        using var stream = new MemoryStream();
        source.Save(stream, new PngEncoder());
        using var eager = Image.Load<Rgba64>(stream.ToArray());
        var renderings = ReadRenderings(new MemoryStream(stream.ToArray()));
        Assert.Equal([(true, 1, 1)], renderings.Select(rendering => (rendering.IsFinal, rendering.Downsampling, rendering.CompletedPasses)));
        Assert.True(GetPixels(eager).AsSpan().SequenceEqual(renderings[0].Pixels));
    }

    [Fact]
    public void IntermediateRenderingsCountTowardTheTotalPixels()
    {
        using var source = CreateImage(300, 270, PixelFormat.Rgb24);
        var data = Save(source, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Progressive = true });
        const long Pixels = 300 * 270;

        // The frame and its three intermediate renderings; the frame alone needs its own pixels only
        Assert.HasCount(4, ReadRenderings(new MemoryStream(data), CreateOptions(4 * Pixels)));
        Assert.Throws<ImageResourceLimitException>(() => ReadRenderings(new MemoryStream(data), CreateOptions((4 * Pixels) - 1)));
        using var reader = Image.OpenReader<Rgba64>(new MemoryStream(data), CreateOptions(Pixels));
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);

        static ImageReaderOptions CreateOptions(long maxTotalPixels)
            => new() { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxTotalPixels = maxTotalPixels } } };
    }

    [Fact]
    public void TheDestinationIsValidatedLikeTheOneOfAFrame()
    {
        using var source = CreateImage(40, 24, PixelFormat.Rgb24);
        var data = Save(source, new JpegXlEncoder());
        using var reader = Image.OpenReader<Rgb24>(new MemoryStream(data));
        Assert.Throws<ArgumentNullException>(() => reader.ReadFrameRefinementInto(null!));
        using var wrongSize = new Image<Rgb24>(41, 24);
        Assert.Throws<ArgumentException>(() => reader.ReadFrameRefinementInto(wrongSize));

        // An invalid argument reads nothing: the reader is still usable
        using var image = new Image<Rgb24>(40, 24);
        Assert.True(reader.ReadFrameRefinementInto(image)!.IsFinal);
    }

    private sealed record Rendering(bool IsFinal, int Downsampling, int CompletedPasses, Rgba64[] Pixels);

    private static List<Rendering> ReadRenderings(Stream stream, ImageReaderOptions? options = null)
    {
        using var reader = Image.OpenReader<Rgba64>(stream, options);
        using var image = new Image<Rgba64>(reader.Info.Width, reader.Info.Height);
        var result = new List<Rendering>();
        while (reader.ReadFrameRefinementInto(image) is { } refinement)
        {
            result.Add(new Rendering(refinement.IsFinal, refinement.Downsampling, refinement.CompletedPasses, GetPixels(image)));
        }

        return result;
    }

    private static async Task<List<Rendering>> ReadRenderingsAsync(Stream stream)
    {
        await using var reader = await Image.OpenReaderAsync<Rgba64>(stream, cancellationToken: TestContext.Current.CancellationToken);
        using var image = new Image<Rgba64>(reader.Info.Width, reader.Info.Height);
        var result = new List<Rendering>();
        while (await reader.ReadFrameRefinementIntoAsync(image, TestContext.Current.CancellationToken) is { } refinement)
        {
            result.Add(new Rendering(refinement.IsFinal, refinement.Downsampling, refinement.CompletedPasses, GetPixels(image)));
        }

        return result;
    }

    private static void AssertSameRenderings(List<Rendering> expected, List<Rendering> actual, string context)
    {
        Assert.HasCount(expected.Count, actual);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal((expected[i].IsFinal, expected[i].Downsampling, expected[i].CompletedPasses), (actual[i].IsFinal, actual[i].Downsampling, actual[i].CompletedPasses));
            Assert.True(expected[i].Pixels.AsSpan().SequenceEqual(actual[i].Pixels), $"{context}: the rendering {i} differs.");
        }
    }

    /// <summary>Opens a reader on a prefix of a file, or returns <see langword="null"/> when the prefix does not hold the image header.</summary>
    private static ImageReader<Rgba64>? OpenReaderOrNull(byte[] data)
    {
        try
        {
            return Image.OpenReader<Rgba64>(new MemoryStream(data));
        }
        catch (ImageException)
        {
            return null;
        }
    }

    /// <summary>The sum of the squared differences of the color samples.</summary>
    private static double GetColorError(Rgba64[] actual, Rgba64[] expected)
    {
        double sum = 0;
        for (var i = 0; i < actual.Length; i++)
        {
            double red = actual[i].R - expected[i].R;
            double green = actual[i].G - expected[i].G;
            double blue = actual[i].B - expected[i].B;
            sum += (red * red) + (green * green) + (blue * blue);
        }

        return sum;
    }

    private static Rgba64[] GetPixels(Image<Rgba64> image)
    {
        var pixels = new Rgba64[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return pixels;
    }

    private static List<Rendering> ReadRenderingsInPieces(byte[] data, int maxBytesPerRead)
    {
        using var stream = CreateStream(data, maxBytesPerRead, isAsynchronous: false);
        return ReadRenderings(stream);
    }

    /// <summary>A stream that cannot seek and gives a few bytes at each read.</summary>
    private static TestInputStream CreateStream(byte[] data, int maxBytesPerRead, bool isAsynchronous)
        => new(data) { Seekable = false, MaxBytesPerRead = maxBytesPerRead, ForbidSynchronousReads = isAsynchronous };

    private static byte[] Encode(int width, int height, PixelFormat format, ImageEncoder encoder)
    {
        using var source = CreateImage(width, height, format);
        return Save(source, encoder);
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Smooth colors with detail at every scale, and an alpha channel with opaque, translucent and transparent areas.</summary>
    private static Image CreateImage(int width, int height, PixelFormat format, int seed = 0)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = 128 + (90 * Math.Sin((x + seed) / 23.0) * Math.Cos(y / 17.0)) + (12 * Math.Sin(x * 1.3) * Math.Sin(y * 1.7));
                var green = 128 + (70 * Math.Sin((x + (2 * y)) / 31.0)) + (25 * Math.Sin(x / 3.0) * Math.Sin(y / 4.0));
                var blue = 128 + (100 * Math.Cos((y + seed) / 29.0)) + (10 * Math.Cos((x + y) * 0.9));
                var alpha = (x / 16) % 3 == 0 ? 255 : (x / 16) % 3 == 1 ? (x * 7) + (y * 3) : 0;
                pixels[(y * width) + x] = new Rgba32((byte)Math.Clamp(red, 0, 255), (byte)Math.Clamp(green, 0, 255), (byte)Math.Clamp(blue, 0, 255), (byte)alpha);
            }
        }

        using var source = Image.ImportPixelData<Rgba32>(pixels, width, height);
        var flatten = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) };
        return format switch
        {
            PixelFormat.Rgba32 => source.Clone(),
            PixelFormat.Rgba64 => source.CloneAs<Rgba64>(),
            PixelFormat.Rgb24 => source.CloneAs<Rgb24>(flatten),
            PixelFormat.Gray8 => source.CloneAs<Gray8>(flatten),
            PixelFormat.Gray16 => source.CloneAs<Gray16>(flatten),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }
}
