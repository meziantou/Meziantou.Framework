using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Exclusive scoped pixel leases, reentrancy and conflict detection, stable multi-lease order.</summary>
public sealed class PixelLeaseTests
{
    private delegate void LeaseCallback(PixelLease lease);

    private static StorageOwner CreateOwner(SlabPool? pool = null)
        => new(new AllocationScope(ImageResourceLimits.Default, pool ?? new SlabPool()));

    /// <summary>The pattern used by the public callbacks: the lease is released in a finally block.</summary>
    private static void Process(PixelStorage storage, LeaseCallback callback)
    {
        using var lease = storage.AcquireLease();
        callback(lease);
    }

    [Fact]
    public void LeasesAreExclusive()
    {
        var storage = CreateOwner().Allocate(3, 3, 1);
        var lease = storage.AcquireLease();
        try
        {
            Assert.True(lease.IsActive);
            Assert.True(storage.IsLeased);
            Assert.Equal(1, storage.Owner.ActiveLeaseCount);
            Assert.Throws<InvalidOperationException>(() => Process(storage, static _ => { }));
        }
        finally
        {
            lease.Dispose();
        }

        Assert.False(storage.IsLeased);
        Assert.Equal(0, storage.Owner.ActiveLeaseCount);
        Process(storage, static _ => { });
    }

    [Fact]
    public void ReentrantAccessFromACallbackIsRejected()
    {
        var storage = CreateOwner().Allocate(3, 3, 1);
        var nested = false;
        Process(storage, lease =>
        {
            Assert.Throws<InvalidOperationException>(() => Process(storage, static _ => { }));
            nested = true;
            Assert.True(lease.IsActive);
            lease.GetRowBytes(0)[0] = 42;
        });

        Assert.True(nested);
        Process(storage, static lease => Assert.Equal(42, lease.GetRowBytes(0)[0]));
    }

    [Fact]
    public void LeasesAreReleasedWhenCallbacksThrow()
    {
        var storage = CreateOwner().Allocate(3, 3, 1);
        Assert.Throws<FormatException>(() => Process(storage, static _ => throw new FormatException()));
        Assert.False(storage.IsLeased);
        Assert.Equal(0, storage.Owner.ActiveLeaseCount);

        storage.Owner.EnsureCanModify("remove a frame");
        Assert.True(storage.Owner.Dispose());
        Assert.Equal(0, storage.Owner.Scope.LiveBytes);
    }

    [Fact]
    public void ReentrantDisposalCannotReturnVisibleMemoryToThePool()
    {
        var pool = new SlabPool();
        var owner = CreateOwner(pool);
        var storage = owner.Allocate(4, 4, 4, new PixelStorageLayoutOptions { TargetSlabBytes = 16 });
        var liveBytes = owner.Scope.LiveBytes;

        Process(storage, lease =>
        {
            lease.GetRow<Rgba32>(2)[1] = new Rgba32(1, 2, 3, 4);

            Assert.Throws<InvalidOperationException>(() => owner.Dispose());
            Assert.Throws<InvalidOperationException>(storage.Dispose);
            Assert.Throws<InvalidOperationException>(() => owner.EnsureCanModify("resize the image"));
            Assert.Throws<InvalidOperationException>(() => new PixelStorageTransaction(owner, "resize the image"));

            // Nothing went back to the pool and the rows are still the caller's
            Assert.Equal(0, pool.GetDiagnostics().RetainedBuffers);
            Assert.Equal(liveBytes, owner.Scope.LiveBytes);
            Assert.False(owner.IsDisposed);
            Assert.False(storage.IsDisposed);
            Assert.Equal(new Rgba32(1, 2, 3, 4), lease.GetRow<Rgba32>(2)[1]);
        });

        Assert.True(owner.Dispose());
        Assert.Equal(4, pool.GetDiagnostics().RetainedBuffers);
        Assert.Equal(0, owner.Scope.LiveBytes);
    }

