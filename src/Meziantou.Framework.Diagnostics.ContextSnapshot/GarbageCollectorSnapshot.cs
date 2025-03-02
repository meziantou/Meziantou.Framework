#if NET7_0_OR_GREATER
using System.Collections.Immutable;
#endif

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class GarbageCollectorSnapshot
{
    internal GarbageCollectorSnapshot()
    {
    }

    public int MaxGeneration { get; } = GC.MaxGeneration;
    public long TotalAllocatedBytes { get; } = GC.GetTotalAllocatedBytes(precise: false);
    public long TotalMemory { get; } = GC.GetTotalMemory(forceFullCollection: false);

#if NET7_0_OR_GREATER
    public TimeSpan TotalPauseDuration { get; } = GC.GetTotalPauseDuration();
    public ImmutableDictionary<string, object> ConfigurationVariables { get; } = GC.GetConfigurationVariables().ToImmutableDictionary();
#endif
}
