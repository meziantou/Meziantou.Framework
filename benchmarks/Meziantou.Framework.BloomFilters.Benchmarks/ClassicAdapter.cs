namespace Meziantou.Framework.BloomFilters.Benchmarks;

internal readonly struct ClassicAdapter(BloomFilterXXHash128 filter) : IFilterAdapter
{
    public void Add(ulong value) => filter.Add(value);
    public bool MayContain(ulong value) => filter.MayContain(value);
}