    [Fact]
    public void ConcurrentDisposalDuringALeaseIsRejected()
    {
        var owner = CreateOwner();
        var storage = owner.Allocate(2, 2, 1);
        var lease = storage.AcquireLease();
        try
        {
            var exception = RunOnOtherThread(() => Record.Exception(() => owner.Dispose()));
            Assert.IsType<InvalidOperationException>(exception);
            Assert.False(owner.IsDisposed);
        }
        finally
        {
            lease.Dispose();
        }

        Assert.True(RunOnOtherThread(owner.Dispose));
    }

    [Fact]
    public void ReleasedLeasesAreInertAndIdempotent()
    {
        var storage = CreateOwner().Allocate(2, 2, 1);
        var first = storage.AcquireLease();
        var copy = first;
        first.Dispose();
        first.Dispose();
        Assert.False(first.IsActive);
        Assert.False(copy.IsActive);
        AssertInactive(copy);

        var second = storage.AcquireLease();
        try
        {
            copy.Dispose(); // a stale copy must not release the new lease
            Assert.True(second.IsActive);
            Assert.True(storage.IsLeased);
            AssertInactive(copy);
        }
        finally
        {
            second.Dispose();
        }

        PixelLease none = default;
        Assert.False(none.IsActive);
        none.Dispose();
        AssertInactive(none);
    }

    [Fact]
    public void LeasesOfDifferentFramesDoNotConflictButBlockStructuralEdits()
    {
        var owner = CreateOwner();
        var frame0 = owner.Allocate(2, 2, 1);
        var frame1 = owner.Allocate(2, 2, 1);
        var scratch = owner.Allocate(2, 2, 1);
        Process(frame0, lease =>
        {
            Process(frame1, static _ => { });

            // Structural edits are gated owner-wide; storage-level release only refuses a leased storage (it is also
            // used to roll back replacements that are not visible to anyone)
            Assert.Throws<InvalidOperationException>(() => owner.EnsureCanModify("remove a frame"));
            Assert.Throws<InvalidOperationException>(frame0.Dispose);
            scratch.Dispose();
        });

        frame1.Dispose();
        Assert.Equal(1, owner.LiveStorageCount);
    }

    [Fact]
    public void PairsAreAcquiredInStableOrder()
    {
        var owner = CreateOwner();
        var low = owner.Allocate(2, 2, 1);
        var high = owner.Allocate(2, 2, 1);
        Assert.True(low.Id < high.Id);

        foreach (var (first, second) in new[] { (low, high), (high, low) })
        {
            var pair = PixelLeasePair.Acquire(first, second);
            try
            {
                Assert.False(pair.IsAliased);
                Assert.Same(first, pair.First.Storage);
                Assert.Same(second, pair.Second.Storage);

                // Lease tokens increase: the lower identifier was always leased first
                var lowToken = ReferenceEquals(first, low) ? pair.First.Token : pair.Second.Token;
                var highToken = ReferenceEquals(first, low) ? pair.Second.Token : pair.First.Token;
                Assert.True(lowToken < highToken);
            }
            finally
            {
                pair.Dispose();
            }

            Assert.False(low.IsLeased);
            Assert.False(high.IsLeased);
        }
    }

