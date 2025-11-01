using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading;

/// <summary>Provides an asynchronous lock mechanism that locks based on a key, allowing concurrent operations on different keys.</summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <example>
/// <code><![CDATA[
/// var keyedLock = new KeyedAsyncLock<string>();
/// 
/// async Task ProcessUserAsync(string userId)
/// {
///     using (await keyedLock.LockAsync(userId))
///     {
///         // Only one operation per userId at a time
///         // Multiple different userIds can be processed concurrently
///         await UpdateUserDataAsync(userId);
///     }
/// }
/// ]]></code>
/// </example>
public sealed class KeyedAsyncLock<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, AsyncLock> _locks;

    /// <summary>Initializes a new instance of the <see cref="KeyedAsyncLock{TKey}"/> class.</summary>
    public KeyedAsyncLock()
        : this(comparer: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="KeyedAsyncLock{TKey}"/> class with the specified equality comparer.</summary>
    /// <param name="comparer">The equality comparer to use when comparing keys.</param>
    public KeyedAsyncLock(IEqualityComparer<TKey>? comparer)
    {
        _locks = new ConcurrentDictionary<TKey, AsyncLock>(comparer);
    }

    /// <summary>Asynchronously acquires the lock for the specified key.</summary>
    /// <param name="key">The key to lock on.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the lock.</param>
    /// <returns>A task that returns a disposable lease. Disposing the lease releases the lock.</returns>
    public ValueTask<AsyncLock.AsyncLockLease> LockAsync(TKey key, CancellationToken cancellationToken = default)
    {
        var instance = _locks.GetOrAdd(key, _ => new AsyncLock());
        return instance.LockAsync(cancellationToken);
    }
}
