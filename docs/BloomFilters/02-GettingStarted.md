---
title: Getting started
description: Install the package, create a Bloom filter for an expected number of values, add values and test them.
---

## Install the package

Add the `Meziantou.Framework.BloomFilters` package to a project:

```bash
dotnet add package Meziantou.Framework.BloomFilters
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.BloomFilters;`.

## Create a filter, add values and test them

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=BloomFilter_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Configure the filter for ~1,000 items and 1% false positive probability
var size = BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01);
var filter = BloomFilter.CreateXXHash3(size);

filter.Add("alice@example.com");
filter.Add("bob@example.com");

// Item may exist (no false negatives)
Assert.True(filter.MayContain("alice@example.com"));

// Approximate number of inserted items
var estimatedItemCount = filter.GetEstimateCount();
Assert.Equal(2, estimatedItemCount, tolerance: 0.1);
```
<!-- endref -->

- `BloomFilterSize.CreateOptimalSize` computes the size of a filter for the number of values you expect to add and the
  false positive probability you accept. See [Sizing a filter](03-SizingAFilter.md).
- `BloomFilter.CreateXXHash3` creates a filter of that size that hashes the values with XXH3. Each hash algorithm has
  its factory method; see [Hash algorithms](06-HashAlgorithms.md).
- `Add` adds a value. `MayContain` returns `false` when the value was certainly not added, and `true` when it probably
  was.
- `GetEstimateCount` estimates the number of distinct values added, from the number of bits set.

## Use the filter in front of a lookup

Ask the filter first, and only do the expensive lookup when it answers `true`. The lookup is still needed then, because
`true` can be a false positive:

1. Add every existing value to the filter, for instance at startup.
2. Add each new value when it is created.
3. For a value to look up, call `MayContain`. When it returns `false`, the value does not exist: skip the lookup.

A Bloom filter cannot forget a value. When values are deleted, rebuild the filter from time to time, or use a
[counting Bloom filter](05-CountingBloomFilters.md).

## Next steps

- [Sizing a filter](03-SizingAFilter.md): the memory a filter uses, and what happens when you add more values.
- [Adding values](04-AddingValues.md): the types of values, and adding from several threads.
