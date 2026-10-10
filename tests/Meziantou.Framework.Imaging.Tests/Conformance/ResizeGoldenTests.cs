using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Resize checked against the independent high-precision reference of the test harness
/// (<see cref="ReferenceResampler"/>: exact rational geometry, decimal kernels and weights, direct two-dimensional sums),
/// over every raw reference of the golden corpus (frames and posters, 8- and 16-bit, alpha, odd sizes) and over synthetic
/// sources (alpha ramps with transparent edges, 16-bit low-bit gradients, one-pixel axes, extreme aspect ratios).
/// </summary>
/// <remarks>
/// Tolerance: none beyond rounding. Each sample must be the correctly rounded exact value (ties upward); only when the exact
/// value lies within <see cref="ReferenceFilterResult.TieWindow"/> (1e-6 sample units) of a rounding boundary is the other
/// neighbor accepted, because the double-precision accumulation of the library may then legitimately round either way.
/// Alpha follows the same rule (it is filtered), and a zero output alpha requires transparent black. The number of such
/// near ties is reported; dimensions, channel order, alpha handling and 16-bit low bits are never covered by a tolerance.
/// </remarks>
public sealed class ResizeGoldenTests
{

    private static readonly (string Name, Func<int, int, ReferenceResizeOptions> Create)[] Specs =
    [
        ("stretch-down-catmull-rom", (w, h) => new(Math.Max(1, ((2 * w) + 2) / 3), Math.Max(1, (h + 1) / 2), ReferenceResizeMode.Stretch, ReferenceKernel.CatmullRom)),
        ("stretch-up-triangle", (w, h) => new((2 * w) + 1, h + 3, ReferenceResizeMode.Stretch, ReferenceKernel.Triangle)),
        ("contain-lanczos3", (w, h) => new(Math.Max(1, w - 1), 3 * h, ReferenceResizeMode.Contain, ReferenceKernel.Lanczos3)),
        ("contain-thumbnail-triangle", (_, _) => new(2, 2, ReferenceResizeMode.Contain, ReferenceKernel.Triangle)),
        ("contain-no-upscale", (w, h) => new(3 * w, 3 * h, ReferenceResizeMode.Contain, ReferenceKernel.Lanczos3, AllowUpscaling: false)),
        ("cover-top-left-catmull-rom", (w, h) => new(((w + 1) / 2) + 1, h, ReferenceResizeMode.Cover, ReferenceKernel.CatmullRom, AnchorX: 0, AnchorY: 0)),
        ("cover-center-lanczos3", (w, h) => new(w + 2, (2 * h) + 1, ReferenceResizeMode.Cover, ReferenceKernel.Lanczos3)),
        ("cover-bottom-right-triangle", (w, h) => new(Math.Max(1, w / 3), Math.Max(1, h - 1), ReferenceResizeMode.Cover, ReferenceKernel.Triangle, AnchorX: 1, AnchorY: 1)),
        ("stretch-nearest", (w, h) => new((2 * w) + 1, Math.Max(1, h - 1), ReferenceResizeMode.Stretch, ReferenceKernel.Nearest)),
        ("contain-linear-lanczos3", (w, h) => new(Math.Max(1, (w + 1) / 2), Math.Max(1, (h + 1) / 2), ReferenceResizeMode.Contain, ReferenceKernel.Lanczos3, Linear: true)),
        ("stretch-linear-up-catmull-rom", (w, h) => new(w + 3, (2 * h) + 1, ReferenceResizeMode.Stretch, ReferenceKernel.CatmullRom, Linear: true)),
    ];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in CaseList())
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Fact]
    public void CorpusCoversAnimationsPostersAlphaAndSixteenBitSamples()
    {
        var cases = CaseList().Select(item => (item.Fixture.Id, item.Format)).ToArray();
        Assert.Contains(("apng/separate-poster", PixelFormat.Rgba32), cases);
        Assert.Contains(("apng/ffmpeg-rgba16", PixelFormat.Rgba64), cases);
        Assert.Contains(("png/rgba16-low-bit-gradient", PixelFormat.Rgba64), cases);
        Assert.Contains(("png/gray16-low-bit-gradient", PixelFormat.Gray16), cases);
        Assert.Contains(("png/graya8-alpha-ramp", PixelFormat.Rgba32), cases);
        Assert.Contains(("png/single-pixel", PixelFormat.Bgra32), cases);
        Assert.Contains(("png/rgb8-odd-width", PixelFormat.Rgb24), cases);
        Assert.Contains(("png/gray8-checkerboard", PixelFormat.Gray8), cases);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ResizedFramesAndPostersMatchTheIndependentReference(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        foreach (var (name, create) in Specs)
        {
            var options = create(fixture.Expected.Width, fixture.Expected.Height);
            switch (Enum.Parse<PixelFormat>(format))
            {
                case PixelFormat.Rgba32: Check<Rgba32>(fixture, name, options); break;
                case PixelFormat.Bgra32: Check<Bgra32>(fixture, name, options); break;
                case PixelFormat.Rgb24: Check<Rgb24>(fixture, name, options); break;
                case PixelFormat.Rgba64: Check<Rgba64>(fixture, name, options); break;
                case PixelFormat.Gray8: Check<Gray8>(fixture, name, options); break;
                case PixelFormat.Gray16: Check<Gray16>(fixture, name, options); break;
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }
    }

    public static TheoryData<string> SyntheticSources => [.. Synthetic().Select(item => item.Name)];

    [Theory]
    [MemberData(nameof(SyntheticSources))]
    public void SyntheticSourcesMatchTheIndependentReference(string name)
    {
        var source = Synthetic().Single(item => item.Name == name).Buffer;
        foreach (var (specName, create) in Specs)
        {
            var options = create(source.Width, source.Height);
            if (source.Layout == RawPixelLayout.Rgba16Le)
            {
                CheckSingle<Rgba64>(source, $"{name}, {specName}", options);
            }
            else
            {
                CheckSingle<Rgba32>(source, $"{name}, {specName}", options);
            }
        }

        // Every kernel in both working spaces, and every Cover anchor, on a strong downscale and an upscale
        foreach (var kernel in Enum.GetValues<ReferenceKernel>())
        {
            foreach (var linear in new[] { false, true })
            {
                foreach (var anchor in new[] { (0m, 0m), (0.5m, 1m), (1m, 0.5m) })
                {
                    var down = new ReferenceResizeOptions(Math.Max(1, source.Width / 4), Math.Max(1, (source.Height / 3) + 1), ReferenceResizeMode.Cover, kernel, anchor.Item1, anchor.Item2, Linear: linear);
                    var up = new ReferenceResizeOptions((source.Width * 3) + 1, (source.Height * 2) + 1, ReferenceResizeMode.Cover, kernel, anchor.Item1, anchor.Item2, Linear: linear);
                    foreach (var options in new[] { down, up })
                    {
                        if (source.Layout == RawPixelLayout.Rgba16Le)
                        {
                            CheckSingle<Rgba64>(source, name, options);
                        }
                        else
                        {
                            CheckSingle<Rgba32>(source, name, options);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void TheReferenceDetectsWrongResults()
    {
        // The comparison must reject an 8-bit reduction of 16-bit samples, a channel swap, a one-unit error, and straight
        // (non-premultiplied) filtering around transparent pixels
        var source = Synthetic().Single(item => item.Name == "rgba16-low-bit-alpha-ramp").Buffer;
        var options = new ReferenceResizeOptions(5, 4, ReferenceResizeMode.Stretch, ReferenceKernel.CatmullRom);
        var reference = ReferenceResampler.Resize(source, options);
        using var image = Import<Rgba64>(source);
        image.Resize(ToLibrary(options), TestContext.Current.CancellationToken);
        var actual = ImageSnapshots.CaptureFrameRows(image.Frames[0]);
        Assert.True(reference.Compare(actual, "baseline", writePreviews: false).IsMatch);

        var reduced = ReduceTo8Bits(actual);
        Assert.False(reference.Compare(reduced, "8-bit", writePreviews: false).IsMatch);
        Assert.False(reference.Compare(actual.SwapChannels(0, 2), "swap", writePreviews: false).IsMatch);
        var x = actual.GetSample(2, 1, 3) == 0 ? 3 : 2;
        Assert.False(reference.Compare(actual.WithSample(x, 1, 1, actual.GetSample(x, 1, 1) ^ 1), "one unit", writePreviews: false).IsMatch);

        var straight = Straight(source, options);
        var result = reference.Compare(straight, "straight", writePreviews: false);
        Assert.False(result.IsMatch);
        Assert.Contains("rejected", result.Describe(), StringComparison.Ordinal);
    }

    internal static IEnumerable<(GoldenFixture Fixture, PixelFormat Format)> CaseList()
    {
        foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid))
        {
            var sixteenBit = fixture.CanonicalLayout == RawPixelLayout.Rgba16Le;
            yield return (fixture, sixteenBit ? PixelFormat.Rgba64 : PixelFormat.Rgba32);
            if (!sixteenBit && fixture.Expected.FrameCount == 1)
            {
                yield return (fixture, PixelFormat.Bgra32);
                if (RawImport.IsOpaque(fixture.GetFrame(0, RawPixelLayout.Rgba8)))
                {
                    yield return (fixture, PixelFormat.Rgb24);
                }
            }

            if (fixture.Layouts.Contains(RawPixelLayout.Gray16Le))
            {
                yield return (fixture, PixelFormat.Gray16);
            }

            if (fixture.Layouts.Contains(RawPixelLayout.Gray8))
            {
                yield return (fixture, PixelFormat.Gray8);
            }
        }
    }

    internal static RawPixelBuffer GetFrame(GoldenFixture fixture, int index, PixelFormat format, bool poster)
    {
        if (format == PixelFormat.Rgb24)
            return RawImport.DropOpaqueAlpha(poster ? fixture.GetPoster(RawPixelLayout.Rgba8) : fixture.GetFrame(index, RawPixelLayout.Rgba8));

        var layout = ImageSnapshots.GetLayout(format);
        return poster ? fixture.GetPoster(layout) : fixture.GetFrame(index, layout);
    }

    private static void Check<TPixel>(GoldenFixture fixture, string name, ReferenceResizeOptions options)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var frames = Enumerable.Range(0, fixture.Expected.FrameCount).Select(i => GetFrame(fixture, i, format, poster: false)).ToArray();
        var poster = fixture.Expected.Poster is null ? null : GetFrame(fixture, 0, format, poster: true);
        using var image = Import<TPixel>(frames[0]);
        for (var i = 1; i < frames.Length; i++)
        {
            using var single = Import<TPixel>(frames[i]);
            image.AppendFrame(single.Frames[0]);
        }

        if (poster is not null)
        {
            using var single = Import<TPixel>(poster);
            image.SetPosterFrame(single.Frames[0]);
        }

        var frameObjects = ((IEnumerable<ImageFrame<TPixel>>)image.Frames).ToArray();
        image.Resize(ToLibrary(options), TestContext.Current.CancellationToken);

        var context = $"{fixture.Id} ({format}, {name})";
        var size = ReferenceResampler.ComputeSize(frames[0].Width, frames[0].Height, options)!.Value;
        Assert.Equal(new Size(size.Width, size.Height), image.Size);
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Same(frameObjects[i], image.Frames[i]);
            AssertMatches(ReferenceResampler.Resize(frames[i], options), ImageSnapshots.CaptureFrameRows(frameObjects[i]), string.Create(CultureInfo.InvariantCulture, $"{context}, frame {i}"));
        }

        if (poster is not null)
        {
            AssertMatches(ReferenceResampler.Resize(poster, options), ImageSnapshots.CaptureFrameRows(image.PosterFrame!), context + ", poster");
        }
    }

    private static void CheckSingle<TPixel>(RawPixelBuffer source, string context, ReferenceResizeOptions options)
        where TPixel : unmanaged
    {
        using var image = Import<TPixel>(source);
        image.Resize(ToLibrary(options), TestContext.Current.CancellationToken);
        AssertMatches(ReferenceResampler.Resize(source, options), ImageSnapshots.CaptureFrameRows(image.Frames[0]), context);
    }

    private static void AssertMatches(ReferenceResizeResult reference, RawPixelBuffer actual, string context)
    {
        var result = reference.Compare(actual, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static Image<TPixel> Import<TPixel>(RawPixelBuffer buffer)
        where TPixel : unmanaged
        => Image.ImportPixelBytes<TPixel>(RawImport.ToPixelBytes(buffer, PixelFormats.GetPixelFormat<TPixel>()), buffer.Width, buffer.Height);

    private static ResizeOptions ToLibrary(ReferenceResizeOptions options) => new(options.TargetWidth, options.TargetHeight)
    {
        Mode = options.Mode switch
        {
            ReferenceResizeMode.Contain => ResizeMode.Contain,
            ReferenceResizeMode.Stretch => ResizeMode.Stretch,
            _ => ResizeMode.Cover,
        },
        Filter = options.Kernel switch
        {
            ReferenceKernel.Nearest => ResamplingFilter.NearestNeighbor,
            ReferenceKernel.Triangle => ResamplingFilter.Bilinear,
            ReferenceKernel.CatmullRom => ResamplingFilter.Bicubic,
            _ => ResamplingFilter.Lanczos3,
        },
        Anchor = (options.AnchorX, options.AnchorY) switch
        {
            (0, 0) => ResizeAnchor.TopLeft,
            (0.5m, 0) => ResizeAnchor.Top,
            (1, 0) => ResizeAnchor.TopRight,
            (0, 0.5m) => ResizeAnchor.Left,
            (0.5m, 0.5m) => ResizeAnchor.Center,
            (1, 0.5m) => ResizeAnchor.Right,
            (0, 1) => ResizeAnchor.BottomLeft,
            (0.5m, 1) => ResizeAnchor.Bottom,
            _ => ResizeAnchor.BottomRight,
        },
        AllowUpscaling = options.AllowUpscaling,
        WorkingSpace = options.Linear ? ResizeWorkingSpace.LinearSrgb : ResizeWorkingSpace.Encoded,
    };

    /// <summary>Hand-defined synthetic sources: alpha ramps with transparent edges and hidden colors, 16-bit low-bit gradients, thin and extreme shapes.</summary>
    internal static IEnumerable<(string Name, RawPixelBuffer Buffer)> Synthetic()
    {
        yield return ("rgba8-alpha-ramp-transparent-edges", Build(9, 7, RawPixelLayout.Rgba8, static (x, y) =>
        {
            // A transparent border with saturated hidden colors around an opaque-to-transparent ramp
            if (x == 0 || y == 0 || x == 8 || y == 6)
                return [255, (x * 37) % 256, 0, 0];

            return [(x * 31) % 256, 200 - (y * 20), (x * y * 9) % 256, Math.Clamp(255 - ((x - 1) * 36), 0, 255)];
        }));

        yield return ("rgba16-low-bit-alpha-ramp", Build(7, 5, RawPixelLayout.Rgba16Le, static (x, y) =>
        {
            // Samples differ only in their low byte; alpha ramps from 0 to 65535 with odd steps
            var alpha = x == 6 ? 0 : Math.Min(65535, (x * 13107) + y);
            return [0x1200 + (x * 3) + y, 0x8000 + (y * 5) + x, 0xFF00 + (x * y), alpha];
        }));

        yield return ("rgba16-single-row", Build(13, 1, RawPixelLayout.Rgba16Le, static (x, _) => [x * 5041, 65535 - (x * 17), 0x00FF, x % 4 == 0 ? 0 : 65535 - x]));

        yield return ("rgba8-single-column", Build(1, 11, RawPixelLayout.Rgba8, static (_, y) => [y * 23, 255 - (y * 23), 128, 255 - (y * 25)]));

        yield return ("rgba8-wide-strip", Build(61, 2, RawPixelLayout.Rgba8, static (x, y) => [(x * 4) % 256, y * 255, (x * x) % 256, x % 7 == 0 ? 0 : 255]));
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

    private static RawPixelBuffer ReduceTo8Bits(RawPixelBuffer buffer)
    {
        var builder = new RawPixelBufferBuilder(buffer.Width, buffer.Height, buffer.Layout);
        for (var y = 0; y < buffer.Height; y++)
        {
            for (var x = 0; x < buffer.Width; x++)
            {
                for (var c = 0; c < buffer.Layout.ChannelCount; c++)
                {
                    builder.SetSample(x, y, c, ((buffer.GetSample(x, y, c) * 255) + 32767) / 65535 * 257);
                }
            }
        }

        return builder.Build();
    }

    /// <summary>The same reference, but filtering straight (non-premultiplied) colors: what a fringe-producing implementation outputs.</summary>
    private static RawPixelBuffer Straight(RawPixelBuffer source, ReferenceResizeOptions options)
    {
        // Filter an opaque copy (colors only) and take alpha from the real reference
        var opaque = source.WithChannel(source.Layout.AlphaChannel, source.Layout.MaxSampleValue);
        var colors = ReferenceResampler.Resize(opaque, options).ToRoundedBuffer();
        var alpha = ReferenceResampler.Resize(source, options).ToRoundedBuffer();
        var builder = new RawPixelBufferBuilder(colors.Width, colors.Height, colors.Layout);
        for (var y = 0; y < colors.Height; y++)
        {
            for (var x = 0; x < colors.Width; x++)
            {
                var a = alpha.GetSample(x, y, 3);
                for (var c = 0; c < 3; c++)
                {
                    builder.SetSample(x, y, c, a == 0 ? 0 : colors.GetSample(x, y, c));
                }

                builder.SetSample(x, y, 3, a);
            }
        }

        return builder.Build();
    }
}
