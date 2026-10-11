using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Size classes, clearing and retention bounds of the internal slab pool.</summary>
public sealed class SlabPoolTests
{
    [Theory]
    [InlineData(1, 64)]
    [InlineData(64, 64)]
    [InlineData(65, 80)]
    [InlineData(80, 80)]
    [InlineData(81, 96)]
    [InlineData(112, 112)]
    [InlineData(113, 128)]
    [InlineData(129, 160)]
    [InlineData(400, 448)]
    [InlineData(1000, 1024)]
    [InlineData(1025, 1280)]
    [InlineData(3_000_000, 3_145_728)]
    [InlineData(8 * 1024 * 1024, 8 * 1024 * 1024)]
    public void SizeClassesHaveFourStepsPerOctave(int length, int expectedCapacity)
    {
        var pool = new SlabPool();
        Assert.Equal(expectedCapacity, pool.GetRentCapacity(length));
        Assert.HasCount(expectedCapacity, pool.Rent(length, clear: false));
    }

    [Fact]
    public void SizeClassesAreConsistentAndWasteAtMostOneQuarter()
    {
        var pool = new SlabPool(new SlabPoolOptions { MaxPooledLength = 1 << 16 });
        var previous = 0L;
        for (var length = 1; length <= 1 << 16; length++)
        {
            var capacity = pool.GetRentCapacity(length);
            Assert.True(capacity >= length);
            Assert.True(capacity >= previous);
            Assert.True(length <= SlabPool.MinimumLength || (capacity - length) * 4 < capacity, $"waste too large for {length}: {capacity}");
            var index = SlabPool.GetBucketIndex(length);
            Assert.Equal(capacity, SlabPool.GetBucketLength(index));
            previous = capacity;
        }
    }

    [Fact]
    public void OversizedRequestsAreExactAndNeverRetained()
    {
        var pool = new SlabPool(new SlabPoolOptions { MaxPooledLength = 1024 });
        Assert.Equal(1025, pool.GetRentCapacity(1025));
        var buffer = pool.Rent(1025, clear: true);
        Assert.HasCount(1025, buffer);
        Assert.False(pool.Return(buffer));

        var diagnostics = pool.GetDiagnostics();
        Assert.Equal(1, diagnostics.UnpooledAllocations);
        Assert.Equal(1, diagnostics.DroppedReturns);
        Assert.Equal(0, diagnostics.RetainedBytes);
    }

    [Fact]
    public void ZeroLengthRentalsUseNoCapacity()
    {
        var pool = new SlabPool();
        Assert.Equal(0, pool.GetRentCapacity(0));
        Assert.Empty(pool.Rent(0, clear: true));
    }

    [Fact]
    public void ReusedBuffersAreClearedOnRequest()
    {
        var pool = new SlabPool();
        var dirty = pool.Rent(100, clear: false);
        dirty.AsSpan().Fill(0xAB);
        Assert.True(pool.Return(dirty));

        var reused = pool.Rent(100, clear: true);
        Assert.Same(dirty, reused);
        Assert.All(reused.AsSpan(0, 100).ToArray(), value => Assert.Equal(0, value));
        Assert.Equal(1, pool.GetDiagnostics().ReusedRentals);
    }

    [Fact]
    public void RetentionIsBoundedPerSizeClass()
    {
        var pool = new SlabPool(new SlabPoolOptions { MaxRetainedBuffersPerBucket = 2, MaxRetainedBytes = long.MaxValue });
        var buffers = Enumerable.Range(0, 4).Select(_ => pool.Rent(64, clear: false)).ToArray();
        Assert.Equal([true, true, false, false], buffers.Select(pool.Return).ToArray());

        var diagnostics = pool.GetDiagnostics();
        Assert.Equal(2, diagnostics.RetainedBuffers);
        Assert.Equal(128, diagnostics.RetainedBytes);
        Assert.Equal(2, diagnostics.RetainedReturns);
        Assert.Equal(2, diagnostics.DroppedReturns);
    }

    [Fact]
    public void RetentionIsBoundedInTotalBytes()
    {
        var pool = new SlabPool(new SlabPoolOptions { MaxRetainedBuffersPerBucket = 100, MaxRetainedBytes = 1000 });
        var buffers = Enumerable.Range(0, 10).Select(_ => pool.Rent(256, clear: false)).ToArray();
        var results = buffers.Select(pool.Return).ToArray();
        Assert.Equal(3, results.Count(retained => retained)); // 3 * 256 <= 1000 < 4 * 256
        Assert.Equal(768, pool.GetDiagnostics().RetainedBytes);

        pool.Trim();
        Assert.Equal(0, pool.GetDiagnostics().RetainedBytes);
        Assert.Equal(0, pool.GetDiagnostics().RetainedBuffers);
    }

    [Fact]
    public void RetentionCanBeDisabled()
    {
        var pool = new SlabPool(new SlabPoolOptions { MaxRetainedBuffersPerBucket = 0 });
        Assert.False(pool.Return(pool.Rent(64, clear: false)));
        pool = new SlabPool(new SlabPoolOptions { MaxRetainedBytes = 0 });
        Assert.False(pool.Return(pool.Rent(64, clear: false)));
    }

    [Fact]
    public void ForeignBuffersAreNotRetained()
    {
        var pool = new SlabPool();
        Assert.False(pool.Return(new byte[100])); // not a size class
        Assert.True(pool.Return(new byte[96]));
    }

    [Fact]
    public void OptionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlabPoolOptions { MaxPooledLength = 1000 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlabPoolOptions { MaxPooledLength = 64 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlabPoolOptions { MaxRetainedBytes = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlabPoolOptions { MaxRetainedBuffersPerBucket = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlabPool().Rent(-1, clear: false));
    }

    [Fact]
    public void ConcurrentRentAndReturnKeepTheBounds()
    {
        var pool = new SlabPool(new SlabPoolOptions { MaxRetainedBuffersPerBucket = 4, MaxRetainedBytes = 4096 });
        Parallel.For(0, 64, i =>
        {
            for (var j = 0; j < 200; j++)
            {
                var buffer = pool.Rent(64 + ((i + j) % 512), clear: true);
                buffer[0] = 1;
                pool.Return(buffer);
            }
        });

        var diagnostics = pool.GetDiagnostics();
        Assert.InRange(diagnostics.RetainedBytes, 0, 4096);
    }
}
