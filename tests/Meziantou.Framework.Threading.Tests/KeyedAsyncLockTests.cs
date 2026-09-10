namespace Meziantou.Framework.Threading.Tests;

public sealed class KeyedAsyncLockTests
{
    [Fact]
    public async Task Test()
    {
        var locks = new KeyedAsyncLock<string>(StringComparer.Ordinal);
        using (await locks.LockAsync("a"))
        using (await locks.LockAsync("b"))
        {
            // If a and b are the same instance, this test should timeout
        }
    }

    [Fact]
    public async Task ReleasedKeysAreEvicted()
    {
        var locks = new KeyedAsyncLock<int>();
        for (var i = 0; i < 1000; i++)
        {
            using (await locks.LockAsync(i))
            {
            }
        }

        // Entries must be removed once released, otherwise the table grows without bound.
        Assert.Equal(0, locks.EntryCount);
    }

    [Fact]
    public async Task CanceledAcquisitionEvictsKey()
    {
        var locks = new KeyedAsyncLock<int>();

        // Hold the lock so the second acquisition has to wait, then cancel it.
        using (await locks.LockAsync(1))
        {
            using var cts = new CancellationTokenSource();
            var pending = locks.LockAsync(1, cts.Token);
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        }

        Assert.Equal(0, locks.EntryCount);
    }

    [Fact]
    public async Task SameKeySerializesConcurrentAccess()
    {
        var locks = new KeyedAsyncLock<string>(StringComparer.Ordinal);
        var counter = 0;

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            using (await locks.LockAsync("key"))
            {
                var value = counter;
                await Task.Yield();
                counter = value + 1;
            }
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(50, counter);
    }

    [Fact]
    public async Task DifferentKeysDoNotBlockEachOther()
    {
        var locks = new KeyedAsyncLock<int>();
        using (await locks.LockAsync(1))
        using (await locks.LockAsync(2).AsTask().WaitAsync(TimeSpan.FromSeconds(30)))
        {
        }
    }

    [Fact]
    public async Task NestedKeyIsKeptWhileHeld()
    {
        var locks = new KeyedAsyncLock<int>();
        using (await locks.LockAsync(1))
        {
            Assert.Equal(1, locks.EntryCount);
        }
    }

    [Fact]
    public async Task CustomComparer_SameKeyDifferentCase_AreTreatedAsSameLock()
    {
        // Keys are bucketed by hash code before the comparer gets to compare them, so the hash must come from the
        // comparer too. Otherwise equal keys could land in different buckets and get a lock each.
        var locks = new KeyedAsyncLock<string>(StringComparer.OrdinalIgnoreCase);

        var held = await locks.LockAsync("KEY");
        var blocked = locks.LockAsync("key").AsTask();

        Assert.False(blocked.IsCompleted); // blocked by the held "KEY" lock (same entry)
        Assert.Equal(1, locks.EntryCount);

        held.Dispose();
        using (await blocked.WaitAsync(TimeSpan.FromSeconds(30)))
        {
        }

        Assert.Equal(0, locks.EntryCount);
    }

    [Fact]
    public async Task AlreadyCanceledToken_DoesNotTrackTheKey()
    {
        var locks = new KeyedAsyncLock<int>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await locks.LockAsync(1, cts.Token));

        // The key is never reserved, so there is nothing to evict.
        Assert.Equal(0, locks.EntryCount);
    }

    [Fact]
    public async Task Dispose_LeaseTwice_DoesNotReleaseTheNextAcquisition()
    {
        var locks = new KeyedAsyncLock<string>(StringComparer.Ordinal);
        var stale = await locks.LockAsync("key");

        // Queue a second acquisition so the entry stays alive across the release, which makes the stale disposal
        // target the very entry that is still in use.
        var pending = locks.LockAsync("key").AsTask();
        stale.Dispose();
        var held = await pending.WaitAsync(TimeSpan.FromSeconds(30));

        stale.Dispose();

        // The stale disposal must neither release the key nor drop the reference count of the live entry, which
        // would evict it and let another caller acquire a brand new lock for the same key.
        Assert.Equal(1, locks.EntryCount);
        var blocked = locks.LockAsync("key").AsTask();
        Assert.False(blocked.IsCompleted, "The second disposal released a lock it no longer owns");

        held.Dispose();
        (await blocked.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
        Assert.Equal(0, locks.EntryCount);
    }
}
