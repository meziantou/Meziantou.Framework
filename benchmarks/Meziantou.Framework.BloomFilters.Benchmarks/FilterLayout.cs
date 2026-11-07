namespace Meziantou.Framework.BloomFilters.Benchmarks;

public enum FilterLayout
{
    // BloomFilterXXHash128: HashCount probes spread over the whole filter.
    Classic,

    // BlockedBloomFilterXXHash128 with the same number of bits as Classic, so the false positive rate is higher.
    BlockedEqualMemory,

    // BlockedBloomFilterXXHash128 sized by BlockedBloomFilterSize.CreateOptimalSize for the same false positive rate.
    BlockedEqualFpr,
}
