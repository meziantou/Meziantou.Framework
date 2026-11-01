---
title: Saving and loading filters
description: Save a filter to a stream and load it after a restart or in another process, without adding its values again.
---

A filter lives in memory. To keep it across restarts, or to send it to another process, save a snapshot of it to a
stream, and load the snapshot later: the values do not have to be added again.

## Save and load a filter

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=BloomFilter_Snapshot;format=md-fenced-code-block;dedent=true -->
```csharp
var filter = BloomFilter.CreateXXHash128(BloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));
filter.Add("alice@example.com");

// Save the filter to a stream, such as a FileStream, and load it after a restart
using var stream = new MemoryStream();
filter.Save(stream);

stream.Position = 0;
var restored = BloomFilter.Load<BloomFilterXXHash128>(stream);

Assert.True(restored.MayContain("alice@example.com"));
Assert.False(restored.MayContain("bob@example.com"));
```
<!-- endref -->

- [Save](cref:Meziantou.Framework.BloomFilters.BloomFilter.Save) writes the hash algorithm, the size and the bits of the filter. Any stream works: a `FileStream`, a
  `MemoryStream`, a network stream.
- [Load](cref:Meziantou.Framework.BloomFilters.BloomFilter.Load) creates a new filter with the same hash algorithm, size and bits, so it gives the same answers as the
  saved filter. `Load<BloomFilterXXHash128>` returns the concrete type, so its `AddRange` overloads are available.
  `Load` without a type argument returns a [BloomFilter](cref:Meziantou.Framework.BloomFilters.BloomFilter) of the hash algorithm of the snapshot.
- The stream is left after the snapshot, so a stream can hold other data after it.
- [SaveAsync](cref:Meziantou.Framework.BloomFilters.BloomFilter.SaveAsync) and [LoadAsync](cref:Meziantou.Framework.BloomFilters.BloomFilter.LoadAsync) do the same asynchronously.

The filter is copied through a small buffer: saving or loading a large filter does not allocate a second copy of it.
A snapshot is as large as the filter in memory, plus 40 bytes.

## Counting Bloom filters

[CountingBloomFilter](cref:Meziantou.Framework.BloomFilters.CountingBloomFilter) has the same methods. The counters are saved as they are, saturated ones included:

<!-- ref:../../tests/Meziantou.Framework.BloomFilters.Tests/BloomFilterTests.cs;region=CountingBloomFilter_Snapshot;format=md-fenced-code-block;dedent=true -->
```csharp
var filter = CountingBloomFilter.CreateXXHash128(CountingBloomFilterSize.CreateOptimalSize(expectedItemCount: 1000, falsePositiveProbability: 0.01));
filter.Add("alice@example.com");
filter.Add("alice@example.com");

using var stream = new MemoryStream();
await filter.SaveAsync(stream);

stream.Position = 0;
var restored = await CountingBloomFilter.LoadAsync<CountingBloomFilterXXHash128>(stream);

Assert.Equal(2, restored.GetEstimatedCount("alice@example.com"));
```
<!-- endref -->

A snapshot of a Bloom filter cannot be loaded as a counting Bloom filter, nor the reverse.

## Stop updating the filter while saving

Stop adding and removing values while a snapshot is written. Each bit or counter is read atomically, but a value
added during the write may be saved partially: some of its bits are in the snapshot, others are not, and the loaded
filter may report it as absent. Querying the filter while saving is safe.

## Compatibility

The snapshot records everything that decides where a value goes in the filter, and [Load](cref:Meziantou.Framework.BloomFilters.BloomFilter.Load) checks it:

- **Version**: the snapshot has a format version and a version of the way hashes become positions. A future version
  of the library that changes either rejects the older snapshots instead of loading a filter that would miss values:
  rebuild the filter from its values then.
- **Byte order**: values are hashed from their bytes in memory, so `42` or `"alice"` give other hashes on a big-endian
  machine. The snapshot records the byte order of the machine that wrote it, and a machine with another byte order
  rejects it. The values passed as `ReadOnlySpan<byte>` do not depend on it, but the snapshot is rejected all the same.
- **Hash algorithm**: `Load<T>` rejects a snapshot of another hash algorithm.

## Invalid snapshots

[Load](cref:Meziantou.Framework.BloomFilters.BloomFilter.Load) throws an `InvalidDataException` when the data is not a snapshot, is truncated or corrupted, or is not
compatible:

- The header (version, filter kind, hash algorithm, size, payload length) is validated before the filter is allocated.
  A seekable stream must also be long enough for the filter, so a corrupted size cannot make it allocate a huge filter.
  A non-seekable stream is only known to be truncated when its end is reached, after the filter is allocated.
- The snapshot ends with a checksum of its content, verified once the filter is read: a corrupted bit is detected.

Only load snapshots from a source you trust: a valid snapshot can describe a filter as large as the memory allows.
