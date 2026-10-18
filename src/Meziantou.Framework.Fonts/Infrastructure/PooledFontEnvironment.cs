namespace Meziantou.Framework.Fonts;

/// <summary>
/// The environment of a caller that has no allocation scopes of its own: the temporary buffers of an operation are
/// rented from the shared array pool, and their live bytes are bounded per operation.
/// </summary>
internal sealed class PooledFontEnvironment : FontEnvironment
{
    private readonly long _maxLiveAllocationBytes;

    public PooledFontEnvironment(FontResourceLimits limits, long maxLiveAllocationBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLiveAllocationBytes);
        Limits = limits;
        _maxLiveAllocationBytes = maxLiveAllocationBytes;
    }

    public override FontResourceLimits Limits { get; }

    public override FontAllocator CreateAllocator(string name) => new PooledFontAllocator(_maxLiveAllocationBytes);
}
