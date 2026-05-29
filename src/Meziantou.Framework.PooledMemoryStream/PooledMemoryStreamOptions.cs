using System.Collections.Immutable;

namespace Meziantou.Framework;

/// <summary>
/// Configures the buffer sizes and pooling behavior used by <see cref="PooledMemoryStream"/>.
/// </summary>
/// <remarks>
/// <para>
/// To benefit from pooling, the stream only ever rents byte arrays in the discrete sizes listed in
/// <see cref="BufferSizes"/> (or, when a contiguous buffer larger than the largest tier is required, a multiple of the
/// largest tier). Arrays of arbitrary sizes are never rented, which keeps the shared pool buckets small and reusable.
/// </para>
/// <para>
/// An instance becomes immutable (frozen) the first time it is used to create a <see cref="PooledMemoryStream"/>;
/// the <see cref="Default"/> instance is always frozen. Setting any property on a frozen instance throws an
/// <see cref="InvalidOperationException"/>. Configure all properties before passing the options to a stream.
/// </para>
/// </remarks>
public sealed class PooledMemoryStreamOptions
{
    private ImmutableArray<int> _bufferSizes = [4 * 1024, 64 * 1024, 1024 * 1024]; // 4 KiB / 64 KiB / 1 MiB
    private long _maxRetainedBytesPerBucket = 16L * 1024 * 1024; // 16 MiB per distinct array size
    private bool _clearOnReturn;
    private bool _frozen;

    /// <summary>
    /// Gets the default options instance (4 KiB / 64 KiB / 1 MiB tiers). This instance is frozen (immutable) and is
    /// shared across all streams that don't specify custom options.
    /// </summary>
    public static PooledMemoryStreamOptions Default { get; } = CreateFrozenDefault();

    /// <summary>Gets a value indicating whether this instance has been frozen and can no longer be modified.</summary>
    public bool IsFrozen => _frozen;

    /// <summary>
    /// Gets or sets the discrete pooled buffer sizes, in bytes, in strictly ascending order (for example
    /// <c>[4096, 131072, 1048576, 10485760]</c>). Small streams use the smallest size and grow through the larger
    /// sizes; the largest size is used for big streams, and contiguous buffers larger than it round up to a multiple
    /// of it. Must contain at least one size. Defaults to <c>[4096, 65536, 1048576]</c>.
    /// </summary>
    public ImmutableArray<int> BufferSizes
    {
        get => _bufferSizes;
        set
        {
            ThrowIfFrozen();
            ValidateBufferSizes(value, nameof(value));
            _bufferSizes = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of bytes retained by the shared pool for each distinct array size. When a
    /// returned buffer would exceed this limit, it is left for the garbage collector instead. Defaults to 16 MiB.
    /// </summary>
    public long MaxRetainedBytesPerBucket
    {
        get => _maxRetainedBytesPerBucket;
        set
        {
            ThrowIfFrozen();
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be greater than or equal to 0.");
            _maxRetainedBytesPerBucket = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether buffers are cleared (zeroed) when returned to the pool. This is slower
    /// but ensures previously written data cannot be observed by another stream that later rents the same array.
    /// Defaults to <see langword="false"/>. Note that <see cref="PooledMemoryStream"/> never exposes bytes that were
    /// not explicitly written, so this option is only relevant for defense-in-depth scenarios.
    /// </summary>
    public bool ClearOnReturn
    {
        get => _clearOnReturn;
        set
        {
            ThrowIfFrozen();
            _clearOnReturn = value;
        }
    }

    /// <summary>Freezes the instance so it can no longer be modified. Called when a stream is created. Idempotent.</summary>
    internal void Freeze() => _frozen = true;

    /// <summary>
    /// Returns the discrete array size to use for a new block, given the total capacity already allocated by the
    /// stream. Small streams use the smallest tier; larger streams escalate through the tiers to bound the number of
    /// segments.
    /// </summary>
    internal int GetBlockSize(long currentCapacity)
    {
        var sizes = _bufferSizes;
        for (var i = 0; i < sizes.Length - 1; i++)
        {
            if (currentCapacity < sizes[i + 1])
                return sizes[i];
        }

        return sizes[^1];
    }

    /// <summary>
    /// Returns a discrete array size that is at least <paramref name="minimumSize"/> bytes. The result is one of the
    /// configured tiers, or a multiple of the largest tier when a single contiguous buffer larger than the largest
    /// tier is required (e.g. <c>GetSpan</c> with a big hint, or <c>GetBuffer</c> on a large stream).
    /// </summary>
    internal int GetContiguousBlockSize(int minimumSize)
    {
        var sizes = _bufferSizes;
        for (var i = 0; i < sizes.Length; i++)
        {
            if (minimumSize <= sizes[i])
                return sizes[i];
        }

        // Larger than the largest tier: round up to a multiple of it so the size stays discrete and poolable.
        var largest = sizes[^1];
        var multiples = (minimumSize + largest - 1) / largest;
        return checked(multiples * largest);
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("The options cannot be modified after they have been used to create a PooledMemoryStream.");
    }

    private static void ValidateBufferSizes(ImmutableArray<int> sizes, string paramName)
    {
        if (sizes.IsDefaultOrEmpty)
            throw new ArgumentException("At least one buffer size must be specified.", paramName);

        for (var i = 0; i < sizes.Length; i++)
        {
            if (sizes[i] <= 0)
                throw new ArgumentOutOfRangeException(paramName, sizes[i], "Buffer sizes must be greater than 0.");

            if (i > 0 && sizes[i] <= sizes[i - 1])
                throw new ArgumentException("Buffer sizes must be in strictly ascending order.", paramName);
        }
    }

    private static PooledMemoryStreamOptions CreateFrozenDefault()
    {
        var options = new PooledMemoryStreamOptions();
        options.Freeze();
        return options;
    }
}
