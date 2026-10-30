---
title: Adding values
description: Add numbers, GUIDs, strings and bytes, add many values in one call, and update a filter from several threads.
---

## Supported values

`Add` and `MayContain` have an overload for each supported type: `int`, `uint`, `long`, `ulong`, `Int128`, `UInt128`,
`Guid`, `string` and `ReadOnlySpan<byte>`. Other values go through `ReadOnlySpan<byte>`: pass their bytes, for
instance their UTF-8 encoding or a binary key.

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=BloomFilter_Values;format=md-fenced-code-block;dedent=true -->
```csharp
var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));

var userId = new Guid("b2c4a0a6-4f5e-4d58-9a43-5d1c37bf0f4e");
filter.Add(userId);
filter.Add(42);
filter.Add("alice@example.com"u8); // the bytes of a value

// Add several 64-bit values in one call
filter.AddRange(1L, 2L, 3L);

Assert.True(filter.MayContain(userId));
Assert.True(filter.MayContain(42));
Assert.True(filter.MayContain("alice@example.com"u8));
Assert.True(filter.MayContain(2L));

// The type is part of the value: 42 as an int and 42 as a long are different values,
// like a string and its UTF-8 bytes
Assert.False(filter.MayContain(42L));
Assert.False(filter.MayContain("alice@example.com"));
```
<!-- endref -->

A value is hashed from its bytes, so its type is part of it:

- `42` as an `int` (4 bytes) and `42` as a `long` (8 bytes) are different values. Use the same type to add a value and
  to test it, especially with integer literals.
- A `string` is hashed from its UTF-16 characters, so it is a different value from its UTF-8 bytes.
- The comparison is ordinal: `"Alice"` and `"alice"` are different values. Normalize the values (case, Unicode form,
  whitespace) before adding and testing them when they should match.

## Add many values

`AddRange` adds several values in one call. It exists on the filters that hash 64-bit or 32-bit integers natively:

| Filter | `AddRange` overloads |
| --- | --- |
| [BloomFilterXXHash128](cref:Meziantou.Framework.BloomFilters.BloomFilterXXHash128), [BloomFilterXXHash64](cref:Meziantou.Framework.BloomFilters.BloomFilterXXHash64) | `long`, `ulong` |
| [BloomFilterXXHash32](cref:Meziantou.Framework.BloomFilters.BloomFilterXXHash32) | `int`, `uint` |
| [CountingBloomFilterXXHash128](cref:Meziantou.Framework.BloomFilters.CountingBloomFilterXXHash128), [CountingBloomFilterXXHash64](cref:Meziantou.Framework.BloomFilters.CountingBloomFilterXXHash64) | `long`, `ulong` |
| [CountingBloomFilterXXHash32](cref:Meziantou.Framework.BloomFilters.CountingBloomFilterXXHash32) | `int`, `uint` |

The factory methods return these concrete types ([BloomFilter.CreateXXHash128](cref:Meziantou.Framework.BloomFilters.BloomFilter.CreateXXHash128) returns a [BloomFilterXXHash128](cref:Meziantou.Framework.BloomFilters.BloomFilterXXHash128)), so
`AddRange` is available on the variable they initialize. For the other filters and types, call `Add` in a loop.

## Use a filter from several threads

`Add`, `MayContain` and [GetEstimateCount](cref:Meziantou.Framework.BloomFilters.BloomFilter.GetEstimateCount) can be called from several threads at the same time, without a lock: the
bits are set with atomic operations, so no value added concurrently is lost.

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=BloomFilter_Concurrency;format=md-fenced-code-block;dedent=true -->
```csharp
var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 100_000, falsePositiveProbability: 0.01));

// Add values from several threads at the same time, without a lock
Parallel.For(0, 100_000, i => filter.Add(i));

for (var i = 0; i < 100_000; i++)
{
    Assert.True(filter.MayContain(i));
}
```
<!-- endref -->

A `MayContain` that runs at the same time as the `Add` of the same value can return either answer. Once `Add` has
returned, `MayContain` returns `true` for that value. The counters of a
[counting Bloom filter](05-CountingBloomFilters.md) are updated atomically too.

## Write code that works with any filter

The filters implement [IBloomFilter](cref:Meziantou.Framework.BloomFilters.IBloomFilter), and the counting filters [ICountingBloomFilter](cref:Meziantou.Framework.BloomFilters.ICountingBloomFilter), which declare `Add` and
`MayContain` for every supported type. Declare your fields and parameters with these interfaces when the hash algorithm
is a configuration choice.
