using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
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
        ("sobel-3x3-bias-alpha-preserved", new(3, 3, [-1, 0, 1, -2, 0, 2, -1, 0, 1], PreserveAlpha: true, Bias: 0.5m)),
        ("emboss-3x3-negative-bias-mirror", new(3, 3, [-2, -1, 0, -1, 1, 1, 0, 1, 2], ReferenceEdgeMode.Mirror, Bias: -0.375m)),
        ("binomial-3x3-linear-bias-wrap", new(3, 3, [0.0625m, 0.125m, 0.0625m, 0.125m, 0.25m, 0.125m, 0.0625m, 0.125m, 0.0625m], ReferenceEdgeMode.Wrap, Linear: true, Bias: 0.1m)),
    ];

    /// <summary>
    /// The edge operators, with the matrices written out as published (independently of the presets of the library): the
    /// eight Kirsch and the eight Robinson compass matrices (north, north-west, west, south-west, south, south-east, east,
    /// north-east), whose largest response the reference takes over all eight.
    /// </summary>
    private static readonly (string Name, EdgeDetector Detector, int Size, decimal[][] Matrices, ReferenceEdgeCombination Combination)[] EdgeOperators =
    [
        ("sobel", EdgeDetector.Sobel, 3, [[-1, 0, 1, -2, 0, 2, -1, 0, 1], [-1, -2, -1, 0, 0, 0, 1, 2, 1]], ReferenceEdgeCombination.Magnitude),
        ("scharr", EdgeDetector.Scharr, 3, [[-3, 0, 3, -10, 0, 10, -3, 0, 3], [-3, -10, -3, 0, 0, 0, 3, 10, 3]], ReferenceEdgeCombination.Magnitude),
        ("prewitt", EdgeDetector.Prewitt, 3, [[-1, 0, 1, -1, 0, 1, -1, 0, 1], [-1, -1, -1, 0, 0, 0, 1, 1, 1]], ReferenceEdgeCombination.Magnitude),
        ("kirsch", EdgeDetector.Kirsch, 3,
        [
            [5, 5, 5, -3, 0, -3, -3, -3, -3],
            [5, 5, -3, 5, 0, -3, -3, -3, -3],
            [5, -3, -3, 5, 0, -3, 5, -3, -3],
            [-3, -3, -3, 5, 0, -3, 5, 5, -3],
            [-3, -3, -3, -3, 0, -3, 5, 5, 5],
            [-3, -3, -3, -3, 0, 5, -3, 5, 5],
            [-3, -3, 5, -3, 0, 5, -3, -3, 5],
            [-3, 5, 5, -3, 0, 5, -3, -3, -3],
        ], ReferenceEdgeCombination.Maximum),
        ("robinson", EdgeDetector.Robinson, 3,
        [
            [1, 2, 1, 0, 0, 0, -1, -2, -1],
            [2, 1, 0, 1, 0, -1, 0, -1, -2],
            [1, 0, -1, 2, 0, -2, 1, 0, -1],
            [0, -1, -2, 1, 0, -1, 2, 1, 0],
            [-1, -2, -1, 0, 0, 0, 1, 2, 1],
            [-2, -1, 0, -1, 0, 1, 0, 1, 2],
            [-1, 0, 1, -2, 0, 2, -1, 0, 1],
            [0, 1, 2, -1, 0, 1, -2, -1, 0],
        ], ReferenceEdgeCombination.Maximum),
        ("laplacian-3x3", EdgeDetector.Laplacian3x3, 3, [[0, 1, 0, 1, -4, 1, 0, 1, 0]], ReferenceEdgeCombination.Absolute),
        ("laplacian-5x5", EdgeDetector.Laplacian5x5, 5, [[2, 4, 4, 4, 2, 4, 0, -8, 0, 4, 4, -8, -24, -8, 4, 4, 0, -8, 0, 4, 2, 4, 4, 4, 2]], ReferenceEdgeCombination.Absolute),
        ("laplacian-of-gaussian", EdgeDetector.LaplacianOfGaussian, 5, [[0, 0, 1, 0, 0, 0, 1, 2, 1, 0, 1, 2, -16, 2, 1, 0, 1, 2, 1, 0, 0, 0, 1, 0, 0]], ReferenceEdgeCombination.Absolute),
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
                CheckOperation(fixture, format, name, image => image.Convolve(ToLibrary(options), TestContext.Current.CancellationToken), frame => ReferenceConvolver.Convolve(frame, options), segmented);
            }
        }
    }

    /// <summary>
    /// The box blur, the median and the unsharp mask of every frame and poster of the golden corpus (every pixel format,
    /// alpha, odd sizes, images smaller than the windows): a box blur is the convolution by a matrix of equal weights, an
    /// unsharp mask adds the difference from the Gaussian blur, and a median is checked against a sort of every window.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void BoxBlursMediansAndUnsharpMasksOfFramesAndPostersMatchTheIndependentReference(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var ct = TestContext.Current.CancellationToken;
        var box1 = Box(1, ReferenceEdgeMode.Clamp);
        var box3 = Box(3, ReferenceEdgeMode.Mirror);
        var box2 = Box(2, ReferenceEdgeMode.Wrap) with { Linear = true };
        var gaussian = Gaussian(1.2, ReferenceEdgeMode.Zero);
        (string Name, Action<Image> Apply, Func<RawPixelBuffer, ReferenceFilterResult> Reference)[] operations =
        [
            ("box-1", image => image.BoxBlur(1, cancellationToken: ct), frame => ReferenceConvolver.Convolve(frame, box1)),
            ("box-3-mirror", image => image.BoxBlur(3, ConvolutionEdgeMode.Mirror, cancellationToken: ct), frame => ReferenceConvolver.Convolve(frame, box3)),
            ("box-2-wrap-linear", image => image.BoxBlur(2, ConvolutionEdgeMode.Wrap, ConvolutionWorkingSpace.LinearSrgb, ct), frame => ReferenceConvolver.Convolve(frame, box2)),
            ("median-1", image => image.MedianBlur(1, cancellationToken: ct), frame => ReferenceConvolver.Median(frame, 1, ReferenceEdgeMode.Clamp)),
            ("median-2-zero", image => image.MedianBlur(2, ConvolutionEdgeMode.Zero, ct), frame => ReferenceConvolver.Median(frame, 2, ReferenceEdgeMode.Zero)),
            ("sharpen-1.2-zero", image => image.Sharpen(1.2, 1.5, edgeMode: ConvolutionEdgeMode.Zero, cancellationToken: ct), frame => ReferenceConvolver.Sharpen(frame, gaussian, 1.5m, 0)),
        ];

        foreach (var (name, apply, reference) in operations)
        {
            foreach (var segmented in new[] { false, true })
            {
                CheckOperation(fixture, format, name, apply, reference, segmented);
            }
        }
    }

    /// <summary>
    /// The edges of every frame and poster of the golden corpus (every pixel format, alpha, odd sizes, images smaller than
    /// the matrices), with and without grayscale, against the decimal reference: the exact combination of the exact sums,
    /// rounded once. Each operator takes another edge mode.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EdgesOfFramesAndPostersMatchTheIndependentReference(string id, string format)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var ct = TestContext.Current.CancellationToken;
        var modes = Enum.GetValues<ReferenceEdgeMode>();
        for (var i = 0; i < EdgeOperators.Length; i++)
        {
            var (name, detector, size, matrices, combination) = EdgeOperators[i];
            var mode = modes[i % modes.Length];
            var edgeMode = ToLibrary(new ReferenceConvolutionOptions(1, 1, [1], mode)).EdgeMode;
            foreach (var grayscale in new[] { false, true })
            {
                CheckOperation(fixture, format, $"{name}-{mode}{(grayscale ? "-grayscale" : "")}", image => image.DetectEdges(detector, grayscale, edgeMode, ct), frame => ReferenceConvolver.DetectEdges(frame, size, matrices, combination, mode, grayscale), segmented: grayscale);
            }
        }
    }

    /// <summary>Every edge operator under every edge mode, with and without grayscale, over the synthetic sources and noise.</summary>
    [Theory]
    [MemberData(nameof(BlurSources))]
    public void EdgesMatchTheIndependentReference(string name)
    {
        var source = BlurInputs().Single(item => item.Name == name).Buffer;
        foreach (var (operatorName, detector, size, matrices, combination) in EdgeOperators)
        {
            foreach (var mode in Enum.GetValues<ReferenceEdgeMode>())
            {
                var edgeMode = ToLibrary(new ReferenceConvolutionOptions(1, 1, [1], mode)).EdgeMode;
                foreach (var grayscale in new[] { false, true })
                {
                    var actual = Apply(source, image => image.DetectEdges(detector, grayscale, edgeMode, TestContext.Current.CancellationToken));
                    AssertMatches(ReferenceConvolver.DetectEdges(source, size, matrices, combination, mode, grayscale), actual, $"{name}, {operatorName}, {mode}{(grayscale ? ", grayscale" : "")}");
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

    public static TheoryData<string> BlurSources => [.. BlurInputs().Select(item => item.Name)];

    /// <summary>
    /// A Gaussian blur is the convolution by the matrix of the products of its weights, rounded once: the reference sums
    /// that matrix directly, with weights computed from the exponential of the platform in <see cref="decimal"/> (they may
    /// differ from those of the library by a unit in the last place of a double, far below the tie window). Rounding after
    /// each direction (two convolutions) puts 5 to 9% of the samples of noise one unit off, and far more around
    /// translucent pixels.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlurSources))]
    public void GaussianBlursMatchTheIndependentReference(string name)
    {
        var source = BlurInputs().Single(item => item.Name == name).Buffer;
        foreach (var sigma in new[] { 0.6, 1.5, 2.5 })
        {
            var radius = (int)Math.Ceiling(3 * sigma);
            decimal[] row = [.. Enumerable.Range(-radius, (2 * radius) + 1).Select(i => (decimal)Math.Exp(-(i * i) / (2 * sigma * sigma)))];
            var sum = row.Sum();
            row = [.. row.Select(weight => weight / sum)];
            decimal[] matrix = [.. row.SelectMany(vertical => row.Select(horizontal => vertical * horizontal))];
            foreach (var mode in Enum.GetValues<ReferenceEdgeMode>())
            {
                foreach (var linear in new[] { false, true })
                {
                    if (linear && mode != ReferenceEdgeMode.Clamp)
                        continue;

                    var options = new ReferenceConvolutionOptions(row.Length, row.Length, matrix, mode, Linear: linear);
                    var actual = source.Layout == RawPixelLayout.Rgba16Le ? Blur<Rgba64>(source, sigma, options) : Blur<Rgba32>(source, sigma, options);
                    AssertMatches(ReferenceConvolver.Convolve(source, options), actual, string.Create(CultureInfo.InvariantCulture, $"{name}, blur {sigma}, {options}"));
                }
            }
        }
    }

    /// <summary>
    /// A box blur is the convolution by a matrix of <c>n^2</c> equal weights <c>1 / n^2</c>, rounded once: the reference
    /// sums that matrix directly. The radii include one whose window is wider and taller than the noise (23x17), so that
    /// the edge modes read several periods beyond the image.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlurSources))]
    public void BoxBlursMatchTheIndependentReference(string name)
    {
        var source = BlurInputs().Single(item => item.Name == name).Buffer;
        foreach (var radius in new[] { 1, 2, 5, 13 })
        {
            foreach (var mode in Enum.GetValues<ReferenceEdgeMode>())
            {
                foreach (var linear in new[] { false, true })
                {
                    if (linear && mode != ReferenceEdgeMode.Clamp)
                        continue;

                    var options = Box(radius, mode) with { Linear = linear };
                    var actual = Apply(source, image => image.BoxBlur(radius, ToLibrary(options).EdgeMode, linear ? ConvolutionWorkingSpace.LinearSrgb : ConvolutionWorkingSpace.Encoded, TestContext.Current.CancellationToken));
                    AssertMatches(ReferenceConvolver.Convolve(source, options), actual, string.Create(CultureInfo.InvariantCulture, $"{name}, box {radius}, {options}"));
                }
            }
        }
    }

    /// <summary>
    /// An unsharp mask adds to every filtered sample its difference from the Gaussian blur, times the amount, unless the
    /// difference is below the threshold: the reference computes the blur of the matrix of the products of the weights and
    /// the original samples in <see cref="decimal"/>, premultiplied by alpha or in linear light like the convolution.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlurSources))]
    public void UnsharpMasksMatchTheIndependentReference(string name)
    {
        var source = BlurInputs().Single(item => item.Name == name).Buffer;
        foreach (var (sigma, amount, threshold) in new[] { (0.7, 1m, 0m), (1.5, 2.5m, 0.03m), (2.5, 0.6m, 0.004m) })
        {
            foreach (var mode in Enum.GetValues<ReferenceEdgeMode>())
            {
                foreach (var linear in new[] { false, true })
                {
                    if (linear && mode != ReferenceEdgeMode.Clamp)
                        continue;

                    var options = Gaussian(sigma, mode) with { Linear = linear };
                    var actual = Apply(source, image => image.Sharpen(sigma, (double)amount, (double)threshold, ToLibrary(options).EdgeMode, linear ? ConvolutionWorkingSpace.LinearSrgb : ConvolutionWorkingSpace.Encoded, TestContext.Current.CancellationToken));
                    AssertMatches(ReferenceConvolver.Sharpen(source, options, amount, threshold), actual, string.Create(CultureInfo.InvariantCulture, $"{name}, sharpen {sigma} {amount} {threshold}, {options}"));
                }
            }
        }
    }

    /// <summary>A median is one of the samples of its window: the reference sorts every window, and the comparison is exact.</summary>
    [Theory]
    [MemberData(nameof(BlurSources))]
    public void MediansMatchTheIndependentReference(string name)
    {
        var source = BlurInputs().Single(item => item.Name == name).Buffer;
        foreach (var radius in new[] { 1, 2, 3, 12 })
        {
            foreach (var mode in Enum.GetValues<ReferenceEdgeMode>())
            {
                var actual = Apply(source, image => image.MedianBlur(radius, ToLibrary(Box(1, mode)).EdgeMode, TestContext.Current.CancellationToken));
                AssertMatches(ReferenceConvolver.Median(source, radius, mode), actual, string.Create(CultureInfo.InvariantCulture, $"{name}, median {radius}, {mode}"));
            }
        }
    }

    /// <summary>The convolution options of a box blur: a matrix of <c>(2 radius + 1)^2</c> weights equal to their inverse.</summary>
    private static ReferenceConvolutionOptions Box(int radius, ReferenceEdgeMode mode)
    {
        var size = (2 * radius) + 1;
        return new ReferenceConvolutionOptions(size, size, [.. Enumerable.Repeat(1m / (size * size), size * size)], mode);
    }

    /// <summary>The convolution options of a Gaussian blur: the matrix of the products of the weights of the definition, from the exponential of the platform.</summary>
    private static ReferenceConvolutionOptions Gaussian(double sigma, ReferenceEdgeMode mode)
    {
        var radius = (int)Math.Ceiling(3 * sigma);
        decimal[] row = [.. Enumerable.Range(-radius, (2 * radius) + 1).Select(i => (decimal)Math.Exp(-(i * i) / (2 * sigma * sigma)))];
        var sum = row.Sum();
        row = [.. row.Select(weight => weight / sum)];
        return new ReferenceConvolutionOptions(row.Length, row.Length, [.. row.SelectMany(vertical => row.Select(horizontal => vertical * horizontal))], mode);
    }

    private static RawPixelBuffer Apply(RawPixelBuffer source, Action<Image> apply)
        => source.Layout == RawPixelLayout.Rgba16Le ? Apply<Rgba64>(source, apply) : Apply<Rgba32>(source, apply);

    private static RawPixelBuffer Apply<TPixel>(RawPixelBuffer source, Action<Image> apply)
        where TPixel : unmanaged
    {
        using var image = Import<TPixel>(source, segmented: true);
        apply(image);
        return ImageSnapshots.CaptureFrameRows(image.Frames[0]);
    }

    /// <summary>The synthetic sources, and noise with and without translucent pixels.</summary>
    private static IEnumerable<(string Name, RawPixelBuffer Buffer)> BlurInputs()
    {
        foreach (var item in ResizeGoldenTests.Synthetic())
        {
            yield return item;
        }

        foreach (var layout in new[] { RawPixelLayout.Rgba8, RawPixelLayout.Rgba16Le })
        {
            foreach (var opaque in new[] { false, true })
            {
                // Deterministic noise: the position and the channel hashed
                var max = layout == RawPixelLayout.Rgba8 ? 255 : 65535;
                var builder = new RawPixelBufferBuilder(23, 17, layout);
                for (var y = 0; y < 17; y++)
                {
                    for (var x = 0; x < 23; x++)
                    {
                        for (var c = 0; c < 4; c++)
                        {
                            var hash = unchecked((uint)((x * 73856093) ^ (y * 19349663) ^ (c * 83492791)) * 2654435761u);
                            builder.SetSample(x, y, c, c == 3 && opaque ? max : (int)((hash >> 8) % (uint)(max + 1)));
                        }
                    }
                }

                yield return (string.Create(CultureInfo.InvariantCulture, $"noise-{(max == 255 ? 8 : 16)}{(opaque ? "-opaque" : string.Empty)}"), builder.Build());
            }
        }
    }

    private static RawPixelBuffer Blur<TPixel>(RawPixelBuffer source, double sigma, ReferenceConvolutionOptions options)
        where TPixel : unmanaged
    {
        using var image = Import<TPixel>(source, segmented: true);
        image.GaussianBlur(sigma, ToLibrary(options).EdgeMode, options.Linear ? ConvolutionWorkingSpace.LinearSrgb : ConvolutionWorkingSpace.Encoded, TestContext.Current.CancellationToken);
        return ImageSnapshots.CaptureFrameRows(image.Frames[0]);
    }

    /// <summary>
    /// Linear-light convolution decodes the samples with the transfer function that the CICP color description of the
    /// image declares, against the <see cref="decimal"/> reference.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(18)]
    public void LinearLightUsesTheTransferCharacteristicsOfTheImage(int transfer)
    {
        decimal[] weights = [0.0625m, -0.125m, 0.25m, 0.5m, 0.375m, -0.0625m, 0, 0.125m, -0.125m];
        foreach (var (name, source) in ResizeGoldenTests.Synthetic().Where(item => item.Name is "rgba8-alpha-ramp-transparent-edges" or "rgba16-low-bit-alpha-ramp"))
        {
            foreach (var preserveAlpha in new[] { false, true })
            {
                CheckSynthetic(source, name, new ReferenceConvolutionOptions(3, 3, weights, ReferenceEdgeMode.Clamp, preserveAlpha, Linear: true, Transfer: transfer));
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

    private static void CheckOperation(GoldenFixture fixture, string format, string name, Action<Image> apply, Func<RawPixelBuffer, ReferenceFilterResult> reference, bool segmented)
    {
        switch (Enum.Parse<PixelFormat>(format))
        {
            case PixelFormat.Rgba32: Check<Rgba32>(fixture, name, apply, reference, segmented); break;
            case PixelFormat.Bgra32: Check<Bgra32>(fixture, name, apply, reference, segmented); break;
            case PixelFormat.Rgb24: Check<Rgb24>(fixture, name, apply, reference, segmented); break;
            case PixelFormat.Rgba64: Check<Rgba64>(fixture, name, apply, reference, segmented); break;
            case PixelFormat.Gray8: Check<Gray8>(fixture, name, apply, reference, segmented); break;
            case PixelFormat.Gray16: Check<Gray16>(fixture, name, apply, reference, segmented); break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void Check<TPixel>(GoldenFixture fixture, string name, Action<Image> apply, Func<RawPixelBuffer, ReferenceFilterResult> reference, bool segmented)
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
        apply(image);

        var context = $"{fixture.Id} ({format}, {name}, segmented: {segmented})";
        Assert.Equal(new Size(frames[0].Width, frames[0].Height), image.Size);
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Same(frameObjects[i], image.Frames[i]);
            AssertMatches(reference(frames[i]), ImageSnapshots.CaptureFrameRows(frameObjects[i]), string.Create(CultureInfo.InvariantCulture, $"{context}, frame {i}"));
        }

        if (poster is not null)
        {
            AssertMatches(reference(poster), ImageSnapshots.CaptureFrameRows(image.PosterFrame!), context + ", poster");
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
        if (options.Transfer != 13)
            image.Metadata.Cicp = new CicpColorDescription(9, (byte)options.Transfer, 0, fullRange: true);

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
            Bias = (double)options.Bias,
        };
}
