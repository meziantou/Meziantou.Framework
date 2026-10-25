using System.Net;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Http.Caching.Fuzz;

public static partial class HttpCachingFuzzTargets
{
    private static readonly TimeSpan[] Offsets = [TimeSpan.Zero, TimeSpan.FromHours(1), TimeSpan.FromHours(-1), TimeSpan.FromHours(14), TimeSpan.FromHours(-14), new(5, 30, 0), new(-9, -30, 0), new(13, 45, 0)];
    private static readonly string[] SecondaryKeyHeaderNames = ["Accept-Language", "accept-language", "ACCEPT-LANGUAGE", "Accept", "X-Custom", "Cookie", "Authorization"];
    private static readonly string[] SecondaryKeyHeaderValues = ["en", "fr", "\0", "", "en, fr", "*"];

    // The payloads are valid, so that a lookup only depends on the metadata of the entry
    private static readonly SeededPayload[] SeededPayloads =
    [
        CreateSeededPayload(HttpStatusCode.OK, "seeded:ok", []),
        CreateSeededPayload(HttpStatusCode.NotFound, "seeded:not-found", []),
        CreateSeededPayload(HttpStatusCode.OK, "seeded:headers", [("Cache-Control", "max-age=60"), ("ETag", "\"v1\""), ("Vary", "Accept-Language")]),
        CreateSeededPayload(HttpStatusCode.MovedPermanently, "seeded:moved", [("Location", "/b")]),
    ];

    /// <summary>
    /// The input is the metadata of a <see cref="HttpCachePersistenceEntry"/> as an external store could return it: any
    /// time (offsets, <see cref="DateTimeOffset.MinValue"/>, <see cref="DateTimeOffset.MaxValue"/>), any duration
    /// (negative, <see cref="TimeSpan.MinValue"/>, <see cref="TimeSpan.MaxValue"/>), secondary-key headers with either
    /// comparer, any <c>No-Vary-Search</c> value and query. Then the time of the evaluation, a change of the secondary key
    /// and an offset. Nothing throws; the freshness lifetime and the current age are not negative and agree with
    /// <see cref="HttpCachePersistenceEntry.IsObsolete"/>, which turns true exactly at the stale time; a clone, the entry
    /// with its times in another offset, and the entry read back by the cache give the same results; and
    /// <see cref="HttpCachePersistenceEntry.HasSameSecondaryKey"/> is symmetric and implies equal secondary-key hashes.
    /// </summary>
    private static void PersistenceEntry(ReadOnlySpan<byte> data)
    {
        var reader = new InputReader(data);
        var payload = FuzzInput.Pick(SeededPayloads, reader.ReadByte());
        var entry = ReadPersistenceEntry(reader);
        entry.SerializedResponse = payload.Payload;
        var now = ReadDate(reader);
        var mutation = reader.ReadByte();
        var offset = FuzzInput.Pick(Offsets, reader.ReadByte());

        var results = Evaluate(entry, now);
        FuzzAssert.True(results.FreshnessLifetime >= TimeSpan.Zero, $"The freshness lifetime is negative: {results.FreshnessLifetime}");
        FuzzAssert.True(results.CurrentAge >= TimeSpan.Zero, $"The current age is negative: {results.CurrentAge}");
        FuzzAssert.Equal(TimeSpan.Zero, entry.GetStaleTime().Offset, "The stale time is not in UTC");
        FuzzAssert.Equal(entry.IsUnusableWhenStale && results.CurrentAge >= results.FreshnessLifetime, results.IsObsolete, "IsObsolete disagrees with the current age and the freshness lifetime");
        FuzzAssert.True(HttpCachePersistenceEntry.HasSameSecondaryKey(entry, entry), "An entry does not have the same secondary key as itself");

        CheckStaleTime(entry, results);

        var clone = entry.Clone();
        AssertSamePersistenceEntry(entry, clone);
        FuzzAssert.Equal(results, Evaluate(clone, now), "The clone gives other results");
        FuzzAssert.True(HttpCachePersistenceEntry.HasSameSecondaryKey(entry, clone) && HttpCachePersistenceEntry.HasSameSecondaryKey(clone, entry), "The clone does not have the same secondary key");

        // The times are instants: their offsets do not matter
        var shifted = entry.Clone();
        shifted.RequestTime = ToOffset(shifted.RequestTime, offset);
        shifted.ResponseTime = ToOffset(shifted.ResponseTime, offset);
        shifted.ResponseDate = ToOffset(shifted.ResponseDate, offset);
        shifted.Expires = shifted.Expires is { } expires ? ToOffset(expires, offset) : null;
        shifted.LastModified = shifted.LastModified is { } lastModified ? ToOffset(lastModified, offset) : null;
        FuzzAssert.Equal(results, Evaluate(shifted, ToOffset(now, offset)), $"Expressing the times with the offset {offset} changes the results");

        // The cache reads the entry with the same times
        var cacheEntry = CacheEntry.FromPersistenceEntry(entry);
        FuzzAssert.Equal(results.FreshnessLifetime, cacheEntry.FreshnessLifetime, "The cache computes another freshness lifetime");
        FuzzAssert.Equal(results.CurrentAge, cacheEntry.CalculateCurrentAge(now), "The cache computes another current age");
        FuzzAssert.Equal(results with { SecondaryKeyHash = "" }, Evaluate(cacheEntry.ToPersistenceEntry(), now) with { SecondaryKeyHash = "" }, "The entry written back by the cache gives other results");

        var (other, expectSame) = ChangeSecondaryKey(clone, mutation, reader);
        var isSame = HttpCachePersistenceEntry.HasSameSecondaryKey(entry, other);
        FuzzAssert.Equal(isSame, HttpCachePersistenceEntry.HasSameSecondaryKey(other, entry), "HasSameSecondaryKey is not symmetric");
        if (expectSame)
        {
            FuzzAssert.True(isSame, $"The change {mutation % 10} of the secondary key should keep it the same");
        }

        if (isSame)
        {
            FuzzAssert.Equal(results.SecondaryKeyHash, other.ComputeSecondaryKeyHash(), "Two entries with the same secondary key have different secondary-key hashes");
        }
    }

