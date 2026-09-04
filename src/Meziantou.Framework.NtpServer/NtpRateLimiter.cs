using System.Net;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// An approximate per-source rate limiter that uses a fixed amount of memory.
/// </summary>
/// <remarks>
/// Source addresses on a UDP server are attacker-controlled and trivially spoofed, so the limiter
/// cannot allocate an entry per address without becoming a memory-exhaustion vector itself. Addresses
/// are mapped onto a fixed number of buckets instead; colliding addresses share a budget, which makes
/// the limit conservative rather than leaky.
/// </remarks>
internal sealed class NtpRateLimiter
{
    private const int BucketCount = 1024;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);

    private readonly int _maxRequestsPerWindow;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();
    private readonly Bucket[] _buckets = new Bucket[BucketCount];

    public NtpRateLimiter(int maxRequestsPerWindow, TimeProvider timeProvider)
    {
        _maxRequestsPerWindow = maxRequestsPerWindow;
        _timeProvider = timeProvider;
    }

    /// <summary>Records a request and indicates whether it may be answered.</summary>
    /// <param name="address">The source address of the request.</param>
    /// <param name="isFirstRejection">
    /// <see langword="true"/> when this is the first request rejected in the current window. Callers
    /// use it to answer at most one Kiss-o'-Death per window, so that replying to a throttled source
    /// cannot itself be used to reflect traffic.
    /// </param>
    public bool TryAcquire(IPAddress address, out bool isFirstRejection)
    {
        isFirstRejection = false;

        var hash = address.GetHashCode();
        var index = (int)((uint)hash % BucketCount);
        var now = _timeProvider.GetTimestamp();

        lock (_lock)
        {
            ref var bucket = ref _buckets[index];
            var inWindow = bucket.Count > 0 && _timeProvider.GetElapsedTime(bucket.WindowStart, now) < Window;

            if (bucket.Hash != hash)
            {
                // Refuse to hand the bucket to a different address while its current occupant is over
                // the limit: otherwise a flood of spoofed addresses would clear the limiter on every
                // packet, disabling it exactly when it is needed.
                if (inWindow && bucket.Count > _maxRequestsPerWindow)
                    return false;

                bucket = new Bucket { Hash = hash, WindowStart = now, Count = 1 };
                return true;
            }

            if (!inWindow)
            {
                bucket.WindowStart = now;
                bucket.Count = 1;
                return true;
            }

            bucket.Count++;
            if (bucket.Count <= _maxRequestsPerWindow)
                return true;

            isFirstRejection = bucket.Count == _maxRequestsPerWindow + 1;
            return false;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct Bucket
    {
        public int Hash;
        public long WindowStart;
        public int Count;
    }
}
