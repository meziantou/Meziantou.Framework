namespace Meziantou.Framework.BloomFilters;

/// <summary>
/// Receives the hashes computed by the batch hashing of <c>AddRange</c>, so the same vectorized code fills a
/// <see cref="BloomFilter"/> or a <see cref="BlockedBloomFilter"/>.
/// </summary>
/// <remarks>
/// Implement it with a struct: the generic methods that take it are then specialized, and the call is inlined.
/// </remarks>
internal interface IBloomFilterHashSink
{
    void AddHash(BloomFilterHash hash);
}