    /// <summary>
    /// <see cref="HttpCachePersistenceEntry.GetStaleTime"/> is the first time at which an entry that is unusable when stale
    /// is obsolete, clamped to the range of <see cref="DateTimeOffset"/>.
    /// </summary>
    private static void CheckStaleTime(HttpCachePersistenceEntry entry, EntryResults results)
    {
        if (!entry.IsUnusableWhenStale)
            return;

        var staleTime = entry.GetStaleTime();
        if (staleTime < DateTimeOffset.MaxValue)
        {
            FuzzAssert.True(entry.IsObsolete(staleTime), $"The entry is not obsolete at its stale time {staleTime:O}");
        }

        if (staleTime > DateTimeOffset.MinValue && results.FreshnessLifetime > TimeSpan.Zero)
        {
            var before = staleTime - TimeSpan.FromTicks(1);
            FuzzAssert.False(entry.IsObsolete(before), $"The entry is already obsolete before its stale time {staleTime:O}");
        }
    }

    private static EntryResults Evaluate(HttpCachePersistenceEntry entry, DateTimeOffset now)
    {
        return new EntryResults(entry.GetFreshnessLifetime(), entry.GetCurrentAge(now), entry.GetStaleTime(), entry.IsObsolete(now), entry.ComputeSecondaryKeyHash());
    }

