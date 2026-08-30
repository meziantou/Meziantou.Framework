#pragma warning disable MA0011 // IFormatProvider is missing
using System.Collections;
using System.Resources;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.Tests;

public class RelativeDateTests
{
    [Fact]
    public void DefaultDate_ToString()
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new RelativeDate(default, timeProvider).ToString());
    }

    [Fact]
    public void DefaultInstance_ToString_FallsBackToTheSystemTimeProvider()
    {
        // A default instance carries no TimeProvider and holds DateTime.MinValue, so it renders as a very distant past date
        var result = default(RelativeDate).ToString(format: null, CultureInfo.InvariantCulture);
        Assert.EndsWith(" years ago", result);
    }

    [Fact]
    public void DefaultInstance_FromArray_ToString_FallsBackToTheSystemTimeProvider()
    {
        var dates = new RelativeDate[1];
        var result = dates[0].ToString(format: null, CultureInfo.InvariantCulture);
        Assert.EndsWith(" years ago", result);
    }

    [Theory]
    [MemberData(nameof(RelativeDate_ToString_Data))]
    public void RelativeDate_ToString(string dateTimeStr, string nowStr, string expectedValueEn, string expectedValueFr)
    {
        var now = DateTimeOffset.Parse(nowStr, CultureInfo.InvariantCulture);
        var dateTime = DateTimeOffset.Parse(dateTimeStr, CultureInfo.InvariantCulture);

        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(now);
        var relativeDate = RelativeDate.Get(dateTime, timeProvider);
        var resultEn = relativeDate.ToString(format: null, CultureInfo.InvariantCulture);
        Assert.Equal(expectedValueEn, resultEn);
        Assert.NotEmpty(expectedValueFr);

#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
        // Swedish is not translated, so it falls back to the neutral (English) resources
        var resultSv = relativeDate.ToString(format: null, CultureInfo.GetCultureInfo("sv"));
        Assert.Equal(expectedValueEn, resultSv);

        var resultFr = relativeDate.ToString(format: null, CultureInfo.GetCultureInfo("fr"));
        Assert.Equal(expectedValueFr, resultFr);
#endif
    }

    public static IEnumerable<object[]> RelativeDate_ToString_Data
    {
        get
        {
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:00:00Z", "now", "maintenant" };

            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:00:01Z", "one second ago", "il y a une seconde" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:00:25Z", "25 seconds ago", "il y a 25 secondes" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:01:00Z", "a minute ago", "il y a une minute" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:10:00Z", "10 minutes ago", "il y a 10 minutes" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 01:00:00Z", "an hour ago", "il y a une heure" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 01:30:00Z", "an hour ago", "il y a une heure" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 01:59:00Z", "an hour ago", "il y a une heure" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 02:00:00Z", "2 hours ago", "il y a 2 heures" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/02 00:00:00Z", "yesterday", "hier" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/03 00:00:00Z", "2 days ago", "il y a 2 jours" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/02/01 00:00:00Z", "one month ago", "il y a un mois" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/04/01 00:00:00Z", "3 months ago", "il y a 3 mois" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2019/01/01 00:00:00Z", "one year ago", "il y a un an" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2021/01/01 00:00:00Z", "3 years ago", "il y a 3 ans" };

            yield return new object[] { "2018/01/01 00:00:01Z", "2018/01/01 00:00:00Z", "in one second", "dans une seconde" };
            yield return new object[] { "2018/01/01 00:00:25Z", "2018/01/01 00:00:00Z", "in 25 seconds", "dans 25 secondes" };
            yield return new object[] { "2018/01/01 00:10:00Z", "2018/01/01 00:00:00Z", "in 10 minutes", "dans 10 minutes" };
            yield return new object[] { "2018/01/01 01:00:00Z", "2018/01/01 00:00:00Z", "in an hour", "dans une heure" };
            yield return new object[] { "2018/01/01 01:30:00Z", "2018/01/01 00:00:00Z", "in an hour", "dans une heure" };
            yield return new object[] { "2018/01/01 01:59:00Z", "2018/01/01 00:00:00Z", "in an hour", "dans une heure" };
            yield return new object[] { "2018/01/01 00:01:00Z", "2018/01/01 00:00:00Z", "in a minute", "dans une minute" };
            yield return new object[] { "2018/01/01 02:00:00Z", "2018/01/01 00:00:00Z", "in 2 hours", "dans 2 heures" };
            yield return new object[] { "2018/01/02 00:00:00Z", "2018/01/01 00:00:00Z", "tomorrow", "demain" };
            yield return new object[] { "2018/01/03 00:00:00Z", "2018/01/01 00:00:00Z", "in 2 days", "dans 2 jours" };
            yield return new object[] { "2018/02/01 00:00:00Z", "2018/01/01 00:00:00Z", "in one month", "dans un mois" };
            yield return new object[] { "2018/04/01 00:00:00Z", "2018/01/01 00:00:00Z", "in 3 months", "dans 3 mois" };
            yield return new object[] { "2019/01/01 00:00:00Z", "2018/01/01 00:00:00Z", "in one year", "dans un an" };
            yield return new object[] { "2021/01/01 00:00:00Z", "2018/01/01 00:00:00Z", "in 3 years", "dans 3 ans" };
        }
    }

    private static RelativeDate CreateDate(DateTime dateTime)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2018, 6, 15, 0, 0, 0, TimeSpan.Zero));
        return RelativeDate.Get(dateTime, timeProvider);
    }

    [Fact]
    public void IComparable_CompareTo_ReturnsPositive_ForNull()
    {
        // null sorts first, even against the smallest representable date
        var date = (IComparable)CreateDate(new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, date.CompareTo(obj: null));
    }

    [Theory]
    [InlineData(42)]
    [InlineData("string")]
    public void IComparable_CompareTo_Throws_ForAnotherType(object value)
    {
        var date = (IComparable)CreateDate(new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Throws<ArgumentException>(() => date.CompareTo(value));
    }

    [Fact]
    public void NonGenericSort_OrdersByDate()
    {
        var min = CreateDate(new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var middle = CreateDate(new DateTime(2018, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var max = CreateDate(new DateTime(2018, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        var items = new object[] { max, min, middle };
        Array.Sort(items);

        Assert.Equal(new object[] { min, middle, max }, items);
    }

    [Fact]
    public void ComparisonOperators_AreConsistent()
    {
        var earlier = CreateDate(new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var later = CreateDate(new DateTime(2018, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var sameAsEarlier = CreateDate(new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(earlier < later);
        Assert.True(earlier <= later);
        Assert.False(earlier > later);
        Assert.False(earlier >= later);
        Assert.True(later > earlier);
        Assert.True(later >= earlier);

        Assert.True(earlier == sameAsEarlier);
        Assert.False(earlier != sameAsEarlier);
        Assert.True(earlier <= sameAsEarlier);
        Assert.True(earlier >= sameAsEarlier);
        Assert.Equal(0, earlier.CompareTo(sameAsEarlier));
        Assert.Equal(earlier.GetHashCode(), sameAsEarlier.GetHashCode());
        Assert.True(earlier.Equals((object)sameAsEarlier));
        // Assigned first so the analyzer does not rewrite this into Assert.NotEqual, which would not exercise Equals(object)
        var equalsAnotherType = earlier.Equals("not a relative date");
        Assert.False(equalsAnotherType);
    }

#if !INVARIANT_GLOBALIZATION_MODE_ENABLED
    [Fact]
    public void LocalDateTime_IsInterpretedInTheTimeProviderTimeZone()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2018, 6, 15, 12, 0, 0, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(timeZone);

        // 10:00 UTC expressed as a wall clock reading in the provider's time zone
        var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(new DateTime(2018, 6, 15, 10, 0, 0, DateTimeKind.Utc), timeZone), DateTimeKind.Local);

        var result = RelativeDate.Get(local, timeProvider).ToString(format: null, CultureInfo.InvariantCulture);
        Assert.Equal("2 hours ago", result);
    }

    [Fact]
    public void LocalDateTime_SkippedByAForwardDstTransition_DoesNotThrow()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2018, 3, 25, 12, 0, 0, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(timeZone);

        // 02:30 does not exist in Paris on 2018-03-25: the clock jumps from 02:00 to 03:00
        var invalid = DateTime.SpecifyKind(new DateTime(2018, 3, 25, 2, 30, 0), DateTimeKind.Local);
        Assert.True(timeZone.IsInvalidTime(DateTime.SpecifyKind(invalid, DateTimeKind.Unspecified)));

        // Read against the standard-time offset (UTC+1), so 02:30 becomes 01:30 UTC
        var result = RelativeDate.Get(invalid, timeProvider).ToString(format: null, CultureInfo.InvariantCulture);
        Assert.Equal("10 hours ago", result);
    }

    [Fact]
    public void UtcDateTime_IgnoresTheTimeProviderTimeZone()
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2018, 6, 15, 12, 0, 0, TimeSpan.Zero));
        timeProvider.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));

        var result = RelativeDate.Get(new DateTime(2018, 6, 15, 10, 0, 0, DateTimeKind.Utc), timeProvider).ToString(format: null, CultureInfo.InvariantCulture);
        Assert.Equal("2 hours ago", result);
    }

    private static readonly string[] LocalizedCultures = ["de", "es", "fr", "it", "ja", "ko", "nl", "pt", "tr", "zh-Hans"];

    /// <summary>Offsets from "now" reaching every branch of <see cref="RelativeDate.ToString(string, IFormatProvider)"/>, in both directions.</summary>
    private static readonly TimeSpan[] AllBranchOffsets =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(-25), TimeSpan.FromSeconds(-90),
        TimeSpan.FromMinutes(-10), TimeSpan.FromMinutes(-60), TimeSpan.FromHours(-2),
        TimeSpan.FromHours(-30), TimeSpan.FromDays(-3), TimeSpan.FromDays(-31),
        TimeSpan.FromDays(-90), TimeSpan.FromDays(-365), TimeSpan.FromDays(-1095),
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(90),
        TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(60), TimeSpan.FromHours(2),
        TimeSpan.FromHours(30), TimeSpan.FromDays(3), TimeSpan.FromDays(31),
        TimeSpan.FromDays(90), TimeSpan.FromDays(365), TimeSpan.FromDays(1095),
    ];

    public static TheoryData<string> LocalizedCulturesData => new(LocalizedCultures);

    [Theory]
    [MemberData(nameof(LocalizedCulturesData))]
    public void RelativeDate_ToString_UsesLocalizedResources(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var now = new DateTimeOffset(2018, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(now);

        var seen = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        foreach (var offset in AllBranchOffsets)
        {
            var relativeDate = RelativeDate.Get(now + offset, timeProvider);
            var neutral = relativeDate.ToString(format: null, CultureInfo.InvariantCulture);
            var localized = relativeDate.ToString(format: null, culture);

            Assert.NotEmpty(localized);

            // A resource missing from the culture silently falls back to the neutral (English) value
            Assert.NotEqual(neutral, localized);

            // The count must survive the translation
            var count = GetDigits(neutral);
            if (count.Length > 0)
                Assert.Contains(count, localized);

            // Two branches producing the same text means a value was copied to the wrong key
            // (e.g. "yesterday" translated with the word for "tomorrow")
            Assert.False(seen.TryGetValue(localized, out var previousOffset), $"'{localized}' is produced by both {previousOffset} and {offset}");
            seen.Add(localized, offset);
        }
    }

    [Theory]
    [MemberData(nameof(LocalizedCulturesData))]
    public void AllResourcesAreLocalized(string cultureName)
    {
        var resourceManager = new ResourceManager("Meziantou.Framework.RelativeDates", typeof(RelativeDate).Assembly);
        var neutralResources = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(neutralResources);

        var localizedResources = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), createIfNotExists: true, tryParents: false);
        Assert.NotNull(localizedResources);

        foreach (DictionaryEntry entry in neutralResources)
        {
            var name = (string)entry.Key;
            var neutralValue = (string?)entry.Value;
            Assert.NotNull(neutralValue);

            var localizedValue = localizedResources.GetString(name);
            Assert.NotNull(localizedValue);
            Assert.NotEmpty(localizedValue);

            // A resource that formats a count must keep its placeholder
            Assert.Equal(neutralValue.Contains("{0}", StringComparison.Ordinal), localizedValue.Contains("{0}", StringComparison.Ordinal));
        }
    }

    private static string GetDigits(string value) => string.Concat(value.Where(char.IsAsciiDigit));
#endif
}
