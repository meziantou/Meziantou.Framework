namespace Meziantou.Framework.Threading.Tests;

public sealed class KeyedLockTests
{
    [Fact]
    public void Test()
    {
        var locks = new KeyedLock<string>(StringComparer.Ordinal);
        using (locks.Lock("a"))
        using (locks.Lock("b"))
        {
            // If a and b are the same instance, this test should timeout
        }
    }

    [Fact]
    public void ReleasedKeysAreEvicted()
    {
        var locks = new KeyedLock<int>();
        for (var i = 0; i < 1000; i++)
        {
            using (locks.Lock(i))
            {
            }
        }

        // Entries must be removed once released, otherwise the table grows without bound.
        Assert.Equal(0, locks.EntryCount);
    }

    [Fact]
    public void NestedKeysAreKeptWhileHeld()
    {
        var locks = new KeyedLock<int>();
        using (locks.Lock(1))
        {
            Assert.Equal(1, locks.EntryCount);
        }
    }

    [Fact]
    public async Task SameKeySerializesConcurrentAccess()
    {
        var locks = new KeyedLock<string>(StringComparer.Ordinal);
        var counter = 0;

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            using (locks.Lock("key"))
            {
                // Without real mutual exclusion this read-modify-write would lose updates.
                counter++;
            }
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(50, counter);
    }

    [Fact]
    public async Task CustomComparer_SameKeyDifferentCase_AreTreatedAsSameLock()
    {
        // KeyedLock uses System.Threading.Lock, which is thread-affine: the lease must be acquired
        // and disposed on the same thread (no await in between). All lock handling stays synchronous
        // here; the background acquisition runs on its own thread.
        var locks = new KeyedLock<string>(StringComparer.OrdinalIgnoreCase);
        using var secondAcquired = new ManualResetEventSlim(initialState: false);

        var lease = locks.Lock("KEY");
        var blocked = Task.Run(() =>
        {
            using (locks.Lock("key"))
            {
                secondAcquired.Set();
            }
        });

        Assert.False(secondAcquired.Wait(200)); // blocked by the held "KEY" lock (same entry)
        lease.Dispose(); // released on the acquiring thread

        await blocked.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(secondAcquired.IsSet);
    }

    [Fact]
    public void SameKeyReacquiredAfterRelease()
    {
        var locks = new KeyedLock<int>();
        using (locks.Lock(1))
        {
        }

        // Re-acquiring an evicted key must work (a fresh entry is created).
        using (locks.Lock(1))
        {
        }
    }

    [Fact]
    public async Task Lock_DisposedOnAnotherThread_ThrowsAndKeepsTheLockHeld()
    {
        // System.Threading.Lock is thread-affine, so Exit throws here and the lock remains held by the
        // acquiring thread. The entry must therefore stay in the table: evicting it would let another
        // thread lock a fresh entry for the same key while this critical section is still running.
        var locks = new KeyedLock<int>();
        var lease = locks.Lock(1);

        // A dedicated thread, not the thread pool: xunit runs the test body on a pool thread, so Task.Run can
        // schedule the disposal back onto that same thread and Exit would then legitimately succeed.
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));

        Assert.IsType<SynchronizationLockException>(captured);
        Assert.Equal(1, locks.EntryCount);

        using var acquired = new ManualResetEventSlim(initialState: false);
        var blocked = Task.Run(() =>
        {
            using (locks.Lock(1))
            {
                acquired.Set();
            }
        });

        Assert.False(acquired.Wait(200)); // still blocked by the lock this thread owns

        // The failed disposal did not consume the lease, so the owning thread can still release the lock.
        lease.Dispose();

        await blocked.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(acquired.IsSet);
        Assert.Equal(0, locks.EntryCount);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var locks = new KeyedLock<int>();
        var lease = locks.Lock(1);
        lease.Dispose();
        lease.Dispose(); // must not double-release or corrupt the ref count

        Assert.Equal(0, locks.EntryCount);
    }
}
