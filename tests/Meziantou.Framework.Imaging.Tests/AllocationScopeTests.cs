using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Live-byte accounting at actual rented capacity.</summary>
public sealed class AllocationScopeTests
{
    private static AllocationScope CreateScope(long limit, SlabPool? pool = null)
        => new(new ImageResourceLimits { MaxLiveAllocationBytes = limit }, pool ?? new SlabPool());

    [Fact]
    public void AConfigurationAllocatorProvidesEveryBufferOfItsImages()
    {
        #region ImageBufferAllocator_Usage
        var allocator = new PinnedAllocator();
        var configuration = new ImageConfiguration { BufferAllocator = allocator };
        using (var image = new Image<Rgba32>(64, 64, configuration))
        {
            image.GaussianBlur(2);
            using var clone = image.CloneAs<Rgba64>();
            Assert.True(allocator.LiveBytes > 0);
        }

        // Every array is returned once the images are disposed
        Assert.Equal(0, allocator.LiveBytes);
        #endregion

        Assert.True(allocator.Rentals > 2);
    }

    [Fact]
    public void TheLimitsAreChargedBeforeTheAllocatorIsAsked()
    {
        var allocator = new PinnedAllocator();
        var configuration = new ImageConfiguration { BufferAllocator = allocator, Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 1000 } };
        Assert.Throws<ImageResourceLimitException>(() => new Image<Rgba32>(20, 20, configuration));
        Assert.Equal(0, allocator.Rentals);

