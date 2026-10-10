using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Convolution;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Convolve checked against the independent high-precision reference of the test harness
/// (<see cref="ReferenceConvolver"/>: a direct two-dimensional decimal sum per output pixel over a copy of the source),
/// over every raw reference of the golden corpus (frames and posters, 8- and 16-bit, alpha, odd sizes, images smaller
/// than the matrices) and over the synthetic sources of <see cref="ResizeGoldenTests"/> (alpha ramps with transparent
/// edges, 16-bit low-bit gradients, one-pixel axes), with contiguous and segmented storages.
/// </summary>
/// <remarks>
/// Tolerance: none beyond rounding, as for resize. Each sample must be the correctly rounded exact value (ties upward);
/// only when the exact value lies within <see cref="ReferenceFilterResult.TieWindow"/> of a rounding boundary is the other
/// neighbor accepted. Filtered alpha follows the same rule and a zero output alpha requires transparent black; a
/// preserved alpha must be the source alpha exactly.
/// </remarks>
public sealed class ConvolutionGoldenTests
{
    private const decimal Ninth = 1m / 9;

    private static readonly (string Name, ReferenceConvolutionOptions Options)[] Specs =
    [
        ("sharpen-3x3", new(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0])),
        ("box-3x3-mirror", new(3, 3, [Ninth, Ninth, Ninth, Ninth, Ninth, Ninth, Ninth, Ninth, Ninth], ReferenceEdgeMode.Mirror)),
        ("binomial-5x5-wrap", new(5, 5, Binomial5(), ReferenceEdgeMode.Wrap)),
        ("emboss-3x3-zero", new(3, 3, [-2, -1, 0, -1, 1, 1, 0, 1, 2], ReferenceEdgeMode.Zero)),
        ("sobel-3x3-alpha-preserved", new(3, 3, [-1, 0, 1, -2, 0, 2, -1, 0, 1], PreserveAlpha: true)),
        ("tall-1x7-mirror-alpha-preserved", new(1, 7, [0.25m, 0, -0.125m, 0.5m, 0.0625m, 0, 0.3125m], ReferenceEdgeMode.Mirror, PreserveAlpha: true)),
        ("wide-9x1-wrap", new(9, 1, [0.03125m, 0.0625m, 0, 0.25m, 0.375m, 0.125m, 0, 0.09375m, 0.0625m], ReferenceEdgeMode.Wrap)),
        ("asymmetric-3x5-zero", new(3, 5, [0.1m, 0, 0.05m, 0, 0.2m, -0.1m, 0.05m, 0.3m, 0.15m, 0, 0.1m, 0, 0.05m, 0.05m, 0.05m], ReferenceEdgeMode.Zero)),
        ("binomial-3x3-linear", new(3, 3, [0.0625m, 0.125m, 0.0625m, 0.125m, 0.25m, 0.125m, 0.0625m, 0.125m, 0.0625m], Linear: true)),
        ("sharpen-3x3-linear-mirror-alpha-preserved", new(3, 3, [0, -0.5m, 0, -0.5m, 3, -0.5m, 0, -0.5m, 0], ReferenceEdgeMode.Mirror, PreserveAlpha: true, Linear: true)),
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

