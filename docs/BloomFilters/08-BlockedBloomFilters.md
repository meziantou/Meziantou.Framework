---
title: Blocked Bloom filters
description: Make adds and lookups in a large filter faster by keeping the bits of each value in one cache line, for a little more memory.
---

A [BloomFilter](cref:Meziantou.Framework.BloomFilters.BloomFilter) spreads the bits of a value over the whole filter.
When the filter is larger than the CPU caches, each `Add` and each `MayContain` of a present value reads as many
unrelated memory locations as the filter has hash functions.

A [BlockedBloomFilter](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilter) splits its bits into blocks of 512 bits
(64 bytes, one cache line). The hash of a value chooses one block, and every bit of the value is in that block, so an
operation reads a single cache line. Writes from several threads stay safe without a lock.

## Create a blocked filter

Size it with [BlockedBloomFilterSize.CreateOptimalSize](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilterSize.CreateOptimalSize),
then create it with a factory method of [BlockedBloomFilter](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilter):

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=BlockedBloomFilter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// The size for 1 million items and a 1% false positive probability
var size = BlockedBloomFilterSize.CreateOptimalSize(expectedItemCount: 1_000_000, falsePositiveProbability: 0.01);
Assert.Equal(19_328, size.BlockCount); // 64 bytes per block: about 1.24 MB of memory
Assert.Equal(6, size.HashCount);

// A BloomFilter needs 3% less memory for the same probability
Assert.Equal(9_592_955, BloomFilterSize.CreateOptimalSize(1_000_000, 0.01).BitCount);

var filter = BlockedBloomFilter.CreateXXHash128(size);
filter.Add("alice@example.com");
filter.Add(42);

Assert.True(filter.MayContain("alice@example.com"));
Assert.True(filter.MayContain(42));
```
<!-- endref -->

The blocked filters implement [IBloomFilter](cref:Meziantou.Framework.BloomFilters.IBloomFilter), accept the same value
types, and use the same [hash algorithms](06-HashAlgorithms.md) as [BloomFilter](cref:Meziantou.Framework.BloomFilters.BloomFilter).
A value added to a blocked filter is not found in a classic filter, and the reverse: the bits are not at the same place.

## When to use it

- **Use it for large filters**, larger than the CPU caches (several megabytes and more), that are updated or queried
  often, and for filters that many threads update at the same time. It is the case where the cache misses of a classic
  filter dominate.
- **Keep [BloomFilter](cref:Meziantou.Framework.BloomFilters.BloomFilter)** when memory matters more than speed, for a
  very low false positive probability, or for small filters: when the filter fits in the cache, a blocked filter is
  only slightly faster.

## The extra memory

The values do not spread evenly over the blocks: some blocks receive more values than the average, and an absent
value that falls in a crowded block is more often reported as present. To reach the same false positive probability,
a blocked filter needs more bits than a classic one, and the stricter the probability, the more it needs:

| False positive probability | Bits per value, classic | Bits per value, blocked | Extra memory |
| ---: | ---: | ---: | ---: |
| 10% | 4.8 | 4.8 | +0.5% |
| 1% | 9.6 | 9.9 | +3% |
| 0.1% | 14.4 | 15.5 | +8% |
| 0.01% | 19.2 | 21.9 | +14% |
| 0.001% | 24.0 | 29.5 | +23% |

[BlockedBloomFilterSize.CreateOptimalSize](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilterSize.CreateOptimalSize)
accounts for this unevenness. Do not size a blocked filter with
[BloomFilterSize](cref:Meziantou.Framework.BloomFilters.BloomFilterSize): it would have more false positives than
requested.

## Choose the size yourself

[BlockedBloomFilterSize.CreateExact](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilterSize.CreateExact) takes a
number of blocks ([BlockCount](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilterSize.BlockCount)) and a number of
bits set per value ([HashCount](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilterSize.HashCount), from 1 to
512). [GetFalsePositiveProbability](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilterSize.GetFalsePositiveProbability)
returns the probability a size gives once it holds a number of values, so you can check a size you chose, or how a
filter degrades when it receives more values than planned.

[GetEstimateCount](cref:Meziantou.Framework.BloomFilters.BlockedBloomFilter.GetEstimateCount) estimates the number of
values added, like on a classic filter.

## Limitations

- There is no `AddRange`: add the values one at a time.
- There is no [UnionWith](cref:Meziantou.Framework.BloomFilters.BloomFilter.UnionWith): blocked filters cannot be [combined](07-CombiningFilters.md).
- There is no [Save](cref:Meziantou.Framework.BloomFilters.BloomFilter.Save) or `Load`: blocked filters cannot be [saved and loaded](09-SavingAndLoading.md) yet.
- The memory of a blocked filter is pinned, so that its blocks stay aligned on cache lines. It is allocated in segments
  of 1 GiB at most.