        // The charge is the length the allocator declares
        var rounding = new PinnedAllocator { Granularity = 512 };
        using var image = new Image<Gray8>(10, 10, new ImageConfiguration { BufferAllocator = rounding });
        Assert.Equal(512, rounding.LiveBytes);
        Assert.Equal(512, image.Owner.Scope.LiveBytes);
    }

    [Fact]
    public void AnArrayOfAnotherLengthIsReturnedAndRejected()
    {
        var allocator = new PinnedAllocator { Shortfall = 1 };
        var exception = Assert.Throws<InvalidOperationException>(() => new Image<Rgba32>(4, 4, new ImageConfiguration { BufferAllocator = allocator }));
        Assert.Contains("GetBufferLength", exception.Message);
        Assert.Equal(0, allocator.LiveBytes);

        var declaresLess = new PinnedAllocator { Granularity = -1 };
        Assert.Throws<InvalidOperationException>(() => new Image<Rgba32>(4, 4, new ImageConfiguration { BufferAllocator = declaresLess }));
        Assert.Equal(0, declaresLess.Rentals);
    }

    [Fact]
    public void LoadsAndConversionsUseTheAllocatorOfTheirConfiguration()
    {
        using var source = new Image<Rgb24>(8, 8);
        using var stream = new MemoryStream();
        source.Save(stream, new Formats.PngEncoder());
        var allocator = new PinnedAllocator();
        var configuration = new ImageConfiguration { BufferAllocator = allocator };
        using (var image = Image.Load(stream.ToArray(), new ImageDecodeOptions { Configuration = configuration }))
        {
            using var converted = image.CloneAs<Gray8>();
            Assert.True(allocator.Rentals > 0);
        }

        Assert.Equal(0, allocator.LiveBytes);
    }

    #region ImageBufferAllocator_Pinned
    /// <summary>Gives pinned arrays, which native code can read without pinning them, and counts the bytes in use.</summary>
    private sealed class PinnedAllocator : ImageBufferAllocator
    {
        private long _liveBytes;
        private int _rentals;

        public long LiveBytes => Interlocked.Read(ref _liveBytes);

        public int Rentals => Volatile.Read(ref _rentals);

        /// <summary>Gets the multiple the lengths are rounded up to (a negative value declares fewer bytes than requested, for the tests).</summary>
        public int Granularity { get; init; } = 1;

        /// <summary>Gets the number of bytes the arrays lack, for the tests.</summary>
        public int Shortfall { get; init; }

        public override int GetBufferLength(int length) => Granularity < 0 ? length - 1 : (length + Granularity - 1) / Granularity * Granularity;

        public override byte[] Rent(int length)
        {
            var buffer = GC.AllocateArray<byte>(GetBufferLength(length) - Shortfall, pinned: true);
            Interlocked.Add(ref _liveBytes, buffer.Length);
            Interlocked.Increment(ref _rentals);
            return buffer;
        }

        public override void Return(byte[] buffer) => Interlocked.Add(ref _liveBytes, -buffer.Length);
    }
    #endregion

    [Fact]
    public void BuffersAreChargedAtActualCapacity()
    {
        var scope = CreateScope(1000);
        using var buffer = scope.Rent(65, AllocationKind.Temporary);
        Assert.Equal(65, buffer.Length);
        Assert.Equal(80, buffer.Capacity);
        Assert.Equal(80, scope.LiveBytes);
        Assert.Equal(80, scope.GetLiveBytes(AllocationKind.Temporary));
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.ImagePixels));
        Assert.HasCount(65, buffer.Span.ToArray());
        Assert.Equal(65, buffer.Memory.Length);
    }

    [Fact]
    public void LimitIsInclusiveAtActualCapacity()
    {
        // 65 bytes rent an 80-byte buffer: a limit of 80 accepts it, 79 rejects it although 65 < 79
        var pool = new SlabPool();
        var atLimit = CreateScope(80, pool);
        using (atLimit.Rent(65, AllocationKind.DecoderState))
        {
            Assert.Equal(80, atLimit.LiveBytes);
        }

        var overLimit = CreateScope(79, pool);
        var before = pool.GetDiagnostics();
        var exception = Assert.Throws<ImageResourceLimitException>(() => overLimit.Rent(65, AllocationKind.DecoderState));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        Assert.Equal(79, exception.Limit);
        Assert.Equal(80, exception.Requested);

        // Nothing was rented or charged
        Assert.Equal(before, pool.GetDiagnostics());
        var diagnostics = overLimit.GetDiagnostics();
        Assert.Equal(0, diagnostics.LiveBytes);
        Assert.Equal(0, diagnostics.LiveAllocations);
        Assert.Equal(1, diagnostics.RejectedRequests);
    }

    [Fact]
    public void LimitAppliesToTheSumOfLiveBuffers()
    {
        var scope = CreateScope(160);
        using var first = scope.Rent(80, AllocationKind.ImagePixels);
        var second = scope.Rent(70, AllocationKind.CompositorState);
        var exception = Assert.Throws<ImageResourceLimitException>(() => scope.Rent(1, AllocationKind.RestorePreviousState));
        Assert.Equal(160 + 64, exception.Requested);

        second.Dispose();
        using var third = scope.Rent(1, AllocationKind.RestorePreviousState);
        Assert.Equal(80 + 64, scope.LiveBytes);
        Assert.Equal(160, scope.GetDiagnostics().PeakLiveBytes);
    }

    [Fact]
    public void OverflowingRequestsAreRejected()
    {
        var scope = CreateScope(long.MaxValue);
        using var charge = scope.Charge(long.MaxValue - 10, AllocationKind.Metadata);
        var exception = Assert.Throws<ImageResourceLimitException>(() => scope.Charge(11, AllocationKind.Metadata));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        Assert.Null(exception.Requested);
        Assert.Equal(long.MaxValue - 10, scope.LiveBytes);

        using var exact = scope.Charge(10, AllocationKind.Metadata);
        Assert.Equal(long.MaxValue, scope.LiveBytes);
    }

    [Fact]
    public void ChargesTrackNonPooledLibraryBytes()
    {
        var scope = CreateScope(100);
        var charge = scope.Charge(100, AllocationKind.Metadata);
        Assert.Throws<ImageResourceLimitException>(() => scope.Charge(1, AllocationKind.Metadata));
        Assert.Equal(1, scope.GetDiagnostics().LiveAllocations);

        charge.Dispose();
        charge.Dispose();
        Assert.True(charge.IsReleased);
        Assert.Equal(0, scope.LiveBytes);
        Assert.Equal(0, scope.GetDiagnostics().LiveAllocations);
        Assert.Throws<ArgumentOutOfRangeException>(() => scope.Charge(-1, AllocationKind.Metadata));
    }

    [Fact]
    public void BufferDisposalIsIdempotentAndReturnsToThePool()
    {
        var pool = new SlabPool();
        var scope = CreateScope(1000, pool);
        var buffer = scope.Rent(100, AllocationKind.Temporary);
        var array = buffer.RawBuffer;
        buffer.Dispose();
        buffer.Dispose();

        Assert.True(buffer.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => buffer.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => buffer.Memory);
        Assert.Equal(0, scope.LiveBytes);
        Assert.Equal(1, pool.GetDiagnostics().RetainedBuffers);
        Assert.Same(array, pool.Rent(100, clear: true));
    }

    [Fact]
    public void BuffersAreClearedUnlessScratch()
    {
        var pool = new SlabPool();
        var scope = CreateScope(1000, pool);
        var dirty = scope.Rent(100, AllocationKind.Temporary, clear: false);
        dirty.Span.Fill(0xFF);
        dirty.Dispose();

        using var clean = scope.Rent(100, AllocationKind.ImagePixels);
        Assert.Equal(new byte[100], clean.Span.ToArray());
    }

    [Fact]
    public void ReservationsChargeUpFrontAndReleaseTheRemainder()
    {
        var pool = new SlabPool();
        var scope = CreateScope(1000, pool);
        PooledBuffer first;
        using (var reservation = scope.Reserve(300, AllocationKind.DecoderState))
        {
            Assert.Equal(300, scope.LiveBytes);
            Assert.Equal(300, scope.GetDiagnostics().ReservedBytes);
            Assert.True(reservation.Covers(256));
            first = reservation.Rent(200, clear: true); // 224
            Assert.Equal(76, reservation.RemainingBytes);
            Assert.False(reservation.Covers(100));
            Assert.Throws<InvalidOperationException>(() => reservation.Rent(100, clear: true));
            Assert.Equal(300, scope.LiveBytes);
            Assert.Equal(76, scope.GetDiagnostics().ReservedBytes);
        }

        Assert.Equal(224, scope.LiveBytes);
        Assert.Equal(0, scope.GetDiagnostics().ReservedBytes);
        first.Dispose();
        Assert.Equal(0, scope.LiveBytes);
        Assert.Throws<ImageResourceLimitException>(() => scope.Reserve(1001, AllocationKind.Temporary));
        Assert.Equal(0, scope.LiveBytes);
    }

    [Fact]
    public void FailedRentalReleasesTheCharge()
    {
        var pool = new SlabPool { RentFailureInjector = _ => new InjectedAllocationFailureException() };
        var scope = CreateScope(1000, pool);
        Assert.Throws<InjectedAllocationFailureException>(() => scope.Rent(10, AllocationKind.Temporary));
        var diagnostics = scope.GetDiagnostics();
        Assert.Equal(0, diagnostics.LiveBytes);
        Assert.Equal(0, diagnostics.ReservedBytes);
        Assert.Equal(0, diagnostics.LiveAllocations);
    }

    [Fact]
    public void ScopeIsCreatedFromConfiguration()
    {
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 1234 } };
        var scope = AllocationScope.Create(configuration, "image");
        Assert.Equal(1234, scope.Limit);
        Assert.Same(SlabPool.Shared, scope.Pool);
        Assert.Equal("image", scope.Name);
        Assert.Equal(ImageResourceLimits.DefaultMaxLiveAllocationBytes, AllocationScope.Create(configuration: null).Limit);
        Assert.NotEqual(scope.Id, AllocationScope.Create(configuration: null).Id);
        Assert.Contains("image", scope.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConcurrentRentAndDisposeKeepAccountingExact()
    {
        var scope = CreateScope(long.MaxValue, new SlabPool(new SlabPoolOptions { MaxRetainedBuffersPerBucket = 2 }));
        Parallel.For(0, 32, i =>
        {
            for (var j = 0; j < 100; j++)
            {
                using var buffer = scope.Rent(1 + ((i * 31 + j) % 5000), (AllocationKind)(j % 6));
                buffer.Span[0] = 1;
            }
        });

        var diagnostics = scope.GetDiagnostics();
        Assert.Equal(0, diagnostics.LiveBytes);
        Assert.Equal(0, diagnostics.LiveAllocations);
        Assert.Equal(3200, diagnostics.TotalAllocations);
        foreach (var kind in Enum.GetValues<AllocationKind>())
        {
            Assert.Equal(0, scope.GetLiveBytes(kind));
        }
    }
}
