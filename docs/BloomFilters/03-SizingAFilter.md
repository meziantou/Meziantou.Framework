---
title: Sizing a filter
description: Compute the size of a filter from the expected number of values and the false positive probability, or set the bits and hash functions yourself.
---

A Bloom filter is an array of bits. Adding a value sets a few bits, chosen by hashing the value; `MayContain` checks
that all those bits are set. Its size has two parts:

- **The number of bits** ([BitCount](cref:Meziantou.Framework.BloomFilters.BloomFilterSize.BitCount)): the memory of the filter. The filter allocates them all when it is created.
- **The number of hash functions** ([HashCount](cref:Meziantou.Framework.BloomFilters.BloomFilterSize.HashCount)): how many bits each value sets and each test checks.

## Compute the optimal size

[BloomFilterSize.CreateOptimalSize](cref:Meziantou.Framework.BloomFilters.BloomFilterSize.CreateOptimalSize) takes the number of values you expect to add and the false positive probability you
accept, and returns the smallest number of bits, with its number of hash functions, that keeps the probability at or
below this target:

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=BloomFilter_Size;format=md-fenced-code-block;dedent=true -->
```csharp
// The size for 1 million items and a 1% false positive probability
var size = BloomFilterSize.CreateOptimalSize(expectedItemCount: 1_000_000, falsePositiveProbability: 0.01);
Assert.Equal(9_592_955, size.BitCount); // about 1.2 MB of memory
Assert.Equal(7, size.HashCount);

// Or choose the number of bits and of hash functions yourself
var exact = BloomFilterSize.CreateExact(bitCount: 8 * 1024 * 1024, hashCount: 5);
var filter = BloomFilter.CreateXXHash128(exact);
filter.Add("alice@example.com");
Assert.True(filter.MayContain("alice@example.com"));
```
<!-- endref -->

- The probability is computed with the usual approximation of a Bloom filter, `(1 - e^(-kn/m))^k` for `n` values,
  `m` bits and `k` hash functions. It assumes hash functions that spread the values evenly: the false positive rate
  measured on a given set of values is close to the target, but can be a little higher.
- The number of hash functions is a whole number, so a target above 0.5 still uses one hash function, with more bits
  than the formula for the optimal size gives.
- `expectedItemCount` must be greater than 0, and `falsePositiveProbability` between 0 and 1 (both excluded). A size
  too large to represent throws an `ArgumentOutOfRangeException`.
- The memory grows with the number of values, and with the precision: dividing the false positive probability by 10
  adds about 4.8 bits per value.
- [BloomFilterSize.CreateExact](cref:Meziantou.Framework.BloomFilters.BloomFilterSize.CreateExact) takes the number of bits and of hash functions as they are. Use it to reproduce the size
  of another system, or when the memory is the constraint.

The bits are stored in segments rather than in one array, so a filter can have more bits than an array can hold.

## Plan for the number of values

The false positive probability is only reached for the number of values the filter was sized for. Each value added
beyond that sets more bits, and the probability rises, until every value is reported as present. A filter never
refuses a value.

Size the filter for the number of values it will hold at the end of its life, not at its creation. To check how full a
filter is, compare [GetEstimateCount](cref:Meziantou.Framework.BloomFilters.BloomFilter.GetEstimateCount) with the number of values it was sized for: when it gets close, create a larger
filter and add the values again. [GetEstimateCount](cref:Meziantou.Framework.BloomFilters.BloomFilter.GetEstimateCount) returns `double.PositiveInfinity` when every bit is set.

## Counting Bloom filters

[CountingBloomFilterSize](cref:Meziantou.Framework.BloomFilters.CountingBloomFilterSize) works the same way, with a number of counters ([CounterCount](cref:Meziantou.Framework.BloomFilters.CountingBloomFilterSize.CounterCount)) instead of a number of bits.
Each counter takes one byte. See [Counting Bloom filters](05-CountingBloomFilters.md).
