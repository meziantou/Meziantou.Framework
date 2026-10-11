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
        // The limit that a single worker needs is not enough for four: the operation fails before any pixel changes
        long needed;
        using (var probe = CreateImage(800, 600, workers: 1))
        {
            var before = probe.Owner.Scope.LiveBytes;
            probe.Convolve(Blur, Ct);
            needed = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.True(needed > before);
        }

        using var image = CreateImage(800, 600, workers: 4, limit: needed);
        var pixels = CopyBytes(image);
        Assert.Throws<ImageResourceLimitException>(() => image.Convolve(Blur, Ct));
        Assert.Equal(pixels, CopyBytes(image));
        Assert.Equal(PixelStorageBytes(image), image.Owner.Scope.LiveBytes);
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

    private static byte[] CopyBytes(Image image)
    {
        var result = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(result);
        return result;
    }

    private static long PixelStorageBytes(Image image) => image.Owner.Scope.GetLiveBytes(AllocationKind.ImagePixels);
}
