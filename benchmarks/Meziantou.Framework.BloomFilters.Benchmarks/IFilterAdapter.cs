namespace Meziantou.Framework.BloomFilters.Benchmarks;

internal interface IFilterAdapter
{
    void Add(ulong value);
    bool MayContain(ulong value);
}
