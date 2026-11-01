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

// A value is identified by its bytes, not by its type. 42 as an int (4 bytes) and as a long (8 bytes)
// are different values, but 42 as an int and as a uint have the same bytes, so they are the same value
Assert.False(filter.MayContain(42L));
Assert.True(filter.MayContain(42u));

// A string is hashed from its UTF-16 characters, so it is a different value from its UTF-8 bytes
Assert.False(filter.MayContain("alice@example.com"));
```
<!-- endref -->

A value is hashed from its bytes, not from its type. Two values with the same bytes are the same value, and two
values with different bytes are different values:

- `42` as an `int` (4 bytes) and `42` as a `long` (8 bytes) are different values. Use the same type to add a value and
  to test it, especially with integer literals.
- Signed and unsigned integers of the same size are the same value when their bytes match: `42` and `42u`, or `-1`
  and `uint.MaxValue`. In a [counting Bloom filter](05-CountingBloomFilters.md), removing one removes the other.
- A number is hashed from its bytes in the byte order of the machine, so `42` and `BitConverter.GetBytes(42)` are
  the same value. A filter that hashes numbers gives different results on a big-endian machine.
- A `string` is hashed from its UTF-16 characters in the byte order of the machine: it is the same value as these
  bytes, and a different value from its UTF-8 bytes.
- The comparison is ordinal: `"Alice"` and `"alice"` are different values. Normalize the values (case, Unicode form,
  whitespace) before adding and testing them when they should match.

When values of different kinds share a filter, for instance user ids and order ids, add a prefix that identifies the
kind. Write numbers with an explicit byte order when the filter is shared between machines:

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=BloomFilter_KeyPrefix;format=md-fenced-code-block;dedent=true -->
```csharp
var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));

// Prefix the value with its kind, and write numbers with an explicit byte order,
// so user 42 and order 42 are different values on every machine
filter.Add(CreateKey(kind: 1, id: 42)); // user 42

Assert.True(filter.MayContain(CreateKey(kind: 1, id: 42)));
Assert.False(filter.MayContain(CreateKey(kind: 2, id: 42))); // order 42

static byte[] CreateKey(byte kind, int id)
{
    var key = new byte[5];
    key[0] = kind;
    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(key.AsSpan(1), id);
    return key;
}
```
<!-- endref -->

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
