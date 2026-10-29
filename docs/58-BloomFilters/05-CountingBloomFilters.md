---
title: Counting Bloom filters
description: Remove values from a filter and estimate how many times a value was added, with counting Bloom filters.
---

A counting Bloom filter replaces each bit with a counter. Adding a value increments its counters, removing it decrements
them, so values can be removed, and the counters give an estimate of how many times a value was added.

## Add, count and remove values

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=CountingBloomFilter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var size = CountingBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01);
var filter = CountingBloomFilter.CreateXXHash3(size);

filter.Add("alice@example.com");
filter.Add("alice@example.com");

Assert.True(filter.MayContain("alice@example.com"));
var estimatedCount = filter.GetEstimatedCount("alice@example.com");
Assert.True(estimatedCount >= 2);

filter.Remove("alice@example.com");
```
<!-- endref -->

- `CountingBloomFilterSize.CreateOptimalSize` sizes the filter like `BloomFilterSize.CreateOptimalSize`, with a number
  of counters instead of a number of bits. Each counter takes one byte, so a counting filter uses 8 times the memory of
  a Bloom filter with the same false positive probability.
- `CountingBloomFilter` has the same factory methods as `BloomFilter`, one per [hash algorithm](06-HashAlgorithms.md).
- `GetEstimatedCount` returns the smallest counter of the value: the number of times it was added, or more when other
  values share all its counters. It never returns less, as long as only added values are removed.
- `Remove` decrements the counters of the value. The value was added twice here, so it is still present after one
  removal.

## Only remove values that were added

`Remove` ignores a value when one of its counters is zero: the filter knows the value is absent. But a value that was
never added and is reported as present (a false positive) decrements counters that belong to other values, and those
values may then be reported as absent: the filter then has false negatives.

Remove a value only when you know it was added, for instance because your application keeps the list of values
elsewhere and the filter only speeds up its lookups.

## Counters saturate at 255

Counters are 8-bit. A counter that reaches 255 stops there and is never decremented again, because the filter no longer
knows how many values share it:

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=CountingBloomFilter_Saturation;format=md-fenced-code-block;dedent=true -->
```csharp
var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));

filter.Add("bob@example.com");
filter.Remove("bob@example.com");
Assert.False(filter.MayContain("bob@example.com"));

// Removing a value that was never added is ignored when the filter knows it is absent
filter.Remove("carol@example.com");

// Counters stop at 255: past that, the count is not tracked and the value cannot be removed
for (var i = 0; i < 300; i++)
{
    filter.Add("alice@example.com");
}

Assert.Equal(255, filter.GetEstimatedCount("alice@example.com"));
filter.Remove("alice@example.com");
Assert.True(filter.MayContain("alice@example.com"));
```
<!-- endref -->

- `GetEstimatedCount` returns at most 255.
- A value whose counters all saturated can no longer be removed: it keeps being reported as present, but it is never
  reported as absent.

Counting Bloom filters suit values that are added a few times each. To count frequent values, use a dictionary or
another structure.
