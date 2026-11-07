using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Transactional storage replacement budgets old and new buffers simultaneously.</summary>
public sealed class PixelStorageTransactionTests
{
    // 10 x 10 Rgba32 = 400 bytes (448 charged); 5 x 20 Rgba32 = 400 bytes (448 charged); 20 x 20 Rgba32 = 1600 bytes (1792 charged)
    private const long OriginalCapacity = 448;
    private const long ReplacementCapacity = 1792;

    private static (StorageOwner Owner, PixelStorage[] Frames) CreateImage(long limit, int frameCount = 2)
    {
        var owner = new StorageOwner(new AllocationScope(new ImageResourceLimits { MaxLiveAllocationBytes = limit }, new SlabPool()));
        var frames = new PixelStorage[frameCount];
        for (var i = 0; i < frameCount; i++)
        {
            frames[i] = owner.Allocate(10, 10, 4);
            using var lease = frames[i].AcquireLease();
            lease.GetRow<Rgba32>(i)[i] = new Rgba32((byte)(i + 1), 2, 3, 4);
        }

        return (owner, frames);
    }

    /// <summary>A minimal "resize" (nearest neighbor, 2x upscale) of every frame, structured as the geometry operations will be.</summary>
    private static void Upscale(StorageOwner owner, PixelStorage[] frames, bool reserveUpFront, Action? beforeCommit = null)
    {
        using var transaction = new PixelStorageTransaction(owner, "resize the image");
        if (reserveUpFront)
        {
            transaction.Reserve(frames.Length, 20, 20, 4);
        }

        var replacements = new PixelStorage[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            replacements[i] = transaction.Allocate(20, 20, 4);
            using var pair = PixelLeasePair.Acquire(frames[i], replacements[i]);
            for (var y = 0; y < 20; y++)
            {
                var source = pair.First.GetRow<Rgba32>(y / 2);
                var destination = pair.Second.GetRow<Rgba32>(y);
                for (var x = 0; x < 20; x++)
                {
                    destination[x] = source[x / 2];
                }
            }
        }

        beforeCommit?.Invoke();
        transaction.Commit();

        // Publication: no failure point from here on
        for (var i = 0; i < frames.Length; i++)
        {
            var original = frames[i];
            frames[i] = replacements[i];
            original.Dispose();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplacementSucceedsWhenOldAndNewFitTogether(bool reserveUpFront)
    {
        var (owner, frames) = CreateImage((2 * OriginalCapacity) + (2 * ReplacementCapacity));
        Upscale(owner, frames, reserveUpFront);

        Assert.Equal(2 * ReplacementCapacity, owner.Scope.LiveBytes);
        Assert.Equal((2 * OriginalCapacity) + (2 * ReplacementCapacity), owner.Scope.GetDiagnostics().PeakLiveBytes);
        Assert.Equal(0, owner.Scope.GetDiagnostics().ReservedBytes);
        Assert.Equal(2, owner.LiveStorageCount);
        using var lease = frames[1].AcquireLease();
        Assert.Equal(new Rgba32(2, 2, 3, 4), lease.GetRow<Rgba32>(3)[3]);
        Assert.Equal(20, lease.Width);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InsufficientBudgetLeavesTheOriginalIntact(bool reserveUpFront)
    {
        // The replacements alone fit in the limit, but not together with the originals
        var limit = (2 * OriginalCapacity) + (2 * ReplacementCapacity) - 1;
        var (owner, frames) = CreateImage(limit);
        var originals = frames.ToArray();

        var exception = Assert.Throws<ImageResourceLimitException>(() => Upscale(owner, frames, reserveUpFront));
        Assert.Equal(limit, exception.Limit);
        Assert.Equal(limit + 1, exception.Requested);

        Assert.Equal(originals, frames);
        Assert.Equal(2 * OriginalCapacity, owner.Scope.LiveBytes);
        Assert.Equal(0, owner.Scope.GetDiagnostics().ReservedBytes);
        Assert.Equal(2, owner.LiveStorageCount);
        for (var i = 0; i < frames.Length; i++)
        {
            using var lease = frames[i].AcquireLease();
            Assert.Equal(10, lease.Width);
            Assert.Equal(new Rgba32((byte)(i + 1), 2, 3, 4), lease.GetRow<Rgba32>(i)[i]);
        }
    }

    [Fact]
    public void FailureBeforeCommitReleasesTheReplacements()
    {
        var (owner, frames) = CreateImage(long.MaxValue);
        var originals = frames.ToArray();
        Assert.Throws<OperationCanceledException>(() => Upscale(owner, frames, reserveUpFront: true, () => throw new OperationCanceledException()));
        Assert.Equal(originals, frames);
        Assert.Equal(2 * OriginalCapacity, owner.Scope.LiveBytes);
        Assert.Equal(2, owner.LiveStorageCount);
    }

    [Fact]
    public void CommitIsRejectedWhileALeaseIsActive()
    {
        var (owner, frames) = CreateImage(long.MaxValue);
        var originals = frames.ToArray();
        var lease = frames[0].AcquireLease();
        try
        {
            Assert.Throws<InvalidOperationException>(() => Upscale(owner, frames, reserveUpFront: false));
        }
        finally
        {
            lease.Dispose();
        }

        // The transaction could not even start; now a lease is taken after it started and is still active at commit
        var token = 0L;
        Assert.Throws<InvalidOperationException>(() => Upscale(owner, frames, reserveUpFront: false, () => token = frames[1].AcquireLease().Token));
        Assert.Equal(originals, frames);
        Assert.Equal(2 * OriginalCapacity, owner.Scope.LiveBytes);
        Assert.True(frames[1].IsLeased);
        owner.ReleaseLease(frames[1], token);
        Assert.True(owner.Dispose());
        Assert.Equal(0, owner.Scope.LiveBytes);
    }

    [Fact]
    public void CompletedTransactionsCannotBeReused()
    {
        var (owner, _) = CreateImage(long.MaxValue, frameCount: 1);
        var transaction = new PixelStorageTransaction(owner, "crop the image");
        var replacement = transaction.Allocate(2, 2, 4);
        transaction.Commit();
        Assert.Throws<InvalidOperationException>(transaction.Commit);
        Assert.Throws<InvalidOperationException>(() => transaction.Allocate(1, 1, 1));
        Assert.Throws<InvalidOperationException>(() => transaction.Reserve(1, 1, 1, 1));
        transaction.Dispose();
        Assert.False(replacement.IsDisposed); // ownership was transferred by the commit
        Assert.Single(transaction.Allocated);
    }

    [Fact]
    public void ReservationOverflowIsReported()
    {
        var (owner, _) = CreateImage(long.MaxValue, frameCount: 1);
        using var transaction = new PixelStorageTransaction(owner, "resize the image");
        var exception = Assert.Throws<ImageResourceLimitException>(() => transaction.Reserve(int.MaxValue, int.MaxValue, 1 << 20, 8));
        Assert.Null(exception.Requested);
        Assert.Equal(OriginalCapacity, owner.Scope.LiveBytes);
    }
}
