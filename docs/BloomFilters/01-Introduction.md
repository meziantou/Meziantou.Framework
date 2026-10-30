---
title: Introduction
description: Check whether a value may be in a large set using a small, fixed amount of memory, with Bloom filters and counting Bloom filters.
---

`Meziantou.Framework.BloomFilters` provides Bloom filters: data structures that answer "is this value in the set?" using
a small, fixed amount of memory, whatever the size of the values.

The answer is either "no" or "maybe":

- **No false negatives.** When `MayContain` returns `false`, the value was never added.
- **Some false positives.** When `MayContain` returns `true`, the value was probably added, but it may not have been.
  You choose the probability of this error when you size the filter.

A Bloom filter does not store the values, so it cannot list them. Use it in front of an expensive lookup, to skip the
lookup for most of the values that are not there: a database query for a user name, a disk read for a key, a download
of a URL already seen.

## What you can do with it

- **Add values and test them** with `BloomFilter`. See [Getting started](02-GettingStarted.md).
- **Size a filter** for a number of values and a false positive probability, or set its size yourself. See
  [Sizing a filter](03-SizingAFilter.md).
- **Add values of several types**, many at once or from several threads. See [Adding values](04-AddingValues.md).
- **Remove values and count them** with `CountingBloomFilter`. See
  [Counting Bloom filters](05-CountingBloomFilters.md).
- **Choose the hash algorithm.** See [Hash algorithms](06-HashAlgorithms.md).

## Namespace

The types are in the [Meziantou.Framework.BloomFilters](cref:Meziantou.Framework.BloomFilters) namespace:

| Type | Contains |
| --- | --- |
| `BloomFilter` | The factory methods of Bloom filters, one per hash algorithm |
| `BloomFilterSize` | The size of a Bloom filter: its number of bits and of hash functions |
| `IBloomFilter` | The methods of a Bloom filter, whatever its hash algorithm |
| `CountingBloomFilter` | The factory methods of counting Bloom filters |
| `CountingBloomFilterSize` | The size of a counting Bloom filter: its number of counters and of hash functions |
| `ICountingBloomFilter` | The methods of a counting Bloom filter, whatever its hash algorithm |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and create your first filter.
- [Sizing a filter](03-SizingAFilter.md): choose the number of values and the false positive probability.
