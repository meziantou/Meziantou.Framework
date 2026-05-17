using System.Collections.Immutable;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of the garbage collector state including generation count, total allocated bytes, and configuration.</summary>
public sealed class GarbageCollectorSnapshot
{
    internal GarbageCollectorSnapshot()
    {
    }

    public int MaxGeneration { get; } = GC.MaxGeneration;
    public long TotalAllocatedBytes { get; } = GC.GetTotalAllocatedBytes(precise: false);
    public long TotalMemory { get; } = GC.GetTotalMemory(forceFullCollection: false);
    public TimeSpan TotalPauseDuration { get; } = GC.GetTotalPauseDuration();
    public ImmutableDictionary<string, object> ConfigurationVariables { get; } = GC.GetConfigurationVariables().ToImmutableDictionary();
}
