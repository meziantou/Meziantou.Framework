#if NET7_0_OR_GREATER
using System.Collections.Immutable;
#endif

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of garbage collector information at a specific point in time.</summary>
public sealed class GarbageCollectorSnapshot
{
    internal GarbageCollectorSnapshot()
    {
    }

    /// <summary>Gets the maximum number of generations supported by the garbage collector.</summary>
    public int MaxGeneration { get; } = GC.MaxGeneration;
    /// <summary>Gets the total number of bytes allocated since the process started.</summary>
    public long TotalAllocatedBytes { get; } = GC.GetTotalAllocatedBytes(precise: false);
    /// <summary>Gets the total memory currently used by the garbage collector.</summary>
    public long TotalMemory { get; } = GC.GetTotalMemory(forceFullCollection: false);

#if NET7_0_OR_GREATER
    /// <summary>Gets the total time spent paused for garbage collection.</summary>
    public TimeSpan TotalPauseDuration { get; } = GC.GetTotalPauseDuration();
    /// <summary>Gets the garbage collector configuration variables.</summary>
    public ImmutableDictionary<string, object> ConfigurationVariables { get; } = GC.GetConfigurationVariables().ToImmutableDictionary();
#endif
}
