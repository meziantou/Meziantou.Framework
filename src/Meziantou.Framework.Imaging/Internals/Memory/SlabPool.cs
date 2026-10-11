using System.Diagnostics;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A bounded pool of byte buffers. Lengths are rounded up to size classes of four
/// steps per power of two (64, 80, 96, 112, 128, 160...), so a rental wastes at most 25% of its capacity; the rounded
/// capacity is what allocation scopes charge. Requests above <see cref="SlabPoolOptions.MaxPooledLength"/> are allocated
/// exactly and never retained.
/// </summary>
/// <remarks>
/// <para>
/// Retention is bounded per size class and in total bytes, independently of any allocation scope: returning a buffer beyond
/// a bound leaves it to the garbage collector. Live accounting is unaffected by retention (a retained buffer is not charged
/// to anyone).
/// </para>
/// <para>
/// Rented buffers may contain data of a previous rental unless <c>clear</c> is requested; callers that expose a buffer
/// (pixel storage) always request clearing. The pool is thread-safe.
/// </para>
/// </remarks>
internal sealed class SlabPool
{
    /// <summary>The smallest size class.</summary>
    public const int MinimumLength = 64;

    private const int MinimumLengthLog2 = 6;
    private const int StepsPerOctave = 4;

    private readonly Lock _lock = new();
    private readonly byte[]?[][] _buckets;
    private readonly int[] _bucketCounts;
    private long _retainedBytes;
    private int _retainedBuffers;
    private long _reusedRentals;
    private long _newPooledAllocations;
    private long _unpooledAllocations;
    private long _retainedReturns;
    private long _droppedReturns;

    public SlabPool(SlabPoolOptions? options = null)
    {
        Options = options ?? new SlabPoolOptions();
        var bucketCount = GetBucketIndex(Options.MaxPooledLength) + 1;
        _buckets = new byte[]?[bucketCount][];
        _bucketCounts = new int[bucketCount];
        for (var i = 0; i < bucketCount; i++)
        {
            _buckets[i] = new byte[]?[Options.MaxRetainedBuffersPerBucket];
        }
    }

    /// <summary>Gets the process-wide pool used by default.</summary>
    public static SlabPool Shared { get; } = new();

    public SlabPoolOptions Options { get; }

    /// <summary>
    /// Gets or sets a test hook called before every rental with the requested length. Returning an exception makes the
    /// rental throw it, which simulates allocation failures. Never set on <see cref="Shared"/>.
    /// </summary>
    internal Func<int, Exception?>? RentFailureInjector { get; set; }

    /// <summary>
    /// Gets or sets a test hook called with every buffer this pool hands out (after clearing), for rental audits. Never set
    /// on <see cref="Shared"/>.
    /// </summary>
    internal Action<byte[]>? RentObserver { get; set; }

    /// <summary>
    /// Gets or sets a test hook called with every returned buffer before retention. Returning <see langword="true"/> takes the
    /// buffer over (it is neither retained nor reused), which lets audits poison and quarantine returned buffers to detect
    /// use after return. Never set on <see cref="Shared"/>.
    /// </summary>
    internal Func<byte[], bool>? ReturnInterceptor { get; set; }

    /// <summary>Gets the capacity of the buffer that <see cref="Rent(int, bool)"/> returns for a length, which is the charged size.</summary>
    /// <param name="length">The requested length (non-negative; may exceed the maximum buffer length).</param>
    public long GetRentCapacity(long length)
    {
        Debug.Assert(length >= 0);
        if (length == 0)
            return 0;

        if (length > Options.MaxPooledLength)
            return length;

        return GetBucketLength(GetBucketIndex((int)length));
    }

