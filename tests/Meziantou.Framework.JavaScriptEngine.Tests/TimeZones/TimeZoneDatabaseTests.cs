using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Tests.TimeZones;

public sealed class TimeZoneDatabaseTests
{
    private static NamedTimeZone GetZone(string id) => TimeZoneDatabase.Default.GetTimeZone(id) ?? throw new InvalidOperationException("Unknown zone " + id);

    private static long ToEpochSeconds(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
    {
        return (CalendarMath.DaysFromCivil(year, month, day) * 86_400) + (hour * 3600) + (minute * 60) + second;
    }

    [Fact]
    public void Version()
    {
        Assert.Equal(TimeZoneDatabase.EmbeddedTzdataVersion, TimeZoneDatabase.Default.Version);
        Assert.Same(TimeZoneDatabase.Default, TimeZoneDatabase.Default);
    }

    [Theory]
    [InlineData("America/New_York", "America/New_York", "America/New_York")]
    [InlineData("america/new_york", "America/New_York", "America/New_York")]
    [InlineData("Asia/Calcutta", "Asia/Calcutta", "Asia/Kolkata")]
    [InlineData("ASIA/KOLKATA", "Asia/Kolkata", "Asia/Kolkata")]
    [InlineData("utc", "UTC", "UTC")]
    [InlineData("Etc/UTC", "Etc/UTC", "UTC")]
    [InlineData("Etc/GMT", "Etc/GMT", "UTC")]
    [InlineData("GMT", "GMT", "UTC")]
    [InlineData("Etc/Zulu", "Etc/Zulu", "UTC")]
    [InlineData("Etc/GMT+5", "Etc/GMT+5", "Etc/GMT+5")]
    [InlineData("Europe/Amsterdam", "Europe/Amsterdam", "Europe/Amsterdam")]
    [InlineData("US/Pacific", "US/Pacific", "America/Los_Angeles")]
    [InlineData("est5edt", "EST5EDT", "America/New_York")]
    [InlineData("CST6CDT", "CST6CDT", "America/Chicago")]
    [InlineData("MST7MDT", "MST7MDT", "America/Denver")]
    [InlineData("PST8PDT", "PST8PDT", "America/Los_Angeles")]
    [InlineData("CET", "CET", "Europe/Brussels")]
    public void TryGetTimeZone(string id, string expectedIdentifier, string expectedPrimary)
    {
        Assert.True(TimeZoneDatabase.Default.TryGetTimeZone(id, out var result));
        Assert.Equal(expectedIdentifier, result.Identifier);
        Assert.Equal(expectedPrimary, result.PrimaryIdentifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Europe/Unknown")]
    [InlineData("+05:30")]
    [InlineData("Factory")]
    [InlineData("America/New_York ")]
    public void TryGetTimeZone_Unknown(string id)
    {
        Assert.False(TimeZoneDatabase.Default.TryGetTimeZone(id, out _));
    }

    [Fact]
    public void AvailableIdentifiers()
    {
        var identifiers = TimeZoneDatabase.Default.AvailableIdentifiers;
        Assert.Contains("America/New_York", identifiers);
        Assert.Contains("Asia/Calcutta", identifiers);
        Assert.Contains("UTC", identifiers);
        Assert.DoesNotContain("Factory", identifiers);
        Assert.Equal(identifiers.Order(StringComparer.Ordinal), identifiers);
        foreach (var identifier in identifiers)
        {
            Assert.True(TimeZoneDatabase.Default.TryGetTimeZone(identifier, out var result));
            Assert.Equal(identifier, result.Identifier);
        }
    }

    [Theory]
    [InlineData("Romance Standard Time", "Europe/Paris")]
    [InlineData("eastern standard time", "America/New_York")]
    [InlineData("UTC", "Etc/UTC")]
    public void WindowsIds(string windowsId, string expected)
    {
        Assert.True(TimeZoneDatabase.Default.TryGetIanaIdFromWindowsId(windowsId, out var ianaId));
        Assert.Equal(expected, ianaId);
    }

    [Fact]
    public void NewYork_2007Rules()
    {
        var zone = GetZone("America/New_York");
        var start = ToEpochSeconds(2007, 3, 11, 7);
        var end = ToEpochSeconds(2007, 11, 4, 6);
        Assert.Equal(-5 * 3600, zone.GetOffsetSeconds(start - 1));
        Assert.Equal(-4 * 3600, zone.GetOffsetSeconds(start));
        Assert.Equal(-4 * 3600, zone.GetOffsetSeconds(end - 1));
        Assert.Equal(-5 * 3600, zone.GetOffsetSeconds(end));
        Assert.Equal("EDT", zone.GetLocalTimeType(start).Abbreviation);
        Assert.Equal("EST", zone.GetLocalTimeType(end).Abbreviation);
        Assert.Equal(start, zone.GetNextTransition(ToEpochSeconds(2007, 1, 1)));
        Assert.Equal(end, zone.GetNextTransition(start));
        Assert.Equal(start, zone.GetPreviousTransition(end));
        Assert.Equal(start, zone.GetPreviousTransition(start + 1));

        // 2006 still used the first Sunday of April
        Assert.Equal(ToEpochSeconds(2006, 4, 2, 7), zone.GetNextTransition(ToEpochSeconds(2006, 1, 1)));
    }

    [Fact]
    public void NewYork_RecurringRules()
    {
        var zone = GetZone("America/New_York");
        Assert.NotNull(zone.RecurringRule);

        // Second Sunday of March and first Sunday of November, computed from the rules
        Assert.Equal(ToEpochSeconds(2100, 3, 14, 7), zone.GetNextTransition(ToEpochSeconds(2100, 1, 1)));
        Assert.Equal(ToEpochSeconds(2100, 11, 7, 6), zone.GetNextTransition(ToEpochSeconds(2100, 3, 14, 7)));
        Assert.Equal(ToEpochSeconds(2099, 11, 1, 6), zone.GetPreviousTransition(ToEpochSeconds(2100, 1, 1)));
        Assert.Equal(-5 * 3600, zone.GetOffsetSeconds(ToEpochSeconds(275760, 1, 1)));
        Assert.Equal(-4 * 3600, zone.GetOffsetSeconds(ToEpochSeconds(9999, 7, 1)));

        // Transitions across the boundary between the explicit transitions and the rules
        var current = zone.GetNextTransition(ToEpochSeconds(2000, 1, 1))!.Value;
        for (var i = 0; i < 100; i++)
        {
            var next = zone.GetNextTransition(current);
            Assert.NotNull(next);
            Assert.True(next.Value > current);
            Assert.Equal(current, zone.GetPreviousTransition(next.Value));
            Assert.NotEqual(zone.GetOffsetSeconds(next.Value - 1), zone.GetOffsetSeconds(next.Value));
            current = next.Value;
        }
    }

    [Fact]
    public void London_BritishStandardTime()
    {
        var zone = GetZone("Europe/London");

        // From 1968-10-27 to 1971-10-31, the UK stayed on UTC+1 all year
        Assert.Equal(3600, zone.GetOffsetSeconds(ToEpochSeconds(1970, 1, 1)));
        Assert.Equal("BST", zone.GetLocalTimeType(ToEpochSeconds(1970, 1, 1)).Abbreviation);
        Assert.Equal(ToEpochSeconds(1971, 10, 31, 2), zone.GetNextTransition(ToEpochSeconds(1970, 1, 1)));
        Assert.Equal(0, zone.GetOffsetSeconds(ToEpochSeconds(1971, 12, 1)));
    }

    [Fact]
    public void LordHowe_HalfHourDaylightSavingTime()
    {
        var zone = GetZone("Australia/Lord_Howe");
        Assert.Equal((10 * 3600) + 1800, zone.GetOffsetSeconds(ToEpochSeconds(2024, 7, 1)));
        Assert.Equal(11 * 3600, zone.GetOffsetSeconds(ToEpochSeconds(2024, 1, 1)));
        Assert.Equal(11 * 3600, zone.GetOffsetSeconds(ToEpochSeconds(2300, 1, 1)));
        Assert.Equal((10 * 3600) + 1800, zone.GetOffsetSeconds(ToEpochSeconds(2300, 7, 1)));

        // The first Sunday of October at 2:00 local standard time, the clocks go forward by 30 minutes
        var transition = ToEpochSeconds(2024, 10, 5, 15, 30);
        Assert.Equal(transition, zone.GetNextTransition(ToEpochSeconds(2024, 7, 1)));
        var localSeconds = ToEpochSeconds(2024, 10, 6, 2, 15);
        Span<long> results = stackalloc long[2];
        Assert.Equal(0, zone.GetPossibleEpochSeconds(localSeconds, results));
    }

    [Fact]
    public void Apia_SkipsDecember30()
    {
        var zone = GetZone("Pacific/Apia");
        var transition = ToEpochSeconds(2011, 12, 30, 10);
        Assert.Equal(-10 * 3600, zone.GetOffsetSeconds(transition - 1));
        Assert.Equal(14 * 3600, zone.GetOffsetSeconds(transition));
        Assert.Equal(transition, zone.GetNextTransition(ToEpochSeconds(2011, 12, 1)));

        Span<long> results = stackalloc long[2];
        Assert.Equal(0, zone.GetPossibleEpochSeconds(ToEpochSeconds(2011, 12, 30, 12), results));
        Assert.Equal(1, zone.GetPossibleEpochSeconds(ToEpochSeconds(2011, 12, 31), results));
        Assert.Equal(transition, results[0]);
    }

    [Fact]
    public void Casablanca_RamadanRules()
    {
        var zone = GetZone("Africa/Casablanca");
        Assert.Equal(3600, zone.GetOffsetSeconds(ToEpochSeconds(2026, 2, 1)));
        Assert.Equal(0, zone.GetOffsetSeconds(ToEpochSeconds(2026, 3, 1)));
        Assert.Equal(3600, zone.GetOffsetSeconds(ToEpochSeconds(2026, 4, 1)));
        Assert.Equal("+01", zone.GetLocalTimeType(ToEpochSeconds(2026, 4, 1)).Abbreviation);
        Assert.Equal(ToEpochSeconds(2026, 2, 15, 2), zone.GetNextTransition(ToEpochSeconds(2026, 2, 1)));
    }

    [Fact]
    public void Dublin_NegativeSave()
    {
        var zone = GetZone("Europe/Dublin");
        var winter = zone.GetLocalTimeType(ToEpochSeconds(2024, 1, 1));
        Assert.Equal(new TimeZoneLocalTimeType(0, IsDaylightSavingTime: true, "GMT"), winter);
        var summer = zone.GetLocalTimeType(ToEpochSeconds(2024, 7, 1));
        Assert.Equal(new TimeZoneLocalTimeType(3600, IsDaylightSavingTime: false, "IST"), summer);
        Assert.Equal(new TimeZoneLocalTimeType(3600, IsDaylightSavingTime: false, "IST"), zone.GetLocalTimeType(ToEpochSeconds(2500, 7, 1)));
        Assert.Equal(ToEpochSeconds(2500, 3, 28, 1), zone.GetNextTransition(ToEpochSeconds(2500, 1, 1)));
    }

    [Fact]
    public void SubMinuteOffsets()
    {
        Assert.Equal(-((44 * 60) + 30), GetZone("Africa/Monrovia").GetOffsetSeconds(ToEpochSeconds(1950, 1, 1)));
        Assert.Equal((19 * 60) + 32, GetZone("Europe/Amsterdam").GetOffsetSeconds(ToEpochSeconds(1800, 1, 1)));
        Assert.Equal("LMT", GetZone("Europe/Amsterdam").GetLocalTimeType(ToEpochSeconds(1800, 1, 1)).Abbreviation);
        Assert.Equal(-((44 * 60) + 30) * 1_000_000_000L, GetZone("Africa/Monrovia").GetOffsetNanoseconds((Int128)ToEpochSeconds(1950, 1, 1) * 1_000_000_000));
    }

    [Fact]
    public void PossibleEpochSeconds_Overlap()
    {
        var zone = GetZone("America/New_York");

        // 2024-11-03 01:30 happens twice
        Span<long> results = stackalloc long[2];
        Assert.Equal(2, zone.GetPossibleEpochSeconds(ToEpochSeconds(2024, 11, 3, 1, 30), results));
        Assert.Equal(ToEpochSeconds(2024, 11, 3, 5, 30), results[0]);
        Assert.Equal(ToEpochSeconds(2024, 11, 3, 6, 30), results[1]);

        // 2024-03-10 02:30 does not exist
        Assert.Equal(0, zone.GetPossibleEpochSeconds(ToEpochSeconds(2024, 3, 10, 2, 30), results));
        Assert.Equal(1, zone.GetPossibleEpochSeconds(ToEpochSeconds(2024, 3, 10, 3, 30), results));
        Assert.Equal(ToEpochSeconds(2024, 3, 10, 7, 30), results[0]);
    }

    [Fact]
    public void TransitionsInNanoseconds()
    {
        var zone = GetZone("America/New_York");
        var transition = (Int128)ToEpochSeconds(2024, 3, 10, 7) * 1_000_000_000;
        Assert.Equal(transition, zone.GetNextTransition(transition - 1));
        Assert.NotEqual(transition, zone.GetNextTransition(transition));
        Assert.Equal(transition, zone.GetPreviousTransition(transition + 1));
        Assert.NotEqual(transition, zone.GetPreviousTransition(transition));
    }

    [Fact]
    public void FixedZones()
    {
        var zone = GetZone("Etc/GMT-14");
        Assert.Equal(14 * 3600, zone.GetOffsetSeconds(0));
        Assert.Null(zone.GetNextTransition(0));
        Assert.Null(zone.GetPreviousTransition(0));
        Assert.Equal("+14", zone.GetLocalTimeType(0).Abbreviation);
        Assert.Equal(0, GetZone("UTC").GetOffsetSeconds(long.MaxValue / 2));
    }

    [Fact]
    public void LastTransitionOfAZoneWithoutRules()
    {
        var zone = GetZone("Asia/Kolkata");
        Assert.Equal((5 * 3600) + 1800, zone.GetOffsetSeconds(ToEpochSeconds(2024, 1, 1)));
        Assert.Null(zone.GetNextTransition(ToEpochSeconds(2024, 1, 1)));
        Assert.NotNull(zone.GetPreviousTransition(ToEpochSeconds(2024, 1, 1)));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Test data, with fixed seeds")]
    public void CachedRuleTransitions_ConcurrentLookups()
    {
        // The zones cache the transitions computed from their rules. A zone decoded for a single lookup has an empty cache:
        // its answer is the reference for the shared zone, queried from several threads in random order.
        byte[] data;
        using (var stream = typeof(TimeZoneDatabase).Assembly.GetManifestResourceStream("Meziantou.Framework.JavaScriptEngine.TimeZones.tzdb.bin")!)
        using (var brotli = new System.IO.Compression.BrotliStream(stream, System.IO.Compression.CompressionMode.Decompress))
        using (var memory = new MemoryStream())
        {
            brotli.CopyTo(memory);
            data = memory.ToArray();
        }

        string[] ids = ["Europe/Paris", "America/New_York", "Australia/Lord_Howe", "America/Santiago", "Europe/Dublin", "Africa/Casablanca", "Asia/Kolkata"];
        var random = new Random(42);
        var queries = new List<(string Id, long Time, long LocalTime)>();
        foreach (var id in ids)
        {
            for (var i = 0; i < 150; i++)
            {
                // Historical offsets, recent and future years, the far future, and the seconds around a transition
                var time = (i % 5) switch
                {
                    0 => random.NextInt64(ToEpochSeconds(1850, 1, 1), ToEpochSeconds(1990, 1, 1)),
                    1 => random.NextInt64(ToEpochSeconds(1990, 1, 1), ToEpochSeconds(2060, 1, 1)),
                    2 => random.NextInt64(ToEpochSeconds(2060, 1, 1), ToEpochSeconds(275000, 1, 1)),
                    _ => (GetZone(id).GetNextTransition(random.NextInt64(ToEpochSeconds(2000, 1, 1), ToEpochSeconds(2100, 1, 1))) ?? 0) + random.Next(-2, 2),
                };
                queries.Add((id, time, time + random.Next(-86_400, 86_400)));
            }
        }

        var expected = queries.Select(query =>
        {
            var zone = TimeZoneDatabase.Load(data).GetTimeZone(query.Id)!;
            var next = TimeZoneDatabase.Load(data).GetTimeZone(query.Id)!.GetNextTransition(query.Time);
            var possible = new long[2];
            var possibleCount = TimeZoneDatabase.Load(data).GetTimeZone(query.Id)!.GetPossibleEpochSeconds(query.LocalTime, possible);
            return (Offset: zone.GetOffsetSeconds(query.Time), Next: next, Possible: string.Join(',', possible.Take(possibleCount)));
        }).ToArray();

        Parallel.For(0, 8, worker =>
        {
            var order = Enumerable.Range(0, queries.Count).ToArray();
            new Random(worker).Shuffle(order);
            var possible = new long[2];
            foreach (var index in order)
            {
                var (id, time, localTime) = queries[index];
                var zone = GetZone(id);
                Assert.Equal(expected[index].Offset, zone.GetOffsetSeconds(time));
                Assert.Equal(expected[index].Next, zone.GetNextTransition(time));
                var possibleCount = zone.GetPossibleEpochSeconds(localTime, possible);
                Assert.Equal(expected[index].Possible, string.Join(',', possible.Take(possibleCount)));
            }
        });

        // DST boundaries of the rules: the last Sunday of March and of October in Paris
        var paris = GetZone("Europe/Paris");
        Assert.Equal(3600, paris.GetOffsetSeconds(ToEpochSeconds(2124, 3, 26, 0, 59, 59)));
        Assert.Equal(7200, paris.GetOffsetSeconds(ToEpochSeconds(2124, 3, 26, 1)));
        Assert.Equal(7200, paris.GetOffsetSeconds(ToEpochSeconds(2124, 10, 29, 0, 59, 59)));
        Assert.Equal(3600, paris.GetOffsetSeconds(ToEpochSeconds(2124, 10, 29, 1)));
    }
}
