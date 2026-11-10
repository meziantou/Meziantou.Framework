using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Http.Caching.InMemory;

namespace Meziantou.Framework.Http.Caching.Fuzz;

public static partial class HttpCachingFuzzTargets
{
    private static readonly DateTimeOffset BoundedStoreNow = new(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);
    private static readonly string[] BoundedStoreKeys = ["GET https://example.com/a", "GET https://example.com/b", "HEAD https://example.com/a", "GET https://example.com/c"];
    private static readonly string?[] BoundedStoreVariants = [null, "en", "fr"];

    /// <summary>
    /// The input is the limits of a bounded <see cref="InMemoryHttpCacheStore"/> (a maximum entry count and a maximum total
    /// size, each of them optional), then a sequence of operations: write an entry of a given size, for a key and a variant,
    /// that may be obsolete; read, remove or prune. After each operation, the store holds exactly the entries of a reference
    /// LRU cache, which evicts the least recently used entries until it is within the limits, and its accounting matches.
    /// </summary>
    private static void BoundedStore(ReadOnlySpan<byte> data)
    {
        var reader = new InputReader(data);
        var countByte = reader.ReadByte();
        var sizeByte = reader.ReadByte();
        long? maximumEntryCount = countByte % 9 is 0 ? null : countByte % 9;
        long? maximumTotalSize = sizeByte % 65 is 0 ? null : sizeByte % 65;
        if (maximumEntryCount is null && maximumTotalSize is null)
        {
            maximumEntryCount = 8;
        }

        var store = new InMemoryHttpCacheStore(new HttpCacheStoreLimits { MaximumEntryCount = maximumEntryCount, MaximumTotalSize = maximumTotalSize });

        // The least recently used entry comes first
        var model = new List<(string Key, string? Variant, int Size, bool Obsolete)>();

        while (!reader.IsEmpty)
        {
            var operation = reader.ReadByte();
            var key = BoundedStoreKeys[(operation >> 2) % BoundedStoreKeys.Length];
            switch (operation % 4)
            {
                case 0:
                    {
                        var argument = reader.ReadByte();
                        var variant = BoundedStoreVariants[argument % BoundedStoreVariants.Length];
                        var obsolete = (argument & 0x80) is not 0;
                        var size = reader.ReadByte() % 24;
                        store.SetEntryAsync(key, CreateBoundedStoreEntry(variant, size, obsolete), CancellationToken.None).AsTask().GetAwaiter().GetResult();

                        model.RemoveAll(item => item.Key == key && item.Variant == variant);
                        model.Add((key, variant, size, obsolete));
                        while (model.Count > (maximumEntryCount ?? long.MaxValue) || model.Sum(item => (long)item.Size) > (maximumTotalSize ?? long.MaxValue))
                        {
                            model.RemoveAt(0);
                        }

                        break;
                    }

                case 1:
                    {
                        var entries = store.GetEntriesAsync(key, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                        var expected = model.Where(item => item.Key == key).ToList();
                        FuzzAssert.Equal(expected.Count, entries.Count, $"The number of entries of {key}");

                        // Reading makes the entries of the key the most recently used, in the order the store holds them
                        foreach (var entry in entries)
                        {
                            var variant = GetVariant(entry);
                            var index = model.FindIndex(item => item.Key == key && item.Variant == variant);
                            FuzzAssert.True(index >= 0, $"The variant {FuzzAssert.Format(variant)} of {key} is not expected");
                            var item = model[index];
                            FuzzAssert.Equal(item.Size, entry.SerializedResponse.Length, "The size of the entry");
                            model.RemoveAt(index);
                            model.Add(item);
                        }

                        break;
                    }

                case 2:
                    store.RemoveEntriesAsync(key, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                    model.RemoveAll(item => item.Key == key);
                    break;

                default:
                    store.PruneObsoleteEntriesAsync(BoundedStoreNow, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                    model.RemoveAll(item => item.Obsolete);
                    break;
            }

            var usage = store.GetUsage();
            FuzzAssert.True(usage is not null, "A bounded store reports no usage");
            var (entryCount, totalSize) = usage!.Value;
            FuzzAssert.Equal(model.Count, entryCount, "The entry count of the store");
            FuzzAssert.Equal(model.Sum(item => (long)item.Size), totalSize, "The total size of the store");
            FuzzAssert.True(entryCount <= (maximumEntryCount ?? long.MaxValue), $"The store holds {entryCount} entries, more than {maximumEntryCount}");
            FuzzAssert.True(totalSize <= (maximumTotalSize ?? long.MaxValue), $"The store holds {totalSize} bytes, more than {maximumTotalSize}");

            var snapshot = store.GetSnapshot();
            FuzzAssert.Equal(model.Count, snapshot.Values.Sum(entries => entries.Length), "The number of stored entries");
            foreach (var (entryKey, variant, size, _) in model)
            {
                FuzzAssert.True(snapshot.TryGetValue(entryKey, out var entries) && entries.Any(entry => GetVariant(entry) == variant && entry.SerializedResponse.Length == size), $"The variant {FuzzAssert.Format(variant)} of {entryKey} is missing");
            }
        }

        static string? GetVariant(HttpCachePersistenceEntry entry) => entry.SecondaryKeyHeaders?.GetValueOrDefault("Accept-Language");
    }

    private static HttpCachePersistenceEntry CreateBoundedStoreEntry(string? variant, int size, bool obsolete)
    {
        var responseTime = BoundedStoreNow - TimeSpan.FromMinutes(2);
        return new HttpCachePersistenceEntry
        {
            RequestTime = responseTime,
            ResponseTime = responseTime,
            ResponseDate = responseTime,
            MaxAge = obsolete ? TimeSpan.Zero : TimeSpan.FromHours(1),
            SecondaryKeyHeaders = variant is null ? null : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Accept-Language"] = variant },
            SerializedResponse = new byte[size],
        };
    }

    private static IEnumerable<FuzzSeed> GetBoundedStoreSeeds()
    {
        // The limits, then operations: a write is followed by its variant and its size, the other operations take no argument
        yield return new FuzzSeed("count-and-size", InputReader.Create(2, 10, 0, 0, 3, 4, 0, 3, 1, 8, 0, 3, 12, 0, 9, 1, 5, 9, 13));
        yield return new FuzzSeed("variants", InputReader.Create(3, 0, 0, 0, 3, 0, 1, 3, 0, 2, 3, 1, 4, 0, 1, 3, 0, 4, 1, 3));
        yield return new FuzzSeed("remove-and-prune", InputReader.Create(4, 64, 0, 0x80, 5, 4, 0, 6, 2, 3, 0, 0, 1, 1));
        yield return new FuzzSeed("too-large", InputReader.Create(0, 5, 0, 0, 6, 1));
        yield return new FuzzSeed("unbounded-count", InputReader.Create(0, 20, 0, 0, 10, 4, 0, 10, 8, 0, 10, 12, 0, 10, 1, 5));
    }
}
