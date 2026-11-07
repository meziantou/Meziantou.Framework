using System.Collections.Concurrent;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Convolution;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Bounded parallel convolution: <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers
/// convolve row bands of large frames in place; the result never depends on the worker count (each band reads the rows
/// of its neighbors before any of them is rewritten), concurrency never exceeds the configured bound, the per-worker
/// scratch is budgeted and released, and failures surface as in the sequential path.
/// </summary>
public sealed class ParallelConvolutionTests
{
    private static readonly ConvolutionOptions Blur = new(new ConvolutionKernel(3, 3, [0.0625, 0.125, 0.0625, 0.125, 0.25, 0.125, 0.0625, 0.125, 0.0625]));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<PixelFormat, ConvolutionEdgeMode> Cases()
    {
        var data = new TheoryData<PixelFormat, ConvolutionEdgeMode>();
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Rgba64 })
        {
            foreach (var mode in Enum.GetValues<ConvolutionEdgeMode>())
            {
                data.Add(format, mode);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TheResultDoesNotDependOnTheWorkerCount(PixelFormat format, ConvolutionEdgeMode mode)
    {
        var random = new Random(11);

        // The last kernel is taller than a band of the 7-worker run (about 37 rows): its bands read rows far beyond
        // their neighbors, and beyond the image at both ends
        (int Width, int Height)[] sizes = [(300, 400), (260, 257)];
        (int Width, int Height)[] kernels = [(3, 3), (5, 5), (9, 1), (1, 81)];
        foreach (var (width, height) in sizes)
        {
            var bytes = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
            random.NextBytes(bytes);
            foreach (var (kernelWidth, kernelHeight) in kernels)
            {
                var weights = new double[kernelWidth * kernelHeight];
                for (var i = 0; i < weights.Length; i++)
                {
                    weights[i] = kernelHeight > 9 && i % 8 != 0 ? 0 : (random.NextDouble() - 0.3) / Math.Min(weights.Length, 12);
                }

                var options = new ConvolutionOptions(new ConvolutionKernel(kernelWidth, kernelHeight, weights)) { EdgeMode = mode };
                var expected = Convolve(bytes, width, height, format, options, workers: 1);
                Assert.NotEqual(bytes, expected);
                foreach (var workers in new[] { 2, 3, 4, 7 })
                {
                    Assert.Equal(expected, Convolve(bytes, width, height, format, options, workers));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void AGaussianBlurDoesNotDependOnTheWorkerCount(PixelFormat format, ConvolutionEdgeMode mode)
    {
        // The rows of a band are filtered horizontally into its ring, and those of its neighbors into its halos, before any
        // row is rewritten; a deviation of 14 reads 42 rows on each side, more than a band of the 7-worker run
        var random = new Random(12);
        var (width, height) = (256, 260);
        Assert.Equal(7, RowBands.GetWorkerCount(new Size(width, height), 7));
        var bytes = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
        random.NextBytes(bytes);
        foreach (var sigma in new[] { 1.3, 14 })
        {
            var expected = Blur(bytes, workers: 1);
            Assert.NotEqual(bytes, expected);
            foreach (var workers in new[] { 2, 3, 4, 7 })
            {
                Assert.Equal(expected, Blur(bytes, workers));
            }

            byte[] Blur(byte[] source, int workers)
            {
                var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
                using var image = format switch
                {
                    PixelFormat.Rgba32 => (Image)Image.ImportPixelBytes<Rgba32>(source, width, height, configuration: configuration),
                    PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(source, width, height, configuration: configuration),
                    PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(source, width, height, configuration: configuration),
                    PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(source, width, height, configuration: configuration),
                    _ => throw new ArgumentOutOfRangeException(nameof(format)),
                };

                image.GaussianBlur(sigma, mode, cancellationToken: Ct);
                return CopyBytes(image);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void BoxBlursMediansUnsharpMasksAndSeparableMatricesDoNotDependOnTheWorkerCount(PixelFormat format, ConvolutionEdgeMode mode)
    {
        // A band of the 7-worker run has about 37 rows. The box blur sums its rows by blocks at fixed positions: a band
        // that starts inside a block computes the end of that block from its window; a radius of 45 makes blocks of 91
        // rows, and windows that read beyond the neighboring bands and beyond the image at both ends. The median, the
        // unsharp mask, the separable matrix and the edge detectors read their halos before any row is rewritten
        var random = new Random(13);
        var (width, height) = (256, 260);
        Assert.Equal(7, RowBands.GetWorkerCount(new Size(width, height), 7));
        var bytes = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
        random.NextBytes(bytes);
        double[] row = [.. Enumerable.Range(0, 17).Select(_ => random.NextDouble() - 0.2)];
        double[] column = [.. Enumerable.Range(0, 41).Select(_ => (random.NextDouble() - 0.2) / 10)];
        var separable = new ConvolutionOptions(ConvolutionKernel.CreateSeparable(row, column)) { EdgeMode = mode };
        (string Name, Action<Image> Apply)[] operations =
        [
            ("box 1", image => image.BoxBlur(1, mode, cancellationToken: Ct)),
            ("box 45", image => image.BoxBlur(45, mode, cancellationToken: Ct)),
            ("box 45 linear", image => image.BoxBlur(45, mode, ConvolutionWorkingSpace.LinearSrgb, Ct)),
            ("median 1", image => image.MedianBlur(1, mode, Ct)),
            ("median 40", image => image.MedianBlur(40, mode, Ct)),
            ("sharpen", image => image.Sharpen(12, 1.5, 0.01, mode, cancellationToken: Ct)),
            ("separable", image => image.Convolve(separable, Ct)),
            ("emboss with a bias", image => image.Convolve(new ConvolutionOptions(ConvolutionKernel.Emboss) { EdgeMode = mode, Bias = 0.25 }, Ct)),
            ("sobel", image => image.DetectEdges(EdgeDetector.Sobel, grayscale: false, mode, Ct)),
            ("kirsch", image => image.DetectEdges(EdgeDetector.Kirsch, edgeMode: mode, cancellationToken: Ct)),
            ("laplacian of gaussian", image => image.DetectEdges(EdgeDetector.LaplacianOfGaussian, grayscale: false, mode, Ct)),
        ];

        foreach (var (name, apply) in operations)
        {
            // The wide median costs the most: one 8-bit and one 16-bit format cover its two kinds of histograms
            if (name == "median 40" && format is not (PixelFormat.Gray8 or PixelFormat.Rgba64))
                continue;

            var expected = Apply(bytes, workers: 1);
            Assert.NotEqual(bytes, expected);
            foreach (var workers in new[] { 3, 7 })
            {
                Assert.True(expected.AsSpan().SequenceEqual(Apply(bytes, workers)), $"{name}, {workers} workers");
            }

            byte[] Apply(byte[] source, int workers)
            {
                var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
                using var image = format switch
                {
                    PixelFormat.Rgba32 => (Image)Image.ImportPixelBytes<Rgba32>(source, width, height, configuration: configuration),
                    PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(source, width, height, configuration: configuration),
                    PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(source, width, height, configuration: configuration),
                    PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(source, width, height, configuration: configuration),
                    _ => throw new ArgumentOutOfRangeException(nameof(format)),
                };

                apply(image);
                return CopyBytes(image);
            }
        }
    }

    [Theory]
    [InlineData(PixelFormat.RgbaFloat)]
    [InlineData(PixelFormat.RgbaHalf)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void FloatingPointBoxBlursAndMediansDoNotDependOnTheWorkerCount(PixelFormat format)
    {
        if (format == PixelFormat.RgbaFloat)
        {
            CheckFloatingPointWorkers<RgbaFloat>();
        }
        else
        {
            CheckFloatingPointWorkers<RgbaHalf>();
        }
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static void CheckFloatingPointWorkers<TPixel>()
        where TPixel : unmanaged
    {
        // Values above 1, negative ones, infinities and numbers that are not numbers. The samples of RgbaFloat are sorted
        // for every window of the median: its radius stays small
        var random = new Random(14);
        var (width, height) = (256, 260);
        Assert.Equal(7, RowBands.GetWorkerCount(new Size(width, height), 7));
        var pixels = new TPixel[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = ProcessingTests.FloatPixel<TPixel>(Sample(), Sample(), Sample(), (float)random.NextDouble());
        }

        foreach (var radius in new[] { 2, 45 })
        {
            foreach (var mode in Enum.GetValues<ConvolutionEdgeMode>())
            {
                var expected = Apply(1, radius, mode);
                foreach (var workers in new[] { 3, 7 })
                {
                    Assert.True(expected.AsSpan().SequenceEqual(Apply(workers, radius, mode)), $"radius {radius}, {mode}, {workers} workers");
                }
            }
        }

        float Sample() => random.Next(200) switch
        {
            0 => float.NaN,
            1 => float.PositiveInfinity,
            2 => float.NegativeInfinity,
            _ => (float)((random.NextDouble() * 3) - 1),
        };

        byte[] Apply(int workers, int radius, ConvolutionEdgeMode mode)
        {
            var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
            using var image = Image.ImportPixelData<TPixel>(pixels, width, height, configuration: configuration);
            image.BoxBlur(radius, mode, cancellationToken: Ct);
            image.MedianBlur(radius == 2 ? 2 : 1, mode, Ct);
            return CopyBytes(image);
        }
    }

    [Theory]
    [InlineData(ConvolutionEdgeMode.Clamp)]
    [InlineData(ConvolutionEdgeMode.Mirror)]
    [InlineData(ConvolutionEdgeMode.Wrap)]
    [InlineData(ConvolutionEdgeMode.Zero)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void BandsMatchTheIndependentReference(ConvolutionEdgeMode mode)
    {
        // An asymmetric kernel (every weight exact in binary) over random pixels with every alpha value, four bands
        const int Width = 260;
        const int Height = 257;
        decimal[] weights = [0.125m, -0.25m, 0, 0.5m, 0.75m, -0.125m, 0.0625m, 0, -0.0625m];
        var bytes = new byte[Width * Height * 4];
        new Random(5).NextBytes(bytes);
        var options = new ConvolutionOptions(new ConvolutionKernel(3, 3, [.. weights.Select(weight => (double)weight)])) { EdgeMode = mode };
        var reference = ReferenceConvolver.Convolve(
            RawPixelBuffer.Create(Width, Height, RawPixelLayout.Rgba8, bytes),
            new ReferenceConvolutionOptions(3, 3, weights, Enum.Parse<ReferenceEdgeMode>(mode.ToString())));

        using var image = Image.ImportPixelBytes<Rgba32>(bytes, Width, Height, configuration: new ImageConfiguration { MaxDegreeOfParallelism = 4 });
        var bands = new ConcurrentBag<int>();
        Convolver.TestBandObserver = (band, started) =>
        {
            if (started)
            {
                bands.Add(band);
            }
        };

        try
        {
            image.Convolve(options, Ct);
        }
        finally
        {
            Convolver.TestBandObserver = null;
        }

        Assert.Equal([0, 1, 2, 3], bands.Order());
        var result = reference.Compare(ImageSnapshots.CaptureFrame(image.Frames[0]), $"parallel {mode}", writePreviews: false);
        Assert.True(result.IsMatch, result.Describe());
    }

    [Fact]
    public void ConcurrencyNeverExceedsTheConfiguredBound()
    {
        foreach (var workers in new[] { 1, 2, 3, 4 })
        {
            var active = 0;
            var maxActive = 0;
            var bands = new ConcurrentBag<int>();
            var threads = new ConcurrentDictionary<int, bool>();
            Convolver.TestBandObserver = (band, started) =>
            {
                if (started)
                {
                    bands.Add(band);
                    threads[Environment.CurrentManagedThreadId] = true;
                    var now = Interlocked.Increment(ref active);
                    InterlockedMax(ref maxActive, now);
                    Thread.Sleep(5); // keep bands overlapping when they can
                }
                else
                {
                    Interlocked.Decrement(ref active);
                }
            };

            try
            {
                using var image = CreateImage(600, 600, workers);
                image.Convolve(Blur, Ct);
            }
            finally
            {
                Convolver.TestBandObserver = null;
            }

            Assert.Equal(Enumerable.Range(0, workers), bands.Order());
            Assert.InRange(maxActive, 1, workers);
            Assert.InRange(threads.Count, 1, workers);
            if (workers == 1)
            {
                Assert.Equal(Environment.CurrentManagedThreadId, Assert.Single(threads.Keys));
            }
        }

        static void InterlockedMax(ref int target, int value)
        {
            int current;
            while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
            {
            }
        }
    }

    [Theory]
    [InlineData(1, 1000, 1000, 1)]
    [InlineData(8, 100, 100, 1)]       // fewer than 65,536 pixels
    [InlineData(8, 4000, 40, 1)]       // fewer than 32 rows per band
    [InlineData(8, 4000, 70, 2)]
    [InlineData(8, 1000, 1000, 8)]
    [InlineData(3, 1000, 1000, 3)]
    public void SmallFramesUseFewerWorkers(int maxDegreeOfParallelism, int width, int height, int expected)
    {
        using var plan = new ConvolutionPlan(Blur.Kernel, ConvolutionEdgeMode.Clamp, preserveAlpha: false, linear: false, PixelFormat.Rgba32, new Size(width, height), maxDegreeOfParallelism);
        Assert.Equal(expected, plan.Workers);
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Bgra32)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.La16)]
    [InlineData(PixelFormat.RgbaFloat)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void EffectsDoNotDependOnTheWorkerCount(PixelFormat format)
    {
        // 257 rows is not a multiple of the blocks of the pixelation. An oil painting of radius 40 reads 40 rows on each
        // side of a band: its bands (3 with 16 workers) are at least as tall as its window
        var random = new Random(13);
        var (width, height) = (300, 257);
        Assert.Equal(8, RowBands.GetWorkerCount(new Size(width, height), 16));
        var bytes = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
        if (format is PixelFormat.RgbaFloat)
        {
            for (var i = 0; i < bytes.Length; i += sizeof(float))
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(i), random.NextSingle());
            }
        }
        else
        {
            random.NextBytes(bytes);
        }

        Action<Image>[] effects =
        [
            image => image.Pixelate(7, Ct),
            image => image.Pixelate(64, Ct),
            image => image.OilPaint(levels: 20, radius: 3, Ct),
            image => image.OilPaint(levels: 8, radius: 40, Ct),
            image => image.Flatten(new Rgba64(0x1234, 0x8000, 0xFEDC, 0xFFFF), Ct),
        ];

        foreach (var effect in effects)
        {
            var expected = Apply(effect, workers: 1);
            if (PixelFormats.HasAlpha(format))
            {
                Assert.NotEqual(bytes, expected);
            }

            foreach (var workers in new[] { 2, 3, 16 })
            {
                Assert.Equal(expected, Apply(effect, workers));
            }
        }

        byte[] Apply(Action<Image> effect, int workers)
        {
            using var image = Import(bytes, width, height, format, workers);
            effect(image);
            return CopyBytes(image);
        }
    }

    [Theory]
    [InlineData(16, 3, 8)]     // a band of 32 rows holds the window of 7 rows
    [InlineData(16, 40, 3)]    // 257 rows hold 3 windows of 81 rows
    [InlineData(16, 200, 1)]
    [InlineData(1, 3, 1)]
    public void OilPaintBandsAreAtLeastAsTallAsTheWindow(int maxDegreeOfParallelism, int radius, int expected)
    {
        using var image = CreateImage(300, 257, maxDegreeOfParallelism);
        using var scratch = EffectScratch.ForOilPaint(image.Owner.Scope, PixelFormat.Rgba32, image.Size, radius, levels: 20, maxDegreeOfParallelism);
        Assert.Equal(expected, scratch.Workers);
        Assert.Equal(expected > 1 ? radius : 0, scratch.HaloRows);
        Assert.Equal(257, scratch.GetBandStart(scratch.Workers));
        for (var band = 0; band < scratch.Workers && scratch.Workers > 1; band++)
        {
            Assert.True(scratch.GetBandStart(band + 1) - scratch.GetBandStart(band) >= (2 * radius) + 1);
        }
    }

    [Theory]
    [InlineData(64, 5)]        // 257 rows are 5 rows of blocks, the last one of 1 row
    [InlineData(7, 8)]
    [InlineData(300, 1)]
    public void PixelationBandsStartOnARowOfBlocks(int size, int expected)
    {
        using var image = CreateImage(300, 257, workers: 16);
        using var scratch = EffectScratch.ForPixelate(image.Owner.Scope, PixelFormat.Rgba32, image.Size, size, maxDegreeOfParallelism: 16);
        Assert.Equal(expected, scratch.Workers);
        Assert.Equal(257, scratch.GetBandStart(scratch.Workers));
        for (var band = 0; band < scratch.Workers; band++)
        {
            Assert.Equal(0, scratch.GetBandStart(band) % size);
        }
    }

    [Fact]
    public void EachWorkerHasItsOwnBudgetedScratchAndNothingAccumulates()
    {
        long sequentialPeak;
        using (var image = CreateImage(800, 600, workers: 1))
        {
            var baseline = image.Owner.Scope.LiveBytes;
            image.Convolve(Blur, Ct);
            sequentialPeak = image.Owner.Scope.GetDiagnostics().PeakLiveBytes - baseline;
            Assert.True(sequentialPeak > 0);
        }

        using var parallel = CreateImage(800, 600, workers: 4);
        var start = parallel.Owner.Scope.LiveBytes;
        var peaks = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            using var copy = parallel.Clone();
            var before = copy.Owner.Scope.LiveBytes;
            copy.Convolve(Blur, Ct);
            var diagnostics = copy.Owner.Scope.GetDiagnostics();
            peaks.Add(diagnostics.PeakLiveBytes - before);

            // After the operation only the pixel storage is live: the scratch of every worker was released
            Assert.Equal(before, diagnostics.LiveBytes);
            Assert.Equal(PixelStorageBytes(copy), diagnostics.LiveBytes);
        }

        // Four workers need more scratch than one, the same amount every time
        Assert.All(peaks, peak => Assert.Equal(peaks[0], peak));
        Assert.True(peaks[0] > sequentialPeak);
        Assert.Equal(start, parallel.Owner.Scope.LiveBytes);
    }

    [Fact]
    public void ParallelScratchIsCheckedAgainstTheLiveAllocationLimit()
    {
        // The limit that a single worker needs is not enough for four: the operation runs with one worker, and fails before
        // any pixel changes only when one worker does not fit either
        long needed;
        byte[] expected;
        using (var probe = CreateImage(800, 600, workers: 1))
        {
            var before = probe.Owner.Scope.LiveBytes;
            probe.Convolve(Blur, Ct);
            needed = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.True(needed > before);
            expected = CopyBytes(probe);
        }

        using (var image = CreateImage(800, 600, workers: 4, limit: needed))
        {
            var bands = new ConcurrentBag<int>();
            Convolver.TestBandObserver = (band, started) =>
            {
                if (started)
                {
                    bands.Add(band);
                }
            };

            try
            {
                image.Convolve(Blur, Ct);
            }
            finally
            {
                Convolver.TestBandObserver = null;
            }

            Assert.Equal([0], bands);
            Assert.Equal(expected, CopyBytes(image));
            Assert.Equal(needed, image.Owner.Scope.GetDiagnostics().PeakLiveBytes);
            Assert.Equal(PixelStorageBytes(image), image.Owner.Scope.LiveBytes);
        }

        using var tooSmall = CreateImage(800, 600, workers: 4, limit: needed - 1);
        var pixels = CopyBytes(tooSmall);
        Assert.Throws<ImageResourceLimitException>(() => tooSmall.Convolve(Blur, Ct));
        Assert.Equal(pixels, CopyBytes(tooSmall));
        Assert.Equal(PixelStorageBytes(tooSmall), tooSmall.Owner.Scope.LiveBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void PrepareUsesTheLargestWorkerCountWhoseScratchFits(int fitting)
    {
        var size = new Size(600, 600);
        var kernel = new ConvolutionKernel(1, 41, Enumerable.Repeat(1.0 / 41, 41).ToArray());
        var unlimited = new AllocationScope(ImageResourceLimits.Default);
        using var probe = new ConvolutionPlan(kernel, ConvolutionEdgeMode.Mirror, preserveAlpha: false, linear: false, PixelFormat.Rgba32, size, maxDegreeOfParallelism: 8);
        Assert.Equal(8, probe.Workers);
        var bytes = Enumerable.Range(1, 8).Select(workers => probe.GetScratchBytes(unlimited, workers)).ToArray();
        for (var i = 1; i < bytes.Length; i++)
        {
            Assert.True(bytes[i] > bytes[i - 1]);
        }

        // Exactly the scratch of the fitting worker count: one more worker does not fit
        var scope = new AllocationScope(new ImageResourceLimits { MaxLiveAllocationBytes = bytes[fitting - 1] });
        using (var plan = new ConvolutionPlan(kernel, ConvolutionEdgeMode.Mirror, preserveAlpha: false, linear: false, PixelFormat.Rgba32, size, maxDegreeOfParallelism: 8))
        {
            plan.Prepare(scope);
            Assert.Equal(fitting, plan.Workers);
            Assert.Equal(bytes[fitting - 1], plan.ScratchBytes);
            Assert.Equal(bytes[fitting - 1], scope.LiveBytes);
        }

        Assert.Equal(0, scope.LiveBytes);
    }

    [Fact]
    public void PrepareFailsWhenOneWorkerDoesNotFit()
    {
        var size = new Size(600, 600);
        var unlimited = new AllocationScope(ImageResourceLimits.Default);
        using var probe = new ConvolutionPlan(Blur.Kernel, ConvolutionEdgeMode.Clamp, preserveAlpha: false, linear: false, PixelFormat.Rgba32, size, maxDegreeOfParallelism: 4);
        var scope = new AllocationScope(new ImageResourceLimits { MaxLiveAllocationBytes = probe.GetScratchBytes(unlimited, workers: 1) - 1 });
        using var plan = new ConvolutionPlan(Blur.Kernel, ConvolutionEdgeMode.Clamp, preserveAlpha: false, linear: false, PixelFormat.Rgba32, size, maxDegreeOfParallelism: 4);
        Assert.Throws<ImageResourceLimitException>(() => plan.Prepare(scope));
        Assert.Equal(1, plan.Workers);
        Assert.Equal(0, scope.LiveBytes);
    }

    [Theory]
    [InlineData(ConvolutionEdgeMode.Clamp, 2)]     // the first and the last row
    [InlineData(ConvolutionEdgeMode.Mirror, 20)]   // every row, read on both sides
    [InlineData(ConvolutionEdgeMode.Wrap, 20)]
    [InlineData(ConvolutionEdgeMode.Zero, 0)]      // nothing beyond the image
    public void AKernelTallerThanTheImageReservesOneRowPerImageRow(ConvolutionEdgeMode mode, int haloRows)
    {
        // 81 rows read 40 rows on each side of a frame of 20 rows: the ring holds the 20 rows of the frame, and the 80
        // positions of the halo share the rows they map to
        var scope = new AllocationScope(ImageResourceLimits.Default);
        using var plan = new ConvolutionPlan(new ConvolutionKernel(1, 81, Enumerable.Repeat(1.0 / 81, 81).ToArray()), mode, preserveAlpha: false, linear: false, PixelFormat.Rgba32, new Size(300, 20));
        var layout = Assert.Single(plan.GetBandLayouts(1));
        Assert.Equal(20, layout.RingSize);
        Assert.Equal(haloRows, layout.Halo.RowCount);

        // Ring and halo rows (300 pixels of 4 samples, no padding for one column) and the accumulator
        var expected = scope.GetChargedSize(((1200L * (20 + haloRows)) + 1200) * sizeof(double));
        Assert.Equal(expected, plan.GetScratchBytes(scope, workers: 1));
        plan.Prepare(scope);
        Assert.Equal(expected, plan.ScratchBytes);
    }

    [Theory]
    [InlineData(ConvolutionEdgeMode.Clamp, new[] { 2, 2, 2, 2 })]
    [InlineData(ConvolutionEdgeMode.Zero, new[] { 1, 2, 2, 1 })]   // nothing above the first band or below the last one
    public void TheScratchOfEveryWorkerHasAnExactSize(ConvolutionEdgeMode mode, int[] haloRows)
    {
        // 400 rows in bands of 100 rows. The kernel is separable: the ring and halo rows (300 pixels of 4 samples) are
        // filtered horizontally from one input row padded by one pixel on each side
        var scope = new AllocationScope(ImageResourceLimits.Default);
        using var plan = new ConvolutionPlan(Blur.Kernel, mode, preserveAlpha: false, linear: false, PixelFormat.Rgba32, new Size(300, 400), maxDegreeOfParallelism: 4);
        var layouts = plan.GetBandLayouts(4);
        Assert.Equal([3, 3, 3, 3], layouts.Select(layout => layout.RingSize));
        Assert.Equal(haloRows, layouts.Select(layout => layout.Halo.RowCount));

        Assert.True(plan.IsSeparable);
        var expected = haloRows.Sum(rows => scope.GetChargedSize(((1200L * (3 + rows)) + 1200 + 1208) * sizeof(double)));
        Assert.Equal(expected, plan.GetScratchBytes(scope, workers: 4));
        plan.Prepare(scope);
        Assert.Equal(4, plan.Workers);
        Assert.Equal(expected, plan.ScratchBytes);
    }

    [Theory]
    [InlineData(ConvolutionEdgeMode.Clamp)]
    [InlineData(ConvolutionEdgeMode.Mirror)]
    [InlineData(ConvolutionEdgeMode.Wrap)]
    [InlineData(ConvolutionEdgeMode.Zero)]
    public void ALargeGaussianBlurWithFewerWorkersThanRequestedHasTheSamePixels(ConvolutionEdgeMode mode)
    {
        // A deviation of 25 reads 75 rows on each side: eight workers need much more scratch than one, and the limit only
        // leaves room for some of them
        byte[] expected;
        long needed;
        using (var probe = CreateImage(500, 400, workers: 1))
        {
            probe.GaussianBlur(25, mode, cancellationToken: Ct);
            expected = CopyBytes(probe);
            needed = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
        }

        foreach (var limit in new[] { needed, needed * 3 / 2, needed * 3 })
        {
            using var image = CreateImage(500, 400, workers: 8, limit);
            image.GaussianBlur(25, mode, cancellationToken: Ct);
            Assert.Equal(expected, CopyBytes(image));
            Assert.True(image.Owner.Scope.GetDiagnostics().PeakLiveBytes <= limit);
        }
    }

    [Fact]
    public void CancellationInAWorkerIsAnOperationCanceledException()
    {
        using var image = CreateImage(800, 600, workers: 4);
        using var source = new CancellationTokenSource();
        Convolver.TestBandObserver = (band, started) =>
        {
            if (started && band == 2)
            {
                source.Cancel();
            }
        };

        try
        {
            var exception = Assert.ThrowsAny<OperationCanceledException>(() => image.Convolve(Blur, source.Token));
            Assert.Equal(source.Token, exception.CancellationToken);
        }
        finally
        {
            Convolver.TestBandObserver = null;
        }

        // Some rows may be convolved; the image stays valid and nothing else is held
        Assert.Equal(new Size(800, 600), image.Size);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.Equal(PixelStorageBytes(image), image.Owner.Scope.LiveBytes);
        image.Convolve(Blur, Ct);
    }

    [Fact]
    public void AFailureInAWorkerIsRethrownUnwrapped()
    {
        using var image = CreateImage(800, 600, workers: 3);
        Convolver.TestBandObserver = (band, started) =>
        {
            if (started && band == 1)
                throw new InjectedAllocationFailureException();
        };

        try
        {
            Assert.Throws<InjectedAllocationFailureException>(() => image.Convolve(Blur, Ct));
        }
        finally
        {
            Convolver.TestBandObserver = null;
        }

        Assert.Equal(new Size(800, 600), image.Size);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);
        Assert.Equal(PixelStorageBytes(image), image.Owner.Scope.LiveBytes);
    }

    private static Image<Rgba32> CreateImage(int width, int height, int workers, long? limit = null)
    {
        var configuration = new ImageConfiguration
        {
            MaxDegreeOfParallelism = workers,
            Limits = limit is { } value ? new ImageResourceLimits { MaxLiveAllocationBytes = value } : ImageResourceLimits.Default,
        };

        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)(i * 7), (byte)(i >> 3), (byte)(i >> 9), (byte)(255 - (i % 200)));
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height, configuration: configuration);
    }

    private static byte[] Convolve(byte[] bytes, int width, int height, PixelFormat format, ConvolutionOptions options, int workers)
    {
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
        using var image = format switch
        {
            PixelFormat.Rgba32 => (Image)Image.ImportPixelBytes<Rgba32>(bytes, width, height, configuration: configuration),
            PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(bytes, width, height, configuration: configuration),
            PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(bytes, width, height, configuration: configuration),
            PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(bytes, width, height, configuration: configuration),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        image.Convolve(options, Ct);
        return CopyBytes(image);
    }

    private static Image Import(byte[] bytes, int width, int height, PixelFormat format, int workers)
    {
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers };
        return format switch
        {
            PixelFormat.Rgba32 => Image.ImportPixelBytes<Rgba32>(bytes, width, height, configuration: configuration),
            PixelFormat.Bgra32 => Image.ImportPixelBytes<Bgra32>(bytes, width, height, configuration: configuration),
            PixelFormat.Rgb24 => Image.ImportPixelBytes<Rgb24>(bytes, width, height, configuration: configuration),
            PixelFormat.Gray8 => Image.ImportPixelBytes<Gray8>(bytes, width, height, configuration: configuration),
            PixelFormat.Rgba64 => Image.ImportPixelBytes<Rgba64>(bytes, width, height, configuration: configuration),
            PixelFormat.La16 => Image.ImportPixelBytes<La16>(bytes, width, height, configuration: configuration),
            PixelFormat.RgbaFloat => Image.ImportPixelBytes<RgbaFloat>(bytes, width, height, configuration: configuration),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static byte[] CopyBytes(Image image)
    {
        var result = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(result);
        return result;
    }

    private static long PixelStorageBytes(Image image) => image.Owner.Scope.GetLiveBytes(AllocationKind.ImagePixels);
}
