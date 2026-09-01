using System.Reflection;

namespace Meziantou.Framework.Threading.Tests;

public sealed class KeyedLockTests
{
    private static System.Collections.ICollection GetEntries<TKey>(KeyedLock<TKey> locks)
        where TKey : notnull
    {
        var field = typeof(KeyedLock<TKey>).GetField("_locks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (System.Collections.ICollection)field.GetValue(locks)!;
    }

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

        // Entries must be removed once released, otherwise the dictionary grows without bound.
        Assert.Empty(GetEntries(locks));
    }

    [Fact]
    public void NestedKeysAreKeptWhileHeld()
    {
        var locks = new KeyedLock<int>();
        using (locks.Lock(1))
        {
            Assert.Single(GetEntries(locks));
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
    public void Lock_DisposedOnAnotherThread_ThrowsButStillEvictsTheEntry()
    {
        // System.Threading.Lock is thread-affine, so Exit throws here. The reference count must still be
        // released, otherwise the entry stays in the dictionary and every later Lock(1) deadlocks.
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
        Assert.Empty(GetEntries(locks));

        // A fresh entry is created, so the key is usable again rather than permanently wedged.
        using (locks.Lock(1))
        {
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var locks = new KeyedLock<int>();
        var lease = locks.Lock(1);
        lease.Dispose();
        lease.Dispose(); // must not double-release or corrupt the ref count

        Assert.Empty(GetEntries(locks));
    }
}
