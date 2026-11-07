namespace Meziantou.Framework.BloomFilters.Benchmarks;

public enum FilterMemory
{
    // Fits in the L2 cache of current desktop and server cores.
    Small512KiB,

    // Exceeds the last-level cache of current desktop and server CPUs.
    Large128MiB,
}
