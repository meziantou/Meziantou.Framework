using System.Reflection;

namespace Meziantou.Framework.Threading.Tests;

public sealed class KeyedAsyncLockTests
{
    private static System.Collections.ICollection GetEntries<TKey>(KeyedAsyncLock<TKey> locks)
        where TKey : notnull
    {
        var field = typeof(KeyedAsyncLock<TKey>).GetField("_locks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (System.Collections.ICollection)field.GetValue(locks)!;
    }

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

        // Entries must be removed once released, otherwise the dictionary grows without bound.
        Assert.Empty(GetEntries(locks));
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

        Assert.Empty(GetEntries(locks));
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
            Assert.Single(GetEntries(locks));
        }
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
        Assert.Single(GetEntries(locks));
        var blocked = locks.LockAsync("key").AsTask();
        Assert.False(blocked.IsCompleted, "The second disposal released a lock it no longer owns");

        held.Dispose();
        (await blocked.WaitAsync(TimeSpan.FromSeconds(30))).Dispose();
        Assert.Empty(GetEntries(locks));
    }
}
