using System.Collections.Concurrent;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Bounded parallel resizing: <see cref="ImageConfiguration.MaxDegreeOfParallelism"/>
/// workers resize row bands of large frames; the result never depends on the worker count, concurrency never exceeds the
/// configured bound, the per-worker scratch is budgeted and released, and failures surface as in the sequential path.
/// </summary>
public sealed class ParallelResizeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Cases: pixel format, filter and vertical strategy ("default", "gather" or "scatter").</summary>
    public static TheoryData<PixelFormat, ResamplingFilter, string> Cases()
    {
        var data = new TheoryData<PixelFormat, ResamplingFilter, string>();
        foreach (var format in new[] { PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Rgba64 })
        {
            data.Add(format, ResamplingFilter.Bilinear, "default");
            data.Add(format, ResamplingFilter.Bicubic, "gather");
            data.Add(format, ResamplingFilter.Lanczos3, "scatter");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TheResultDoesNotDependOnTheWorkerCount(PixelFormat format, ResamplingFilter filter, string verticalStrategy)
    {
        ResizeVerticalStrategy? strategy = verticalStrategy switch
        {
            "gather" => ResizeVerticalStrategy.Gather,
            "scatter" => ResizeVerticalStrategy.Scatter,
            _ => null,
        };

        var random = new Random(7);
        (int Width, int Height, int TargetWidth, int TargetHeight)[] sizes = [(300, 400, 280, 301), (500, 900, 333, 129), (257, 260, 520, 530)];
        foreach (var (width, height, targetWidth, targetHeight) in sizes)
        {
            var bytes = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
            random.NextBytes(bytes);
            var options = new ResizeOptions(targetWidth, targetHeight) { Mode = ResizeMode.Stretch, Filter = filter, AllowUpscaling = true };
            var expected = Resize(bytes, width, height, format, options, workers: 1, strategy);
            foreach (var workers in new[] { 2, 3, 4, 7 })
            {
                Assert.Equal(expected, Resize(bytes, width, height, format, options, workers, strategy));
            }
        }
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
            Resampler.TestBandObserver = (band, started) =>
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
                image.Resize(new ResizeOptions(500, 500) { Mode = ResizeMode.Stretch }, Ct);
            }
            finally
            {
                Resampler.TestBandObserver = null;
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
    [InlineData(8, 100, 100, 1)]       // fewer than 65,536 output pixels
    [InlineData(8, 4000, 40, 1)]       // fewer than 32 output rows per band
    [InlineData(8, 4000, 70, 2)]
    [InlineData(8, 1000, 1000, 8)]
    [InlineData(3, 1000, 1000, 3)]
    public void SmallOutputsUseFewerWorkers(int maxDegreeOfParallelism, int width, int height, int expected)
        => Assert.Equal(expected, RowBands.GetWorkerCount(new Size(width, height), maxDegreeOfParallelism));

    [Fact]
    public void NearestNeighborIsSequential()
    {
        var geometry = ResizeGeometry.Compute(new Size(2000, 2000), new ResizeOptions(1000, 1000) { Mode = ResizeMode.Stretch });
        using var nearest = new ResizePlan(geometry, ResamplingFilter.NearestNeighbor, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32, maxDegreeOfParallelism: 4);
        using var bicubic = new ResizePlan(geometry, ResamplingFilter.Bicubic, ResizeWorkingSpace.Encoded, PixelFormat.Rgba32, maxDegreeOfParallelism: 4);
        Assert.Equal(1, nearest.Workers);
        Assert.Equal(4, bicubic.Workers);
    }

    [Fact]
    public void EachWorkerHasItsOwnBudgetedScratchAndNothingAccumulates()
    {
        long sequentialPeak;
        using (var image = CreateImage(800, 600, workers: 1))
        {
            var baseline = image.Owner.Scope.LiveBytes;
            image.Resize(new ResizeOptions(640, 480) { Mode = ResizeMode.Stretch }, Ct);
            sequentialPeak = image.Owner.Scope.GetDiagnostics().PeakLiveBytes - baseline;
        }

        using var parallel = CreateImage(800, 600, workers: 4);
        var start = parallel.Owner.Scope.LiveBytes;
        var peaks = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            using var copy = parallel.Clone();
            var before = copy.Owner.Scope.LiveBytes;
            copy.Resize(new ResizeOptions(640, 480) { Mode = ResizeMode.Stretch }, Ct);
            var diagnostics = copy.Owner.Scope.GetDiagnostics();
            peaks.Add(diagnostics.PeakLiveBytes - before);

            // After the operation only the replacement storage is live: the scratch of every worker was released
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
            probe.Resize(new ResizeOptions(640, 480) { Mode = ResizeMode.Stretch }, Ct);
            needed = probe.Owner.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.True(needed > before);
        }

        using var image = CreateImage(800, 600, workers: 4, limit: needed);
        var pixels = CopyBytes(image);
        Assert.Throws<ImageResourceLimitException>(() => image.Resize(new ResizeOptions(640, 480) { Mode = ResizeMode.Stretch }, Ct));
        Assert.Equal(new Size(800, 600), image.Size);
        Assert.Equal(pixels, CopyBytes(image));
    }

    [Fact]
    public void CancellationInAWorkerIsAnOperationCanceledExceptionAndTheImageIsUnchanged()
    {
        using var image = CreateImage(800, 600, workers: 4);
        var pixels = CopyBytes(image);
        using var source = new CancellationTokenSource();
        Resampler.TestBandObserver = (band, started) =>
        {
            if (started && band == 2)
            {
                source.Cancel();
            }
        };

        try
        {
            var exception = Assert.ThrowsAny<OperationCanceledException>(() => image.Resize(new ResizeOptions(640, 480) { Mode = ResizeMode.Stretch }, source.Token));
            Assert.Equal(source.Token, exception.CancellationToken);
        }
        finally
        {
            Resampler.TestBandObserver = null;
        }

        Assert.Equal(new Size(800, 600), image.Size);
        Assert.Equal(pixels, CopyBytes(image));
        Assert.Equal(PixelStorageBytes(image), image.Owner.Scope.LiveBytes);
    }

    [Fact]
    public void AFailureInAWorkerIsRethrownUnwrapped()
    {
        using var image = CreateImage(800, 600, workers: 3);
        Resampler.TestBandObserver = (band, started) =>
        {
            if (started && band == 1)
                throw new InjectedAllocationFailureException();
        };

        try
        {
            Assert.Throws<InjectedAllocationFailureException>(() => image.Resize(new ResizeOptions(640, 480) { Mode = ResizeMode.Stretch }, Ct));
        }
        finally
        {
            Resampler.TestBandObserver = null;
        }

        Assert.Equal(new Size(800, 600), image.Size);
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

    private static byte[] Resize(byte[] bytes, int width, int height, PixelFormat format, ResizeOptions options, int workers, ResizeVerticalStrategy? strategy)
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

        ResizePlan.TestStrategyOverride = strategy;
        try
        {
            image.Resize(options, Ct);
        }
        finally
        {
            ResizePlan.TestStrategyOverride = null;
        }

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
