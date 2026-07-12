# Meziantou.Framework.BloomFilters

High-performance in-memory Bloom filters and counting Bloom filters for .NET.

Use `BloomFilter` when you need fast membership checks (`Add` + `MayContain`) and can tolerate false positives.

````c#
using Meziantou.Framework.BloomFilters;

// Configure the filter for ~1,000 items and 1% false positive probability
var size = BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01);
var filter = BloomFilter.CreateXXHash3(size);

filter.Add("alice@example.com");
filter.Add("bob@example.com");

if (filter.MayContain("alice@example.com"))
{
    // Item may exist (no false negatives)
}

// Approximate number of inserted items
var estimatedItemCount = filter.GetEstimateCount();
````

Use `CountingBloomFilter` when you need to remove values or estimate per-value occurrences.

````c#
using Meziantou.Framework.BloomFilters;

var size = CountingBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01);
var filter = CountingBloomFilter.CreateXXHash3(size);

filter.Add("alice@example.com");
filter.Add("alice@example.com");

var mayContainAlice = filter.MayContain("alice@example.com"); // true
var estimatedCount = filter.GetEstimatedCount("alice@example.com"); // >= 2

filter.Remove("alice@example.com");
````

Available factory methods:

- `BloomFilter.CreateXXHash128`, `CreateXXHash64`, `CreateXXHash32`, `CreateXXHash3`, `CreateCrc64`, `CreateCrc32`
- `CountingBloomFilter.CreateXXHash128`, `CreateXXHash64`, `CreateXXHash32`, `CreateXXHash3`, `CreateCrc64`, `CreateCrc32`

Supported value types include `int`, `uint`, `long`, `ulong`, `Guid`, `string`, `Int128`, `UInt128`, and `ReadOnlySpan<byte>`.
