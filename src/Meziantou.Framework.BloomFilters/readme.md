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

Counters are 8-bit. A counter that reaches 255 saturates and is never decremented again, so `GetEstimatedCount` returns at most 255, and a value whose counters all saturated can no longer be removed (it may keep being reported as present, but is never reported as absent).

Only remove values that were added. `Remove` ignores a value the filter can prove is absent, but a value that is absent yet reported as present (a false positive) decrements counters that belong to other values, and those values may then be reported as absent.

Available factory methods:

- `BloomFilter.CreateXXHash128`, `CreateXXHash64`, `CreateXXHash32`, `CreateXXHash3`, `CreateCrc64`, `CreateCrc32`, `CreateAdler32`
- `CountingBloomFilter.CreateXXHash128`, `CreateXXHash64`, `CreateXXHash32`, `CreateXXHash3`, `CreateCrc64`, `CreateCrc32`, `CreateAdler32`

`XXHash128` is the fastest algorithm and the recommended default.

The 32-bit algorithms (`XXHash32`, `Crc32` and `Adler32`) cannot tell apart two values with the same 32-bit hash, so the false positive rate cannot go below about `itemCount / 2^32` whatever the filter size (about 2.3% for 100 million items). Use a 64-bit or 128-bit algorithm for large filters.

Adler32 distributes short inputs poorly: many small integers or short strings share the same checksum. With a filter configured for a 1% false positive rate, the measured rate is about 90% for sequential integers and 60% for short strings. Prefer another algorithm unless you need Adler32 specifically.

Supported value types include `int`, `uint`, `long`, `ulong`, `Guid`, `string`, `Int128`, `UInt128`, and `ReadOnlySpan<byte>`.
