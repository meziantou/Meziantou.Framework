using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Retention bounds of a <see cref="SlabPool"/>. They are independent of the live-allocation accounting of allocation scopes.</summary>
internal sealed class SlabPoolOptions
{
    /// <summary>The default largest pooled buffer length (8 MiB). Larger buffers are allocated exactly and never retained.</summary>
    public const int DefaultMaxPooledLength = 8 * 1024 * 1024;

    /// <summary>The default maximum number of retained buffers per size class.</summary>
    public const int DefaultMaxRetainedBuffersPerBucket = 8;

    /// <summary>The default maximum number of bytes retained by the pool across all size classes (32 MiB).</summary>
    public const long DefaultMaxRetainedBytes = 32L * 1024 * 1024;

    /// <summary>Gets the largest pooled buffer length. Must be a power of two between 128 and 2^30.</summary>
    public int MaxPooledLength
    {
        get;
        init
        {
            if (value < 128 || value > (1 << 30) || !BitOperations.IsPow2(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum pooled length must be a power of two between 128 and 2^30.");

            field = value;
        }
    } = DefaultMaxPooledLength;

    /// <summary>Gets the maximum number of retained buffers per size class. Zero disables retention.</summary>
    public int MaxRetainedBuffersPerBucket
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = DefaultMaxRetainedBuffersPerBucket;

    /// <summary>Gets the maximum number of bytes (sum of buffer capacities) retained by the pool. Zero disables retention.</summary>
    public long MaxRetainedBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = DefaultMaxRetainedBytes;
}
