---
title: Hash algorithms
description: Choose the hash algorithm of a filter, and the limits of the 32-bit algorithms and of Adler-32.
---

Each hash algorithm has its factory method, on [BloomFilter](cref:Meziantou.Framework.BloomFilters.BloomFilter) and on [CountingBloomFilter](cref:Meziantou.Framework.BloomFilters.CountingBloomFilter):

| Factory method | Hash | Width |
| --- | --- | --- |
| `CreateXXHash128` | XXH128 | 128 bits |
| `CreateXXHash3` | XXH3 | 64 bits |
| `CreateXXHash64` | XXH64 | 64 bits |
| `CreateCrc64` | CRC-64 | 64 bits |
| `CreateXXHash32` | XXH32 | 32 bits |
| `CreateCrc32` | CRC-32 | 32 bits |
| `CreateAdler32` | Adler-32 | 32 bits |

`CreateXXHash128` is the fastest algorithm and the recommended default.

The hashes are not seeded: the same value always sets the same bits in a filter of the same size.

## The limit of the 32-bit algorithms

A 32-bit algorithm cannot tell apart two values with the same 32-bit hash: whatever the size of the filter, the false
positive rate cannot go below about `itemCount / 2^32`, about 2.3% for 100 million values. Use a 64-bit or 128-bit
algorithm for large filters.

## Avoid Adler-32

Adler-32 distributes short inputs poorly: many small integers or short strings share the same checksum. A filter that
uses it has a much higher false positive rate than the one it was sized for, with sequential integers and short strings
in particular. Use another algorithm unless you need Adler-32 specifically, for instance to match another system.