    private static void AssertSamePersistenceEntry(HttpCachePersistenceEntry expected, HttpCachePersistenceEntry actual)
    {
        FuzzAssert.Equal(Describe(expected), Describe(actual), "The clone differs from the entry");
        FuzzAssert.Equal(expected.SecondaryKeyHeaders?.Comparer, actual.SecondaryKeyHeaders?.Comparer, "The clone compares the secondary-key headers differently");
        if (expected.SecondaryKeyHeaders is not null)
        {
            FuzzAssert.False(ReferenceEquals(expected.SecondaryKeyHeaders, actual.SecondaryKeyHeaders), "The clone shares the secondary-key headers");
        }

        static string Describe(HttpCachePersistenceEntry entry)
        {
            var headers = entry.SecondaryKeyHeaders is null ? "null" : string.Join(", ", entry.SecondaryKeyHeaders.Select(header => FuzzAssert.Format(header.Key) + "=" + FuzzAssert.Format(header.Value)));
            return string.Join('\n',
                entry.SecondaryKeyMatchNone, headers, FuzzAssert.Format(entry.NoVarySearch), FuzzAssert.Format(entry.NormalizedQuery),
                entry.RequestTime.ToString("O", CultureInfo.InvariantCulture), entry.ResponseTime.ToString("O", CultureInfo.InvariantCulture), entry.ResponseDate.ToString("O", CultureInfo.InvariantCulture),
                entry.AgeValue.Ticks, entry.MaxAge?.Ticks, entry.SharedMaxAge?.Ticks, entry.Expires?.ToString("O", CultureInfo.InvariantCulture),
                entry.MustRevalidate, entry.ProxyRevalidate, entry.ResponseNoCache, entry.Public, entry.Private, entry.NoTransform, entry.Immutable,
                entry.StaleIfError?.Ticks, FuzzAssert.Format(entry.ETag), entry.LastModified?.ToString("O", CultureInfo.InvariantCulture), Convert.ToHexString(entry.SerializedResponse.Span));
        }
    }

