using System.Collections.Concurrent;

namespace Meziantou.Framework;

/// <summary>
/// A process-wide pool of byte arrays shared by all <see cref="PooledMemoryStream"/> instances. Arrays are bucketed by
/// their exact length, so only the discrete sizes produced by <see cref="PooledMemoryStreamOptions"/> are ever pooled.
/// </summary>
internal sealed class PooledBufferPool
{
    /// <summary>The pool shared by all <see cref="PooledMemoryStream"/> instances.</summary>
    public static PooledBufferPool Shared { get; } = new();

    private readonly ConcurrentDictionary<int, Bucket> _buckets = new();

    public byte[] Rent(int exactSize)
    {
        if (_buckets.TryGetValue(exactSize, out var bucket) && bucket.TryRent(out var array))
            return array;

        return new byte[exactSize];
    }

    public void Return(byte[] array, long maxRetainedBytesPerBucket, bool clear)
    {
        if (array.Length == 0 || maxRetainedBytesPerBucket <= 0)
            return;

        if (clear)
            Array.Clear(array, 0, array.Length);

        var bucket = _buckets.GetOrAdd(array.Length, static _ => new Bucket());
        bucket.Return(array, maxRetainedBytesPerBucket);
    }

    private sealed class Bucket
    {
        private readonly ConcurrentQueue<byte[]> _arrays = new();
        private long _retainedBytes;

        public bool TryRent(out byte[] array)
        {
            if (_arrays.TryDequeue(out array!))
            {
                Interlocked.Add(ref _retainedBytes, -array.Length);
                return true;
            }

            array = null!;
            return false;
        }

        public void Return(byte[] array, long maxRetainedBytes)
        {
            // Best-effort cap: a small amount of overshoot under concurrency is acceptable.
            if (Interlocked.Read(ref _retainedBytes) + array.Length > maxRetainedBytes)
                return;

            Interlocked.Add(ref _retainedBytes, array.Length);
            _arrays.Enqueue(array);
        }
    }
}