    [Fact]
    public void FailedPairAcquisitionReleasesTheFirstLease()
    {
        var owner = CreateOwner();
        var low = owner.Allocate(2, 2, 1);
        var high = owner.Allocate(2, 2, 1);
        Process(high, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => AcquirePair(low, high));
            Assert.Throws<InvalidOperationException>(() => AcquirePair(high, low));
            Assert.False(low.IsLeased);
        });

        Process(low, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => AcquirePair(high, low));
            Assert.False(high.IsLeased);
        });

        Assert.Equal(0, owner.ActiveLeaseCount);
    }

    [Fact]
    public void AliasedPairsShareOneLease()
    {
        var storage = CreateOwner().Allocate(3, 2, 1);
        var pair = PixelLeasePair.Acquire(storage, storage);
        try
        {
            Assert.True(pair.IsAliased);
            Assert.Equal(1, storage.Owner.ActiveLeaseCount);
            pair.First.GetRowBytes(1)[2] = 7;
            Assert.Equal(7, pair.Second.GetRowBytes(1)[2]);
            pair.First.CopyTo(pair.Second); // in-place copy is a no-op
            Assert.Equal(7, pair.Second.GetRowBytes(1)[2]);
        }
        finally
        {
            pair.Dispose();
        }

        Assert.False(storage.IsLeased);
    }

    [Fact]
    public void CrossImageCopiesRequireMatchingGeometry()
    {
        var source = CreateOwner().Allocate(3, 2, 2, new PixelStorageLayoutOptions { RowAlignment = 16 });
        var destination = CreateOwner().Allocate(3, 2, 2, new PixelStorageLayoutOptions { TargetSlabBytes = 1 });
        var other = CreateOwner().Allocate(2, 3, 2);
        var pair = PixelLeasePair.Acquire(source, destination);
        try
        {
            pair.First.GetRow<Gray16>(1)[2] = new Gray16(0xBEEF);
            pair.First.CopyTo(pair.Second);
            Assert.Equal(new Gray16(0xBEEF), pair.Second.GetRow<Gray16>(1)[2]);
            pair.Second.Clear();
            Assert.Equal(new byte[6], pair.Second.GetRowBytes(1).ToArray());
        }
        finally
        {
            pair.Dispose();
        }

        Assert.Throws<ArgumentException>(() => CopyBetween(source, other));
        Assert.False(source.IsLeased);
        Assert.False(other.IsLeased);
    }

    [Fact]
    public void LeaseSetsDeduplicateSortAndReleaseOnFailure()
    {
        var owner = CreateOwner();
        var a = owner.Allocate(1, 1, 1);
        var b = owner.Allocate(1, 1, 1);
        var c = new StorageOwner(owner.Scope).Allocate(1, 1, 1); // another image sharing nothing but the scope

        var set = PixelLeaseSet.Acquire([c, a, b, a]);
        try
        {
            Assert.Equal(4, set.Count);
            Assert.Equal(3, set.DistinctCount);
            Assert.True(set.AreAliased(1, 3));
            Assert.False(set.AreAliased(0, 1));
            Assert.Same(c, set[0].Storage);
            Assert.Same(a, set[3].Storage);
            Assert.True(set[1].Token < set[2].Token);
            Assert.True(set[2].Token < set[0].Token);
            set[0].GetRowBytes(0)[0] = 5;
        }
        finally
        {
            set.Dispose();
        }

        Assert.False(a.IsLeased || b.IsLeased || c.IsLeased);

        // b is busy: a (acquired first) must be released, c never acquired
        Process(b, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => AcquireSet([c, b, a]));
            Assert.False(a.IsLeased);
            Assert.False(c.IsLeased);
        });

        c.Owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => AcquireSet([a, c]));
        Assert.False(a.IsLeased);
        AcquireSet([]);
    }

    private static T RunOnOtherThread<T>(Func<T> func)
    {
        T result = default!;
        var thread = new Thread(() => result = func());
        thread.Start();
        thread.Join();
        return result;
    }

    private static void AssertInactive(PixelLease lease)
    {
        try
        {
            _ = lease.GetRowBytes(0);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        Assert.Fail("The lease should not give access to the rows.");
    }

    private static void AcquirePair(PixelStorage first, PixelStorage second)
    {
        using var pair = PixelLeasePair.Acquire(first, second);
    }

    private static void AcquireSet(PixelStorage[] storages)
    {
        using var set = PixelLeaseSet.Acquire(storages);
    }

    private static void CopyBetween(PixelStorage source, PixelStorage destination)
    {
        using var pair = PixelLeasePair.Acquire(source, destination);
        pair.First.CopyTo(pair.Second);
    }
}
