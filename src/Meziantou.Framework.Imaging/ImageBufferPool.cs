using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// The settings of the process-wide pool of the buffers the library rents for pixels, decoders, encoders and scratch
/// memory.
/// </summary>
/// <remarks>
/// <para>
/// A disposed image, or a finished operation, returns its buffers to the pool, which keeps some of them for the next
/// rentals instead of leaving them to the garbage collector. Retention is bounded per buffer size and in total bytes
/// (<see cref="MaxRetainedBytes"/>, 32 MiB by default), and buffers larger than 8 MiB are never retained. The retained
/// buffers are not part of any <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/> budget.
/// </para>
/// <para>
/// In a memory-constrained container, lower <see cref="MaxRetainedBytes"/>, or set it to 0 to disable retention: every
/// buffer then goes back to the garbage collector when it is returned. The settings never change the output of an
/// operation. This type is thread-safe.
/// </para>
/// </remarks>
public static class ImageBufferPool
{
    /// <summary>The default value of <see cref="MaxRetainedBytes"/> (32 MiB).</summary>
    public const long DefaultMaxRetainedBytes = SlabPoolOptions.DefaultMaxRetainedBytes;

    /// <summary>
    /// Gets or sets the maximum number of bytes the pool retains across all buffer sizes. Defaults to
    /// <see cref="DefaultMaxRetainedBytes"/>; 0 disables retention.
    /// </summary>
    /// <remarks>Lowering the value releases retained buffers immediately, until the retained bytes fit.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public static long MaxRetainedBytes
    {
        get => SlabPool.Shared.MaxRetainedBytes;
        set => SlabPool.Shared.MaxRetainedBytes = value;
    }

    /// <summary>Gets the number of bytes the pool currently retains.</summary>
    public static long RetainedBytes => SlabPool.Shared.GetDiagnostics().RetainedBytes;

    /// <summary>Releases every retained buffer to the garbage collector. Buffers that are in use are not affected.</summary>
    public static void Trim() => SlabPool.Shared.Trim();
}