    /// <summary>Rents a buffer of at least <paramref name="length"/> bytes; its length is <see cref="GetRentCapacity(long)"/>.</summary>
    /// <param name="length">The requested length, from 0 to <see cref="Array.MaxLength"/>.</param>
    /// <param name="clear"><see langword="true"/> to zero the first <paramref name="length"/> bytes.</param>
    /// <exception cref="OutOfMemoryException">The buffer cannot be allocated.</exception>
    public byte[] Rent(int length, bool clear)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Array.MaxLength);

        if (RentFailureInjector?.Invoke(length) is { } failure)
            throw failure;

        if (length == 0)
            return [];

        if (length > Options.MaxPooledLength)
        {
            var unpooled = clear ? new byte[length] : unsafe(GC.AllocateUninitializedArray<byte>(length));
            Interlocked.Increment(ref _unpooledAllocations);
            RentObserver?.Invoke(unpooled);
            return unpooled;
        }

        var index = GetBucketIndex(length);
        byte[]? buffer = null;
        lock (_lock)
        {
            ref var count = ref _bucketCounts[index];
            if (count > 0)
            {
                count--;
                buffer = _buckets[index][count];
                _buckets[index][count] = null;
                _retainedBuffers--;
                _retainedBytes -= buffer!.Length;
                _reusedRentals++;
            }
        }

        if (buffer is not null)
        {
            if (clear)
            {
                buffer.AsSpan(0, length).Clear();
            }

            RentObserver?.Invoke(buffer);
            return buffer;
        }

        var bucketLength = GetBucketLength(index);
        buffer = clear ? new byte[bucketLength] : unsafe(GC.AllocateUninitializedArray<byte>(bucketLength));
        Interlocked.Increment(ref _newPooledAllocations);
        RentObserver?.Invoke(buffer);
        return buffer;
    }

    /// <summary>Returns a buffer obtained from <see cref="Rent(int, bool)"/>. The caller must not use it afterward.</summary>
    /// <returns><see langword="true"/> if the buffer is retained for reuse; <see langword="false"/> if it is left to the garbage collector.</returns>
    public bool Return(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0)
            return false;

        if (ReturnInterceptor?.Invoke(buffer) == true)
            return false;

        if (buffer.Length > Options.MaxPooledLength)
        {
            Interlocked.Increment(ref _droppedReturns);
            return false;
        }

        var index = GetBucketIndex(buffer.Length);
        if (GetBucketLength(index) != buffer.Length)
        {
            Interlocked.Increment(ref _droppedReturns);
            return false;
        }

        lock (_lock)
        {
            ref var count = ref _bucketCounts[index];
            if (count < _buckets[index].Length && _retainedBytes + buffer.Length <= Options.MaxRetainedBytes)
            {
                _buckets[index][count] = buffer;
                count++;
                _retainedBuffers++;
                _retainedBytes += buffer.Length;
                _retainedReturns++;
                return true;
            }

            _droppedReturns++;
            return false;
        }
    }

    /// <summary>Releases every retained buffer.</summary>
    public void Trim()
    {
        lock (_lock)
        {
            for (var i = 0; i < _buckets.Length; i++)
            {
                Array.Clear(_buckets[i]);
                _bucketCounts[i] = 0;
            }

            _retainedBuffers = 0;
            _retainedBytes = 0;
        }
    }

    public SlabPoolDiagnostics GetDiagnostics()
    {
        lock (_lock)
        {
            return new SlabPoolDiagnostics(
                _retainedBuffers,
                _retainedBytes,
                _reusedRentals,
                Interlocked.Read(ref _newPooledAllocations),
                Interlocked.Read(ref _unpooledAllocations),
                _retainedReturns,
                Interlocked.Read(ref _droppedReturns));
        }
    }

    /// <summary>Gets the index of the smallest size class that holds <paramref name="length"/> bytes (1 to the maximum pooled length).</summary>
    internal static int GetBucketIndex(int length)
    {
        Debug.Assert(length > 0);
        if (length <= MinimumLength)
            return 0;

        // 2^k < length <= 2^(k+1); the classes of this octave are 2^k + s * 2^(k-2) for s = 1..4
        var k = 31 - BitOperations.LeadingZeroCount((uint)(length - 1));
        var quarterLog2 = k - 2;
        var step = (length - (1 << k) + (1 << quarterLog2) - 1) >> quarterLog2;
        return 1 + ((k - MinimumLengthLog2) * StepsPerOctave) + (step - 1);
    }

    /// <summary>Gets the length of a size class.</summary>
    internal static int GetBucketLength(int index)
    {
        Debug.Assert(index >= 0);
        if (index == 0)
            return MinimumLength;

        var k = MinimumLengthLog2 + ((index - 1) / StepsPerOctave);
        var step = ((index - 1) % StepsPerOctave) + 1;
        return (1 << k) + (step << (k - 2));
    }
}