    [Fact]
    public void SpecsCoverEveryEdgeModeAlphaModeAndWorkingSpace()
    {
        foreach (var mode in Enum.GetValues<ReferenceEdgeMode>())
        {
            Assert.Contains(Specs, spec => spec.Options.EdgeMode == mode);
        }

        Assert.Contains(Specs, spec => spec.Options.PreserveAlpha && !spec.Options.Linear);
        Assert.Contains(Specs, spec => !spec.Options.PreserveAlpha && spec.Options.Linear);
        Assert.Contains(Specs, spec => spec.Options.PreserveAlpha && spec.Options.Linear);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConvolvedFramesAndPostersMatchTheIndependentReference(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        foreach (var (name, options) in Specs)
        {
            foreach (var segmented in new[] { false, true })
            {
                switch (Enum.Parse<PixelFormat>(format))
                {
                    case PixelFormat.Rgba32: Check<Rgba32>(fixture, name, options, segmented); break;
                    case PixelFormat.Bgra32: Check<Bgra32>(fixture, name, options, segmented); break;
                    case PixelFormat.Rgb24: Check<Rgb24>(fixture, name, options, segmented); break;
                    case PixelFormat.Rgba64: Check<Rgba64>(fixture, name, options, segmented); break;
                    case PixelFormat.Gray8: Check<Gray8>(fixture, name, options, segmented); break;
                    case PixelFormat.Gray16: Check<Gray16>(fixture, name, options, segmented); break;
                    default: throw new ArgumentOutOfRangeException(nameof(format));
                }
            }
        }
    }

    public static TheoryData<string> SyntheticSources => [.. ResizeGoldenTests.Synthetic().Select(item => item.Name)];

    [Theory]
    [MemberData(nameof(SyntheticSources))]
    public void SyntheticSourcesMatchTheIndependentReference(string name)
    {
        var source = ResizeGoldenTests.Synthetic().Single(item => item.Name == name).Buffer;
        foreach (var (specName, options) in Specs)
        {
            CheckSynthetic(source, $"{name}, {specName}", options);
        }

        // One matrix under every combination of edge mode, alpha mode and working space
        decimal[] weights = [0.0625m, -0.125m, 0.25m, 0.5m, 0.375m, -0.0625m, 0, 0.125m, -0.125m];
        foreach (var mode in Enum.GetValues<ReferenceEdgeMode>())
        {
            foreach (var preserveAlpha in new[] { false, true })
            {
                foreach (var linear in new[] { false, true })
                {
                    CheckSynthetic(source, name, new ReferenceConvolutionOptions(3, 3, weights, mode, preserveAlpha, linear));
                }
            }
        }
    }

    [Fact]
    public void TheReferenceDetectsWrongResults()
    {
        // The comparison must reject a channel swap, a one-unit error, a flipped matrix, another edge mode and straight
        // (non-premultiplied) filtering around transparent pixels
        var source = ResizeGoldenTests.Synthetic().Single(item => item.Name == "rgba16-low-bit-alpha-ramp").Buffer;
        var options = new ReferenceConvolutionOptions(3, 3, [0.0625m, -0.125m, 0.25m, 0.5m, 0.375m, -0.0625m, 0, 0.125m, -0.125m]);
        var reference = ReferenceConvolver.Convolve(source, options);
        var actual = Convolve<Rgba64>(source, options);
        Assert.True(reference.Compare(actual, "baseline", writePreviews: false).IsMatch);

        Assert.False(reference.Compare(actual.SwapChannels(0, 2), "swap", writePreviews: false).IsMatch);
        var x = actual.GetSample(2, 1, 3) == 0 ? 3 : 2;
        Assert.False(reference.Compare(actual.WithSample(x, 1, 1, actual.GetSample(x, 1, 1) ^ 1), "one unit", writePreviews: false).IsMatch);

        var flipped = options with { Weights = [.. options.Weights.Reverse()] };
        Assert.False(reference.Compare(Convolve<Rgba64>(source, flipped), "flipped", writePreviews: false).IsMatch);
        Assert.False(reference.Compare(Convolve<Rgba64>(source, options with { EdgeMode = ReferenceEdgeMode.Wrap }), "edge mode", writePreviews: false).IsMatch);

        var result = reference.Compare(Convolve<Rgba64>(source, options with { PreserveAlpha = true }), "straight", writePreviews: false);
        Assert.False(result.IsMatch);
        Assert.Contains("rejected", result.Describe(), StringComparison.Ordinal);
    }

    private static decimal[] Binomial5()
    {
        // The outer product of 1 4 6 4 1 with itself, divided by 256: every weight is exact in binary
        int[] row = [1, 4, 6, 4, 1];
        return [.. row.SelectMany(vertical => row.Select(horizontal => vertical * horizontal / 256m))];
    }

    private static void Check<TPixel>(GoldenFixture fixture, string name, ReferenceConvolutionOptions options, bool segmented)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var frames = Enumerable.Range(0, fixture.Expected.FrameCount).Select(i => ResizeGoldenTests.GetFrame(fixture, i, format, poster: false)).ToArray();
        var poster = fixture.Expected.Poster is null ? null : ResizeGoldenTests.GetFrame(fixture, 0, format, poster: true);
        using var image = Import<TPixel>(frames[0], segmented);
        for (var i = 1; i < frames.Length; i++)
        {
            using var single = Import<TPixel>(frames[i], segmented: false);
            image.AppendFrame(single.Frames[0]);
        }

        if (poster is not null)
        {
            using var single = Import<TPixel>(poster, segmented: false);
            image.SetPosterFrame(single.Frames[0]);
        }

        var frameObjects = ((IEnumerable<ImageFrame<TPixel>>)image.Frames).ToArray();
        image.Convolve(ToLibrary(options), TestContext.Current.CancellationToken);

        var context = $"{fixture.Id} ({format}, {name}, segmented: {segmented})";
        Assert.Equal(new Size(frames[0].Width, frames[0].Height), image.Size);
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Same(frameObjects[i], image.Frames[i]);
            AssertMatches(ReferenceConvolver.Convolve(frames[i], options), ImageSnapshots.CaptureFrameRows(frameObjects[i]), string.Create(CultureInfo.InvariantCulture, $"{context}, frame {i}"));
        }

