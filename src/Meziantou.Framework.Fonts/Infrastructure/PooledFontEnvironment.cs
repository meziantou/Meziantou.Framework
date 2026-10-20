namespace Meziantou.Framework.Fonts;

/// <summary>
/// The environment of a caller that has no allocation scopes of its own: the temporary buffers of an operation are
/// rented from the shared array pool, and their live bytes are bounded per operation.
/// </summary>
public sealed class PooledFontEnvironment : FontEnvironment
{
    private readonly long _maxLiveAllocationBytes;

    /// <summary>Initializes a new instance of the <see cref="PooledFontEnvironment"/> class.</summary>
    /// <param name="limits">The limits that bound the work of the font code.</param>
    /// <param name="maxLiveAllocationBytes">The largest number of bytes the temporary buffers of one operation may hold at the same time.</param>
    public PooledFontEnvironment(FontResourceLimits limits, long maxLiveAllocationBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLiveAllocationBytes);
        Limits = limits;
        _maxLiveAllocationBytes = maxLiveAllocationBytes;
    }

    /// <inheritdoc/>
    public override FontResourceLimits Limits { get; }

    /// <inheritdoc/>
    public override FontAllocator CreateAllocator(string name) => new PooledFontAllocator(_maxLiveAllocationBytes);
}
