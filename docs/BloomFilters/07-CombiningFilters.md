---
title: Combining filters
description: Merge the values of two Bloom filters into one, and how it changes the false positive probability.
---

[UnionWith](cref:Meziantou.Framework.BloomFilters.BloomFilter.UnionWith) adds to a filter every value of another filter,
without the values themselves: it combines the bits of both filters with a bitwise OR. Use it to merge filters built
separately, for instance one per partition, per worker or per day, once the values are no longer available.

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=BloomFilter_Union;format=md-fenced-code-block;dedent=true -->
```csharp
// Size each filter for the total number of values: the merged filter holds them all
var size = BloomFilterSize.CreateOptimalSize(expectedItemCount: 2000, falsePositiveProbability: 0.01);
var europe = BloomFilter.CreateXXHash128(size);
var america = BloomFilter.CreateXXHash128(size);

europe.Add("alice@example.com");
america.Add("bob@example.com");

// Add the values of america to europe
europe.UnionWith(america);
Assert.True(europe.MayContain("alice@example.com"));
Assert.True(europe.MayContain("bob@example.com"));

// The filters must use the same hash algorithm, number of bits and number of hash functions
var other = BloomFilter.CreateXXHash3(size);
Assert.Throws<ArgumentException>(() => europe.UnionWith(other));
```
<!-- endref -->

- Every value added to either filter is reported as present in the merged filter: the union has no false negatives.
- The other filter is not modified. Merging a filter with itself does nothing, and merging the same filter twice gives
  the same result as merging it once.
- Counting Bloom filters do not support this operation.

## The filters must be compatible

A value sets the same bits in two filters only when they hash it the same way, so both filters must have:

- the same hash algorithm: they come from the same factory method, such as [BloomFilter.CreateXXHash128](cref:Meziantou.Framework.BloomFilters.BloomFilter.CreateXXHash128);
- the same number of bits and the same number of hash functions: create them from the same
  [BloomFilterSize](cref:Meziantou.Framework.BloomFilters.BloomFilterSize).

[UnionWith](cref:Meziantou.Framework.BloomFilters.BloomFilter.UnionWith) throws an `ArgumentException` otherwise. It
checks the filters before writing any bit, so the filter is left unchanged when it throws.

## The merged filter has more false positives

The merged filter holds the values of both filters in the same number of bits, so more of its bits are set. Its false
positive probability is the one of a filter that received all the values: two filters sized for 1,000 values each at
1% give a filter of 2,000 values, with a false positive probability of about 16%. The union does not keep the false
positive probability the filters were sized for.

Size each filter for the total number of values when you plan to merge them, as in the example above. Merging filters
of fewer values does not cost more memory: each filter has the size of the merged one, whatever the number of values it
holds.

## Merge while other threads use the filters

[UnionWith](cref:Meziantou.Framework.BloomFilters.BloomFilter.UnionWith) can run while other threads add values to
either filter or test them:

- The bits of the merged filter are set with atomic operations, so no value added to it at the same time is lost.
- The other filter is read 64 bits at a time, not as a snapshot. A value added to it during the merge may or may not be
  included, and a `MayContain` on the merged filter during the merge may return `false` for a value that is not merged
  yet. Once [UnionWith](cref:Meziantou.Framework.BloomFilters.BloomFilter.UnionWith) has returned, every value that the
  other filter held when it was called is included.
