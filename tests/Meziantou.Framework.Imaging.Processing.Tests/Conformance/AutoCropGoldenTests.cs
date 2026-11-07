using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.AutoCrop;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Auto-crop checked against the independent reference of the test harness (<see cref="ReferenceAutoCrop"/>: plain
/// loops over every pixel, linear color lists, exact <see cref="System.Numerics.BigInteger"/> weights), over every raw
/// reference of the golden corpus (frames and posters, 8- and 16-bit, alpha, animations) as is and wrapped in synthetic
/// borders, and over synthetic sources built for the retry, the luma buckets and the weights, with contiguous and
/// segmented storages.
/// </summary>
/// <remarks>
/// Tolerance: none. The detected box, the background and every output pixel are exact; the analysis must leave the pixels
/// untouched. Only the weights, which the library returns as doubles, are compared with the exact fractions within
/// 1e-12, and a padded rectangle is only compared when no rounding of the weights can change its truncated shift
/// (<see cref="ReferenceAutoCrop.HasRobustShifts"/>).
/// </remarks>
public sealed class AutoCropGoldenTests
{
    private static readonly (string Name, ReferenceAutoCropOptions Options)[] Specs =
    [
        ("default", new()),
        ("padding-expand", new(PaddingX: 2, PaddingY: 3)),
        ("padding-contain", new(PaddingX: 5, PaddingY: 1, Contain: true)),
        ("low-threshold-bucket", new(PaddingX: 1, ColorThreshold: 6, BucketThreshold: 0.5m)),
        ("high-threshold", new(ColorThreshold: 120)),
        ("weights", new(PaddingX: 40, PaddingY: 30, AnalyzeWeights: true)),
        ("weights-contain", new(PaddingX: 4, PaddingY: 11, ColorThreshold: 20, Contain: true, AnalyzeWeights: true)),
    ];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in ResizeGoldenTests.CaseList())
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    public static TheoryData<string> SyntheticSources => [.. Synthetic().Select(item => item.Name)];

    [Fact]
    public void SourcesCoverEveryOutcome()
    {
        // Computed with the reference only: the sources must exercise each branch of the contract
        var analyses = new List<(ReferenceAutoCropAnalysis Analysis, ReferenceAutoCropOptions Options, int Width, int Height)>();
        foreach (var (_, frames) in Synthetic())
        {
            foreach (var (_, options) in Specs)
            {
                analyses.Add((ReferenceAutoCrop.Analyze(frames, options), options, frames[0].Width, frames[0].Height));
            }
        }

        var fixture = GoldenCorpus.Default.Get("png/rgba8-corner-markers");
        foreach (var (_, frames) in Sources([fixture.GetFrame(0, RawPixelLayout.Rgba8)]))
        {
            analyses.Add((ReferenceAutoCrop.Analyze(frames, Specs[0].Options), Specs[0].Options, frames[0].Width, frames[0].Height));
        }

        Assert.Contains(analyses, item => item.Analysis.Success && !item.Analysis.UsedRetry);
        Assert.Contains(analyses, item => item.Analysis.Success && item.Analysis.UsedRetry);
        Assert.Contains(analyses, item => !item.Analysis.Success && item.Analysis.UsedRetry);
        Assert.Contains(analyses, item => !item.Analysis.Success && !item.Analysis.UsedRetry);
        Assert.Contains(analyses, item => !item.Analysis.WeightXNumerator.IsZero && !item.Analysis.WeightYNumerator.IsZero);
        Assert.Contains(analyses, item => item.Analysis.Success && item.Options.BucketThreshold is not null && !item.Analysis.UsedRetry && ReferenceAutoCrop.Analyze(Synthetic().First(source => source.Name == "gray8-noisy-border").Frames, item.Options with { BucketThreshold = null }).UsedRetry);

        // Enlarged canvases, clamped rectangles and non-zero shifts
        Assert.Contains(analyses, item => item.Analysis.Success && !item.Options.Contain && ReferenceAutoCrop.GetKeptRectangle(item.Width, item.Height, item.Analysis, item.Options).X < 0);
        Assert.Contains(analyses, item => item.Analysis.Success && item.Options.Contain && ReferenceAutoCrop.GetKeptRectangle(item.Width, item.Height, item.Analysis, item.Options).Width < item.Analysis.Width + (2 * item.Options.PaddingX));
        Assert.Contains(analyses, item => item.Analysis.Success && item.Options.AnalyzeWeights && ReferenceAutoCrop.HasRobustShifts(item.Analysis, item.Options)
            && ReferenceAutoCrop.GetKeptRectangle(item.Width, item.Height, item.Analysis, item.Options with { Contain = false }) != ReferenceAutoCrop.GetKeptRectangle(item.Width, item.Height, item.Analysis with { WeightXNumerator = 0, WeightYNumerator = 0 }, item.Options with { Contain = false }));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FixturesMatchTheIndependentReference(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        switch (Enum.Parse<PixelFormat>(format))
        {
            case PixelFormat.Rgba32: CheckFixture<Rgba32>(fixture); break;
            case PixelFormat.Bgra32: CheckFixture<Bgra32>(fixture); break;
            case PixelFormat.Rgb24: CheckFixture<Rgb24>(fixture); break;
            case PixelFormat.Rgba64: CheckFixture<Rgba64>(fixture); break;
            case PixelFormat.Gray8: CheckFixture<Gray8>(fixture); break;
            case PixelFormat.Gray16: CheckFixture<Gray16>(fixture); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Theory]
    [MemberData(nameof(SyntheticSources))]
    public void SyntheticSourcesMatchTheIndependentReference(string name)
    {
        var frames = Synthetic().Single(item => item.Name == name).Frames;
        var layout = frames[0].Layout;
        foreach (var (specName, options) in Specs)
        {
            foreach (var segmented in new[] { false, true })
            {
                var context = string.Create(CultureInfo.InvariantCulture, $"{name} ({specName}, segmented: {segmented})");
                if (layout == RawPixelLayout.Rgba8)
                {
                    Check<Rgba32>(frames, posterIndex: -1, options, segmented, context);
                    Check<Bgra32>(frames, posterIndex: -1, options, segmented, context + ", Bgra32");
                }
                else if (layout == RawPixelLayout.Rgba16Le)
                {
                    Check<Rgba64>(frames, posterIndex: -1, options, segmented, context);
                }
                else if (layout == RawPixelLayout.Rgb8)
                {
                    Check<Rgb24>(frames, posterIndex: -1, options, segmented, context);
                }
                else if (layout == RawPixelLayout.Gray8)
                {
                    Check<Gray8>(frames, posterIndex: -1, options, segmented, context);
                }
                else
                {
                    Check<Gray16>(frames, posterIndex: -1, options, segmented, context);
                }
            }
        }
    }

    private static void CheckFixture<TPixel>(GoldenFixture fixture)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var buffers = Enumerable.Range(0, fixture.Expected.FrameCount).Select(i => ResizeGoldenTests.GetFrame(fixture, i, format, poster: false)).ToList();
        var posterIndex = -1;
        if (fixture.Expected.Poster is not null)
        {
            posterIndex = buffers.Count;
            buffers.Add(ResizeGoldenTests.GetFrame(fixture, 0, format, poster: true));
        }

        foreach (var (sourceName, source) in Sources(buffers))
        {
            for (var i = 0; i < Specs.Length; i++)
            {
                // Alternate the storage layout instead of doubling the run time of the corpus
                var segmented = i % 2 == 1;
                var context = string.Create(CultureInfo.InvariantCulture, $"Fixture '{fixture.Id}' ({format}, {sourceName}, {Specs[i].Name}, segmented: {segmented})");
                Check<TPixel>(source, posterIndex, Specs[i].Options, segmented, context);
            }
        }
    }

    /// <summary>
    /// The buffers as they are, then wrapped in an uneven border (2 columns on the left, 4 on the right, 3 rows above, 1
    /// below) of a color that is not in the image, and of the color of the first pixel.
    /// </summary>
    private static IEnumerable<(string Name, IReadOnlyList<RawPixelBuffer> Frames)> Sources(List<RawPixelBuffer> buffers)
    {
        yield return ("as is", buffers);

        var layout = buffers[0].Layout;
        var width = buffers[0].Width;
        var height = buffers[0].Height;
        var fixedFill = new int[layout.ChannelCount];
        var firstPixel = new int[layout.ChannelCount];
        for (var c = 0; c < layout.ChannelCount; c++)
        {
            fixedFill[c] = c == layout.AlphaChannel ? layout.MaxSampleValue : layout.MaxSampleValue - 3 - (2 * c);
            firstPixel[c] = buffers[0].GetSample(0, 0, c);
        }

        yield return ("bordered", [.. buffers.Select(buffer => buffer.Extend(-2, -3, width + 6, height + 4, fixedFill))]);
        yield return ("bordered with the first pixel", [.. buffers.Select(buffer => buffer.Extend(-2, -3, width + 6, height + 4, firstPixel))]);
    }

    private static void Check<TPixel>(IReadOnlyList<RawPixelBuffer> buffers, int posterIndex, ReferenceAutoCropOptions options, bool segmented, string context)
        where TPixel : unmanaged
    {
        var token = TestContext.Current.CancellationToken;
        var reference = ReferenceAutoCrop.Analyze(buffers, options);
        var libraryOptions = ToLibrary(options);
        using var image = Build<TPixel>(buffers, posterIndex, segmented);
        var frameObjects = GetFrames(image, posterIndex);

        var analysis = image.AnalyzeAutoCrop(libraryOptions, token);

        // The background is reported widened to 16 bits: 8-bit samples are multiplied by 257
        var widen = buffers[0].Layout.BytesPerSample == 1 ? 257 : 1;
        Assert.Equal(reference.Success, analysis.Success, context);
        Assert.Equal(new Size(buffers[0].Width, buffers[0].Height), analysis.CanvasSize);
        Assert.Equal(new Rectangle(reference.X, reference.Y, reference.Width, reference.Height), analysis.Bounds, context);
        var background = new Rgba64((ushort)(reference.Background.Red * widen), (ushort)(reference.Background.Green * widen), (ushort)(reference.Background.Blue * widen), (ushort)(reference.Background.Alpha * widen));
        Assert.Equal(background, ((AutoCropAnalysis)analysis).BackgroundColor, context);

        // The typed background is the same pixel in the storage format of the image
        var layout = buffers[0].Layout;
        var samples = new int[layout.ChannelCount];
        for (var c = 0; c < samples.Length; c++)
        {
            samples[c] = layout.GetChannelName(c) switch
            {
                'R' or 'Y' => reference.Background.Red,
                'G' => reference.Background.Green,
                'B' => reference.Background.Blue,
                _ => reference.Background.Alpha,
            };
        }

        var backgroundPixel = RawImport.ToPixelData<TPixel>(buffers[0].Extend(-1, 0, 1, 1, samples))[0];
        Assert.True(EqualityComparer<TPixel>.Default.Equals(backgroundPixel, analysis.BackgroundColor), $"{context}: expected the background pixel {backgroundPixel}, got {analysis.BackgroundColor}");
        Assert.Equal(reference.WeightX, analysis.WeightX, tolerance: 1e-12, context);
        Assert.Equal(reference.WeightY, analysis.WeightY, tolerance: 1e-12, context);

        // The analysis is read-only
        for (var i = 0; i < buffers.Count; i++)
        {
            AssertExact(buffers[i], ImageSnapshots.CaptureFrameRows(frameObjects[i]), string.Create(CultureInfo.InvariantCulture, $"{context}, after the analysis, buffer {i}"));
        }

        if (options.AnalyzeWeights && !ReferenceAutoCrop.HasRobustShifts(reference, options))
            return;

        var expected = buffers.Select(buffer => ReferenceAutoCrop.Apply(buffer, reference, options)).ToArray();

        // Applying the analysis to a copy gives the same result as analyzing and cropping in one call
        using (var clone = image.Clone())
        {
            Assert.Equal(!ReferenceEquals(expected[0], buffers[0]), clone.AutoCrop(analysis, libraryOptions, token), context);
            var cloned = GetFrames(clone, posterIndex);
            for (var i = 0; i < buffers.Count; i++)
            {
                AssertExact(expected[i], ImageSnapshots.CaptureFrameRows(cloned[i]), string.Create(CultureInfo.InvariantCulture, $"{context}, known analysis, buffer {i}"));
            }
        }

        var changed = image.AutoCrop(libraryOptions, token);
        Assert.Equal(!ReferenceEquals(expected[0], buffers[0]), changed, context);
        Assert.Equal(new Size(expected[0].Width, expected[0].Height), image.Size);
        var after = GetFrames(image, posterIndex);
        for (var i = 0; i < buffers.Count; i++)
        {
            Assert.Same(frameObjects[i], after[i]);
            AssertExact(expected[i], ImageSnapshots.CaptureFrameRows(after[i]), string.Create(CultureInfo.InvariantCulture, $"{context}, buffer {i}"));
        }
    }

    private static ImageFrame<TPixel>[] GetFrames<TPixel>(Image<TPixel> image, int posterIndex)
        where TPixel : unmanaged
    {
        var frames = ((IEnumerable<ImageFrame<TPixel>>)image.Frames).ToList();
        if (posterIndex >= 0)
        {
            Assert.HasCount(posterIndex, frames);
            frames.Add(image.PosterFrame!);
        }

        return [.. frames];
    }

    private static Image<TPixel> Build<TPixel>(IReadOnlyList<RawPixelBuffer> buffers, int posterIndex, bool segmented)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var first = buffers[0];
        var layoutOptions = segmented ? new PixelStorageLayoutOptions { RowAlignment = 16, TargetSlabBytes = 2 * ((first.RowBytes + 15) / 16 * 16) } : null;
        var image = Image.ImportPixelBytesCore<TPixel>(RawImport.ToPixelBytes(first, format), first.Width, first.Height, first.RowBytes, ImageConfiguration.Default, layoutOptions);
        try
        {
            for (var i = 1; i < buffers.Count; i++)
            {
                using var single = Image.ImportPixelBytes<TPixel>(RawImport.ToPixelBytes(buffers[i], format), first.Width, first.Height);
                if (i == posterIndex)
                {
                    image.SetPosterFrame(single.Frames[0]);
                }
                else
                {
                    image.AppendFrame(single.Frames[0]);
                }
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static AutoCropOptions ToLibrary(ReferenceAutoCropOptions options) => new()
    {
        PaddingX = options.PaddingX,
        PaddingY = options.PaddingY,
        ColorThreshold = options.ColorThreshold,
        BucketThreshold = (double?)options.BucketThreshold,
        PaddingMode = options.Contain ? AutoCropPaddingMode.Contain : AutoCropPaddingMode.Expand,
        AnalyzeWeights = options.AnalyzeWeights,
    };

    private static void AssertExact(RawPixelBuffer expected, RawPixelBuffer actual, string context)
    {
        var result = PixelBufferComparer.Compare(expected, actual, ComparisonPolicy.Exact, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    /// <summary>
    /// Sources built for the branches the corpus cannot reach: a noisy outer frame that only the retry gets past (or that
    /// is too thick for it), a noisy border accepted by its luma bucket, off-center content (non-zero weights), frames
    /// with content in different places, hidden colors under transparent pixels, and content too thin to crop to.
    /// </summary>
    private static IEnumerable<(string Name, IReadOnlyList<RawPixelBuffer> Frames)> Synthetic()
    {
        // 60 x 44: the inset of the retry is 3 columns and 2 rows; the noise is 2 pixels thick
        yield return ("rgba8-noisy-frame-retry", [Build(60, 44, RawPixelLayout.Rgba8, static (x, y) =>
        {
            if (x < 2 || x >= 58 || y < 2 || y >= 42)
                return [(x * 7) % 256, (y * 11) % 256, (x * y) % 256, 255];

            if (x is >= 30 and < 47 && y is >= 9 and < 20)
                return [(x * 9) % 256, 40, (y * 13) % 256, 255];

            // 222: 18 below the background, the halved threshold; 221: 19 below
            return (x, y) switch
            {
                (12, 30) => [221, 221, 221, 255],
                (8, 35) => [222, 222, 222, 255],
                _ => [240, 240, 240, 255],
            };
        })]);

        // The noise is 4 pixels thick: the retry starts inside it
        yield return ("rgba16-thick-noisy-frame", [Build(60, 44, RawPixelLayout.Rgba16Le, static (x, y) =>
        {
            if (x < 4 || x >= 56 || y < 4 || y >= 40)
                return [(x * 1021) % 65536, (y * 2039) % 65536, (x * y * 31) % 65536, 65535];

            return x is >= 20 and < 30 && y is >= 15 and < 25 ? [1000, 2000, 3000, 65535] : [60000, 60001, 60002, 65535];
        })]);

        // 16 gray levels on the border, all in the luma bucket 8 (186 to 208): accepted by the bucket of a threshold of 6
        yield return ("gray8-noisy-border", [Build(31, 23, RawPixelLayout.Gray8, static (x, y) =>
        {
            if (x is >= 6 and < 15 && y is >= 12 and < 19)
                return [(x * y) % 97];

            return [190 + (((x * 3) + (y * 5)) % 16)];
        })]);

        // Off-center content with a soft edge: non-zero weights on both axes, in 16 bits with low bits
        yield return ("rgba16-off-center", [Build(37, 29, RawPixelLayout.Rgba16Le, static (x, y) =>
        {
            if (x is >= 22 and < 33 && y is >= 3 and < 12)
                return [513 + (x * 771), 20000 - (y * 333), 40000 + (x * y), x == 22 ? 30000 : 65535];

            return [65278, 65021, 64764, 65535];
        })]);

        yield return ("rgb8-off-center", [Build(41, 19, RawPixelLayout.Rgb8, static (x, y) =>
        {
            if (x is >= 3 and < 9 && y is >= 10 and < 17)
                return [x * 20, y * 10, 200];

            return [12, 13, 11];
        })]);

        // Three frames with a square in different places on a transparent background with hidden colors
        yield return ("rgba8-animated-transparent", [.. Enumerable.Range(0, 3).Select(frame => Build(24, 18, RawPixelLayout.Rgba8, (x, y) =>
        {
            var left = 3 + (frame * 6);
            var top = 9 - (frame * 3);
            if (x >= left && x < left + 5 && y >= top && y < top + 5)
                return [200 - (frame * 40), x * 10, y * 14, x == left ? 90 : 255];

            return [(x * 11) % 256, (y * 13) % 256, frame * 100, 0];
        }))]);

        // Content two rows high: no box of at least 3 x 3
        yield return ("gray16-thin-content", [Build(40, 9, RawPixelLayout.Gray16Le, static (x, y) => x is >= 5 and < 30 && y is 3 or 4 ? [x * 100] : [65000])]);

        // The same, three rows high
        yield return ("gray16-low-content", [Build(40, 9, RawPixelLayout.Gray16Le, static (x, y) => x is >= 5 and < 30 && y is >= 3 and < 6 ? [x * 100] : [65000])]);
    }

    private static RawPixelBuffer Build(int width, int height, RawPixelLayout layout, Func<int, int, int[]> pixel)
    {
        var builder = new RawPixelBufferBuilder(width, height, layout);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var samples = pixel(x, y);
                for (var c = 0; c < samples.Length; c++)
                {
                    builder.SetSample(x, y, c, samples[c]);
                }
            }
        }

        return builder.Build();
    }
}