        if (poster is not null)
        {
            AssertMatches(ReferenceConvolver.Convolve(poster, options), ImageSnapshots.CaptureFrameRows(image.PosterFrame!), context + ", poster");
        }
    }

    private static void CheckSynthetic(RawPixelBuffer source, string context, ReferenceConvolutionOptions options)
    {
        var actual = source.Layout == RawPixelLayout.Rgba16Le ? Convolve<Rgba64>(source, options) : Convolve<Rgba32>(source, options);
        AssertMatches(ReferenceConvolver.Convolve(source, options), actual, context);
    }

    private static RawPixelBuffer Convolve<TPixel>(RawPixelBuffer source, ReferenceConvolutionOptions options)
        where TPixel : unmanaged
    {
        using var image = Import<TPixel>(source, segmented: false);
        image.Convolve(ToLibrary(options), TestContext.Current.CancellationToken);
        return ImageSnapshots.CaptureFrameRows(image.Frames[0]);
    }

    private static void AssertMatches(ReferenceFilterResult reference, RawPixelBuffer actual, string context)
    {
        var result = reference.Compare(actual, context);
        if (!result.IsMatch)
            throw new GoldenAssertionException(result.Describe());
    }

    private static Image<TPixel> Import<TPixel>(RawPixelBuffer buffer, bool segmented)
        where TPixel : unmanaged
    {
        // Segmented: two padded rows per slab, so the rows of a matrix come from several slabs
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var layoutOptions = segmented ? new PixelStorageLayoutOptions { RowAlignment = 16, TargetSlabBytes = 2 * ((buffer.RowBytes + 15) / 16 * 16) } : null;
        return Image.ImportPixelBytesCore<TPixel>(RawImport.ToPixelBytes(buffer, format), buffer.Width, buffer.Height, buffer.RowBytes, ImageConfiguration.Default, layoutOptions);
    }

    private static ConvolutionOptions ToLibrary(ReferenceConvolutionOptions options)
        => new(new ConvolutionKernel(options.KernelWidth, options.KernelHeight, [.. options.Weights.Select(weight => (double)weight)]))
        {
            EdgeMode = options.EdgeMode switch
            {
                ReferenceEdgeMode.Clamp => ConvolutionEdgeMode.Clamp,
                ReferenceEdgeMode.Mirror => ConvolutionEdgeMode.Mirror,
                ReferenceEdgeMode.Wrap => ConvolutionEdgeMode.Wrap,
                _ => ConvolutionEdgeMode.Zero,
            },
            PreserveAlpha = options.PreserveAlpha,
            WorkingSpace = options.Linear ? ConvolutionWorkingSpace.LinearSrgb : ConvolutionWorkingSpace.Encoded,
        };
}