    /// <summary>Returns a copy of the entry whose secondary key is changed, and whether the change keeps the secondary key the same.</summary>
    private static (HttpCachePersistenceEntry Entry, bool ExpectSame) ChangeSecondaryKey(HttpCachePersistenceEntry entry, byte mutation, InputReader reader)
    {
        var headers = entry.SecondaryKeyHeaders;
        switch (mutation % 10)
        {
            case 0:
                return (entry, true);

            case 1 when headers is not null:
                // Another insertion order
                entry.SecondaryKeyHeaders = CopyHeaders(headers.Reverse(), headers.Comparer);
                return (entry, entry.SecondaryKeyHeaders.Count == headers.Count);

            case 2 when headers is not null:
                // The other comparer, which can merge the names that only differ by case
                entry.SecondaryKeyHeaders = CopyHeaders(headers, ReferenceEquals(headers.Comparer, StringComparer.OrdinalIgnoreCase) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
                return (entry, entry.SecondaryKeyHeaders.Count == headers.Count);

            case 3 when headers is not null:
                // Header names are case-insensitive
                entry.SecondaryKeyHeaders = CopyHeaders(headers.Select(header => KeyValuePair.Create(SwapAsciiCase(header.Key), header.Value)), headers.Comparer);
                return (entry, false);

            case 4 when headers is { Count: > 0 }:
                entry.SecondaryKeyHeaders = CopyHeaders(headers, headers.Comparer);
                entry.SecondaryKeyHeaders[headers.Keys.First()] = ReadSecondaryKeyHeaderValue(reader);
                return (entry, false);

            case 5:
                entry.SecondaryKeyMatchNone = !entry.SecondaryKeyMatchNone;
                return (entry, false);

            case 6:
                entry.NoVarySearch = entry.NoVarySearch is null ? reader.ReadLine() : null;
                return (entry, false);

            case 7:
                entry.NormalizedQuery = (reader.ReadByte() & 1) is 0 ? null : reader.ReadLine();
                return (entry, false);

            case 8:
                // An empty set of headers is no headers
                if (headers is { Count: 0 })
                {
                    entry.SecondaryKeyHeaders = null;
                    return (entry, true);
                }

                if (headers is null)
                {
                    entry.SecondaryKeyHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    return (entry, true);
                }

                entry.SecondaryKeyHeaders = CopyHeaders(headers.Skip(1), headers.Comparer);
                return (entry, false);

            default:
                entry.SecondaryKeyHeaders = headers is null ? new Dictionary<string, string>(StringComparer.Ordinal) : CopyHeaders(headers, headers.Comparer);
                entry.SecondaryKeyHeaders.TryAdd(ReadSecondaryKeyHeaderName(reader), ReadSecondaryKeyHeaderValue(reader));
                return (entry, false);
        }

        static Dictionary<string, string> CopyHeaders(IEnumerable<KeyValuePair<string, string>> headers, IEqualityComparer<string> comparer)
        {
            var result = new Dictionary<string, string>(comparer);
            foreach (var (key, value) in headers)
            {
                result.TryAdd(key, value);
            }

            return result;
        }

        static string SwapAsciiCase(string value)
        {
            return string.Create(value.Length, value, static (span, value) =>
            {
                for (var i = 0; i < value.Length; i++)
                {
                    span[i] = value[i] switch
                    {
                        >= 'a' and <= 'z' => (char)(value[i] - 32),
                        >= 'A' and <= 'Z' => (char)(value[i] + 32),
                        _ => value[i],
                    };
                }
            });
        }
    }

    /// <summary>Reads an entry: 3 bytes of flags, the times, the optional values, the secondary-key headers, and the optional texts.</summary>
    private static HttpCachePersistenceEntry ReadPersistenceEntry(InputReader reader)
    {
        var flags = reader.ReadByte() | (reader.ReadByte() << 8) | (reader.ReadByte() << 16);
        bool Has(int bit) => (flags & (1 << bit)) is not 0;

        var entry = new HttpCachePersistenceEntry
        {
            SecondaryKeyMatchNone = Has(0),
            MustRevalidate = Has(5),
            ProxyRevalidate = Has(6),
            ResponseNoCache = Has(7),
            Public = Has(8),
            Private = Has(9),
            NoTransform = Has(10),
            Immutable = Has(11),
            RequestTime = ReadDate(reader),
            ResponseTime = ReadDate(reader),
            ResponseDate = ReadDate(reader),
            AgeValue = ReadTimeSpan(reader),
        };

        entry.MaxAge = Has(12) ? ReadTimeSpan(reader) : null;
        entry.SharedMaxAge = Has(13) ? ReadTimeSpan(reader) : null;
        entry.Expires = Has(14) ? ReadDate(reader) : null;
        entry.StaleIfError = Has(15) ? ReadTimeSpan(reader) : null;
        entry.LastModified = Has(17) ? ReadDate(reader) : null;

        if (Has(1))
        {
            // A store that deserializes the entry gets a dictionary with the default comparer
            entry.SecondaryKeyHeaders = new Dictionary<string, string>(Has(2) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var count = reader.ReadByte() % 4;
            for (var i = 0; i < count; i++)
            {
                entry.SecondaryKeyHeaders.TryAdd(ReadSecondaryKeyHeaderName(reader), ReadSecondaryKeyHeaderValue(reader));
            }
        }

        entry.NoVarySearch = Has(3) ? reader.ReadLine() : null;
        entry.NormalizedQuery = Has(4) ? reader.ReadLine() : null;
        entry.ETag = Has(16) ? reader.ReadLine() : null;
        return entry;
    }

    private static string ReadSecondaryKeyHeaderName(InputReader reader)
    {
        var value = reader.ReadByte();
        return value is 0xFF ? reader.ReadLine() : FuzzInput.Pick(SecondaryKeyHeaderNames, value);
    }

    private static string ReadSecondaryKeyHeaderValue(InputReader reader)
    {
        var value = reader.ReadByte();
        return value is 0xFF ? reader.ReadLine() : FuzzInput.Pick(SecondaryKeyHeaderValues, value);
    }

    /// <summary>
    /// Reads a time: the low nibble selects a remarkable value, a number of seconds from <see cref="StartTime"/>, or any
    /// number of ticks; the high nibble selects the offset, when the time can be expressed with it.
    /// </summary>
    private static DateTimeOffset ReadDate(InputReader reader)
    {
        var selector = reader.ReadByte();
        var value = (selector & 0x0F) switch
        {
            0 => StartTime,
            1 => StartTime.AddSeconds(-1),
            2 => StartTime.AddSeconds(1),
            3 => StartTime.AddHours(-1),
            4 => StartTime.AddHours(1),
            5 => StartTime.AddDays(-1),
            6 => DateTimeOffset.UnixEpoch,
            7 => DateTimeOffset.MinValue,
            8 => DateTimeOffset.MaxValue,
            9 => DateTimeOffset.MinValue.AddTicks(1),
            10 => DateTimeOffset.MaxValue.AddTicks(-1),
            11 => FromUtcTicks(StartTime.UtcTicks + (reader.ReadInt64(2) * TimeSpan.TicksPerSecond)),
            12 => FromUtcTicks(StartTime.UtcTicks + (reader.ReadInt64(4) * TimeSpan.TicksPerSecond)),
            _ => new DateTimeOffset((long)((ulong)reader.ReadInt64(8) % ((ulong)DateTime.MaxValue.Ticks + 1)), TimeSpan.Zero),
        };

        return ToOffset(value, FuzzInput.Pick(Offsets, (byte)(selector >> 4)));

        static DateTimeOffset FromUtcTicks(long ticks) => new(Math.Clamp(ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks), TimeSpan.Zero);
    }

    /// <summary>Reads a duration: the low nibble selects a remarkable value, a number of seconds, or any number of ticks.</summary>
    private static TimeSpan ReadTimeSpan(InputReader reader)
    {
        return (reader.ReadByte() & 0x0F) switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromTicks(1),
            2 => TimeSpan.FromTicks(-1),
            3 => TimeSpan.FromSeconds(1),
            4 => TimeSpan.FromSeconds(-1),
            5 => TimeSpan.FromSeconds(60),
            6 => TimeSpan.FromHours(1),
            7 => TimeSpan.FromDays(1),
            8 => TimeSpan.FromDays(3650),
            9 => TimeSpan.MaxValue,
            10 => TimeSpan.MinValue,
            11 => TimeSpan.MaxValue - TimeSpan.FromTicks(1),
            12 => TimeSpan.FromSeconds(reader.ReadInt64(2)),
            13 => TimeSpan.FromSeconds(reader.ReadInt64(4)),
            _ => TimeSpan.FromTicks(reader.ReadInt64(8)),
        };
    }

    /// <summary>Expresses the time with the offset, or keeps it as is when the time cannot be expressed with it.</summary>
    private static DateTimeOffset ToOffset(DateTimeOffset value, TimeSpan offset)
    {
        var localTicks = value.UtcTicks + offset.Ticks;
        return localTicks >= DateTime.MinValue.Ticks && localTicks <= DateTime.MaxValue.Ticks ? value.ToOffset(offset) : value;
    }

    private static SeededPayload CreateSeededPayload(HttpStatusCode statusCode, string body, (string Name, string Value)[] headers)
    {
        using var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return new SeededPayload(statusCode, Encoding.UTF8.GetBytes(body), Serialize(response));
    }

    private static IEnumerable<FuzzSeed> GetPersistenceEntrySeeds()
    {
        // The payload, the entry (flags, times, optional values, headers, texts), the time of the evaluation, the change
        // of the secondary key, and the offset.
        // Flags: 0 Vary: *, 1 headers, 2 case-insensitive headers, 3 No-Vary-Search, 4 normalized query, 5-11 directives,
        // 12 max-age, 13 s-maxage, 14 Expires, 15 stale-if-error, 16 ETag, 17 Last-Modified.
        // Times: 0 start, 1/2 -/+1s, 3/4 -/+1h, 5 -1d, 6 Unix epoch, 7 min, 8 max, 9 min+1 tick, 10 max-1 tick, 11/12 seconds
        // from the start (2 or 4 bytes), 13+ ticks (8 bytes); the high nibble is the offset.
        // Durations: 0 zero, 1/2 +/-1 tick, 3/4 +/-1s, 5 60s, 6 1h, 7 1d, 8 3650d, 9 max, 10 min, 11 max-1 tick, 12/13
        // seconds (2 or 4 bytes), 14+ ticks (8 bytes).
        yield return EntrySeed("max-age", 0, 0, 0x10, 0, 0, 0, 0, 0, 5, 2, 0, 1);
        yield return EntrySeed("max-age-stale", 0, 0, 0x10, 0, 0, 0, 0, 0, 5, 0x0C, 120, 0, 0, 0, 0, 2);
        yield return EntrySeed("vary", 2, 0x06, 0x10, 0, 0, 0, 0, 0, 5, 2, 0, 1, 3, 1, 0x13, 3);
        yield return EntrySeed("vary-case-variants", 0, 0x02, 0x10, 0, 0, 0, 0, 0, 5, 3, 0, 0, 1, 1, 6, 0, 2, 4);
        yield return EntrySeed("vary-custom-name", 0, 0x06, 0x10, 0, 0, 0, 0, 0, 5, 1, 0xFF, "X-Ünicode", 0xFF, "value", 2, 3, 5);
        yield return EntrySeed("vary-star", 0, 0x01, 0, 0, 0, 0, 0, 0, 0, 5, 6);
        yield return EntrySeed("extreme-age", 0, 0, 0x10, 0, 7, 8, 0, 9, 9, 7, 1, 3);
        yield return EntrySeed("extreme-age-max-response-time", 0, 0, 0x10, 0, 10, 10, 10, 9, 9, 0, 0, 0);
        yield return EntrySeed("negative-durations", 0, 0, 0xB0, 0, 4, 0, 3, 10, 10, 10, 10, 2, 0, 0);
        yield return EntrySeed("negative-ticks", 0, 0, 0x10, 0, 0, 0, 0, 0x0E, -1L, 0x0E, long.MinValue, 0x0D, 1L << 40, 0, 0);
        yield return EntrySeed("expires", 1, 0, 0x40, 0, 0, 0, 0, 0, 4, 3, 7, 4);
        yield return EntrySeed("expires-min", 1, 0, 0x40, 0x01, 0, 0, 0, 0, 7, "\"v1\"", 0, 0, 0);
        yield return EntrySeed("expires-extremes", 1, 0, 0x40, 0, 7, 8, 8, 0, 7, 8, 0, 0);
        yield return EntrySeed("heuristic", 0, 0, 0, 0x02, 0, 0, 0, 0, 7, 2, 0, 0);
        yield return EntrySeed("heuristic-future", 0, 0, 0, 0x02, 0, 0, 7, 0, 8, 2, 0, 0);
        yield return EntrySeed("stale-if-error", 0, 0, 0x90, 0, 0, 0, 0, 0, 3, 9, 4, 0, 0);
        yield return EntrySeed("no-vary-search", 0, 0x18, 0x10, 0, 0, 0, 0, 0, 5, "params", "", 0, 6, 0);
        yield return EntrySeed("no-vary-search-query", 0, 0x18, 0x10, 0, 0, 0, 0, 0, 5, "key-order, except=(\"a\")", "a=1&b=2", 0, 7, 1, "b=2&a=1", 0);
        yield return EntrySeed("offsets", 0, 0, 0x50, 0x02, 0x31, 0x42, 0x50, 0, 5, 0x67, 0x7A, 0x14, 0, 4);
        yield return EntrySeed("seconds", 0, 0, 0x10, 0, 0x0B, 0xFF, 0xFF, 0x0C, 0, 0, 0, 0x80, 0x0B, 0x10, 0x0E, 0, 0x0D, 0x00, 0x00, 0x00, 0x80, 0x0C, 0x10, 0x0E, 0, 0);
        yield return EntrySeed("all-flags", 3, 0xFF, 0xFF, 0x03, 0, 1, 2, 3, 5, 6, 4, 7, 1, 3, 2, 1, 3, 3, 4, 4, "params", "", "\"etag\"", 2, 9, 0);
    }

    private static FuzzSeed EntrySeed(string name, params object[] parts) => new("entry-" + name, InputReader.Create(parts));

    private sealed record SeededPayload(HttpStatusCode StatusCode, byte[] Body, byte[] Payload);

    private sealed record EntryResults(TimeSpan FreshnessLifetime, TimeSpan CurrentAge, DateTimeOffset StaleTime, bool IsObsolete, string SecondaryKeyHash);
}
